# 计划评审：`2026-11-08-hotkey-tray.md`（Issue #8 全局快捷键 / 托盘）

评审对象：`docs/superpowers/plans/2026-11-08-hotkey-tray.md`（2502 行，10 个任务）
对照文档：`docs/superpowers/specs/2026-11-08-hotkey-tray-design.md`、`docs/superpowers/DECISIONS.md`
真实现状：`MainWindow.xaml.cs`、`App.xaml.cs`、`Core/Services/GlobalHotkeyService.cs`、`Core/Models/HotkeySettings.cs`、`ViewModels/SettingsViewModel.cs`、`BashCommandManager.csproj`、`Views/HotkeyCaptureWindow.xaml.cs` 等
评审日期：2026-11-08

## 结论

**有条件通过。** 计划质量高：文件路径与仓库现状吻合、任务间接口自洽、代码块可直接编译、测试计数自洽，且"进程级 message-only 宿主"这一核心技术前提在本机实测成立。但执行前必须先修 4 项，其中 B2 会让"捕获带 Alt 的组合"这条验收路径**静默产出一个永远不触发的热键并污染 `settings.json`**，属于功能级缺陷；B1 违反 `DECISIONS.md` D2 的明文要求。

放行条件：修完 B1–B4（B2 是 1 行修改 + 1 个手工步骤；B1 是 1 行 XML）。

**本次评审的验证方式（未修改本仓库任何文件）：**

1. 把计划里全部 28 个文件（10 新增 + 8 修改 + 10 测试）的代码块按原文抽到仓库的临时副本（`%TEMP%\hotkey-plan-probe\repo`），补上 `HotkeySettings.Clone()` 与 csproj 排除项后执行：
   - `dotnet build BashCommandManager.csproj -c Release` → **0 个警告 / 0 个错误**（SDK 10.0.301，离线还原，含 HandyControl/CommunityToolkit/Dapper/SQLite）。
   - `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj` → **通过 40 / 失败 0**，与计划声明的 10（Task 3）/7（Task 4）/6（Task 5）/10（Task 5）/2（Task 5）/39（Task 5 Step 13）/40（Task 7 Step 6、Task 10 Step 2）逐项一致。
   - 复跑计划 Task 8 Step 7 与 Task 10 Step 3 的 `Select-String`：命中数分别为 **0** 与 **2**，与计划期望一致。
2. 用独立 STA 探针（引用计划原样的 `HotkeyMessageWindow.cs` + `HotkeyNativeMethods.cs`）实测 message-only 宿主：`EnsureCreated()=true`、句柄非 0、`RegisterHotKey(hwnd,...,vk=0x42)` 返回 true、`PostMessage(WM_HOTKEY)` 经钩子转发到 `HotkeyIdReceived(wParam=id)`、窗口存活期 `UnregisterHotKey` 返回 true、`Dispose()` 幂等。

## 阻断性问题（必须改）

### B1 `DefaultItemExcludes` 只排了 `tests\**`，缺 `BashCommandManager.Cli\**`（违反 DECISIONS D2）

- 计划位置：L16（Global Constraints）、L52（File Structure 修改表）、L186 与 L198（Task 1 Step 4 的具体 XML）。
- 事实：`DECISIONS.md:26` 明确要求 `<DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>`，并在 D2 里写明"主项目排除测试与 CLI 源码，统一用 `DefaultItemExcludes`"；spec:63（A2）只写了 `tests\**`，但 `DECISIONS.md:4` 规定冲突时以 DECISIONS 为准。计划只在 L186/L198 写了 `tests\**`。
- 后果：当前仓库没有 `BashCommandManager.Cli/`，所以不会立刻失败；但 #10 一旦建目录，主工程默认 glob 会把 CLI 的 `*.cs`（以及其 `obj/*.AssemblyInfo.cs`）编进主程序集，直接触发 `CS0579`/`CS0246`，破坏 D5.1「根目录 `dotnet build -c Release` 只编主项目且成功」。而 D2 的设计意图正是"排除项在 #8 首次搭建时一次写全，后续特性不再改主 csproj"。
- 修法：L186 与 L198 改为
  ```xml
  <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
  ```
  同步 L16 与 L52 的文字（"排除 `tests\**` 与预留的 `BashCommandManager.Cli\**`"），并在 Task 1 Step 4 补一句"CLI 目录由 #10 创建，这里先预留排除项，避免 #10 再改主 csproj"。
- 验证依据：本次已在仓库副本实测 `tests\**` 生效——去掉该属性后主工程报 `CS0246: 未能找到类型或命名空间名"Xunit"`（干净树）/ `CS0579: 特性重复`（已有 `tests/obj` 时）；加上后 0 警告 0 错误。

### B2 捕获带 Alt 的组合会存下 `Key.System`（→ VK `0x0`），改键"成功"但热键永久失效，且污染 `settings.json`

