# 全局快捷键在托盘隐藏期间失效的修复设计（Issue #8）

| 项 | 内容 |
|---|---|
| 关联 Issue | #8「全局快捷键效果不够完善」 |
| 状态 | 设计定稿，待实现（本文件只产出 spec，不改源码） |
| 日期 | 2026-11-08 |
| 影响范围 | 热键注册/注销时机、托盘隐藏与显示路径、设置页保存逻辑、App 启动与退出 |
| 不涉及 | 数据库 schema、`settings.json` 字段形状、托盘菜单文案、单实例 NamedPipe 激活路径 |

## 背景与现状
- `App.OnStartup`（`App.xaml.cs:16-53`）先做单实例判定再注册 DI：`IGlobalHotkeyService`、`ISettingsService` 为单例（`:74-77`），`MainWindow` 从根容器解析一次（`:50-52`、`:86-87`），进程内只有一个主窗口。
- 热键注册挂在主窗口生命周期上：构造函数订阅 `HotkeyPressed`（`MainWindow.xaml.cs:29`），`Loaded` 里调 `RegisterHotkey()`（`:34-40`、`:49-64`）；`Register` 取主窗口 HWND，`RegisterHotKey(handle, 9000, ...)`，再用 `HwndSource.FromHwnd(handle)` 装 `WM_HOTKEY`(0x0312) 钩子（`Core/Services/GlobalHotkeyService.cs:43-61`、`:73-92`）。
- 注销发生在 `Closing`：`MainWindow_Closing` 调 `Unregister()` 并退订事件（`MainWindow.xaml.cs:46`、`:66-70`）。
- 托盘隐藏有两条不对称路径：最小化按钮 → `StateChanged` → `Hide()`（`:43`、`:188-194`）；标题栏 X → 重写的 `OnClosing` 置 `e.Cancel = true`、`WindowState = Minimized`、`Hide()`，末尾仍 `base.OnClosing(e)`（`:196-210`）。真正退出走托盘"退出"先置 `_isExiting` 再 `Close()`（`:182-186`）。托盘图标见 `MainWindow.xaml:19-29`。
- 设置：`HotkeySettings` 只有 `Enabled/Key/Modifiers`（`Core/Models/HotkeySettings.cs`），存到 `%LocalAppData%\BashCommandManager\settings.json`（`Core/Services/SettingsService.cs:21-33`）；`SettingsViewModel.Save` **先落盘后注册**（`ViewModels/SettingsViewModel.cs:81`、`:86`）；改键对话框靠 `KeyDown` 捕获（`Views/HotkeyCaptureWindow.xaml.cs:34-53`），期间热键仍注册。
- 结论：热键生命周期被绑在"主窗口 + 它的 `Closing`"上，而两条托盘隐藏路径对 `Closing` 的语义完全不同。这是本 Issue 的结构性成因。

## 问题根因分析
### 直接根因（高置信度，代码可证）：关闭到托盘时热键被注销且事件被退订
1. 点 X → `OnClosing`（`MainWindow.xaml.cs:196`）；非退出分支 `e.Cancel = true`、`WindowState = Minimized`（触发 `StateChanged` → `Hide()`）、`Hide()`（`:206-208`）。
2. 随后 `base.OnClosing(e)`（`:209`）。WPF 的 `Window.OnClosing` 实现就是触发 `Closing` 事件，与 `e.Cancel` 当前取值无关，因此 `MainWindow_Closing` 必然执行。
3. 该处理器做两件致命的事（`:66-70`）：`Unregister()`（`:68`）→ `UnregisterHotKey(hwnd, 9000)`、`_isRegistered = false`、`RemoveHook`（`GlobalHotkeyService.cs:94-108`）；`HotkeyPressed -= ...`（`:69`）→ 之后即使注册成功也无订阅者。
4. 不会自动重注册：`RegisterHotkey()` 只在 `Loaded` 里调一次，`Loaded` 对同一窗口只触发一次；恢复窗口的 `ShowWindow()`（`:175-180`）与单实例回调（`App.xaml.cs:20-28`）都不注册。
5. 结果：X 到托盘后热键在本进程余下生命周期内永久失效；窗口在任务栏（可见）期一切正常，因为 `Closing` 未发生。用最小化按钮隐藏不触发 `Closing`，热键仍有效——这解释了问题表现的间歇性；CLAUDE.md 记录"关闭按钮默认最小化到托盘"，故绝大多数用户走失效路径。
6. **可区分"注册丢失"与"订阅丢失"的诊断签名：** X 到托盘 → 托盘恢复 → 打开设置 → 保存。`UpdateHotkey` 返回 `true`（`_window` 与 HWND 都还有效），但按热键仍无反应；在 `HwndHook`（`GlobalHotkeyService.cs:123-131`）下断点可见 `HotkeyPressed == null`。