- 计划位置：Task 9 Files L2153–2158（只列了 `ViewModels/SettingsViewModel.cs`）、Task 9 Step 1 L2162–2298（未改 `Views/HotkeyCaptureWindow.xaml.cs`）、手工步骤 L2382（手工 6 明确要求"按当前组合 `Ctrl+Alt+B`"）、L2383（手工 7）、验收映射 L2452（验收标准第 8 条）。
- 事实链：
  1. `Views/HotkeyCaptureWindow.xaml.cs:48` 是 `_capturedKey = e.Key;`（同文件 49 行 `_capturedModifiers = Keyboard.Modifiers;`、52 行 `CanConfirm = _capturedModifiers != ModifierKeys.None;`）。
  2. WPF 在 Alt 按下时把 `KeyEventArgs.Key` 置为 `Key.System`，真实键在 `KeyEventArgs.SystemKey`（`Ctrl+Alt+B` 走 `WM_SYSKEYDOWN`）。计划全篇未处理这一情况。
  3. 实测 `KeyInterop.VirtualKeyFromKey(Key.System) == 0x0`；在 message-only 宿主上用 `RegisterHotKey(hwnd, id, MOD_CONTROL|MOD_ALT, vk:0)` **本机返回 True（err=0）**。
  4. 于是 `GlobalHotkeyService.TryRegister` 返回 `Success` → Task 9 的 `Save` 判定成功 → `SaveHotkeySettings` 把 `"Key": 156` 写进 `settings.json` → Growl「设置已保存」；而旧组合已在同一次 `TryRegister` 里被 `UnregisterNative` 注销。结果：**用户被告知保存成功，热键却再也按不出来，配置文件里留下一个无效 Key**（违反 G5"改键立即生效/不污染配置"、A7"旧版本可回读"）。
  5. 计划的手工 6 恰好要求捕获默认组合 `Ctrl+Alt+B`：捕获窗口会显示 `Ctrl+Alt+System`（`HotkeySettings.ToDisplayString()` 会走 `Ctrl+Alt` 分支拼 `Key.System`），该步骤当场失败。
- 修法（可直接落地）：
  1. Task 9 Files 增加 `Modify: Views/HotkeyCaptureWindow.xaml.cs`；在 Step 1 之后加一步：`_capturedKey = e.Key == Key.System ? e.SystemKey : e.Key;`（`UpdateDisplayText` 用解析后的键）。
  2. 更符合 G7/可测性：抽纯函数 `HotkeyCaptureResolver.Resolve(Key key, Key systemKey)`（建议放 `Core/Models`），加两条 xunit：`(Key.System, Key.B) → Key.B`、`(Key.B, Key.None) → Key.B`；若采纳，需把 L1115（39）、L1497（40）、L2356（40）的计数同步 +2。
  3. 手工 6 期望改为"捕获窗口显示 `Ctrl+Alt+B`"，并补"取消捕获后按 `Ctrl+Alt+B` 仍能切换"（对应 spec:261 手工 8 的后半句）。
  4. 兜底：`SettingsViewModel.Save` 成功前加一道 `if (result.Status == HotkeyRegistrationStatus.InvalidCombination)` 分支（见 S7），至少让"无效键"不落盘。

### B3 模态决策表的"隐藏"分支没有测试（spec:242 明确要求的用例缺失）

- 计划位置：L390–404（`Decide_never_hides_when_an_app_modal_window_is_open`）；L392 注释写的是"模态 + 隐藏"，但形参是 `isMinimized`，L399 把 `IsVisible: true` 写死。
- 事实：spec:242 要求 `[Theory]` 覆盖"`IsAppModalOpen=true` × 可见/隐藏 × 激活/非激活 → 一律 `ActivateExisting`"；计划的 4 条 `InlineData` 全部只覆盖 `IsVisible=true`（另外两个维度是 `isMinimized`/`isActive`），"隐藏 + 模态"没有任何断言。注释与代码不一致，容易被误认为已覆盖。
- 修法：把形参改成 `bool isVisible, bool isMinimized, bool isActive`，4 条用例覆盖 `(true,false,true)`、`(true,false,false)`、`(false,true,true)`、`(false,true,false)`；用例数仍是 4，因此 Task 3 L486「通过: 10」、Task 5 L1115「39」、Task 7 L1497「40」、Task 10 L2356「40」都不用改。

### B4 与 `DECISIONS.md` 的偏差未登记：D5.3（删/改公开接口）与 D5.4（"每个任务先有失败测试"被 4 个任务豁免）

- 计划位置：L1517–1521（新接口 `Produces`）、L2146（Task 8 提交信息，仅一行主题）、L22（Global Constraints 自设 TDD 例外）、L2481–2488（Self-Review §5 偏差表只对 spec，不对 DECISIONS）。
- 事实：
  - `DECISIONS.md:4`："实现阶段必须遵守本文件；与本文件冲突时以本文件为准，**并在提交说明里记录偏差**"。
  - D5.3（`DECISIONS.md:91`）"不删除、不改名任何已有公开类/方法（只允许新增）"与计划冲突：删除 `IGlobalHotkeyService.Register(Window, Key, ModifierKeys)`、把 `UpdateHotkey` 返回类型由 `bool` 改为 `HotkeyRegistrationResult`。D4（`DECISIONS.md:74`）已把接口形状裁给 #8，所以**形状本身可接受**，但偏差必须留痕——计划的 4 处提交信息都是单行主题，无偏差说明。
  - D5.4（`DECISIONS.md:92`）"每个任务都必须先有失败测试，再有实现"没有例外条款；计划的 Task 6、8、9 完全没有测试，Task 7 只有 1 个用例，Task 10 只有手工。L22 自行加了一条"例外仅限 Task 6/7/8/9"，属于计划单方面豁免项目级不变量。