### 候选根因（需日志确认后才能升级为结论）
- **RC-2 句柄生命周期：** 注册时缓存 `WindowInteropHelper.Handle`，`Unregister/UpdateHotkey` 每次重算（`GlobalHotkeyService.cs:96-100`、`:119`）。若 HWND 重建，注销会用错句柄、旧注册泄漏，随后同组合报冲突。当前仓库无重建 HWND 的路径（未改 `WindowStyle`/`AllowsTransparency`，只 `Show/Hide`），排除为主因。验证：注册与注销处打印 `hwnd`，隐藏/显示十次看是否变化。
- **RC-3 `WM_HOTKEY` 被 HandyControl 吞掉：** 被现有证据否定——窗口可见时同一钩子工作正常；且 `HwndSource.AddHook` 的钩子由 `HwndSource` 的窗口过程在分发阶段优先调用，`hc:Window` 抢不到它前面。
- **RC-4 隐藏窗口收不到消息：** 与 Win32 语义不符。`WM_HOTKEY` 由系统投递到**注册它的线程**的队列并定向到传入 `hWnd`，只要线程有消息泵（WPF Dispatcher 一直在跑）就送达，窗口可见与否不影响。

### 复现与诊断验证步骤
1. 启动后窗口可见，按 `Ctrl+Alt+B` → 隐藏到托盘；再按 → 显示并激活（均正常）。
2. 点 X 隐藏到托盘，按热键：**无反应**（缺陷复现）。
3. 托盘"显示窗口"恢复，改用最小化按钮隐藏，按热键：**有效**（对照：证明 HWND 与注册可用，差异只在 `Closing`）。
4. 在 `MainWindow_Closing` 与 `Unregister` 各加一行日志重跑第 2/3 步：只有第 2 步出现这两条日志。
5. 复现后断言 `HotkeyPressed is null`，确认是订阅丢失而非注册丢失。
6. 排查 RC-2 时用第 3 步日志观察句柄是否在隐藏/显示前后变化。

## 目标（Goals）
- G1 托盘隐藏（X、最小化按钮或任何 `Hide()`）期间热键始终有效。
- G2 热键宿主与主窗口彻底解耦：隐藏、显示、`Closing` 被取消、将来窗口重建都不影响注册。
- G3 托盘隐藏时按热键：显示、恢复 `Normal`、置于前台并获得键盘焦点。
- G4 窗口可见且前台时按热键：最小化到托盘，语义与现状一致。
- G5 运行时改键立即生效；新组合被占用时保留旧组合且不污染已保存配置。
- G6 真正退出时释放注册与消息窗口，无孤儿注册。
- G7 决策（快照 → 动作）是无 Win32/WPF 依赖的纯逻辑，可单测；Win32 走薄适配层。
- G8 不改用户可见语义、不新增配置项、不改 `settings.json` 字段。

## 非目标（Non-Goals）
- 不改单实例 NamedPipe 激活回调（`App.xaml.cs:20-28`）的抢焦点实现；新控制器已可供后续复用。
- 不用低级键盘钩子 `WH_KEYBOARD_LL` 替代 `RegisterHotKey`（理由见备选方案 C）。
- 不支持多热键、"仅显示不隐藏"模式、连发抑制等新功能；不改托盘菜单/图标/`Growl` 文案、数据库与 `App.xaml` 资源。
- 不做 `Core` 层整体分层重构（`GlobalHotkeyService` 仍留在 `Core/Services`，与现状一致）。

## 假设与待确认（Assumptions & Open Questions）
本会话是自动化流水线，**没有人可以回答问题**，下列问题以"默认选择"推进；日后若有人 review，可按"风险"列推翻。

| 编号 | 假设 / 问题 | 默认选择 | 理由 | 风险 |
|---|---|---|---|---|
| A1 | 无法向人类确认范围 | 只修 Issue #8 相关路径 | 任务边界明确 | 若产品还要求"只显示不隐藏"需追加需求 |
| A2 | 仓库无任何测试项目、无 `.sln` | 新建 `tests/BashCommandManager.Tests/`，主 csproj 加 `DefaultItemExcludes` 排除 `tests\**` | 主项目默认 glob 会把 `tests\**\*.cs` 与测试项目 `obj/` 编进主程序集，直接编译失败 | 引入新构建图；`dotnet build` 只编主项目 |
| A3 | 主窗口实例唯一且不重建 | 仍按"可能重建"设计，宿主不依赖任何业务窗口 HWND | 解耦后不需要该前提，未来换肤/重建不会回归 | 略增抽象层数 |
| A4 | 用户语义不变 | 隐藏→显示；可见且前台→托盘；可见但非前台→激活 | 与 `MainWindow.xaml.cs:81-103` 现有三态一致 | 无 |
| A5 | 消息窗口可能创建失败 | 先 message-only，失败降级 0×0 隐藏 `HwndSource`（永不 `Show`），再失败返回 `HostUnavailable` 并 Growl 一次 | 隐藏窗口同样能收 posted 消息，功能不丢 | 降级占用一个不可见 HWND |
| A6 | `SetForegroundWindow` 可能被前台锁拒绝 | 不注入伪造输入，只用 `SetForegroundWindow` + `AttachThreadInput` 兜底 | 伪造输入安全软件敏感且不可靠 | 全屏独占时首次可能只闪任务栏 |
| A7 | `settings.json` 需能被旧版本回读 | 不改字段与 JSON 形状，只在内存里克隆编辑副本 | 回滚安全 | 无 |
| A8 | 捕获新组合时已注册热键会吞键 | 捕获前 `Suspend()`（临时注销），关闭后 `Resume()` | 否则无法重捕同一组合，且捕获时按热键会把主窗口藏到对话框后 | 捕获期间被强杀则注册随进程消失，无残留 |

已按默认值解决、无需再问：可见但非前台是否也拉到前台（默认是，沿用现状并补强 `SetForegroundWindow`）；注册冲突是否阻塞保存（默认是，先注册成功再落盘）；注册失败是否重复气泡提醒（默认仅启动与保存各一次）。

## 方案设计
### 架构总览
```
UI 线程（WPF Dispatcher + 消息泵）
  App.OnStartup
    ├─ SettingsService 读 settings.json
    ├─ GlobalHotkeyService.TryRegister ──► HotkeyMessageWindow（HwndSource, Parent=HWND_MESSAGE）
    │        RegisterHotKey(hwnd, id, mods, vk) ──► WM_HOTKEY ──► HwndSource hook ──► HotkeyPressed
    ├─ MainWindow.Show()
    └─ HotkeyPressed ──► HotkeyToggleCoordinator.Execute()
             ├─ WindowTogglePolicy.Decide(snapshot)   ← 纯逻辑，无 Win32
             └─ MainWindowVisibilityController（WPF） ──► IForegroundWindowActivator（user32）
  App.OnExit ──► 退订事件、Unregister、Dispose 消息窗口
```
核心性质：热键宿主是**进程级**的，由 `App` 持有并存活整个进程；主窗口只是"被执行动作"的目标。