- 修法：
  1. Self-Review 增加一张"对 DECISIONS 的偏差"表：一条写清 D5.3（接口形状变更，依据 D4 与 spec:151-164 的接口/枚举定义）、一条写清 D5.4（Task 6/7/8/9 的 TDD 例外，依据 spec 测试策略"单元测试（无需真实 Win32）"）。
  2. Task 8、Task 9 的 `git commit` 用 `-m` 追加第二段，把上述偏差写进提交说明（满足 `DECISIONS.md:4`）。
  3. Task 6 至少补一个能跑的测试，把 L1139 的论证收窄：我在本机用 xunit **专用 STA 线程**跑通了计划原样的 `HotkeyMessageWindow`——`EnsureCreated()==true`、`Handle != IntPtr.Zero`、二次 `EnsureCreated()` 幂等、`Dispose()` 可重复调用，1 passed。前提是主工程加 `<InternalsVisibleTo Include="BashCommandManager.Tests" />`（该类目前是 `internal`）。"xunit 没有消息泵"只否定"收消息"，不否定"建窗/销毁"。采纳后 L1115/L1497/L2356 的计数需 +1。

## 建议改进

### 风险点（照计划执行很可能踩到）

1. 【风险·路径错】L2373 的 Release 产物路径漏了 `win-x64`。实测因主 csproj 有 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`，exe 在 `bin\Release\net8.0-windows10.0.19041.0\win-x64\BashCommandManager.exe`，不在计划写的 `bin\Release\net8.0-windows10.0.19041.0\` 下。手工验收会找不到文件；建议改正（`dotnet run` 那条不受影响）。
2. 【风险·红步骤信息不稳】L179 期望失败信息是 `CS0246 ... Xunit`。实测：干净树（没有 `tests/obj`）确实是 CS0246；但只要 IDE 后台还原或先跑过一次 test，`tests/**/obj/*.AssemblyInfo.cs` 已被 glob 进来，会先报 `CS0579: 重复的 "System.Reflection.AssemblyCompanyAttribute" 特性`。建议把期望写成"CS0246（干净树）或 CS0579（已有 tests/obj）均可，关键判据是报错文件位于 `tests\` 下"。
3. 【风险·Resume 失败静默】L2245 的 `finally { _hotkeyService.Resume(); }` 丢掉了返回值。若捕获期间原组合被别的程序抢走，用户看到"已捕获新键"，但旧键已失效且没有任何提示。建议改成 `var resume = _hotkeyService.Resume(); if (!resume.IsSuccess) { Growl.Warning(...原快捷键恢复失败...); }`。
4. 【风险·错误文案误报】L2265/L2269 把所有 `UpdateHotkey` 失败都写成"可能已被其他程序占用"。`InvalidCombination`（只按了修饰键 → `Key.None`；或 B2 的 `Key.System`）会被误报成"被占用"，用户按提示去关别的程序永远修不好。建议 `switch (result.Status)` 分三档文案。
5. 【风险·状态残留】Task 9 的 `Save` 成功路径（L2286–2291）没有复位 `HasError`/`ErrorMessage`。`SettingsViewModel` 由根容器解析（实际是单例，计划 L2496 自己也指出），一次冲突失败后即使之后保存成功，下次打开设置页仍会显示旧红字。建议成功分支在 `window?.Close()` 前加 `HasError = false; ErrorMessage = string.Empty;`。

### 其它改进

6. **补 spec 手工步骤的两处断言**（L2382–2383）：spec:259 手工 6 还要求"**重启后**新组合生效"，计划手工 7 只查了 `settings.json`，没有重启验证；spec:261 手工 8 还要求"**取消后**原热键恢复"，计划手工 6 未验。建议各补一行（顺带把手工 8 的"`settings.json` 修改时间未变"写成可执行命令，如 `(Get-Item "$env:LOCALAPPDATA\BashCommandManager\settings.json").LastWriteTime`）。
7. **`Select-String` 的"退出码 1 表示没有匹配"不成立**（L2134）：PowerShell 的 `Select-String` 无匹配时只输出空、不改 `$LASTEXITCODE`。建议改为"无输出即通过"；L2367–2369 的期望值本身我已实测成立（第一条 0 命中，第二条恰好 2 命中）。
8. **目录不存在需显式声明**：`Infrastructure/Interop/`、`tests/BashCommandManager.Tests/Fakes/` 当前都不存在（仓库现状：`Infrastructure/` 下只有 `Controls/`、`Converters/`、`DatabaseInitializer.cs`）。写文件会自动建目录，但计划至少应在 Task 1/Task 7 的 Files 里标一句"新建目录"，否则部分执行器会直接对 `git add` 报 `pathspec ... did not match`。
9. **编码检查（可选，D5.6）**：本次扫描仓库 `*.cs`/`*.xaml`，未发现真实乱码（`SettingsWindow.xaml`、`HotkeyCaptureWindow.xaml` 等均为合法 UTF-8）；但 Task 8/9 是"整体替换"式修改，建议各加一条 `git diff` 目视检查中文文案，防止复制粘贴把 UTF-8 写坏（D5.6 只要求新代码 UTF-8，不做额外约束）。
10. **连发（可选）**：`HotkeyNativeMethods` 未加 `MOD_NOREPEAT`（L996–1021），按住热键会连续切换。spec:54 把"连发抑制"列为非目标，因此不算违规；若想顺手消除，在 `ToWin32Modifiers` 里恒加 `0x4000` 即可，但要同步改 `HotkeyNativeMethodsTests` 的 9 条期望值（并说明这是对 spec 测试策略的偏离）。
11. **Task 10 Step 3 的"恰好 2 处"建议写清构成**（L2369）：计划已有 L2455 的口径说明，但 Step 3 本身只写"2 处"。建议直接写成"2 = `App.xaml.cs`（OnExit 生命周期注销）+ `ViewModels/SettingsViewModel.cs`（未启用分支的用户意图注销）"，避免执行者误判验收失败。

## 覆盖矩阵

### G1–G8（spec:42–49）

| spec 条目 | 计划覆盖 | 结论 |
|---|---|---|
| G1 托盘隐藏期间热键有效 | Task 6（宿主与窗口无关）+ Task 8（生命周期移出 Loaded/Closing）；手工 2/3/4/5；Grep Task 10 Step 3 | ✅（本机已验证宿主可行） |
| G2 宿主与主窗口彻底解耦 | Task 6 + Task 8；Task 8 Step 7 / Task 10 Step 3 第一条 grep | ✅（实测 0 命中） |
| G3 隐藏时显示 + `Normal` + 前台 + 焦点 | Task 7 `ShowAndActivate`（顺序固定）+ Task 3 决策表；手工 1/2 | ✅（"焦点"依赖 `Activate`，与 spec:191 一致） |
| G4 可见且前台 → 最小化到托盘 | Task 3/4/7；`WindowTogglePolicyTests`、`HotkeyToggleCoordinatorTests`；手工 1 | ✅ |
| G5 改键立即生效、冲突保留旧组合、不污染配置 | Task 9（先注册后落盘）+ Task 8（1409 保留旧注册）；手工 7/8；`HotkeyRegistrationResultTests` | ⚠️ 逻辑覆盖，但 B2 使 Alt 组合路径反向违反（会污染） |
| G6 退出释放注册与消息窗口 | Task 8 `OnExit`（退订→Unregister→Dispose）；grep；手工 10 | ✅ |
| G7 决策纯逻辑可单测、Win32 薄层 | Task 3/4/5 全纯逻辑 + 5 个测试类；Win32 收敛到 `HotkeyNativeMethods`、`ForegroundWindowActivator` | ✅（40 用例实测全绿，无测试创建 Window） |
| G8 不改语义、不新增配置项、不改 settings.json 字段 | Task 9 只改顺序；`HotkeySettings` 仍 3 字段；手工 7/12 | ✅（B2 修复后） |

### 验收标准（spec:266–275，共 9 条）

| # | spec 条目 | 计划 | 结论 |
|---|---|---|---|
| 1 | X 关闭到托盘后热键能显示并激活 | Task 6/8；手工 2 | ✅ |
| 2 | 最小化/托盘恢复/反复切换后始终有效 | Task 6/8；手工 3/4/5 | ✅ |
| 3 | `MainWindow.xaml.cs` 不再出现相关符号 | Task 8 Step 3/7；Task 10 Step 3 | ✅（实测 0 命中） |
| 4 | 全进程只有 `App.OnExit` 一处注销、退出无残留 | Task 8；Task 10 Step 3 第二条（期望 2 处+口径说明 L2455） | ✅（实测恰好 2 处，口径成立） |
| 5 | 隐藏→显示/Normal/前台/焦点；前台→托盘 | Task 7；手工 1/2/11 | ✅ |
| 6 | 运行时改键立即生效；占用时提示+保留旧+不污染 | Task 9；手工 7/8 | ⚠️ B2 修复后才成立 |
| 7 | `WindowTogglePolicy`/`HotkeyToggleCoordinator` 单测全绿且不依赖 Win32/WPF 窗口 | Task 3/4；`dotnet test` | ✅（实测 10+7 用例全绿） |
| 8 | 捕获期间当前组合可被捕获、关闭后热键恢复 | Task 9 `Suspend`/`finally Resume`；手工 6 | ⚠️ 缺"取消后恢复"断言（建议 6）+ B2 |
| 9 | 启动注册失败只提示一次、其余功能正常；类名路径一致无占位符 | Task 8（OnStartup 单次 Growl）；Self-Review §4 | ✅ |

### 单元测试策略（spec:240–246）

| spec 测试点 | 计划 | 结论 |
|---|---|---|
| 隐藏+已最小化 → `ShowAndActivate` | L373 | ✅ |
| 可见+`IsMinimized` → `ShowAndActivate` | L375 | ✅ |
| 可见+非最小化+`IsActive` → `HideToTray` | L376 | ✅ |
| 可见+非最小化+非激活 → `ActivateExisting` | L377 | ✅ |
| 模态 × 可见/**隐藏** × 激活/非激活 → 一律 `ActivateExisting` | L390–404 | ❌ 缺"隐藏"（B3） |
| 同一快照两次调用返回相同动作（无状态） | L406–417 | ✅ |
| 隐藏快照只调 `ShowAndActivate`、不调 `HideToTray` | L601–610 | ✅ |
| 前台快照只调 `HideToTray` | L612–621 | ✅ |
| `Execute()` 返回值与实际调用一致 | L623–632、L669–676 | ✅ |
| 假控制器抛异常时 `Execute()` 不抛 | L645–667（动作+捕获两处） | ✅ |
| 两次 `Execute()` 动作随状态变化（不缓存） | L669–676 | ✅ |
| `Clone()` 隔离、`IsValid(None)==false` | L256–300 | ✅ |

### 手工步骤（spec:254–264，共 11 条）

| spec 手工 | 计划 | 结论 |
|---|---|---|
| 1 可见时切换 | 手工 1（L2377） | ✅ |
| 2 点 X 后按热键（核心回归） | 手工 2（L2378） | ✅ |
| 3 最小化按钮路径 | 手工 3（L2379） | ✅ |
| 4 托盘恢复后仍有效 | 手工 4（L2380） | ✅ |
| 5 连续 10 次 | 手工 5（L2381） | ✅ |
| 6 改键立即生效 + 旧键无反应 + **重启后生效** | 手工 7（L2383） | ⚠️ 缺"重启后"（建议 6） |
| 7 组合被占用 → 提示/旧键有效/文件未改 | 手工 8（L2384） | ✅ |
| 8 捕获当前组合 + **取消后恢复** | 手工 6（L2382） | ⚠️ 缺取消恢复；且受 B2 阻断 |
| 9 设置窗口开着按热键（模态分支） | 手工 9（L2385） | ✅（`MainViewModel.cs:141-145` 确实把 `Owner` 设为 `MainWindow`，`OwnedWindows` 判定成立） |
| 10 退出后重启立即注册 | 手工 10（L2386） | ✅ |
| 11 前台普通/全屏应用 | 手工 11（L2387） | ✅ |
| （附加）关闭启用开关 → 无反应 → 重新启用恢复 | 手工 12（L2388） | ✅ |

## 已核对无误的项

### 路径与行号

- **Create 路径（10 个生产文件 + 10 个测试文件）**：`Core/Models`、`Core/Services`、`Infrastructure` 均存在；`Infrastructure/Interop/`、`tests/BashCommandManager.Tests/`、`.../Fakes/` 当前不存在，但都是计划里明确新建的目录树（见建议 8，建议显式写"新建目录"）。所有路径与仓库命名约定一致（`Core/`、`Infrastructure/`、`ViewModels/`、`Views/` 均为现有布局）。
- **Modify 文件（8 个）全部存在**：`BashCommandManager.csproj`、`Core/Services/GlobalHotkeyService.cs`（现含 `IGlobalHotkeyService`）、`Core/Models/HotkeySettings.cs`、`MainWindow.xaml.cs`、`App.xaml.cs`、`ViewModels/SettingsViewModel.cs`、`README.md`、`CLAUDE.md`。
- **行号核对（0 行偏差）**：计划 L1748"原第 16–17 行"= 现状 `_hotkeyService`/`_settingsService` 字段（16–17）；L1761"原第 24–29 行"= 服务获取+订阅（24–29）；L1781"原第 46 行"= `Closing += MainWindow_Closing;`（46）；"原第 49–103 行"= `RegisterHotkey`…`ToggleWindowVisibility`（49–103）；`Loaded` 里的 `RegisterHotkey();` 确在 39 行。全部精确吻合（远优于 ±10 行容差）。
- `README.md:61-64` 的「### ⌨️ 全局快捷键」确实是 3 条 bullet，计划的"追加 1 条"成立；`CLAUDE.md` 的「### 系统托盘」（136）与「## 开发计划」（142）位置也与计划描述的插入点一致。

### 可编译性与接口一致性（实测）

- 按计划原文落地后 `dotnet build -c Release`：**0 warning / 0 error**。因此下列跨任务契约全部成立：`IGlobalHotkeyService`（`TryRegister/UpdateHotkey/Suspend/Resume/Unregister/IsRegistered/HotkeyPressed`，含 `Resume` 返回 `HotkeyRegistrationResult` 的偏差）在 `App`、`SettingsViewModel`、`GlobalHotkeyService` 中的用法一致；`HotkeyRegistrationResult` 的 5 个静态工厂与 `Status` 齐全；`HotkeyNativeMethods` 的 `WmHotkey/Mod*/RegisterHotKey/UnregisterHotKey/ToWin32Modifiers/ToVirtualKey` 与 Task 6/8 的调用一致；`HotkeyIdAllocator.Next()` 回绕正确；`WindowActivitySnapshot`/`WindowToggleAction`/`IWindowTogglePolicy`/`IWindowVisibilityController`/`HotkeyToggleCoordinator` 在 Task 3/4/7 之间无错配；`MainWindowVisibilityController(Window, IForegroundWindowActivator)` 与 `App.OnStartup` 的构造一致。
- WPF/Win32 命名空间齐全：`System.Windows.Interop`（`HwndSource`/`WindowInteropHelper`）、`System.Windows.Input`（`Key`/`ModifierKeys`/`KeyInterop`）、`System.Windows`（`Window`/`WindowState`/`Application`）、`System.Windows.Threading`（`Dispatcher`）均已在对应文件 `using`；`App.xaml.cs` 新增 `HandyControl.Controls`/`HandyControl.Data` 后与 `System.Windows` 无类型歧义（实测编译通过）。
- `HotkeyMessageWindow` 的 `internal` 可见性、`HotkeyIdReceived` 的 `EventHandler<int>` 签名、`_hookAdded` 幂等建窗、`Dispose` 重复调用均正确；Task 6 对 spec 原代码"每次 `AddHook` 会重复挂钩"的修正是必要且正确的（spec:118 的写法确实会重复挂）。

### 命令可执行性

- 仓库根目录只有 `BashCommandManager.csproj`，无 `.sln`；`dotnet build BashCommandManager.csproj -c Release|Debug`、`dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`、`dotnet run` 全部成立（已在副本实测）。
- 当前分支 `feat/v1.3.0`（D1 要求）；评审开始前 `git status` 只有 `docs/superpowers/plans/` 未跟踪（spec 与 DECISIONS 已提交），本次评审新增了本报告文件，未触碰其它路径。计划的 `git add` 全部用了确切路径、未用 `-A`，符合 Global Constraints L24。
- 测试包版本 xunit 2.5.3 / xunit.runner.visualstudio 2.5.3 / Microsoft.NET.Test.Sdk 17.8.0 在本机 `%USERPROFILE%\.nuget\packages` 均存在（离线可还原，实测 restore 392ms）。
- 测试工程不加 `RuntimeIdentifiers` 的写法正确：与带 `win-x64` 的主工程 `ProjectReference` 组合未触发 `NETSDK1047`（实测）。
- `DefaultItemExcludes` 机制本身有效：加/去该属性可以稳定复现"主工程吞 tests"与"主工程干净"两种状态；`tests/**/obj` 不再污染主程序集（Task 1 Step 7 的验证口径成立）。
- 计划里的两条 `Select-String` 验收命令在 Windows PowerShell 下可执行且结果与期望一致：第一条 `MainWindow.xaml.cs` 残留 `0` 命中；第二条 `.Unregister()` 恰好 `2` 处，分别落在 `App.xaml.cs`（`OnExit`）与 `ViewModels/SettingsViewModel.cs`（未启用分支），与计划 L2369 的期望值一致。

### 与 spec / DECISIONS 的一致性

- **D2**：测试项目唯一路径 `tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`、TFM `net8.0-windows10.0.19041.0`、`UseWPF=true`、`IsTestProject=true`、三个包版本、`ProjectReference ..\..\BashCommandManager.csproj` 全部与 `DECISIONS.md:19-31` 一致（唯一缺口是 CLI 排除项，见 B1）。
- **D3**：计划未改 `settings.json` 形状——`Core/Services/SettingsService.cs:41-47` 仍直接序列化 `HotkeySettings` 三字段（`Enabled/Key/Modifiers`），计划只改了"何时写"，符合 D3"若 #8 先落地…必须不改 JSON 形状"。
- **D3/A7 相关判断正确**：计划 L80 声称 `SaveHotkeySettings` 按引用保存、因此要传 `Clone()`——`SettingsService.cs:43`（`_hotkeySettings = settings;`）证实该判断成立，`SaveHotkeySettings(HotkeySettings.Clone())` 是必要修正。
- **D4**：`IGlobalHotkeyService` 归属 #8、`TryRegister/UpdateHotkey/Suspend/Resume/Unregister/IsRegistered` + `HotkeyPressed`、宿主为 `HotkeyMessageWindow`，与 `DECISIONS.md:74` 一致；`ISettingsService` 未被改动（D4 说它归 #9 扩展）。
- **D5.1/D5.2/D5.5/D5.6/D5.7**：未新增业务 NuGet；未触碰命令执行/数据库/`App.xaml` 资源/托盘菜单；主工程构建只编主项目（实测）；新增 `public` 类型基本都有中文注释。D5.3/D5.4 的偏差见 B4。
- **spec 的 3 处偏差说明（L76–80）全部成立**：多 `HotkeyNativeMethods`/`HotkeyIdAllocator` 提升了 G7 可测性；`Resume` 返回结果才能表达 `NotRegistered`；`Clone()` 消除别名。这三处不影响 G1–G8。

### 技术前提（实测）

- **message-only `HwndSource` 可行**：`new HwndSource(new HwndSourceParameters("..."){ WindowStyle=0, ParentWindow=(IntPtr)(-3), Width=0, Height=0 })` 建窗成功并返回非 0 HWND；`RegisterHotKey` 用该 HWND 注册成功；`WM_HOTKEY` 经 `AddHook` 钩子把 `wParam`（热键 id）正确转发出来；`UnregisterHotKey` 在窗口存活期间成功；`Dispose` 幂等。这直接支撑 A5 降级路径与 G1 的核心假设。
- **1409 语义确认**：同一进程内重复注册同一组合，第二次返回 `false` 且 `Marshal.GetLastWin32Error()==1409`，与 `HotkeyRegistrationResult.FromRegisterError` 的映射一致；`UpdateHotkey` 的"同组合短路"确有必要。
- **`RegisterHotKey` 的线程亲和**：注册/注销必须在建窗线程调用；计划在 `TryRegister`/`Suspend`/`Resume`/`Unregister` 里用 `_dispatcher.VerifyAccess()` 守住，`App`/`SettingsViewModel` 的调用点都在 UI 线程，正确。
- **`MainViewModel.cs:141-145`** 确实以 `Owner = Application.Current.MainWindow` 打开设置窗口，因此 `MainWindowVisibilityController.Capture()` 的 `OwnedWindows` 模态判定在"设置窗口开着按热键"的场景下成立（手工 9 可验证）。
- **Growl 可用**：HandyControl 3.5.1 程序集含 `GrowlWindow`/`GrowlPanel` 类型，`Growl.Warning` 不依赖 `MainWindow` 里必须有 `GrowlPanel`（现状 XAML 确实没有），故"启动注册失败提示一次"的验收不会因缺少面板而崩。

---

## 修订记录（作者：修订代理）

修订日期：2026-11-08
修订范围：**只改** `docs/superpowers/plans/2026-11-08-hotkey-tray.md` 与本报告末尾本节；未改任何源码，未执行 `git commit`。
基线：本报告评审的 2502 行版本 → 修订后 2827 行（新增 Section「新建目录」、Task 9 的 4 个新步骤、Self-Review §7、Task 10 手工表 3 行）。

### 一、阻断项 B1–B4

| 编号 | 计划中实际改动的小节/位置 | 具体改动 | 自检 |
|---|---|---|---|
| **B1** | Global Constraints 第 2 条；File Structure「修改：生产代码与工程文件」表的 `BashCommandManager.csproj` 行；Task 1 Step 4（正文 + 插入 XML + 插入后 PropertyGroup 全块） | 两处 `DefaultItemExcludes` 均改为 `$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**`；补「CLI 目录由 #10 创建，这里先预留排除项，避免 #10 再改主 csproj」与「不需要也不允许再叠加 `<Compile Remove>`」；Task 1 Step 4 标题改为「排除 tests 与预留的 CLI 目录」 | `grep DefaultItemExcludes>` 3 处（约束 L16、Step 4 的 L203/L217）全部含 `BashCommandManager.Cli` |
| **B2** | 新增 Global Constraints 的「Alt 组合的捕获必须先归一化」条目；File Structure：生产文件 10→11、测试文件 10→11、修改文件 8→9，新增 `Core/Services/HotkeyKeyNormalizer.cs`、`tests/.../HotkeyKeyNormalizerTests.cs`、`Views/HotkeyCaptureWindow.xaml.cs` 三行，并把「与 spec 组件清单偏差」从 3 处改为 4 处（新增第 4 条）；Task 9 整体重写为 Step 1–10；Task 10 Step 2（42）、Step 4（手工第 6 行细化 + 第 13/14/15 行新增 + `settings.json` 核对命令）；Self-Review §1（G5/G7/A8）、§2、§3、§4、§5、§6 | 归一化函数签名按评审建议的命名落地为 `HotkeyKeyNormalizer.Normalize(Key key, Key systemKey)`（放 `Core/Services/`）；给出 `HotkeyKeyNormalizerTests` 完整代码 2 个用例（`Key.System + Key.N → Key.N`、普通键原样返回）；给出 `HotkeyCaptureWindow.xaml.cs` 全文（`Window_KeyDown` 先归一化，**并顺带修正了 Alt 组合下 `Key.LeftAlt`/`Key.RightAlt` 拦截失效**）；Task 9 Step 5 末尾补完整故障链说明（`Key:156` → vk 0 → 返回 true → 注销旧键 → 污染 json）；手工第 13 行为「捕获 Alt+F1 组合 → 保存 → 生效；取消捕获 → 原热键恢复」 | `HotkeyCaptureWindow` 18 处（≥3）；`HotkeyKeyNormalizer` 33 处，定义在 Task 9 Step 3、测试在 Task 9 Step 1；用例计数 40 → **42**（40 + 2），Task 9 Step 8 与 Task 10 Step 2 同步 |
| **B3** | Task 3 Step 1 的 `Decide_never_hides_when_an_app_modal_window_is_open`；Task 3 Step 4 的期望说明 | 形参改为 `(bool isVisible, bool isMinimized, bool isActive)`；4 条 `InlineData` 改为 `(true,false,true)`、`(true,false,false)`、`(false,true,true)`、`(false,true,false)`（`isAppModalOpen` 恒为 `true`）；注释与取值一致，不再出现「注释写隐藏、代码写可见」的矛盾 | Theory 仍 4 条，Task 3「通过: 10」、Task 5「39」、Task 7「40」均不变（已核对） |
| **B4** | Self-Review 新增 §7「与 DECISIONS 的偏差登记（B4）」；Global Constraints 的 TDD 例外条目（收窄清单）；Task 6/7/8/9 的 commit 步骤；Task 6、Task 7、Task 8 的「TDD 例外说明」段落 | §7 用表格逐条登记 D5.3（删除 `Register(Window,…)`、`UpdateHotkey` 返回 `bool → HotkeyRegistrationResult`：理由、3 个调用方、影响面）与 D5.4（Task 6 宿主、Task 7 `MainWindowVisibilityController`、Task 8 服务整体、Task 9 UI 接线的豁免：理由 + 替代验证 + 手工步骤编号）；Task 6/7/8/9 的 `git commit` 各加第二段 `-m`（`DECISIONS-D5.3:` / `DECISIONS-D5.4:`） | 四处提交块均含第二段 `-m`；§7 的留痕位置列与之一一对应 |

### 二、建议项采纳情况

| 本报告建议 | 落点 | 说明 |
|---|---|---|
| 风险 1：Release 产物路径漏 `win-x64` | Task 10 Step 4 的 Run 行 | 改为 `bin\Release\net8.0-windows10.0.19041.0\win-x64\BashCommandManager.exe`，并说明因主 csproj 带 `RuntimeIdentifiers` |
| 风险 2：红步骤信息不稳 | Task 1 Step 3 | 补「`CS0246`（干净树）或 `CS0579`（已有 `tests/obj`）都算红」，判据统一为「报错文件路径在 `tests\` 下」 |
| 风险 3：`Resume()` 失败静默 | Task 9 Step 6 的 `finally`；Global Constraints 的 TDD/A8 说明；Self-Review §1 A8 | 改为 `var resume = _hotkeyService.Resume();`：非 Success 一律 `Debug.WriteLine`；`Conflict`/`HostUnavailable` 额外 `ErrorMessage` + `Growl.Warning("原快捷键恢复失败，可能已被其他程序占用（保存时会重新注册）")`；`NotRegistered`（本来就没启用热键）只记日志不打扰 |
| 风险 4：错误文案误报 | Task 9 Step 6 的 `Save`；Global Constraints 的「用户可见文案」条目；Self-Review §5 | 分档：`Conflict` 逐字用 spec:217 原句；`InvalidCombination`/`HostUnavailable` 用「快捷键无效或系统不可用（已保留原快捷键）」；`_` 兜底「快捷键更新失败（已保留原快捷键）」。同时补 `SetHotkey` 里 `key == Key.None` 的前置拦截（评审 B2 修法 4 的兜底） |
| 风险 5：`HasError` 状态残留 | Task 9 Step 6 的 `Save` 成功路径；Self-Review §6 第 5 条 | `window?.Close()` 之前 `HasError = false; ErrorMessage = string.Empty;`，并说明该 VM 实际是单例 |
| 建议 6：手工补「重启后生效」「取消后恢复」 | Task 10 Step 4 第 14、15 行；并在表后补 `settings.json` 的 `Get-Item`/`Get-Content` 核对命令 | 两条各占一行，编号接在原 12 条之后（原 1–12 编号不动，Self-Review 里「手工 6/7/8」的引用继续有效） |
| 建议 7：`Select-String` 的退出码判据不成立 | Task 10 Step 3（整块重写为 `if + throw`）；Task 8 Step 7（同款问题一并修正） | 两处都改为 `@(Select-String ...)` + 统计 `Count` + `throw` 并打印 `路径:行号`，明确写「不要依赖 `$LASTEXITCODE`」 |
| 建议 8：目录需显式声明 | File Structure 新增「新建目录（2 个，写文件时自动创建；`git add` 前必须已存在）」表；Task 1/4/7 的 Files | `Infrastructure/Interop/`、`tests/.../Fakes/`、`tests/BashCommandManager.Tests/` 均标注「目录，本任务首次创建」 |
| 建议 11：Task 10 Step 3「恰好 2 处」写清构成 | Task 10 Step 3 的 Expected | 明确「`App.xaml.cs`（`OnExit` 生命周期注销）+ `ViewModels/SettingsViewModel.cs`（未启用分支的用户意图注销）」，并补「少一处说明用户意图注销被漏掉」 |

### 三、未采纳的建议与理由

| 本报告建议 | 未采纳理由 |
|---|---|
| B4 修法第 3 条：给 Task 6 补 STA 建窗测试 + `<InternalsVisibleTo Include="BashCommandManager.Tests" />`，并把计数 +1 | 本轮按 B4 的「登记豁免」路线修（D5.4 允许把偏离写进提交说明），不扩大测试面。采纳它会改动主 csproj（新增 `InternalsVisibleTo`）、把用例数从 42 变 43，并需同步 Task 6/7/9/10 四处计数。计划 Self-Review §7 末尾已把它标为「执行时可选：采纳则 +1 并同步四处」，由执行者决定，不阻塞本轮放行。 |
| 建议 9：Task 8/9 各加一条 `git diff` 目视检查中文文案 | 属可选项（D5.6 只要求新代码 UTF-8，本次扫描也未发现乱码）。Task 9 Step 9 的 6 条人工复核已覆盖捕获窗口与 `SettingsViewModel` 的关键行；再加一条「目视 diff」容易被执行者当成硬门槛却不改变可验证性。 |
| 建议 10：给 `ToWin32Modifiers` 加 `MOD_NOREPEAT` 抑制连发 | spec:54 把「连发抑制」列为非目标；采纳要同步改 `HotkeyNativeMethodsTests` 的 9 条期望值，并登记「对 spec 测试策略的偏离」，收益与破坏面不匹配，本轮不做。 |

### 四、本轮自检结果（全部通过）

1. 占位符：`Select-String -Pattern 'TBD|TODO|待补充|类似上文'` → **0 命中**（原 §4 中用于「声明没有占位」的字面举例已改写为描述性文字，避免自检误报；该改写已在计划 §4 里说明）。
2. `DefaultItemExcludes`：全文 8 处提及；其中带 `>` 的 3 处（Global Constraints、Task 1 Step 4 的插入行与 PropertyGroup 块）**全部包含 `BashCommandManager.Cli`**。
3. `HotkeyCaptureWindow`：**18 处**（要求 ≥3）。
4. `HotkeyKeyNormalizer`：**33 处**；**定义**见 Task 9 Step 3 的 `Core/Services/HotkeyKeyNormalizer.cs` 全文，**测试**见 Task 9 Step 1 的 `HotkeyKeyNormalizerTests.cs` 全文（2 个用例）。
5. 结构：文件 2827 行；代码围栏 106 个（偶数，53 个代码块成对）；各任务 Step 编号连续：Task 1 `1–8`、Task 2 `1–5`、Task 3 `1–5`、Task 4 `1–8`、Task 5 `1–14`、Task 6 `1–4`、Task 7 `1–7`、Task 8 `1–9`、Task 9 `1–10`、Task 10 `1–7`。
6. 计数一致性：Task 3 `10`、Task 5 `39`、Task 7 `40`、Task 9 `42`、Task 10 `42`、Self-Review §7「42」六处互相自洽。
7. 未改源码：本轮只编辑计划与本报告两个 Markdown 文件，未运行 `git add`/`git commit`。

**给二次评审的定位建议：** 本轮唯一涉及「新行为」的是 Task 9 Step 5（捕获窗口全文）与 Step 6（`Save` 的 `switch` 分档、`finally` 里的 `Resume` 处理）、Task 10 Step 4 第 13–15 行、Self-Review §7；其余改动是文字、编号、断言写法与目录声明。