### 组件清单
| 文件 | 变更 | 职责 |
|---|---|---|
| `Core/Services/IGlobalHotkeyService.cs` | 新增 | 从 `GlobalHotkeyService.cs` 抽出接口：去掉 `Window` 参数，加 `Suspend/Resume/IsRegistered` |
| `Core/Models/HotkeyRegistrationResult.cs` | 新增 | `HotkeyRegistrationStatus` 枚举 + `HotkeyRegistrationResult` 记录结构 |
| `Core/Services/HotkeyMessageWindow.cs` | 新增 | message-only 宿主（`HwndSource`，`Parent=HWND_MESSAGE`）：建窗、挂钩、转发 `WM_HOTKEY`、销毁 |
| `Core/Services/WindowTogglePolicy.cs`、`Core/Services/IWindowVisibilityController.cs`、`Core/Services/HotkeyToggleCoordinator.cs` | 新增 | 纯逻辑层：`WindowActivitySnapshot`/`WindowToggleAction`/`IWindowTogglePolicy` 实现、窗口动作抽象、"快照 → 决策 → 执行"协调器 |
| `Infrastructure/MainWindowVisibilityController.cs`、`Infrastructure/Interop/ForegroundWindowActivator.cs` | 新增 | 用 `MainWindow` 实现控制器；`IForegroundWindowActivator` + `SetForegroundWindow/AttachThreadInput` 薄封装 |
| `Core/Services/GlobalHotkeyService.cs` | 修改 | 内部委托宿主；id 序列化、冲突保留、`Suspend/Resume` |
| `Core/Models/HotkeySettings.cs` | 修改 | 增加 `Clone()` |
| `MainWindow.xaml.cs` | 修改 | 删除热键相关字段/订阅/注册/退订/`ToggleWindowVisibility`；保留托盘、`StateChanged`、`OnClosing`、退出 |
| `App.xaml.cs` | 修改 | 建宿主与协调器、启动注册、退出清理、DI 注册新类型 |
| `ViewModels/SettingsViewModel.cs` | 修改 | 编辑副本、先注册后落盘、冲突提示、捕获期间 `Suspend/Resume` |
| `BashCommandManager.csproj` | 修改 | 加 `<DefaultItemExcludes>$(DefaultItemExcludes);tests\**</DefaultItemExcludes>` |
| `tests/BashCommandManager.Tests/` | 新增 | xunit 项目（`net8.0-windows10.0.19041.0` + `UseWPF`，`ProjectReference` 主项目）：`BashCommandManager.Tests.csproj`、`WindowTogglePolicyTests.cs`、`HotkeyToggleCoordinatorTests.cs`、`HotkeySettingsTests.cs`、`Fakes/FakeWindowVisibilityController.cs` |

### 热键宿主：message-only 窗口
```csharp
internal sealed class HotkeyMessageWindow : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private HwndSource? _source;
    public IntPtr Handle => _source?.Handle ?? IntPtr.Zero;
    public event EventHandler<int>? HotkeyIdReceived;      // 参数为注册 id

    public bool EnsureCreated()
    {
        _source ??= Create(new IntPtr(-3)) ?? Create(IntPtr.Zero);   // 降级：0×0 隐藏窗口
        if (_source is null) return false;
        _source.AddHook(WndProc);
        return _source.Handle != IntPtr.Zero;
    }

    private static HwndSource? Create(IntPtr parent)
    {
        try
        {
            var source = new HwndSource(new HwndSourceParameters("BashCommandManager.HotkeyHost")
                { WindowStyle = 0, ParentWindow = parent, Width = 0, Height = 0 });
            if (source.Handle != IntPtr.Zero) return source;      // 句柄为 0 视为失败，走降级
            source.Dispose();
        }
        catch (Exception ex) { Debug.WriteLine($"热键宿主创建失败: {ex.Message}"); }
        return null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY) { HotkeyIdReceived?.Invoke(this, wParam.ToInt32()); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()   // 必须在创建它的 UI 线程调用
    { if (_source is null) return; _source.RemoveHook(WndProc); _source.Dispose(); _source = null; }
}
```
- `Parent = HWND_MESSAGE` 的窗口不参与枚举、不显示、不属于可见窗口树，但仍是真实 HWND，可接收 `PostMessage` 投递的 `WM_HOTKEY`。
- 该 HWND 进程内只建一次，不随主窗口 `Show/Hide` 变化，故不受"主窗口隐藏"影响。
- `HwndSource` 钩子在 UI 线程消息泵里执行，`HotkeyPressed` 天然在 UI 线程触发，保留 `CheckAccess` 断言即可，无需 `Dispatcher.Invoke`。

### 服务接口与注册算法
```csharp
public interface IGlobalHotkeyService : IDisposable
{
    HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers);
    HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers);
    void Suspend();        // 保留 Current，仅解除系统注册（捕获热键期间）
    void Resume();         // 用 Current 重新注册
    void Unregister();     // 清空 Current
    bool IsRegistered { get; }
    event EventHandler? HotkeyPressed;
}

public enum HotkeyRegistrationStatus { Success, Conflict, InvalidCombination, HostUnavailable, NotRegistered }
public readonly record struct HotkeyRegistrationResult(HotkeyRegistrationStatus Status, Key Key, ModifierKeys Modifiers)
{ public bool IsSuccess => Status == HotkeyRegistrationStatus.Success; }
```
`TryRegister`/`UpdateHotkey` 共用算法：
1. `_dispatcher.VerifyAccess()`：`RegisterHotKey` 线程亲和，`WM_HOTKEY` 只投给注册线程，必须在有消息泵的 UI 线程注册。
2. `Key.None` 或 `Modifiers == None` → `InvalidCombination`；`EnsureCreated()` 失败 → `HostUnavailable`。
3. 取**新的** id（`0x9000` 起自增，`0x9000–0x90FF` 回绕；Win32 应用可用 `0x0000–0xBFFF`），先注册新组合。
4. 失败时读 `Marshal.GetLastWin32Error()`（DllImport 加 `SetLastError = true`）：`1409 = ERROR_HOTKEY_ALREADY_REGISTERED` → `Conflict` 且**旧注册不动**；其它错误 → `HostUnavailable`。
5. 成功则 `UnregisterHotKey(host.Handle, oldId)` 再替换 `Current`；新旧组合相同则直接 `Success`（避免自冲突）。`Suspend()` 只注销旧 id 并保留 `Current`，`Resume()` 用新 id 重注册（无 `Current` 时返回 `NotRegistered`），`Unregister()` 两者都清，`Dispose()` = `Unregister()` + `host.Dispose()` 且可重复调用。

该顺序保证改键失败不会把用户变成"没有热键"，也消除旧实现"先注销再注册"的空窗期。

### 数据流：一次按键
1. 用户按 `Ctrl+Alt+B`，系统发现该组合已由本进程注册。
2. 系统向注册线程队列 `PostMessage` 一条 `WM_HOTKEY`（`wParam` = 热键 id），目标窗口 = message-only HWND。
3. WPF Dispatcher 泵出消息，`HwndSource` 先跑 `AddHook` 钩子；`WndProc` 触发 `HotkeyIdReceived`，服务校验该 id 属于当前注册后触发 `HotkeyPressed`。
4. `App` 在 UI 线程同步调 `HotkeyToggleCoordinator.Execute()`：取快照 → `WindowTogglePolicy.Decide` → 调 `ShowAndActivate()`/`HideToTray()`/`ActivateExisting()` → 返回动作供日志。

### UI 行为与前台激活
| `IsAppModalOpen` | `!IsVisible` 或 `IsMinimized` | `IsActive` | 动作 |
|---|---|---|---|
| true | 任意 | 任意 | `ActivateExisting` |
| false | true | 任意 | `ShowAndActivate` |
| false | false | true | `HideToTray` |
| false | false | false | `ActivateExisting` |

快照语义：`IsVisible = window.IsVisible`；`IsMinimized = window.WindowState == Minimized`；`IsActive = window.IsActive`；`IsAppModalOpen = window.OwnedWindows.OfType<Window>().Any(w => w.IsVisible)`。模态分支避免"设置对话框开着时按热键把主窗口藏走、留下孤儿对话框"。

`ShowAndActivate()` 顺序不可颠倒：`if (!IsVisible) Show();` → `if (WindowState == Minimized) WindowState = Normal;` → `Activate();` → `IForegroundWindowActivator.TryActivate(new WindowInteropHelper(window).Handle)`。`HideToTray()` 保持现状：`WindowState = Minimized; Hide();`（`StateChanged` 会再 `Hide()`，幂等）。`ActivateExisting()` 若有可见模态子窗口则优先激活子窗口，否则激活主窗口。

```csharp
if (GetForegroundWindow() == target) return true;
if (SetForegroundWindow(target)) return true;                       // 首选
var fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
var attached = fgThread != GetCurrentThreadId() && AttachThreadInput(GetCurrentThreadId(), fgThread, true);
try { BringWindowToTop(target); SetForegroundWindow(target); SetFocus(target); }
finally { if (attached) AttachThreadInput(GetCurrentThreadId(), fgThread, false); }   // 必须解挂
return GetForegroundWindow() == target;                             // 结果可观测，便于日志
```
**`AttachThreadInput` 与 `AllowSetForegroundWindow` 的取舍：** `AllowSetForegroundWindow` 是"当前前台进程把抢前台的权利让给**另一个**进程"的 API，本程序无法对自己使用，不能作兜底；`AttachThreadInput` 把本线程输入状态临时挂到前台线程上，使 `SetForegroundWindow` 通过前台锁检查，是唯一不注入伪造输入的自助方案。代价是必须 `try/finally` 解挂，否则两线程输入队列持续耦合。即便兜底也失败，窗口已 `Show` 且 `Normal`、任务栏按钮可见，用户点一下即可——这是明确可接受的降级，不再叠加 `keybd_event`/`SendInput` 注入式 hack。

### 接线变更
- `MainWindow.xaml.cs`：删除 `_hotkeyService`/`_settingsService` 字段（`:16-17`）、`HotkeyPressed` 订阅（`:29`）、`Loaded` 里的 `RegisterHotkey()`（`:39`）、`Closing` 订阅（`:46`）、`MainWindow_Closing`（`:66-70`）、`HotkeyService_HotkeyPressed`（`:72-79`）、`ToggleWindowVisibility`（`:81-103`）。保留 `_isExiting`、`StateChanged`、`OnClosing`（仍调 `base.OnClosing(e)`，以免影响 `hc:Window` 既有行为）、`ShowWindow`、`Exit_Click`、托盘图标。改完后 `MainWindow` 不再知道热键存在。
- `App.xaml.cs`：`OnStartup` 里 `mainWindow.Show()` **之后**接线（`Growl` 需要已显示窗口，且需补 `using HandyControl.Controls;` / `using HandyControl.Data;`）：
  ```csharp
  var hotkeyService   = ServiceProvider.GetRequiredService<IGlobalHotkeyService>();
  var settingsService = ServiceProvider.GetRequiredService<ISettingsService>();
  var controller      = new MainWindowVisibilityController(mainWindow, new ForegroundWindowActivator());
  _toggleCoordinator  = new HotkeyToggleCoordinator(new WindowTogglePolicy(), controller);
  hotkeyService.HotkeyPressed += OnHotkeyPressed;   // => _toggleCoordinator.Execute()
  var s = settingsService.GetHotkeySettings();
  if (s.Enabled && !hotkeyService.TryRegister(s.Key, s.Modifiers).IsSuccess) Growl.Warning(new GrowlInfo { Message = "全局快捷键注册失败，可能已被其他程序占用", WaitTime = 5 });
  ```
  `OnExit`：`HotkeyPressed -= OnHotkeyPressed;` → `Unregister()` → `Dispose()`。**全进程只有这一处注销热键。**
- `SettingsViewModel`：构造函数把 `GetHotkeySettings()` 结果 `Clone()` 成编辑副本；`SetHotkey` 在 `ShowDialog()` 前后 `Suspend()/Resume()`；`Save` 改为"先注册、成功才落盘"：`if (!UpdateHotkey(...).IsSuccess) { HasError = true; ErrorMessage = "快捷键更新失败，可能已被其他程序占用（已保留原快捷键）"; Growl.Warning(...); return; }`，未启用则 `Unregister()`，最后 `SaveHotkeySettings(HotkeySettings)`。
- `HotkeySettings.Clone()`：`new() { Enabled = Enabled, Key = Key, Modifiers = Modifiers }`，保证冲突时服务内配置仍是旧值，与"保留原快捷键"一致。

### 错误处理与日志
| 场景 | 行为 |
|---|---|
| 消息窗口创建异常或句柄为 0 | 降级 0×0 隐藏窗口；再失败返回 `HostUnavailable`，启动时 Growl 一次，应用其余功能可用 |
| `RegisterHotKey` 失败且错误码 1409 | 返回 `Conflict`；旧注册不动；设置页提示"已被占用"且不落盘 |
| `RegisterHotKey` 其它错误 / `UnregisterHotKey` 失败 | 前者 `HostUnavailable` 并记错误码 + Growl 一次；后者只记日志但仍清空内部状态（避免僵尸注册挡住后续尝试） |
| 捕获热键期间异常或取消 | `finally` 里 `Resume()`，保证原组合恢复 |
| 退出时释放顺序 | `RemoveHook` → `_source.Dispose()`，仅在 UI 线程，重复调用无副作用 |
| 协调器执行动作抛异常 | `Execute` 捕获并 `Debug.WriteLine`，不让 UI 线程因热键崩溃 |

## 备选方案与取舍
| 方案 | 做法 | 优点 | 缺点 | 结论 |
|---|---|---|---|---|
| A 最小修复 | 在 `MainWindow_Closing` 判断 `e.Cancel`，取消关闭时不注销 | 2 行改动即可消除 Issue | 仍混用 `Closing` 与"隐藏"语义；再次触发 `Closing` 或窗口重建会回归；不变量寄托在调用顺序上 | 不采用 |
| **B message-only + 纯策略（推荐）** | `HwndSource(Parent=HWND_MESSAGE)` + `HotkeyToggleCoordinator`/`WindowTogglePolicy` + 薄 Win32 适配 | 与主窗口彻底解耦；决策可单测；改键/冲突/退出路径清晰 | 多出 8 个源文件与 1 个测试项目；需处理 `AttachThreadInput` 解挂 | **采用** |
| C 低级键盘钩子 | `WH_KEYBOARD_LL` 自行匹配组合 | 不受 `RegisterHotKey` 冲突限制 | 需回放或吞键、影响系统输入、杀软敏感、无法自然表达"被占用" | 不采用 |
| D 隐藏辅助窗口 | 用 0×0、`ShowInTaskbar=false` 的普通 `Window` 承载注册 | 实现简单 | 仍是可见窗口树成员，会被枚举、可能影响 Alt-Tab；生命周期仍需管理 | 仅作 A5 降级路径 |

推荐 B：把"何时注册"从 UI 事件流里拿出来，变成与进程同寿命的显式生命周期；同时把行为决策从 Win32 里拿出来，使这类回归能被单元测试拦住。

## 测试策略
### 单元测试（无需真实 Win32）
`WindowTogglePolicyTests`（对决策表逐行断言）：隐藏 + 已最小化 → `ShowAndActivate`；可见 + `IsMinimized` → `ShowAndActivate`；可见 + 非最小化 + `IsActive` → `HideToTray`；可见 + 非最小化 + 非激活 → `ActivateExisting`；`[Theory]` 覆盖模态打开（`IsAppModalOpen=true` × 可见/隐藏 × 激活/非激活）→ 一律 `ActivateExisting`；同一快照连续两次调用返回相同动作（无状态）。

`HotkeyToggleCoordinatorTests`（配合 `FakeWindowVisibilityController` 记录调用）：隐藏快照只调 `ShowAndActivate`、不调 `HideToTray`；前台快照只调 `HideToTray`；`Execute()` 返回的动作与实际调用一致；假控制器抛异常时 `Execute()` 不抛出（守护 UI 线程）；两次 `Execute()` 的动作随假控制器状态变化，证明没有缓存判定结果。

`HotkeySettingsTests`：`Clone()` 后修改副本不影响原对象；`IsValid` 在 `Modifiers == None` 时返回 `false`。

### 集成与手工验证
实现阶段的验证口径（本任务不执行）：
```
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
dotnet run
```
1. 启动后可见，按 `Ctrl+Alt+B` → 隐藏到托盘；再按 → 显示、`Normal`、前台，焦点在窗口内可直接输入。
2. 点 X 到托盘后按热键 → 显示（**本 Issue 核心回归**）。
3. 最小化按钮到托盘后按热键 → 显示（原有路径不回归）。
4. 托盘"显示窗口"恢复后按热键 → 仍有效（证明无二次注销）。
5. 托盘隐藏状态下连续切换 10 次，行为稳定。
6. 设置里改键 → 立即生效，旧组合无反应；重启后新组合生效（`settings.json` 正确）。
7. 先用 AutoHotkey 之类占用目标组合 → 保存时提示"已被占用"，旧组合仍生效，`settings.json` 未被改写。
8. 打开捕获窗口按当前组合 → 能捕获（`Suspend` 生效）；取消后原热键恢复。
9. 设置窗口开着时按热键 → 不隐藏主窗口，而是把应用拉到前台（模态分支）。
10. 托盘"退出" → 进程结束；重启后能立刻注册同一组合（无残留注册）。
11. 前台有普通/全屏应用时按热键 → 本应用确实到前台，而不是只闪任务栏。

## 验收标准
- [ ] X 关闭到托盘后按热键能显示并激活窗口（Issue #8 复现场景通过）。
- [ ] 最小化到托盘、托盘恢复、隐藏/显示反复切换后热键始终有效。
- [ ] 热键不再由 `MainWindow` 的 `Loaded`/`Closing` 决定；`MainWindow.xaml.cs` 中不再出现 `IGlobalHotkeyService`/`HotkeyPressed`/`Unregister`。
- [ ] 全进程只有 `App.OnExit` 一处注销热键，退出后无残留注册。
- [ ] 托盘隐藏时按热键：显示、`Normal`、前台且有焦点；窗口在前台时按热键：最小化到托盘（语义不变）。
- [ ] 运行时改键立即生效；组合被占用时提示并保留旧组合，`settings.json` 不被污染。
- [ ] `WindowTogglePolicy` 与 `HotkeyToggleCoordinator` 单测全绿，且不依赖 Win32/WPF 窗口实例。
- [ ] 捕获快捷键期间当前组合可被捕获，关闭捕获窗口后热键恢复。
- [ ] 启动时注册失败只提示一次，应用其余功能正常；本文件提到的类名与路径与仓库及组件清单一致，无占位符。

## 风险与回滚
| 风险 | 等级 | 缓解 |
|---|---|---|
| `HwndSource(Parent=HWND_MESSAGE)` 在个别环境建窗失败 | 中 | A5 降级 0×0 隐藏窗口；仍失败则 Growl 并保证应用可用 |
| `AttachThreadInput` 未解挂导致输入队列耦合 | 中 | `try/finally` 强制解挂 + `GetForegroundWindow()` 结果校验；只在前台锁拒绝时进入 |
| 主项目默认 glob 把 `tests/**` 编进主程序集导致编译失败 | 高 | 同步改 `BashCommandManager.csproj` 的 `DefaultItemExcludes`；实现时先改 csproj 再建测试项目 |
| 测试项目引用 WPF WinExe 时框架不匹配 | 中 | 目标框架与主项目一致（`net8.0-windows10.0.19041.0`）并设 `UseWPF` |
| 改动面共 19 个文件（8 新增 + 6 修改 + 5 测试），一次提交难 review | 中 | 拆两个提交：`feat(hotkey): 解耦热键宿主` 与 `feat(hotkey): 策略分层与测试`，纯逻辑先行 |
| 前台锁导致少数场景激活失败 | 低 | 窗口仍可见并出现在任务栏；日志记录 `TryActivate` 结果 |
| 单文件发布引入新文件依赖 | 低 | 全为托管代码，无新增内容文件，无需改 `docs/PACKAGING.md` |

**回滚：** 无数据迁移、无 `settings.json` 字段变化、无数据库变更，回滚即 `git revert` 对应提交（或恢复 `MainWindow.xaml.cs`/`App.xaml.cs`/`GlobalHotkeyService.cs`/`SettingsViewModel.cs` 并删除新增文件）。旧版本仍能读新版本写出的 `settings.json`；进程内的热键注册随进程退出自动释放，无需额外清理。
