# 全局快捷键在托盘隐藏期间失效（Issue #8）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把全局热键的注册/注销从主窗口的 `Loaded`/`Closing` 生命周期里彻底移出，改由进程级的 message-only 宿主承载，使托盘隐藏（点 X、最小化按钮或任何 `Hide()`）期间热键始终有效，并把「快照 → 决策 → 执行」抽成可单测的纯逻辑。

**Architecture:** `App` 在 `OnStartup` 用 `IGlobalHotkeyService.TryRegister` 在 message-only `HwndSource`（`Parent = HWND_MESSAGE`）上注册热键，`OnExit` 是唯一的生命周期注销点；`WM_HOTKEY` 由 `HotkeyMessageWindow` 转发给服务，服务校验 id 后触发 `HotkeyPressed`，`App` 同步调用 `HotkeyToggleCoordinator.Execute()` 完成「取快照 → `WindowTogglePolicy.Decide` → `IWindowVisibilityController` 动作」，Win32 只出现在 `ForegroundWindowActivator` 与 `HotkeyNativeMethods` 两个薄层里。

**Tech Stack:** .NET 8 (`net8.0-windows10.0.19041.0`) + WPF + HandyControl 3.5.1 + CommunityToolkit.Mvvm 8.2.2 + Microsoft.Extensions.DependencyInjection 8.0.0；测试用 xunit 2.5.3 + Microsoft.NET.Test.Sdk 17.8.0 + xunit.runner.visualstudio 2.5.3（均取自本机 NuGet 缓存，可离线还原）。SDK 为 dotnet 10.0.301。

**Spec:** `docs/superpowers/specs/2026-11-08-hotkey-tray-design.md`（唯一权威需求来源；本计划中所有「G1–G8」「验收标准」「手工步骤」编号均指该文件）

## Global Constraints

- 目标框架：主工程与测试工程都必须是 `net8.0-windows10.0.19041.0`；测试工程必须 `UseWPF=true`（`HotkeySettings` 依赖 `System.Windows.Input.Key`），`ProjectReference` 指向 `..\..\BashCommandManager.csproj`。
- 主工程是 `WinExe` 且用默认 glob：`tests\**` 与**预留的** `BashCommandManager.Cli\**`（由 #10 创建）都会被编进主程序集，必须先加 `<DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>` 再建测试工程。这是 `DECISIONS.md` D2 的明文要求（同时覆盖 `Compile`/`None`/`Page`），CLI 目录虽然当前不存在，也必须一次写全，避免 #10 再回头改主 csproj。
- 不新增业务 NuGet 包；测试包仅 xunit 2.5.3、xunit.runner.visualstudio 2.5.3、Microsoft.NET.Test.Sdk 17.8.0（本机 `%USERPROFILE%\.nuget\packages` 已存在这些版本）。
- 不改 `settings.json` 字段形状、不改数据库 schema、不改 `App.xaml` 资源、不改托盘菜单与图标、不新增配置项（G8）。
- 命名空间约定：`Core/Models` → `BashCommandManager.Core.Models`；`Core/Services` → `BashCommandManager.Core.Services`；WPF/Win32 适配 → `BashCommandManager.Infrastructure` / `BashCommandManager.Infrastructure.Interop`；测试 → `BashCommandManager.Tests` / `BashCommandManager.Tests.Fakes`。
- 标识符用英文，用户可见文案与代码注释用简体中文；用户可见文案必须与 spec 逐字一致（例如 `"全局快捷键注册失败，可能已被其他程序占用"`）。改键失败按 `HotkeyRegistrationStatus` 分两档：`Conflict` 逐字使用 spec:217 的 `"快捷键更新失败，可能已被其他程序占用（已保留原快捷键）"`；`InvalidCombination`/`HostUnavailable` 使用 `"快捷键无效或系统不可用（已保留原快捷键）"`（这一分档是对 spec:217 单句文案的有意细化，理由与影响见 Self-Review §5、§7）。
- 生命周期注销只有一处：`App.OnExit`。`SettingsViewModel.Save` 在用户关闭「启用全局快捷键」时调用 `Unregister()` 是 spec:217 明确要求的用户意图注销，不算第二处生命周期注销（见 Self-Review）。
- Alt 组合的捕获必须先归一化：WPF 在 Alt 按下时把 `KeyEventArgs.Key` 置为 `Key.System`，真实键在 `SystemKey`（`KeyInterop.VirtualKeyFromKey(Key.System) == 0`）。`Views/HotkeyCaptureWindow.xaml.cs` 必须调用 `HotkeyKeyNormalizer.Normalize(e.Key, e.SystemKey)`；否则 `RegisterHotKey(vk:0)` 会返回 `true`、注销掉旧热键，并把 `Key:156` 写进 `settings.json`，热键永久失效（B2，本计划最严重的功能缺陷）。
- 每个实现步骤之前必须先有一个失败的测试步骤并亲眼看到它失败（TDD Iron Law）。例外仅限「无法在 xunit 进程内建立 Win32 消息泵 / WPF 窗口 / HandyControl `Growl`」的薄层：Task 6 的宿主、Task 7 的 `MainWindowVisibilityController`、Task 8 的服务整体（调用顺序与状态机）、Task 9 的 UI 接线（键位归一化仍必须先写失败测试）。这些任务必须在自己开头写明理由、给出手工验证项，并在 Self-Review §7「与 DECISIONS 的偏差登记」逐条留痕（DECISIONS D5.4 要求偏离必须记录在提交说明里，Task 6/7/8/9 的 commit 因此都带第二段 `-m`）。
- 验证命令优先级：优先 `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~<类名>"`（会增量构建主工程，快）；`dotnet build BashCommandManager.csproj -c Release` 全量约 2.5 分钟，只在 Task 1 与 Task 10 各跑一次。
- 提交只 `git add` 步骤里列出的确切路径。工作区里存在与本次任务无关的改动（`logs/`、`Packaging/` 等），禁止 `git add -A` / `git add .`。
- 每个任务末尾的提交信息必须是下面两个主题之一的前缀，使 `git log` 能按 spec 建议的两个主题聚合：
  - `feat(hotkey): 解耦热键宿主 — …`（Task 6、7、8、9）
  - `feat(hotkey): 策略分层与测试 — …`（Task 1、2、3、4、5、10）

---

## File Structure

### 新建：生产代码（11 个）

| 文件 | 职责（一行） |
|---|---|
| `Core/Models/HotkeyRegistrationResult.cs` | `HotkeyRegistrationStatus` 枚举、`HotkeyRegistrationResult` 记录结构、`1409 → Conflict` 错误码映射 |
| `Core/Services/IGlobalHotkeyService.cs` | 热键服务契约：`TryRegister`/`UpdateHotkey`/`Suspend`/`Resume`/`Unregister`/`IsRegistered`/`HotkeyPressed` |
| `Core/Services/HotkeyNativeMethods.cs` | `user32` 的 `RegisterHotKey`/`UnregisterHotKey` P/Invoke、`WM_HOTKEY` 与 `MOD_*` 常量、`ModifierKeys`→Win32 标志、`Key`→虚拟键 |
| `Core/Services/HotkeyKeyNormalizer.cs` | 捕获键归一化纯函数：`Key.System` + `SystemKey` → 真实键，其余原样返回（修 B2 的 `Key.System`→VK 0 缺陷） |
| `Core/Services/HotkeyIdAllocator.cs` | 在 `0x9000–0x90FF` 区间分配热键 id 并回绕 |
| `Core/Services/HotkeyMessageWindow.cs` | message-only `HwndSource` 宿主：建窗、挂钩、转发 `WM_HOTKEY` 的 id、销毁 |
| `Core/Services/WindowTogglePolicy.cs` | `WindowActivitySnapshot`/`WindowToggleAction`/`IWindowTogglePolicy` + 无状态决策表 |
| `Core/Services/IWindowVisibilityController.cs` | 窗口动作抽象：`Capture`/`ShowAndActivate`/`HideToTray`/`ActivateExisting` |
| `Core/Services/HotkeyToggleCoordinator.cs` | 「取快照 → 决策 → 执行」协调器，捕获并吞掉异常，返回实际执行的动作 |
| `Infrastructure/Interop/ForegroundWindowActivator.cs` | `IForegroundWindowActivator` + `SetForegroundWindow`/`AttachThreadInput` 兜底（含 `try/finally` 解挂） |
| `Infrastructure/MainWindowVisibilityController.cs` | 用 `MainWindow` 实现 `IWindowVisibilityController`（含模态子窗口判定） |

### 修改：生产代码与工程文件（9 个）

| 文件 | 职责（一行） |
|---|---|
| `BashCommandManager.csproj` | 加 `DefaultItemExcludes`，把 `tests\**` 与预留给 #10 的 `BashCommandManager.Cli\**` 从主程序集 glob 里排除（DECISIONS D2） |
| `Core/Services/GlobalHotkeyService.cs` | 重写：委托 `HotkeyMessageWindow`，先注册后注销、id 序列化、`Suspend/Resume`、`IsRegistered` |
| `Core/Models/HotkeySettings.cs` | 增加 `Clone()`，用于「编辑副本」与「冲突时服务内仍是旧值」 |
| `MainWindow.xaml.cs` | 删除全部热键痕迹；保留 `_isExiting`、`StateChanged`、`OnClosing`、`ShowWindow`、`Exit_Click`、托盘 |
| `App.xaml.cs` | 构造宿主/协调器、`OnStartup` 唯一注册点、`OnExit` 唯一生命周期注销点 |
| `ViewModels/SettingsViewModel.cs` | 编辑副本、先注册后落盘、按状态分档提示、捕获期 `Suspend`/`Resume`（`Resume` 失败不静默） |
| `Views/HotkeyCaptureWindow.xaml.cs` | `Window_KeyDown` 先 `HotkeyKeyNormalizer.Normalize(e.Key, e.SystemKey)` 再判修饰键/显示，避免存下 `Key.System` |
| `README.md` | 功能列表补一句「托盘隐藏期间快捷键仍然有效」 |
| `CLAUDE.md` | 增加「全局快捷键」注意事项：宿主进程级、注销唯一位置 |

### 新建：测试（11 个）

| 文件 | 职责（一行） |
|---|---|
| `tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj` | xunit 工程定义（TFM、`UseWPF`、测试包、`ProjectReference` 主工程） |
| `tests/BashCommandManager.Tests/ProjectReferenceSmokeTests.cs` | 冒烟：测试进程能解析主程序集里的 `HotkeySettings` 与 WPF `Key` 类型 |
| `tests/BashCommandManager.Tests/HotkeySettingsTests.cs` | `Clone()` 值相等/隔离、`IsValid` 无修饰键为 `false` |
| `tests/BashCommandManager.Tests/WindowTogglePolicyTests.cs` | 决策表逐行断言（含模态分支、无状态） |
| `tests/BashCommandManager.Tests/Fakes/FakeWindowVisibilityController.cs` | 记录调用序列、可配置异常、状态随动作变化的假控制器 |
| `tests/BashCommandManager.Tests/HotkeyToggleCoordinatorTests.cs` | 每快照只调一个动作、返回值与实际调用一致、异常不冒泡、不缓存决策 |
| `tests/BashCommandManager.Tests/HotkeyRegistrationResultTests.cs` | `IsSuccess` 语义、`1409 → Conflict`、其它错误码 → `HostUnavailable`、结果携带组合 |
| `tests/BashCommandManager.Tests/HotkeyNativeMethodsTests.cs` | `ModifierKeys` → `MOD_*` 标志逐位断言、`Key.B` → `0x42` |
| `tests/BashCommandManager.Tests/HotkeyIdAllocatorTests.cs` | 起始 `0x9000`、连续递增、`0x90FF` 之后回绕到 `0x9000` |
| `tests/BashCommandManager.Tests/HotkeyKeyNormalizerTests.cs` | `(Key.System, Key.N)` → `Key.N`；普通键原样返回 |
| `tests/BashCommandManager.Tests/ForegroundWindowActivatorTests.cs` | `TryActivate(IntPtr.Zero)` 返回 `false` 且不触碰 Win32 |

### 新建目录（2 个，写文件时自动创建；`git add` 前必须已存在）

| 目录 | 由哪个任务创建 | 内容 |
|---|---|---|
| `Infrastructure/Interop/` | Task 7 Step 3 | `ForegroundWindowActivator.cs`（命名空间 `BashCommandManager.Infrastructure.Interop`） |
| `tests/BashCommandManager.Tests/Fakes/` | Task 4 Step 1 | `FakeWindowVisibilityController.cs`（命名空间 `BashCommandManager.Tests.Fakes`） |

`tests/BashCommandManager.Tests/` 本身由 Task 1 Step 1 创建。这三个目录当前都不在仓库里（`Infrastructure/` 下只有 `Controls/`、`Converters/`、`DatabaseInitializer.cs`），因此 Task 1/4/7 的 Files 里都显式标了「新建目录」，防止 `git add` 报 `pathspec ... did not match`。

### 与 spec 组件清单的 4 处偏差（已在 Self-Review 逐条复核）

1. **多出 2 个纯逻辑文件**：`HotkeyNativeMethods.cs`、`HotkeyIdAllocator.cs`。spec 的 G7 要求「决策（快照 → 动作）是无 Win32/WPF 依赖的纯逻辑，可单测；Win32 走薄适配层」，但 spec 的组件清单把「修饰键换算」和「id 回绕」留在 Win32 层里，这两处是本次唯一带算术的非平凡逻辑（错一位就意味着注册的组合与显示的组合不一致 / 256 次改键后 id 冲突）。把它们拆成纯静态类后可以被单测锁住，`GlobalHotkeyService` 里只留 P/Invoke 调用，符合 G7。
2. **`Resume()` 返回 `HotkeyRegistrationResult` 而不是 `void`**：spec 的接口代码块写 `void Resume()`，但同一段文字要求「`Resume()` 用新 id 重注册（无 `Current` 时返回 `NotRegistered`）」。取返回结果才能表达 `NotRegistered` 并让调用方可观测；`void` 版本会让 `NotRegistered` 变成死枚举值。其余签名与 spec 逐字一致。
3. **`Save` 调用 `SaveHotkeySettings(HotkeySettings.Clone())`**：spec 写 `SaveHotkeySettings(HotkeySettings)`。`SettingsService.SaveHotkeySettings` 会把入参对象**按引用**存为内存配置，若直接传编辑副本，之后用户在设置页改键但点「取消」就会悄悄改掉服务内配置，破坏 A7「只在内存里克隆编辑副本」与 spec:218「冲突时服务内配置仍是旧值」。传克隆体消除了这个别名。
4. **多出 1 个纯函数文件**：`Core/Services/HotkeyKeyNormalizer.cs`。spec 的捕获窗口代码直接取 `e.Key`（现状 `Views/HotkeyCaptureWindow.xaml.cs:48` 就是 `_capturedKey = e.Key;`），而 WPF 在 Alt 组合下给的是 `Key.System`（虚拟键 0），这会把「注册成功却永不触发」的无效应写进 `settings.json`。把归一化抽成纯函数既修掉该功能缺陷（B2），又能用 2 个用例锁住 `Key.System + Key.N → Key.N` 与「普通键原样返回」；捕获窗口里只剩一行调用。

---

## 任务顺序与理由

执行顺序：Task 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10。

| 本计划 | 用户建议的对应项 | 说明 |
|---|---|---|
| T1 | T1 测试基础设施 | 相同；必须先改 csproj 再建测试工程，否则主程序集把测试源码编进去直接编译失败 |
| T2 | T2 `HotkeySettings.Clone()` | 相同；放在最前是因为它是 T1 之后最小的完整 TDD 闭环，用来证明测试工程可用 |
| T3 | T3 `WindowTogglePolicy` | 相同；纯逻辑，无依赖 |
| T4 | T4 `HotkeyToggleCoordinator` | 相同；依赖 T3 |
| T5 | T6 的纯逻辑部分（注册结果/修饰键/id） | **拆分**：把 spec 里混在 Win32 层的两段算术拆成纯类，连同 `HotkeyRegistrationResult` 一起先做，仍属「策略分层与测试」主题 |
| T6 | T5 的宿主部分 | **拆分**：`HotkeyMessageWindow` 无单测（无消息泵），与 T5 的验证方式不同，单独成任务提交更利于 review |
| T7 | T7 适配器 | **前移**到服务重写之前：`App` 接线（T8）要 `new MainWindowVisibilityController(...)` 与 `new ForegroundWindowActivator()` |
| T8 | T6 + T8 的原子部分 | **合并**：`IGlobalHotkeyService` 换签名会让 `MainWindow`、`App`、`SettingsViewModel` 同时编译失败，无法拆成两个可构建的提交；本任务把「服务重写 + 主窗口去热键 + App 唯一注册/注销 + 设置页编译适配」放进一个提交，保证每次提交都能 `dotnet build` 通过 |
| T9 | T8 的语义部分 | **独立**：改键「先注册后落盘 / 捕获期挂起」是 G5/A8 的独立行为，与宿主解耦互不阻塞，可单独 review；捕获键归一化（B2）与它同属「设置页改键」链，放在同一任务，且归一化是纯函数、必须先写失败测试 |
| T10 | T9 全量验证 + 文档 | 相同 |

**为什么不能把 T8 再拆小：** `IGlobalHotkeyService.Remove Register(Window, ...)` + `UpdateHotkey` 改返回类型后，`MainWindow.xaml.cs`（`Register(this, …)`）、`App.xaml.cs`（无）、`SettingsViewModel.cs`（`bool registered = …`）三处同时不编译。C# 没有「只改接口不改调用方还能构建」的中间态，唯一选择是把这三个调用方与接口放进同一个提交；若强行拆分，会留下一个 `dotnet build` 失败的提交，违反「每个任务都能独立验证」。

---

## Task 1: 测试基础设施（csproj 排除 + xunit 工程 + 冒烟测试）

**Files:**
- Create: `tests/BashCommandManager.Tests/`（目录，本任务首次创建）
- Create: `tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`
- Create: `tests/BashCommandManager.Tests/ProjectReferenceSmokeTests.cs`
- Modify: `BashCommandManager.csproj`（`PropertyGroup` 内加一行 `DefaultItemExcludes`）

**Interfaces:**
- Consumes: 主工程已有的 `BashCommandManager.Core.Models.HotkeySettings`（`Enabled`/`Key`/`Modifiers` 三个属性）
- Produces: 可运行的测试程序集 `BashCommandManager.Tests`（后续所有任务都用 `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj` 验证）

- [ ] **Step 1: 创建测试工程文件**

`tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj` 全文：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>BashCommandManager.Tests</RootNamespace>
    <AssemblyName>BashCommandManager.Tests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\BashCommandManager.csproj" />
  </ItemGroup>
</Project>
```

注意：不要给测试工程加 `RuntimeIdentifiers`（主工程的 `win-x64` 与之混用会触发 `NETSDK1047`）；不要加 `coverlet.collector`（本次不需要覆盖率）。

- [ ] **Step 2: 写冒烟测试**

`tests/BashCommandManager.Tests/ProjectReferenceSmokeTests.cs` 全文：

```csharp
using BashCommandManager.Core.Models;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class ProjectReferenceSmokeTests
{
    [Fact]
    public void Test_project_resolves_types_from_the_main_application()
    {
        var settings = new HotkeySettings();

        Assert.True(settings.Enabled);
        Assert.Equal(Key.B, settings.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, settings.Modifiers);
    }
}
```

- [ ] **Step 3: 跑主工程构建，确认它因测试源码被 glob 进来而失败**

Run: `dotnet build BashCommandManager.csproj -c Release`

Expected: **FAIL**，错误形如 `error CS0246: 未能找到类型或命名空间名"Xunit"…`，且报错文件路径是 `tests\BashCommandManager.Tests\ProjectReferenceSmokeTests.cs`（SDK 语言不同则提示为英文 `The type or namespace name 'Xunit' could not be found`）。这条失败证明主工程的默认 glob 把 `tests\**` 编进了主程序集。

**两种红都算通过本步：** 若 `tests\BashCommandManager.Tests\obj` 已经存在（IDE 后台还原过、或先跑过一次 `dotnet test`），失败信息会先变成 `error CS0579: 重复的 "System.Reflection.AssemblyCompanyAttribute" 特性`（英文：`Duplicate 'System.Reflection.AssemblyCompanyAttribute' attribute`），因为默认 glob 把 `tests\...\obj\*.AssemblyInfo.cs` 先编了进来。`CS0246`（干净树）与 `CS0579`（已有 `tests/obj`）都算红；统一判据是**报错文件路径位于 `tests\` 下**，且去掉排除项前主工程构建必然失败。

- [ ] **Step 4: 在主工程 csproj 里排除 tests 与预留的 CLI 目录**

`BashCommandManager.csproj` 的 `<PropertyGroup>` 内、`<UseWPF>true</UseWPF>` 之后插入一行（其余内容不动）：

```xml
    <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
```

`BashCommandManager.Cli\**` 由 #10 创建（`DECISIONS.md` D2），这里先预留排除项，避免 #10 再改主 csproj；当前仓库没有该目录，MSBuild 对不存在的 glob 不报错。`DefaultItemExcludes` 同时作用于 `Compile`/`None`/`Page`，因此不需要也不允许再叠加 `<Compile Remove>`。

插入后该 `PropertyGroup` 应为：

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <PublishProfile>Properties\PublishProfiles\MSIX.pubxml</PublishProfile>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
  </PropertyGroup>
```

- [ ] **Step 5: 再跑主工程构建，确认通过**

Run: `dotnet build BashCommandManager.csproj -c Release`

Expected: **PASS**，`已成功生成` / `Build succeeded`，0 个错误；输出里不再出现 `tests\` 路径。（首次 restore 可能较久，本步约 2.5 分钟。）

- [ ] **Step 6: 跑测试工程，确认冒烟测试通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，`已通过! - 失败: 0，通过: 1，跳过: 0`（英文：`Passed! - Failed: 0, Passed: 1, Skipped: 0`）。

- [ ] **Step 7: 确认测试工程的 obj 产物不会再次污染主工程**

Run: `dotnet build BashCommandManager.csproj -c Release`

Expected: **PASS**，0 个错误（若 `DefaultItemExcludes` 写错，此时会出现 `error CS0579: 重复的 "System.Reflection.AssemblyCompanyAttribute" 特性`，即 `tests\...\obj\*.AssemblyInfo.cs` 被编进主程序集）。

- [ ] **Step 8: 提交**

```bash
git add BashCommandManager.csproj tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj tests/BashCommandManager.Tests/ProjectReferenceSmokeTests.cs
git commit -m "feat(hotkey): 策略分层与测试 — 引入 xunit 测试工程并排除 tests 目录"
```

---

## Task 2: `HotkeySettings.Clone()`

**Files:**
- Modify: `Core/Models/HotkeySettings.cs`（在 `IsValid` 之前加 `Clone()`）
- Test: `tests/BashCommandManager.Tests/HotkeySettingsTests.cs`

**Interfaces:**
- Consumes: 现有 `HotkeySettings { bool Enabled; Key Key; ModifierKeys Modifiers; string ToDisplayString(); static bool IsValid(HotkeySettings) }`
- Produces: `public HotkeySettings Clone()`（Task 9 用它构造设置页的编辑副本，并在落盘前断开别名）

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Tests/HotkeySettingsTests.cs` 全文：

```csharp
using BashCommandManager.Core.Models;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeySettingsTests
{
    [Fact]
    public void Clone_returns_an_equivalent_but_distinct_instance()
    {
        var original = new HotkeySettings
        {
            Enabled = true,
            Key = Key.N,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Enabled, clone.Enabled);
        Assert.Equal(original.Key, clone.Key);
        Assert.Equal(original.Modifiers, clone.Modifiers);
    }

    [Fact]
    public void Clone_isolates_edits_from_the_original()
    {
        var original = new HotkeySettings
        {
            Enabled = true,
            Key = Key.B,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt
        };

        var clone = original.Clone();
        clone.Key = Key.N;
        clone.Modifiers = ModifierKeys.Control;
        clone.Enabled = false;

        Assert.Equal(Key.B, original.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, original.Modifiers);
        Assert.True(original.Enabled);
    }

    [Fact]
    public void IsValid_is_false_when_no_modifier_is_pressed()
    {
        var settings = new HotkeySettings { Key = Key.B, Modifiers = ModifierKeys.None };

        Assert.False(HotkeySettings.IsValid(settings));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeySettingsTests"`

Expected: **FAIL（编译失败）**，`error CS1061: "HotkeySettings"未包含"Clone"的定义`（英文：`'HotkeySettings' does not contain a definition for 'Clone'`）。

- [ ] **Step 3: 实现 `Clone()`**

在 `Core/Models/HotkeySettings.cs` 的 `IsValid` 方法之前插入：

```csharp
    /// <summary>
    /// 复制一份独立的设置对象，供设置页编辑，避免改动服务内的当前配置。
    /// </summary>
    public HotkeySettings Clone()
    {
        return new HotkeySettings
        {
            Enabled = Enabled,
            Key = Key,
            Modifiers = Modifiers
        };
    }
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeySettingsTests"`

Expected: **PASS**，`通过: 3`。

- [ ] **Step 5: 提交**

```bash
git add Core/Models/HotkeySettings.cs tests/BashCommandManager.Tests/HotkeySettingsTests.cs
git commit -m "feat(hotkey): 策略分层与测试 — HotkeySettings 增加 Clone 以隔离编辑副本"
```

---

## Task 3: `WindowTogglePolicy` + `WindowActivitySnapshot` + `WindowToggleAction`

**Files:**
- Create: `Core/Services/WindowTogglePolicy.cs`
- Test: `tests/BashCommandManager.Tests/WindowTogglePolicyTests.cs`

**Interfaces:**
- Consumes: 无（纯逻辑，不引用 `System.Windows`）
- Produces:
  - `public readonly record struct WindowActivitySnapshot(bool IsAppModalOpen, bool IsVisible, bool IsMinimized, bool IsActive)`
  - `public enum WindowToggleAction { ShowAndActivate, HideToTray, ActivateExisting }`
  - `public interface IWindowTogglePolicy { WindowToggleAction Decide(WindowActivitySnapshot snapshot); }`
  - `public sealed class WindowTogglePolicy : IWindowTogglePolicy`

- [ ] **Step 1: 写失败测试（决策表逐行）**

`tests/BashCommandManager.Tests/WindowTogglePolicyTests.cs` 全文：

```csharp
using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests;

public class WindowTogglePolicyTests
{
    private readonly WindowTogglePolicy _policy = new();

    [Theory]
    [InlineData(false, false, true, false, WindowToggleAction.ShowAndActivate)]   // 托盘隐藏且状态为最小化
    [InlineData(false, false, false, false, WindowToggleAction.ShowAndActivate)]  // 已 Hide 但状态不是最小化
    [InlineData(false, true, true, false, WindowToggleAction.ShowAndActivate)]    // 可见但最小化
    [InlineData(false, true, false, true, WindowToggleAction.HideToTray)]         // 可见、非最小化、前台
    [InlineData(false, true, false, false, WindowToggleAction.ActivateExisting)]  // 可见、非最小化、非前台
    public void Decide_follows_the_decision_table(
        bool isAppModalOpen,
        bool isVisible,
        bool isMinimized,
        bool isActive,
        WindowToggleAction expected)
    {
        var snapshot = new WindowActivitySnapshot(isAppModalOpen, isVisible, isMinimized, isActive);

        Assert.Equal(expected, _policy.Decide(snapshot));
    }

    [Theory]
    [InlineData(true, false, true)]    // 模态 + 可见 + 非最小化 + 前台
    [InlineData(true, false, false)]   // 模态 + 可见 + 非最小化 + 非前台
    [InlineData(false, true, true)]    // 模态 + 隐藏 + 最小化（托盘状态）+ 前台
    [InlineData(false, true, false)]   // 模态 + 隐藏 + 最小化（托盘状态）+ 非前台
    public void Decide_never_hides_when_an_app_modal_window_is_open(
        bool isVisible,
        bool isMinimized,
        bool isActive)
    {
        var snapshot = new WindowActivitySnapshot(
            IsAppModalOpen: true,
            IsVisible: isVisible,
            IsMinimized: isMinimized,
            IsActive: isActive);

        Assert.Equal(WindowToggleAction.ActivateExisting, _policy.Decide(snapshot));
    }

    [Fact]
    public void Decide_is_stateless_and_returns_the_same_action_for_the_same_snapshot()
    {
        var snapshot = new WindowActivitySnapshot(
            IsAppModalOpen: false,
            IsVisible: true,
            IsMinimized: false,
            IsActive: true);

        Assert.Equal(WindowToggleAction.HideToTray, _policy.Decide(snapshot));
        Assert.Equal(WindowToggleAction.HideToTray, _policy.Decide(snapshot));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~WindowTogglePolicyTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"WindowTogglePolicy"`。

- [ ] **Step 3: 实现策略**

`Core/Services/WindowTogglePolicy.cs` 全文：

```csharp
namespace BashCommandManager.Core.Services;

/// <summary>
/// 一次热键触发时的窗口状态快照。刻意不引用任何 WPF 类型，便于单测。
/// </summary>
public readonly record struct WindowActivitySnapshot(
    bool IsAppModalOpen,
    bool IsVisible,
    bool IsMinimized,
    bool IsActive);

public enum WindowToggleAction
{
    ShowAndActivate,
    HideToTray,
    ActivateExisting
}

public interface IWindowTogglePolicy
{
    WindowToggleAction Decide(WindowActivitySnapshot snapshot);
}

/// <summary>
/// 纯决策表：模态窗口优先激活，其次隐藏/最小化则显示，再次前台则收进托盘，其余只激活。
/// </summary>
public sealed class WindowTogglePolicy : IWindowTogglePolicy
{
    public WindowToggleAction Decide(WindowActivitySnapshot snapshot)
    {
        if (snapshot.IsAppModalOpen)
        {
            return WindowToggleAction.ActivateExisting;
        }

        if (!snapshot.IsVisible || snapshot.IsMinimized)
        {
            return WindowToggleAction.ShowAndActivate;
        }

        if (snapshot.IsActive)
        {
            return WindowToggleAction.HideToTray;
        }

        return WindowToggleAction.ActivateExisting;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~WindowTogglePolicyTests"`

Expected: **PASS**，`通过: 10`（5 个决策表用例 + 4 个模态用例：可见/隐藏 × 激活/非激活，均 `isAppModalOpen: true` + 1 个无状态用例）。

- [ ] **Step 5: 提交**

```bash
git add Core/Services/WindowTogglePolicy.cs tests/BashCommandManager.Tests/WindowTogglePolicyTests.cs
git commit -m "feat(hotkey): 策略分层与测试 — 新增窗口切换决策纯逻辑"
```

---

## Task 4: `IWindowVisibilityController` + `HotkeyToggleCoordinator`

**Files:**
- Create: `Core/Services/IWindowVisibilityController.cs`
- Create: `Core/Services/HotkeyToggleCoordinator.cs`
- Create: `tests/BashCommandManager.Tests/Fakes/`（目录，本任务首次创建）
- Test: `tests/BashCommandManager.Tests/Fakes/FakeWindowVisibilityController.cs`
- Test: `tests/BashCommandManager.Tests/HotkeyToggleCoordinatorTests.cs`

**Interfaces:**
- Consumes: `WindowActivitySnapshot`、`WindowToggleAction`、`IWindowTogglePolicy`、`WindowTogglePolicy`（Task 3）
- Produces:
  - `public interface IWindowVisibilityController { WindowActivitySnapshot Capture(); void ShowAndActivate(); void HideToTray(); void ActivateExisting(); }`
  - `public sealed class HotkeyToggleCoordinator(IWindowTogglePolicy policy, IWindowVisibilityController controller)`，方法 `public WindowToggleAction Execute()`

- [ ] **Step 1: 写假控制器（测试支撑代码，不是测试本身）**

`tests/BashCommandManager.Tests/Fakes/FakeWindowVisibilityController.cs` 全文：

```csharp
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

/// <summary>
/// 记录调用顺序的假控制器：动作会改变自身快照，从而能验证协调器没有缓存决策结果。
/// </summary>
public sealed class FakeWindowVisibilityController : IWindowVisibilityController
{
    public WindowActivitySnapshot Snapshot { get; set; } =
        new(IsAppModalOpen: false, IsVisible: true, IsMinimized: false, IsActive: true);

    public List<string> Calls { get; } = new();

    public Exception? ThrowOnCapture { get; set; }
    public Exception? ThrowOnShowAndActivate { get; set; }
    public Exception? ThrowOnHideToTray { get; set; }
    public Exception? ThrowOnActivateExisting { get; set; }

    public WindowActivitySnapshot Capture()
    {
        Calls.Add(nameof(Capture));
        if (ThrowOnCapture is not null)
        {
            throw ThrowOnCapture;
        }

        return Snapshot;
    }

    public void ShowAndActivate()
    {
        Calls.Add(nameof(ShowAndActivate));
        if (ThrowOnShowAndActivate is not null)
        {
            throw ThrowOnShowAndActivate;
        }

        Snapshot = Snapshot with { IsVisible = true, IsMinimized = false, IsActive = true };
    }

    public void HideToTray()
    {
        Calls.Add(nameof(HideToTray));
        if (ThrowOnHideToTray is not null)
        {
            throw ThrowOnHideToTray;
        }

        Snapshot = Snapshot with { IsVisible = false, IsMinimized = true, IsActive = false };
    }

    public void ActivateExisting()
    {
        Calls.Add(nameof(ActivateExisting));
        if (ThrowOnActivateExisting is not null)
        {
            throw ThrowOnActivateExisting;
        }

        Snapshot = Snapshot with { IsActive = true };
    }
}
```

- [ ] **Step 2: 写失败测试**

`tests/BashCommandManager.Tests/HotkeyToggleCoordinatorTests.cs` 全文：

```csharp
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyToggleCoordinatorTests
{
    private static (HotkeyToggleCoordinator Coordinator, FakeWindowVisibilityController Controller) Create(
        WindowActivitySnapshot snapshot)
    {
        var controller = new FakeWindowVisibilityController { Snapshot = snapshot };
        return (new HotkeyToggleCoordinator(new WindowTogglePolicy(), controller), controller);
    }

    [Fact]
    public void Execute_only_shows_and_activates_a_hidden_window()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, false, true, false));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.ShowAndActivate, action);
        Assert.Equal(new[] { "Capture", "ShowAndActivate" }, controller.Calls);
    }

    [Fact]
    public void Execute_only_hides_a_foreground_window_to_the_tray()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, true));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.HideToTray, action);
        Assert.Equal(new[] { "Capture", "HideToTray" }, controller.Calls);
    }

    [Fact]
    public void Execute_only_activates_a_visible_background_window()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, false));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.ActivateExisting, action);
        Assert.Equal(new[] { "Capture", "ActivateExisting" }, controller.Calls);
    }

    [Fact]
    public void Execute_never_hides_when_a_modal_window_is_open()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(true, true, false, true));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.ActivateExisting, action);
        Assert.DoesNotContain(nameof(IWindowVisibilityController.HideToTray), controller.Calls);
    }

    [Fact]
    public void Execute_swallows_action_failures_so_the_ui_thread_survives()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, true));
        controller.ThrowOnHideToTray = new InvalidOperationException("boom");

        var exception = Record.Exception(() => coordinator.Execute());

        Assert.Null(exception);
        Assert.Contains(nameof(IWindowVisibilityController.HideToTray), controller.Calls);
    }

    [Fact]
    public void Execute_swallows_capture_failures_too()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, true));
        controller.ThrowOnCapture = new InvalidOperationException("capture failed");

        var exception = Record.Exception(() => coordinator.Execute());

        Assert.Null(exception);
        Assert.Equal(new[] { "Capture" }, controller.Calls);
    }

    [Fact]
    public void Execute_reports_the_action_it_performed_and_does_not_cache_the_decision()
    {
        var (coordinator, _) = Create(new WindowActivitySnapshot(false, true, false, true));

        Assert.Equal(WindowToggleAction.HideToTray, coordinator.Execute());
        Assert.Equal(WindowToggleAction.ShowAndActivate, coordinator.Execute());
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyToggleCoordinatorTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"HotkeyToggleCoordinator"`；同时 `IWindowVisibilityController` 也未定义。

- [ ] **Step 4: 实现动作抽象**

`Core/Services/IWindowVisibilityController.cs` 全文：

```csharp
namespace BashCommandManager.Core.Services;

/// <summary>
/// 热键要执行的窗口动作。WPF 实现在 Infrastructure，测试用假实现。
/// </summary>
public interface IWindowVisibilityController
{
    WindowActivitySnapshot Capture();

    void ShowAndActivate();

    void HideToTray();

    void ActivateExisting();
}
```

- [ ] **Step 5: 实现协调器**

`Core/Services/HotkeyToggleCoordinator.cs` 全文：

```csharp
using System.Diagnostics;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 「取快照 → 决策 → 执行」。异常一律吞掉并写 Debug，绝不让热键回调把 UI 线程带崩。
/// </summary>
public sealed class HotkeyToggleCoordinator
{
    private readonly IWindowTogglePolicy _policy;
    private readonly IWindowVisibilityController _controller;

    public HotkeyToggleCoordinator(IWindowTogglePolicy policy, IWindowVisibilityController controller)
    {
        _policy = policy;
        _controller = controller;
    }

    public WindowToggleAction Execute()
    {
        // 兜底为非破坏性动作：快照都取不到时不要动窗口。
        var action = WindowToggleAction.ActivateExisting;

        try
        {
            action = _policy.Decide(_controller.Capture());

            switch (action)
            {
                case WindowToggleAction.ShowAndActivate:
                    _controller.ShowAndActivate();
                    break;
                case WindowToggleAction.HideToTray:
                    _controller.HideToTray();
                    break;
                case WindowToggleAction.ActivateExisting:
                    _controller.ActivateExisting();
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"热键切换窗口失败（动作 {action}）: {ex}");
        }

        return action;
    }
}
```

- [ ] **Step 6: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyToggleCoordinatorTests"`

Expected: **PASS**，`通过: 7`。

- [ ] **Step 7: 跑全量测试，确认没有回归**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0（此前的 `ProjectReferenceSmokeTests`、`HotkeySettingsTests`、`WindowTogglePolicyTests` 仍全绿）。

- [ ] **Step 8: 提交**

```bash
git add Core/Services/IWindowVisibilityController.cs Core/Services/HotkeyToggleCoordinator.cs tests/BashCommandManager.Tests/Fakes/FakeWindowVisibilityController.cs tests/BashCommandManager.Tests/HotkeyToggleCoordinatorTests.cs
git commit -m "feat(hotkey): 策略分层与测试 — 新增热键切换协调器与假控制器"
```

---

## Task 5: `HotkeyRegistrationResult` + `HotkeyNativeMethods` + `HotkeyIdAllocator`（纯逻辑）

**Files:**
- Create: `Core/Models/HotkeyRegistrationResult.cs`
- Create: `Core/Services/HotkeyNativeMethods.cs`
- Create: `Core/Services/HotkeyIdAllocator.cs`
- Test: `tests/BashCommandManager.Tests/HotkeyRegistrationResultTests.cs`
- Test: `tests/BashCommandManager.Tests/HotkeyNativeMethodsTests.cs`
- Test: `tests/BashCommandManager.Tests/HotkeyIdAllocatorTests.cs`

**Interfaces:**
- Consumes: `System.Windows.Input.Key`、`System.Windows.Input.ModifierKeys`、`KeyInterop.VirtualKeyFromKey`
- Produces:
  - `public enum HotkeyRegistrationStatus { Success, Conflict, InvalidCombination, HostUnavailable, NotRegistered }`
  - `public readonly record struct HotkeyRegistrationResult(HotkeyRegistrationStatus Status, Key Key, ModifierKeys Modifiers)`，含 `bool IsSuccess`、`const int ErrorHotkeyAlreadyRegistered = 1409`、静态工厂 `Success/InvalidCombination/HostUnavailable/NotRegistered/FromRegisterError(int win32Error, Key, ModifierKeys)`
  - `public static class HotkeyNativeMethods`：`const int WmHotkey = 0x0312`、`const uint ModAlt/ModControl/ModShift/ModWin`、`RegisterHotKey(IntPtr,int,uint,uint)`、`UnregisterHotKey(IntPtr,int)`、`uint ToWin32Modifiers(ModifierKeys)`、`int ToVirtualKey(Key)`
  - `public sealed class HotkeyIdAllocator`：`const int FirstId = 0x9000`、`const int LastId = 0x90FF`、`int Next()`

- [ ] **Step 1: 写 `HotkeyRegistrationResult` 的失败测试**

`tests/BashCommandManager.Tests/HotkeyRegistrationResultTests.cs` 全文：

```csharp
using BashCommandManager.Core.Models;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyRegistrationResultTests
{
    [Fact]
    public void IsSuccess_is_true_only_for_the_Success_status()
    {
        Assert.True(HotkeyRegistrationResult.Success(Key.B, ModifierKeys.Control).IsSuccess);
        Assert.False(HotkeyRegistrationResult.InvalidCombination(Key.B, ModifierKeys.None).IsSuccess);
        Assert.False(HotkeyRegistrationResult.HostUnavailable(Key.B, ModifierKeys.Control).IsSuccess);
        Assert.False(HotkeyRegistrationResult.NotRegistered(Key.None, ModifierKeys.None).IsSuccess);
        Assert.False(HotkeyRegistrationResult.FromRegisterError(1409, Key.B, ModifierKeys.Control).IsSuccess);
    }

    [Fact]
    public void FromRegisterError_maps_1409_to_Conflict_because_the_combination_is_taken()
    {
        var result = HotkeyRegistrationResult.FromRegisterError(
            1409,
            Key.B,
            ModifierKeys.Control | ModifierKeys.Alt);

        Assert.Equal(HotkeyRegistrationStatus.Conflict, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(87)]
    public void FromRegisterError_maps_every_other_error_code_to_HostUnavailable(int win32Error)
    {
        var result = HotkeyRegistrationResult.FromRegisterError(win32Error, Key.B, ModifierKeys.Control);

        Assert.Equal(HotkeyRegistrationStatus.HostUnavailable, result.Status);
    }

    [Fact]
    public void Result_carries_the_combination_it_was_asked_to_register()
    {
        var result = HotkeyRegistrationResult.Success(Key.N, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal(Key.N, result.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, result.Modifiers);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyRegistrationResultTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"HotkeyRegistrationResult"`。

- [ ] **Step 3: 实现注册结果类型**

`Core/Models/HotkeyRegistrationResult.cs` 全文：

```csharp
using System.Windows.Input;

namespace BashCommandManager.Core.Models;

public enum HotkeyRegistrationStatus
{
    Success,
    Conflict,
    InvalidCombination,
    HostUnavailable,
    NotRegistered
}

/// <summary>
/// 一次注册/改键尝试的结果。失败时携带被尝试的组合，便于调用方提示。
/// </summary>
public readonly record struct HotkeyRegistrationResult(
    HotkeyRegistrationStatus Status,
    Key Key,
    ModifierKeys Modifiers)
{
    /// <summary>Win32 ERROR_HOTKEY_ALREADY_REGISTERED。</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    public bool IsSuccess => Status == HotkeyRegistrationStatus.Success;

    public static HotkeyRegistrationResult Success(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.Success, key, modifiers);

    public static HotkeyRegistrationResult InvalidCombination(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.InvalidCombination, key, modifiers);

    public static HotkeyRegistrationResult HostUnavailable(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.HostUnavailable, key, modifiers);

    public static HotkeyRegistrationResult NotRegistered(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.NotRegistered, key, modifiers);

    /// <summary>
    /// 把 RegisterHotKey 的 GetLastError 映射成状态：1409 表示组合已被占用（Conflict），其余一律视为宿主不可用。
    /// </summary>
    public static HotkeyRegistrationResult FromRegisterError(int win32Error, Key key, ModifierKeys modifiers) =>
        new(
            win32Error == ErrorHotkeyAlreadyRegistered
                ? HotkeyRegistrationStatus.Conflict
                : HotkeyRegistrationStatus.HostUnavailable,
            key,
            modifiers);
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyRegistrationResultTests"`

Expected: **PASS**，`通过: 6`。

- [ ] **Step 5: 写 `HotkeyNativeMethods` 的失败测试**

`tests/BashCommandManager.Tests/HotkeyNativeMethodsTests.cs` 全文：

```csharp
using BashCommandManager.Core.Services;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyNativeMethodsTests
{
    [Theory]
    [InlineData(ModifierKeys.None, 0x0000u)]
    [InlineData(ModifierKeys.Alt, 0x0001u)]
    [InlineData(ModifierKeys.Control, 0x0002u)]
    [InlineData(ModifierKeys.Shift, 0x0004u)]
    [InlineData(ModifierKeys.Windows, 0x0008u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, 0x0003u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, 0x0006u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, 0x0007u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, 0x000Fu)]
    public void ToWin32Modifiers_maps_each_modifier_to_its_user32_flag(ModifierKeys modifiers, uint expected)
    {
        Assert.Equal(expected, HotkeyNativeMethods.ToWin32Modifiers(modifiers));
    }

    [Fact]
    public void ToVirtualKey_maps_a_wpf_key_to_its_win32_virtual_key()
    {
        Assert.Equal(0x42, HotkeyNativeMethods.ToVirtualKey(Key.B));
    }
}
```

- [ ] **Step 6: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyNativeMethodsTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"HotkeyNativeMethods"`。

- [ ] **Step 7: 实现 Win32 薄封装**

`Core/Services/HotkeyNativeMethods.cs` 全文：

```csharp
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace BashCommandManager.Core.Services;

/// <summary>
/// user32 的最薄封装：只放常量、P/Invoke 与纯换算，任何决策都不在这里。
/// </summary>
public static class HotkeyNativeMethods
{
    public const int WmHotkey = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;

        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            result |= ModAlt;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            result |= ModControl;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            result |= ModShift;
        }

        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
        {
            result |= ModWin;
        }

        return result;
    }

    public static int ToVirtualKey(Key key) => KeyInterop.VirtualKeyFromKey(key);
}
```

- [ ] **Step 8: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyNativeMethodsTests"`

Expected: **PASS**，`通过: 10`（9 个修饰键用例 + 1 个虚拟键用例）。

- [ ] **Step 9: 写 `HotkeyIdAllocator` 的失败测试**

`tests/BashCommandManager.Tests/HotkeyIdAllocatorTests.cs` 全文：

```csharp
using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyIdAllocatorTests
{
    [Fact]
    public void Next_returns_consecutive_ids_starting_at_0x9000()
    {
        var allocator = new HotkeyIdAllocator();

        Assert.Equal(0x9000, allocator.Next());
        Assert.Equal(0x9001, allocator.Next());
        Assert.Equal(0x9002, allocator.Next());
    }

    [Fact]
    public void Next_returns_0x90FF_last_and_then_wraps_back_to_0x9000()
    {
        var allocator = new HotkeyIdAllocator();

        var last = 0;
        for (var i = 0; i < 256; i++)
        {
            last = allocator.Next();
        }

        Assert.Equal(0x90FF, last);
        Assert.Equal(0x9000, allocator.Next());
    }
}
```

- [ ] **Step 10: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyIdAllocatorTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"HotkeyIdAllocator"`。

- [ ] **Step 11: 实现 id 分配器**

`Core/Services/HotkeyIdAllocator.cs` 全文：

```csharp
namespace BashCommandManager.Core.Services;

/// <summary>
/// 在 Win32 应用可用的 0x9000–0x90FF 区间内分配热键 id；用尽后回绕。
/// 每次改键都用新 id，从而可以先注册新组合、成功后再注销旧 id。
/// </summary>
public sealed class HotkeyIdAllocator
{
    public const int FirstId = 0x9000;
    public const int LastId = 0x90FF;

    private int _next = FirstId;

    public int Next()
    {
        var id = _next;
        _next = id == LastId ? FirstId : id + 1;
        return id;
    }
}
```

- [ ] **Step 12: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyIdAllocatorTests"`

Expected: **PASS**，`通过: 2`。

- [ ] **Step 13: 跑全量测试**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0（累计 39 个用例：冒烟 1 + `HotkeySettings` 3 + 决策策略 10 + 协调器 7 + 注册结果 6 + 修饰键 10 + id 分配 2）。

- [ ] **Step 14: 提交**

```bash
git add Core/Models/HotkeyRegistrationResult.cs Core/Services/HotkeyNativeMethods.cs Core/Services/HotkeyIdAllocator.cs tests/BashCommandManager.Tests/HotkeyRegistrationResultTests.cs tests/BashCommandManager.Tests/HotkeyNativeMethodsTests.cs tests/BashCommandManager.Tests/HotkeyIdAllocatorTests.cs
git commit -m "feat(hotkey): 策略分层与测试 — 注册结果、修饰键换算与热键 id 分配"
```

---

## Task 6: `HotkeyMessageWindow`（message-only 宿主）

**Files:**
- Create: `Core/Services/HotkeyMessageWindow.cs`

**Interfaces:**
- Consumes: `HotkeyNativeMethods.WmHotkey`（Task 5）、`System.Windows.Interop.HwndSource`/`HwndSourceParameters`
- Produces: `internal sealed class HotkeyMessageWindow : IDisposable`，成员：
  - `IntPtr Handle { get; }`
  - `event EventHandler<int>? HotkeyIdReceived;`（参数为 `WM_HOTKEY` 的 `wParam`，即注册 id）
  - `bool EnsureCreated();`（幂等；先 `Parent = HWND_MESSAGE`，失败降级 `Parent = IntPtr.Zero` 的 0×0 隐藏窗口）
  - `void Dispose();`（`RemoveHook` → `_source.Dispose()`，可重复调用，必须在创建它的 UI 线程调用）

**为什么这个任务没有单元测试（TDD 例外，理由与替代验证）：** `HwndSource` 必须在有消息泵的 STA/UI 线程上创建，xunit 的测试线程既不是 STA 也没有消息泵；spec 的测试策略也明确「单元测试（无需真实 Win32）」，把宿主划入手工验证。本任务把可测的部分（`WM_HOTKEY` 常量、id 匹配、错误码映射）全部放进了 Task 5 的纯类，宿主本身只剩「建窗 + 挂钩 + 转发」，其真实行为由 Task 10 手工步骤 1–5 验证（那些步骤正是 Issue #8 的复现路径）。该豁免已在 Self-Review §7「与 DECISIONS 的偏差登记」按 D5.4 逐条说明，并写进本任务提交说明。

- [ ] **Step 1: 写宿主实现**

`Core/Services/HotkeyMessageWindow.cs` 全文：

```csharp
using System.Diagnostics;
using System.Windows.Interop;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 进程级 message-only 窗口（Parent = HWND_MESSAGE）：不参与枚举、不显示，
/// 但仍是真实 HWND，能接收系统投递的 WM_HOTKEY。生命周期与主窗口无关。
/// </summary>
internal sealed class HotkeyMessageWindow : IDisposable
{
    /// <summary>HWND_MESSAGE，即 (HWND)(-3)。</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    private HwndSource? _source;
    private bool _hookAdded;

    public IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    public event EventHandler<int>? HotkeyIdReceived;

    /// <summary>
    /// 幂等建窗：先尝试 message-only，失败降级为 0×0 隐藏窗口，两者都失败返回 false。
    /// </summary>
    public bool EnsureCreated()
    {
        if (_source is not null)
        {
            return _source.Handle != IntPtr.Zero;
        }

        _source = Create(HwndMessage) ?? Create(IntPtr.Zero);
        if (_source is null)
        {
            return false;
        }

        if (!_hookAdded)
        {
            _source.AddHook(WndProc);
            _hookAdded = true;
        }

        return _source.Handle != IntPtr.Zero;
    }

    private static HwndSource? Create(IntPtr parent)
    {
        try
        {
            var source = new HwndSource(new HwndSourceParameters("BashCommandManager.HotkeyHost")
            {
                WindowStyle = 0,
                ParentWindow = parent,
                Width = 0,
                Height = 0
            });

            if (source.Handle != IntPtr.Zero)
            {
                return source;
            }

            Debug.WriteLine("热键宿主创建失败：HwndSource 句柄为 0");
            source.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"热键宿主创建失败: {ex.Message}");
        }

        return null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNativeMethods.WmHotkey)
        {
            HotkeyIdReceived?.Invoke(this, wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 必须在创建它的 UI 线程调用；重复调用无副作用。
    /// </summary>
    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        if (_hookAdded)
        {
            _source.RemoveHook(WndProc);
            _hookAdded = false;
        }

        _source.Dispose();
        _source = null;
    }
}
```

实现要点（照抄 spec 并修正一处）：spec 的 `EnsureCreated` 每次调用都会 `AddHook`，重复调用会挂多个钩子导致 `HotkeyPressed` 触发多次；这里用 `_hookAdded` 保证只挂一次。

- [ ] **Step 2: 构建确认通过（此任务无单测，构建就是它的红灯边界）**

Run: `dotnet build BashCommandManager.csproj -c Debug`

Expected: **PASS**，0 个错误。若出现 `error CS0246: 未能找到类型或命名空间名"HwndSource"`，说明文件缺少 `using System.Windows.Interop;`。

- [ ] **Step 3: 跑全量测试确认没有回归**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0。

- [ ] **Step 4: 提交**

```bash
git add Core/Services/HotkeyMessageWindow.cs
git commit -m "feat(hotkey): 解耦热键宿主 — 新增 message-only 热键宿主窗口" -m "DECISIONS-D5.4: 宿主无先行失败测试（HwndSource 需 STA + 消息泵），验证方式为手工 1–5，见 docs/superpowers/DECISIONS.md 与计划 Self-Review §7"
```

---

## Task 7: `ForegroundWindowActivator` + `MainWindowVisibilityController`

**Files:**
- Create: `Infrastructure/Interop/`（目录，本任务首次创建）
- Create: `Infrastructure/Interop/ForegroundWindowActivator.cs`
- Create: `Infrastructure/MainWindowVisibilityController.cs`
- Test: `tests/BashCommandManager.Tests/ForegroundWindowActivatorTests.cs`

**Interfaces:**
- Consumes: `IWindowVisibilityController`、`WindowActivitySnapshot`（Task 4/3）；`System.Windows.Window`、`WindowInteropHelper`
- Produces:
  - `public interface IForegroundWindowActivator { bool TryActivate(IntPtr windowHandle); }`
  - `public sealed class ForegroundWindowActivator : IForegroundWindowActivator`
  - `public sealed class MainWindowVisibilityController : IWindowVisibilityController`，构造函数 `MainWindowVisibilityController(Window window, IForegroundWindowActivator activator)`

**为什么只测一个用例（TDD 例外说明）：** `MainWindowVisibilityController` 的方法都要操作真实 `Window`（`Show`/`Hide`/`WindowState`/`IsActive`/`OwnedWindows`），在 xunit 里必须自建 STA 线程 + Dispatcher，属 spec 明确排除的「真实 WPF 窗口实例」范围，行为交给 Task 10 手工步骤 1–11。该豁免已在 Self-Review §7「与 DECISIONS 的偏差登记」按 D5.4 留痕，并写进本任务提交说明。`ForegroundWindowActivator` 唯一能在无窗口条件下验证的是「零句柄必须直接返回 false、不调 user32」这条守卫，写成单测是为了锁住它（否则 `SetForegroundWindow(0)` 这类无意义调用会被静默吞掉）——所以本任务本身仍有先失败后通过的测试，豁免只覆盖控制器那半边。

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Tests/ForegroundWindowActivatorTests.cs` 全文：

```csharp
using BashCommandManager.Infrastructure.Interop;
using Xunit;

namespace BashCommandManager.Tests;

public class ForegroundWindowActivatorTests
{
    [Fact]
    public void TryActivate_returns_false_for_a_zero_handle_without_calling_win32()
    {
        var activator = new ForegroundWindowActivator();

        Assert.False(activator.TryActivate(IntPtr.Zero));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~ForegroundWindowActivatorTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"ForegroundWindowActivator"`。

- [ ] **Step 3: 实现前台激活器**

`Infrastructure/Interop/ForegroundWindowActivator.cs` 全文：

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BashCommandManager.Infrastructure.Interop;

public interface IForegroundWindowActivator
{
    bool TryActivate(IntPtr windowHandle);
}

/// <summary>
/// 尽力把窗口拉到前台：先 SetForegroundWindow；被前台锁拒绝时把本线程输入状态临时
/// AttachThreadInput 到前台线程再试，finally 必须解挂。
/// </summary>
public sealed class ForegroundWindowActivator : IForegroundWindowActivator
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    public bool TryActivate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (GetForegroundWindow() == windowHandle)
        {
            return true;
        }

        if (SetForegroundWindow(windowHandle))
        {
            return true;
        }

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
                       && foregroundThread != currentThread
                       && AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
            SetFocus(windowHandle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        var activated = GetForegroundWindow() == windowHandle;
        if (!activated)
        {
            Debug.WriteLine($"前台激活未能生效: hwnd=0x{windowHandle.ToInt64():X}（窗口仍在任务栏可见）");
        }

        return activated;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~ForegroundWindowActivatorTests"`

Expected: **PASS**，`通过: 1`。

- [ ] **Step 5: 实现主窗口控制器**

`Infrastructure/MainWindowVisibilityController.cs` 全文：

```csharp
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure.Interop;
using System.Windows;
using System.Windows.Interop;

namespace BashCommandManager.Infrastructure;

/// <summary>
/// 把热键动作翻译成对主窗口的 WPF 操作。只认识 Window，不认识热键。
/// </summary>
public sealed class MainWindowVisibilityController : IWindowVisibilityController
{
    private readonly Window _window;
    private readonly IForegroundWindowActivator _activator;

    public MainWindowVisibilityController(Window window, IForegroundWindowActivator activator)
    {
        _window = window;
        _activator = activator;
    }

    public WindowActivitySnapshot Capture()
    {
        return new WindowActivitySnapshot(
            IsAppModalOpen: _window.OwnedWindows.OfType<Window>().Any(w => w.IsVisible),
            IsVisible: _window.IsVisible,
            IsMinimized: _window.WindowState == WindowState.Minimized,
            IsActive: _window.IsActive);
    }

    public void ShowAndActivate()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _activator.TryActivate(new WindowInteropHelper(_window).Handle);
    }

    public void HideToTray()
    {
        _window.WindowState = WindowState.Minimized;
        _window.Hide();
    }

    public void ActivateExisting()
    {
        var modalWindow = _window.OwnedWindows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
        if (modalWindow is not null)
        {
            modalWindow.Activate();
            _activator.TryActivate(new WindowInteropHelper(modalWindow).Handle);
            return;
        }

        _window.Activate();
        _activator.TryActivate(new WindowInteropHelper(_window).Handle);
    }
}
```

- [ ] **Step 6: 构建 + 全量测试**

Run: `dotnet build BashCommandManager.csproj -c Debug`

Expected: **PASS**，0 个错误（`OwnedWindows.OfType<Window>()`/`FirstOrDefault` 依赖 `ImplicitUsings` 提供的 `System.Linq`）。

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0（累计 40 个用例）。

- [ ] **Step 7: 提交**

```bash
git add Infrastructure/Interop/ForegroundWindowActivator.cs Infrastructure/MainWindowVisibilityController.cs tests/BashCommandManager.Tests/ForegroundWindowActivatorTests.cs
git commit -m "feat(hotkey): 解耦热键宿主 — 前台激活与主窗口可见性控制器" -m "DECISIONS-D5.4: MainWindowVisibilityController 无先行失败测试（需真实 Window + Dispatcher），验证方式为手工 1–11，见 docs/superpowers/DECISIONS.md 与计划 Self-Review §7"
```

---

## Task 8: 重写 `IGlobalHotkeyService`/`GlobalHotkeyService`，主窗口去热键，App 唯一注册/注销点

**Files:**
- Create: `Core/Services/IGlobalHotkeyService.cs`
- Modify: `Core/Services/GlobalHotkeyService.cs`（整体替换）
- Modify: `MainWindow.xaml.cs`（删除热键字段/订阅/注册/退订/`ToggleWindowVisibility`）
- Modify: `App.xaml.cs`（整体替换：接线 + 唯一注册/注销）
- Modify: `ViewModels/SettingsViewModel.cs`（仅编译适配：`bool registered` → `result.IsSuccess`）

**Interfaces:**
- Consumes: `HotkeyRegistrationResult`/`HotkeyRegistrationStatus`（Task 5）、`HotkeyNativeMethods`（Task 5）、`HotkeyIdAllocator`（Task 5）、`HotkeyMessageWindow`（Task 6）、`HotkeyToggleCoordinator`/`WindowTogglePolicy`（Task 3/4）、`MainWindowVisibilityController`/`ForegroundWindowActivator`（Task 7）、`HotkeySettings.Clone()`（Task 2）
- Produces:
  - `public interface IGlobalHotkeyService : IDisposable { HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers); HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers); void Suspend(); HotkeyRegistrationResult Resume(); void Unregister(); bool IsRegistered { get; } event EventHandler? HotkeyPressed; }`
  - `public sealed class GlobalHotkeyService : IGlobalHotkeyService`，公共无参构造函数（DI 用），`Dispose()` 可重复调用

**为什么这个任务没有新增单测（TDD 例外说明，并列全 spec 依据）：** `GlobalHotkeyService` 的全部方法都直接调用 `RegisterHotKey`/`UnregisterHotKey`，`Suspend/Resume` 还要与真实消息泵配合才能观察到 `WM_HOTKEY`；xunit 进程内没有 UI 线程消息泵，spec 的测试策略明确这类路径归入「集成与手工验证」。可抽出来的逻辑（1409 映射、修饰键换算、id 回绕、结果语义）已在 Task 5 用 18 个用例锁住，本任务只剩「调用顺序 + 状态机」，由 Task 10 手工步骤 1–11（尤其步骤 1/2/6/7/10）验证。该豁免已在 Self-Review §7「与 DECISIONS 的偏差登记」按 D5.4 逐条说明，并写进本任务提交说明。

- [ ] **Step 1: 写新接口**

`Core/Services/IGlobalHotkeyService.cs` 全文：

```csharp
using BashCommandManager.Core.Models;
using System.Windows.Input;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 进程级全局热键。宿主与主窗口生命周期无关：注册在 App 启动、生命周期的注销只在 App 退出。
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers);

    HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers);

    /// <summary>临时解注册（捕获新组合期间），保留 Current。</summary>
    void Suspend();

    /// <summary>用 Current 重新注册；没有 Current 时返回 NotRegistered。</summary>
    HotkeyRegistrationResult Resume();

    /// <summary>注销并清空 Current。</summary>
    void Unregister();

    bool IsRegistered { get; }

    event EventHandler? HotkeyPressed;
}
```

- [ ] **Step 2: 重写服务实现**

`Core/Services/GlobalHotkeyService.cs` 全文（整体替换旧文件，旧文件里的接口定义已移到独立文件）：

```csharp
using BashCommandManager.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BashCommandManager.Core.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly Dispatcher _dispatcher;
    private readonly HotkeyMessageWindow _host = new();
    private readonly HotkeyIdAllocator _idAllocator = new();

    private Key _currentKey = Key.None;
    private ModifierKeys _currentModifiers = ModifierKeys.None;
    private int _currentId;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkeyService()
    {
        // RegisterHotKey 线程亲和：必须在有消息泵的 UI 线程创建与调用。
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _host.HotkeyIdReceived += OnHotkeyIdReceived;
    }

    public bool IsRegistered => _currentId != 0;

    public HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers)
    {
        _dispatcher.VerifyAccess();

        if (key == Key.None || modifiers == ModifierKeys.None)
        {
            return HotkeyRegistrationResult.InvalidCombination(key, modifiers);
        }

        if (!_host.EnsureCreated())
        {
            return HotkeyRegistrationResult.HostUnavailable(key, modifiers);
        }

        // 先注册新组合，成功后才注销旧 id：改键失败时用户不会变成「没有热键」。
        var newId = _idAllocator.Next();
        if (!RegisterNative(_host.Handle, newId, key, modifiers, out var win32Error))
        {
            return HotkeyRegistrationResult.FromRegisterError(win32Error, key, modifiers);
        }

        UnregisterNative(_host.Handle, _currentId);

        _currentId = newId;
        _currentKey = key;
        _currentModifiers = modifiers;
        return HotkeyRegistrationResult.Success(key, modifiers);
    }

    public HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers)
    {
        _dispatcher.VerifyAccess();

        // 同一组合且已注册：直接成功，避免自冲突（1409）。
        if (IsRegistered && key == _currentKey && modifiers == _currentModifiers)
        {
            return HotkeyRegistrationResult.Success(key, modifiers);
        }

        return TryRegister(key, modifiers);
    }

    public void Suspend()
    {
        _dispatcher.VerifyAccess();

        if (_currentId == 0)
        {
            return;
        }

        UnregisterNative(_host.Handle, _currentId);
        _currentId = 0;
    }

    public HotkeyRegistrationResult Resume()
    {
        _dispatcher.VerifyAccess();

        if (_currentKey == Key.None || _currentModifiers == ModifierKeys.None)
        {
            return HotkeyRegistrationResult.NotRegistered(_currentKey, _currentModifiers);
        }

        if (_currentId != 0)
        {
            return HotkeyRegistrationResult.Success(_currentKey, _currentModifiers);
        }

        return TryRegister(_currentKey, _currentModifiers);
    }

    public void Unregister()
    {
        _dispatcher.VerifyAccess();

        UnregisterNative(_host.Handle, _currentId);
        _currentId = 0;
        _currentKey = Key.None;
        _currentModifiers = ModifierKeys.None;
    }

    private void OnHotkeyIdReceived(object? sender, int id)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(() => OnHotkeyIdReceived(sender, id)));
            return;
        }

        // 只认当前注册的 id：改键/挂起期间漏进来的旧 id 消息必须忽略。
        if (_currentId == 0 || id != _currentId)
        {
            return;
        }

        HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    private static bool RegisterNative(IntPtr hwnd, int id, Key key, ModifierKeys modifiers, out int win32Error)
    {
        var registered = HotkeyNativeMethods.RegisterHotKey(
            hwnd,
            id,
            HotkeyNativeMethods.ToWin32Modifiers(modifiers),
            (uint)HotkeyNativeMethods.ToVirtualKey(key));

        win32Error = registered ? 0 : Marshal.GetLastWin32Error();
        return registered;
    }

    private static void UnregisterNative(IntPtr hwnd, int id)
    {
        if (id == 0 || hwnd == IntPtr.Zero)
        {
            return;
        }

        if (HotkeyNativeMethods.UnregisterHotKey(hwnd, id))
        {
            return;
        }

        // 注销失败只记日志，但仍清空内部状态，避免僵尸注册挡住后续尝试。
        Debug.WriteLine($"注销热键失败: id={id}, hwnd=0x{hwnd.ToInt64():X}, error={Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        Unregister();
        _host.Dispose();
    }
}
```

- [ ] **Step 3: 从 `MainWindow.xaml.cs` 删除全部热键痕迹**

按下面 6 处精确修改（其余内容一字不动）：

1. 删除 4 行 `using`：`using BashCommandManager.Core.Services;`、`using HandyControl.Controls;`、`using HandyControl.Data;`、`using Microsoft.Extensions.DependencyInjection;`。删除后文件头为：

```csharp
using BashCommandManager.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BashCommandManager;

public partial class MainWindow : HandyControl.Controls.Window
{
```

2. 删除两个字段（原第 16–17 行）：

```csharp
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;
```

删除后类开头只剩：

```csharp
    private bool _isExiting = false; // 标记是否真正退出
```

3. 构造函数里删除服务获取与订阅（原第 24–29 行）：

```csharp
        // 从服务提供者获取服务
        _hotkeyService = App.ServiceProvider.GetRequiredService<IGlobalHotkeyService>();
        _settingsService = App.ServiceProvider.GetRequiredService<ISettingsService>();

        // 订阅快捷键事件
        _hotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;
```

4. `Loaded` 处理器改为：

```csharp
        Loaded += async (s, e) =>
        {
            await viewModel.GroupTreeViewModel.LoadGroupsAsync();
        };
```

5. 删除 `Closing += MainWindow_Closing;`（原第 46 行）与其上的注释 `// 窗口关闭时注销快捷键`，并删除这四个方法整体：`RegisterHotkey()`、`MainWindow_Closing(...)`、`HotkeyService_HotkeyPressed(...)`、`ToggleWindowVisibility()`（原第 49–103 行）。

6. 结果：`MainWindow.xaml.cs` 中不得再出现字符串 `IGlobalHotkeyService`、`HotkeyPressed`、`Unregister`、`hotkeyService`、`RegisterHotkey`。保留 `_isExiting`、`MainWindow_StateChanged`、`OnClosing`（仍调 `base.OnClosing(e)`）、`ShowWindow`、`Exit_Click`、`TrayIcon_MouseDoubleClick`、`ShowWindow_Click`、`LoadIcon`、`GroupTree_SelectedItemChanged`、`TreeViewItem_ContextMenuOpening`、`SearchBox_KeyDown`。

7. 改完后 `MainWindow.xaml.cs` 的完整形态如下（可直接整体替换，逐行对齐即可确认第 1–6 处改动都到位；除被删内容外与原文逐字相同）：

```csharp
using BashCommandManager.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BashCommandManager;

public partial class MainWindow : HandyControl.Controls.Window
{
    private bool _isExiting = false; // 标记是否真正退出

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 动态加载图标（支持单文件发布）
        LoadIcon();

        Loaded += async (s, e) =>
        {
            await viewModel.GroupTreeViewModel.LoadGroupsAsync();
        };

        // 订阅窗体状态变化事件
        StateChanged += MainWindow_StateChanged;
    }

    /// <summary>
    /// 动态加载窗口和托盘图标
    /// </summary>
    private void LoadIcon()
    {
        try
        {
            // 使用 Pack URI 加载图标（支持单文件发布）
            var iconUri = new Uri("pack://application:,,,/Assets/app.ico");
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch
        {
            // 图标加载失败不影响应用运行
        }
    }

    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is Core.Models.Group group)
        {
            vm.GroupTreeViewModel.SelectedGroup = group;
        }
    }

    private void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TreeViewItem item && item.ContextMenu != null && DataContext is MainViewModel vm)
        {
            // 检查是否是虚拟节点
            var selectedGroup = vm.GroupTreeViewModel.SelectedGroup;
            bool isVirtual = selectedGroup?.IsVirtual ?? false;

            foreach (var child in item.ContextMenu.Items)
            {
                if (child is MenuItem menuItem)
                {
                    switch (menuItem.Header)
                    {
                        case "新建子分组":
                            menuItem.Command = vm.GroupTreeViewModel.CreateGroupCommand;
                            // 虚拟节点隐藏新建子分组菜单
                            menuItem.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
                            break;
                        case "重命名":
                            menuItem.Command = vm.GroupTreeViewModel.StartRenameGroupCommand;
                            // 虚拟节点隐藏重命名菜单
                            menuItem.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
                            break;
                        case "删除":
                            menuItem.Command = vm.GroupTreeViewModel.DeleteGroupCommand;
                            // 虚拟节点隐藏删除菜单
                            menuItem.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
                            break;
                    }
                }
            }
        }
    }

    private void TrayIcon_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void ShowWindow()
    {
        Show(); // 显示窗体
        WindowState = WindowState.Normal; // 恢复窗体状态
        Activate(); // 激活窗体到前台
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true; // 设置退出标志
        Close(); // 调用关闭方法
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide(); // 隐藏窗体（从任务栏消失）
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            // 真正退出时清理托盘图标
            TrayIcon?.Dispose();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true; // 取消关闭操作
        WindowState = WindowState.Minimized; // 触发最小化
        Hide(); // 隐藏窗体
        base.OnClosing(e);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // 触发搜索命令
            if (DataContext is MainViewModel vm)
            {
                vm.SearchCommand.Execute(null);
            }
        }
    }
}
```

- [ ] **Step 4: 重写 `App.xaml.cs`（接线 + 唯一注册/注销）**

`App.xaml.cs` 全文（整体替换）：

```csharp
using BashCommandManager.Core.Repositories;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;
using BashCommandManager.Infrastructure.Interop;
using BashCommandManager.ViewModels;
using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Windows;

namespace BashCommandManager;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    private SingleInstanceService? _singleInstanceService;
    private IGlobalHotkeyService? _hotkeyService;
    private HotkeyToggleCoordinator? _toggleCoordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceService = new SingleInstanceService();

        bool isFirstInstance = _singleInstanceService.Initialize(() =>
        {
            if (MainWindow != null)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        });

        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var services = new ServiceCollection();

        // 初始化数据库
        var initializer = new DatabaseInitializer();
        initializer.Initialize();

        // 注册数据库服务
        var dbService = new DatabaseService(initializer.ConnectionString);
        services.AddSingleton<IDatabaseService>(dbService);

        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // 创建主窗口
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;  // 赋值给属性以便回调访问
        mainWindow.Show();

        // 热键宿主是进程级的：注册在这里，而不是主窗口的 Loaded/Closing。
        var settingsService = ServiceProvider.GetRequiredService<ISettingsService>();
        _hotkeyService = ServiceProvider.GetRequiredService<IGlobalHotkeyService>();

        var visibilityController = new MainWindowVisibilityController(mainWindow, new ForegroundWindowActivator());
        _toggleCoordinator = new HotkeyToggleCoordinator(new WindowTogglePolicy(), visibilityController);

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        var hotkeySettings = settingsService.GetHotkeySettings();
        if (hotkeySettings.Enabled
            && !_hotkeyService.TryRegister(hotkeySettings.Key, hotkeySettings.Modifiers).IsSuccess)
        {
            Growl.Warning(new GrowlInfo
            {
                Message = "全局快捷键注册失败，可能已被其他程序占用",
                WaitTime = 5
            });
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _toggleCoordinator?.Execute();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // 数据库连接
        services.AddScoped<IDbConnection>(sp =>
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return dbService.CreateConnection();
        });

        // Repositories
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<ICommandRepository, CommandRepository>();

        // Services
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<ICommandService, CommandService>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();

        // 新增：设置服务（单例）
        services.AddSingleton<ISettingsService, SettingsService>();

        // 新增：全局快捷键服务（单例）
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // ViewModels
        services.AddScoped<GroupTreeViewModel>();
        services.AddScoped<CommandListViewModel>();
        services.AddScoped<MainViewModel>();
        services.AddScoped<SettingsViewModel>();

        // Views
        services.AddScoped<MainWindow>();
        services.AddScoped<Views.SettingsWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 全进程唯一的热键生命周期注销点。
        if (_hotkeyService is not null)
        {
            _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyService.Unregister();
            _hotkeyService.Dispose();
            _hotkeyService = null;
        }

        _toggleCoordinator = null;
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 5: 让 `SettingsViewModel` 适配新返回类型（只改编译，不动顺序）**

在 `ViewModels/SettingsViewModel.cs` 中把 `Save` 里这段：

```csharp
        // 如果启用了快捷键，更新注册
        if (HotkeySettings.Enabled)
        {
            var registered = _hotkeyService.UpdateHotkey(HotkeySettings.Key, HotkeySettings.Modifiers);
            if (!registered)
            {
                Growl.Warning(new GrowlInfo
                {
                    Message = "快捷键更新失败，可能已被其他程序占用",
                    WaitTime = 5
                });
            }
        }
```

替换为：

```csharp
        // 如果启用了快捷键，更新注册
        if (HotkeySettings.Enabled)
        {
            var result = _hotkeyService.UpdateHotkey(HotkeySettings.Key, HotkeySettings.Modifiers);
            if (!result.IsSuccess)
            {
                Growl.Warning(new GrowlInfo
                {
                    Message = "快捷键更新失败，可能已被其他程序占用",
                    WaitTime = 5
                });
            }
        }
```

注意：此步骤**故意不调整**「先落盘后注册」的顺序，改键语义留给 Task 9，保证本任务提交的 diff 只包含「宿主解耦」。Task 9 会把这段替换成按 `HotkeyRegistrationStatus` 分档的三档文案（`Conflict` 沿用 spec 原句）、补 `HasError` 复位、以及捕获期 `Resume` 失败提示；本步骤只要求编译通过。

- [ ] **Step 6: 构建确认整个工程自洽**

Run: `dotnet build BashCommandManager.csproj -c Release`

Expected: **PASS**，0 个错误。若出现 `error CS1501: "Register"方法没有采用 3 个参数的重载` 或 `error CS7036`，说明 `MainWindow.xaml.cs` 还残留旧调用。

- [ ] **Step 7: 用 grep 验证 `MainWindow` 已不认识热键**

Run:

```powershell
$leftover = @(Select-String -Path MainWindow.xaml.cs -Pattern "IGlobalHotkeyService|HotkeyPressed|Unregister|hotkeyService|RegisterHotkey|ToggleWindowVisibility")
if ($leftover.Count -ne 0) {
    $detail = ($leftover | ForEach-Object { '{0}:{1}' -f $_.LineNumber, $_.Line.Trim() }) -join '; '
    throw "MainWindow.xaml.cs 仍残留热键符号（$($leftover.Count) 处）：$detail"
}
Write-Host 'OK: MainWindow.xaml.cs 残留 0 处'
```

Expected: 不抛 `throw`、打印 `OK: MainWindow.xaml.cs 残留 0 处`。不要用「`Select-String` 退出码 1 表示没有匹配」来判断——它无匹配时既不输出也不改 `$LASTEXITCODE`，必须像上面这样显式统计 `Count`。

- [ ] **Step 8: 跑全量测试**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0（累计 40 个用例；本任务不加测试）

- [ ] **Step 9: 提交**

```bash
git add Core/Services/IGlobalHotkeyService.cs Core/Services/GlobalHotkeyService.cs MainWindow.xaml.cs App.xaml.cs ViewModels/SettingsViewModel.cs
git commit -m "feat(hotkey): 解耦热键宿主 — 服务重写与 App 统一注册注销" -m "DECISIONS-D5.3: IGlobalHotkeyService 接口签名变更（删除 Register(Window,Key,ModifierKeys)；UpdateHotkey 返回 bool → HotkeyRegistrationResult），理由见计划 Self-Review §7 与 docs/superpowers/DECISIONS.md" -m "DECISIONS-D5.4: GlobalHotkeyService 无先行失败测试（调用顺序/状态机需真实消息泵），验证方式为手工 1/2/6/7/10"
```

---

## Task 9: 捕获键归一化 + `SettingsViewModel` 改键语义（编辑副本 / 先注册后落盘 / 捕获期挂起）

**Files:**
- Create: `Core/Services/HotkeyKeyNormalizer.cs`
- Create: `tests/BashCommandManager.Tests/HotkeyKeyNormalizerTests.cs`
- Modify: `Views/HotkeyCaptureWindow.xaml.cs`（整体替换：`Window_KeyDown` 先归一化捕获键）
- Modify: `ViewModels/SettingsViewModel.cs`（整体替换）

**Interfaces:**
- Consumes: `HotkeySettings.Clone()`（Task 2）、`IGlobalHotkeyService.Suspend()`/`Resume()`/`UpdateHotkey()`/`Unregister()`（Task 8）、`HotkeyRegistrationStatus`（Task 5）、`ISettingsService.GetHotkeySettings()`/`SaveHotkeySettings(HotkeySettings)`（现有）
- Produces:
  - `public static class HotkeyKeyNormalizer { public static Key Normalize(Key key, Key systemKey); }`：`key == Key.System` 时返回 `systemKey`，否则原样返回 `key`
  - 无其它新类型；`SettingsViewModel.Save(System.Windows.Window?)` 的行为契约变为「注册成功才落盘」：失败按 `HotkeyRegistrationStatus` 分档提示、`HasError = true`、不写 `settings.json`、不关闭 settings 窗口；成功时 `HasError = false; ErrorMessage = string.Empty;`

**为什么这个任务只有键位归一化走 TDD（D5.4 例外说明）：** 归一化是纯函数（一次三元判断），可以先写失败测试、再用 2 个用例锁住 `Key.System + Key.N → Key.N` 与「普通键原样返回」，这正是 B2 那个「静默写坏配置」缺陷的最小可测单元。其余部分是 UI 接线：`Save` 的成功/失败分支都必须经过 HandyControl `Growl` 与真实 `Window` 参数，`Growl` 在没有 `Application` 的 xunit 进程内拿不到 `GrowlPanel`；`HotkeyCaptureWindow` 的 `KeyDown` 需要真实 WPF 输入管线与键盘消息。spec 的测试策略也未把 `SettingsViewModel` 与捕获窗口列入单测范围。G5 的「冲突不污染配置」与「Alt 组合可用」由 Task 10 手工步骤 6–10、13–15 验证。该豁免已在 Self-Review §7「与 DECISIONS 的偏差登记」留痕，并写进本任务提交说明。

- [ ] **Step 1: 写 `HotkeyKeyNormalizer` 的失败测试**

`tests/BashCommandManager.Tests/HotkeyKeyNormalizerTests.cs` 全文：

```csharp
using BashCommandManager.Core.Services;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyKeyNormalizerTests
{
    [Fact]
    public void Normalize_returns_the_system_key_when_wpf_reports_Key_System()
    {
        // Alt 组合（WM_SYSKEYDOWN）下 WPF 把 e.Key 置为 Key.System，真实键在 e.SystemKey；
        // 若原样保存 Key.System，RegisterHotKey 的 vk 会是 0（返回 true 但永不触发）。
        Assert.Equal(Key.N, HotkeyKeyNormalizer.Normalize(Key.System, Key.N));
    }

    [Fact]
    public void Normalize_returns_the_key_unchanged_for_a_plain_key()
    {
        Assert.Equal(Key.N, HotkeyKeyNormalizer.Normalize(Key.N, Key.None));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyKeyNormalizerTests"`

Expected: **FAIL（编译失败）**，`error CS0246: 未能找到类型或命名空间名"HotkeyKeyNormalizer"`（英文：`The type or namespace name 'HotkeyKeyNormalizer' could not be found`）。

- [ ] **Step 3: 实现归一化纯函数**

`Core/Services/HotkeyKeyNormalizer.cs` 全文：

```csharp
using System.Windows.Input;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 把捕获到的 WPF 键归一化成可注册的真实键。
/// Alt 组合走 WM_SYSKEYDOWN 时 e.Key 是 Key.System、真实键在 e.SystemKey；
/// 若把 Key.System 直接交给 RegisterHotKey，虚拟键会是 0（注册返回 true 但永不触发）。
/// </summary>
public static class HotkeyKeyNormalizer
{
    /// <summary>
    /// e.Key == Key.System 时返回 systemKey（WPF 在该分支保证 systemKey 是真实键），否则原样返回 key。
    /// </summary>
    public static Key Normalize(Key key, Key systemKey)
    {
        return key == Key.System ? systemKey : key;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~HotkeyKeyNormalizerTests"`

Expected: **PASS**，`通过: 2`。

- [ ] **Step 5: 让捕获窗口使用归一化后的键（修 B2 的功能缺陷）**

`Views/HotkeyCaptureWindow.xaml.cs` 全文（整体替换；与现状相比只多了一个 `using` 与 `Window_KeyDown` 里的键归一化，其余逐字不变）：

```csharp
using BashCommandManager.Core.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace BashCommandManager.Views;

public partial class HotkeyCaptureWindow : Window, INotifyPropertyChanged
{
    private Key _capturedKey;
    private ModifierKeys _capturedModifiers;
    private bool _canConfirm;

    public (Key Key, ModifierKeys Modifiers) CapturedHotkey => (_capturedKey, _capturedModifiers);

    public string CapturedHotkeyText { get; private set; } = "等待输入...";

    public bool CanConfirm
    {
        get => _canConfirm;
        private set
        {
            _canConfirm = value;
            OnPropertyChanged();
        }
    }

    public HotkeyCaptureWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt 组合下 e.Key 是 Key.System，真实键在 e.SystemKey：先归一化再判断与保存。
        var key = HotkeyKeyNormalizer.Normalize(e.Key, e.SystemKey);

        // 忽略单独的修饰键（Alt 组合里 e.SystemKey 才是 Key.LeftAlt/Key.RightAlt）
        if (key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin)
        {
            UpdateDisplayText();
            return;
        }

        _capturedKey = key;
        _capturedModifiers = Keyboard.Modifiers;

        UpdateDisplayText();
        CanConfirm = _capturedModifiers != ModifierKeys.None;
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }

    private void UpdateDisplayText()
    {
        var modifiers = Keyboard.Modifiers;
        var key = _capturedKey;

        if (modifiers == ModifierKeys.None && key == Key.None)
        {
            CapturedHotkeyText = "等待输入...";
        }
        else
        {
            var modString = modifiers switch
            {
                ModifierKeys.Control => "Ctrl+",
                ModifierKeys.Alt => "Alt+",
                ModifierKeys.Shift => "Shift+",
                ModifierKeys.Windows => "Win+",
                ModifierKeys.Control | ModifierKeys.Alt => "Ctrl+Alt+",
                ModifierKeys.Control | ModifierKeys.Shift => "Ctrl+Shift+",
                ModifierKeys.Control | ModifierKeys.Windows => "Ctrl+Win+",
                ModifierKeys.Alt | ModifierKeys.Shift => "Alt+Shift+",
                ModifierKeys.Alt | ModifierKeys.Windows => "Alt+Win+",
                ModifierKeys.Shift | ModifierKeys.Windows => "Shift+Win+",
                ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift => "Ctrl+Alt+Shift+",
                _ => string.Empty
            };

            var keyString = key != Key.None ? key.ToString() : "...";
            CapturedHotkeyText = modString + keyString;
        }

        OnPropertyChanged(nameof(CapturedHotkeyText));
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null!)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

改动要点（3 处，其余逐字不变）：
1. 新增 `using BashCommandManager.Core.Services;`。
2. `Window_KeyDown` 开头先 `var key = HotkeyKeyNormalizer.Normalize(e.Key, e.SystemKey);`，后面「忽略单独修饰键」的判断与 `_capturedKey = key;` 都用归一化后的 `key`。原实现用 `e.Key`，因此 Alt 组合下 `e.Key == Key.System`：既会把 `Key.System` 存进设置（`KeyInterop.VirtualKeyFromKey(Key.System) == 0`），也会让 `Key.LeftAlt`/`Key.RightAlt` 的拦截失效。
3. `UpdateDisplayText()` 读的是 `_capturedKey`，所以捕获窗口显示从 `Ctrl+Alt+System` 变成 `Ctrl+Alt+B`。

**为什么这一行是必须的（B2 的完整故障链）：** 用户捕获 `Ctrl+Alt+B` → `e.Key` 是 `Key.System` → 存下 `Key.System` → `TryRegister` 的 `ToVirtualKey` 得到 `0` → `RegisterHotKey(hwnd, id, MOD_CONTROL|MOD_ALT, vk:0)` **返回 true（err=0）** → 本计划「先注册后注销」的逻辑随即 `UnregisterNative` 掉旧组合 → `Save` 判定 `IsSuccess` 为真 → 把 `"Key": 156`（`Key.System` 的枚举值）写进 `settings.json` 并提示「设置已保存」→ 新组合永远按不出来、旧组合已被注销、配置里留下无效键。归一化后这条链从第一步就断了；即便将来再出现无效键，`Save` 的 `InvalidCombination` 分档（Step 6）也不会落盘。

- [ ] **Step 6: 整体替换 `ViewModels/SettingsViewModel.cs`**

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace BashCommandManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IGlobalHotkeyService _hotkeyService;

    [ObservableProperty]
    private HotkeySettings _hotkeySettings;

    [ObservableProperty]
    private string _hotkeySettingsDisplay = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public SettingsViewModel(ISettingsService settingsService, IGlobalHotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;

        // 编辑副本：冲突时服务内的当前配置仍是旧值，界面上的改动不会污染它。
        HotkeySettings = _settingsService.GetHotkeySettings().Clone();
        UpdateDisplayString();
    }

    partial void OnHotkeySettingsChanged(HotkeySettings value)
    {
        UpdateDisplayString();
    }

    private void UpdateDisplayString()
    {
        HotkeySettingsDisplay = HotkeySettings.ToDisplayString();
    }

    [RelayCommand]
    private void SetHotkey()
    {
        var captureWindow = new Views.HotkeyCaptureWindow();

        // 捕获期间临时解注册，否则按当前组合会被系统吞掉、主窗口被藏到对话框后面。
        _hotkeyService.Suspend();
        try
        {
            if (captureWindow.ShowDialog() != true)
            {
                return;
            }

            var (key, modifiers) = captureWindow.CapturedHotkey;

            if (modifiers == ModifierKeys.None)
            {
                ErrorMessage = "快捷键必须包含至少一个修饰键 (Ctrl/Alt/Shift/Win)";
                HasError = true;
                return;
            }

            if (key == Key.None)
            {
                // 只按下修饰键就确认：Key.None 会被服务判为 InvalidCombination，这里先拦下。
                ErrorMessage = "快捷键无效，请同时按下一个非修饰键";
                HasError = true;
                return;
            }

            HotkeySettings.Key = key;
            HotkeySettings.Modifiers = modifiers;
            UpdateDisplayString();
            HasError = false;
            ErrorMessage = string.Empty;
        }
        finally
        {
            // Resume 失败不能静默：捕获期间原组合可能已被别的程序抢走，
            // 用户看到「已捕获新键」，实际旧键已经失效。
            var resume = _hotkeyService.Resume();
            if (!resume.IsSuccess)
            {
                Debug.WriteLine(
                    $"捕获结束后恢复原热键失败: status={resume.Status}, key={resume.Key}, modifiers={resume.Modifiers}");

                // NotRegistered 表示用户本来就没有启用热键（Current 为空），属正常状态，不打扰。
                if (resume.Status != HotkeyRegistrationStatus.NotRegistered)
                {
                    ErrorMessage = "原快捷键恢复失败，可能已被其他程序占用（保存时会重新注册）";
                    HasError = true;
                    Growl.Warning(new GrowlInfo
                    {
                        Message = "原快捷键恢复失败，可能已被其他程序占用（保存时会重新注册）",
                        WaitTime = 5
                    });
                }
            }
        }
    }

    [RelayCommand]
    private void Save(System.Windows.Window? window)
    {
        if (!HotkeySettings.IsValid(HotkeySettings))
        {
            ErrorMessage = "快捷键设置无效";
            HasError = true;
            return;
        }

        if (HotkeySettings.Enabled)
        {
            // 先注册成功，再落盘：冲突时旧组合继续生效，settings.json 不被改写。
            var result = _hotkeyService.UpdateHotkey(HotkeySettings.Key, HotkeySettings.Modifiers);
            if (!result.IsSuccess)
            {
                // 分档文案：只有 Conflict（1409）才值得让用户去关别的程序；
                // InvalidCombination / HostUnavailable 是组合本身无效或宿主不可用，
                // 统一说「已被占用」会让用户永远修不好。三条分支都不落盘。
                var message = result.Status switch
                {
                    HotkeyRegistrationStatus.Conflict =>
                        "快捷键更新失败，可能已被其他程序占用（已保留原快捷键）",
                    HotkeyRegistrationStatus.InvalidCombination =>
                        "快捷键无效或系统不可用（已保留原快捷键）",
                    HotkeyRegistrationStatus.HostUnavailable =>
                        "快捷键无效或系统不可用（已保留原快捷键）",
                    _ => "快捷键更新失败（已保留原快捷键）"
                };

                ErrorMessage = message;
                HasError = true;
                Growl.Warning(new GrowlInfo
                {
                    Message = message,
                    WaitTime = 5
                });
                return;
            }
        }
        else
        {
            // 用户主动关闭启用开关：这是唯一由用户意图触发的注销。
            _hotkeyService.Unregister();
        }

        // 传克隆体，避免服务内的内存配置与界面编辑副本互为别名。
        _settingsService.SaveHotkeySettings(HotkeySettings.Clone());

        // 成功路径必须复位：本 VM 由根容器解析、实际是单例，否则下次打开设置页还挂着旧红字。
        HasError = false;
        ErrorMessage = string.Empty;

        window?.Close();

        Growl.Success(new GrowlInfo
        {
            Message = "设置已保存",
            WaitTime = 3
        });
    }

    [RelayCommand]
    private void Cancel(System.Windows.Window? window)
    {
        window?.Close();
    }
}
```

- [ ] **Step 7: 构建确认通过**

Run: `dotnet build BashCommandManager.csproj -c Debug`

Expected: **PASS**，0 个错误（`Views/HotkeyCaptureWindow.xaml.cs` 若漏了 `using BashCommandManager.Core.Services;`，这里会报 `CS0246: 未能找到类型或命名空间名"HotkeyKeyNormalizer"`）。

- [ ] **Step 8: 跑全量测试**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0（累计 42 个用例 = Task 7 结束时的 40 + `HotkeyKeyNormalizerTests` 2）。

- [ ] **Step 9: 人工代码复核（此任务的「测试」就是下面 6 条断言）**

Run:

```powershell
Select-String -Path ViewModels/SettingsViewModel.cs, Views/HotkeyCaptureWindow.xaml.cs -Pattern "Suspend\(\)","Resume\(\)","SaveHotkeySettings","UpdateHotkey","Unregister\(\)","Clone\(\)","Normalize\(","SystemKey","HasError = false","_capturedKey = key"
```

Expected: 命中且顺序满足——
1. `GetHotkeySettings().Clone()` 出现在构造函数（编辑副本）。
2. `Suspend()` 在 `ShowDialog()` 之前、`Resume()` 在 `finally` 里，且 `Resume()` 的返回值被接住（`var resume = _hotkeyService.Resume();`）后紧跟 `if (!resume.IsSuccess)` 分支——不得出现丢弃返回值的裸调用。
3. `UpdateHotkey` 出现在 `SaveHotkeySettings` 之前（先注册后落盘）。
4. `Unregister()` 只在「未启用」分支出现一次。
5. `Views/HotkeyCaptureWindow.xaml.cs` 中出现 `Normalize(` 与 `SystemKey`，且 `_capturedKey` 的来源是归一化后的 `key`（`_capturedKey = key;`）；全文不得再出现 `_capturedKey = e.Key;`。
6. `HasError = false;` 既出现在 `SetHotkey` 的成功分支，也出现在 `Save` 的成功路径（`window?.Close()` 之前）。

- [ ] **Step 10: 提交**

```bash
git add Core/Services/HotkeyKeyNormalizer.cs Views/HotkeyCaptureWindow.xaml.cs ViewModels/SettingsViewModel.cs tests/BashCommandManager.Tests/HotkeyKeyNormalizerTests.cs
git commit -m "feat(hotkey): 解耦热键宿主 — 改键先注册后落盘并支持捕获期挂起" -m "DECISIONS-D5.4: SettingsViewModel 与捕获窗口无先行失败测试（需 Growl/Window/WPF 输入管线）；键位归一化已用 2 个纯函数用例锁住，见计划 Self-Review §7"
```

---

## Task 10: 全量验证 + 手工验收 + 文档

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: 前 9 个任务的全部产出
- Produces: 可交付的验证记录与仓库文档

- [ ] **Step 1: 全量 Release 构建**

Run: `dotnet build BashCommandManager.csproj -c Release`

Expected: **PASS**，`0 个错误`（约 2.5 分钟）；记录警告数与内容，本次改动不得新增警告。

- [ ] **Step 2: 全量测试**

Run: `dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`

Expected: **PASS**，失败 0、跳过 0，通过数 = 42（本计划新增 42 个用例：冒烟 1 + `HotkeySettings` 3 + 决策策略 10 + 协调器 7 + 注册结果 6 + 修饰键 10 + id 分配 2 + 键位归一化 2 + 前台激活器 1）。

- [ ] **Step 3: grep 校验两条验收口径（用 `if + throw` 断言，不要依赖退出码）**

`Select-String` 无匹配时**不会**修改 `$LASTEXITCODE`，所以「退出码 1 表示没有匹配」不成立；必须显式统计命中数再 `throw`。

Run:

```powershell
$leftover = @(Select-String -Path MainWindow.xaml.cs -Pattern "IGlobalHotkeyService|HotkeyPressed|Unregister|hotkeyService|RegisterHotkey|ToggleWindowVisibility")
if ($leftover.Count -ne 0) {
    $detail = ($leftover | ForEach-Object { '{0}:{1}' -f $_.LineNumber, $_.Line.Trim() }) -join '; '
    throw "MainWindow.xaml.cs 仍残留热键符号（$($leftover.Count) 处）：$detail"
}
Write-Host 'OK: MainWindow.xaml.cs 残留 0 处'

$unregister = @(Select-String -Path *.cs, Core\Services\*.cs, ViewModels\*.cs, Infrastructure\*.cs, Infrastructure\Interop\*.cs -Pattern "\.Unregister\(\)")
if ($unregister.Count -ne 2) {
    $detail = ($unregister | ForEach-Object { '{0}:{1}' -f $_.Path, $_.LineNumber }) -join '; '
    throw "预期恰好 2 处 .Unregister()，实际 $($unregister.Count) 处：$detail"
}
$unregister | ForEach-Object { Write-Host ('OK: {0}:{1}' -f $_.Path, $_.LineNumber) }
```

Expected:
- 第一条断言：0 处命中（`MainWindow.xaml.cs` 里不得出现 `IGlobalHotkeyService`/`HotkeyPressed`/`Unregister`/`hotkeyService`/`RegisterHotkey`/`ToggleWindowVisibility`）。
- 第二条断言：恰好 2 处 —— `App.xaml.cs`（`OnExit` 内，生命周期注销）与 `ViewModels/SettingsViewModel.cs`（用户关闭启用开关时的用户意图注销）。多出任何一处都说明生命周期注销被泄漏到了别处；少一处说明用户意图注销被漏掉了。

两条断言都不触发 `throw` 即通过；脚本最后会列出两个 `路径:行号`。

- [ ] **Step 4: 手工验收（启动应用，逐条执行）**

Run: `dotnet run`（或先 `dotnet build -c Release` 后启动 `bin\Release\net8.0-windows10.0.19041.0\win-x64\BashCommandManager.exe`——主 csproj 带 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`，产物在 `win-x64` 子目录下，没有它说明路径写错了）

| # | 操作 | 期望结果 | 对应 spec |
|---|---|---|---|
| 1 | 启动后窗口可见，按 `Ctrl+Alt+B`；再按一次 | 第一次隐藏到托盘；第二次显示、状态 `Normal`、前台且可直接键入 | 手工 1 / G3 |
| 2 | 点标题栏 X 到托盘，按 `Ctrl+Alt+B` | **窗口显示并激活**（Issue #8 核心回归） | 手工 2 / G1 |
| 3 | 托盘「显示窗口」恢复后，点最小化按钮到托盘，按热键 | 显示并激活（原路径不回归） | 手工 3 |
| 4 | 托盘恢复后不隐藏，再按一次热键 | 每次都正常切换，无「第二次失效」 | 手工 4 |
| 5 | 托盘隐藏状态下连续按 10 次热键 | 10 次行为一致，无卡死、无异常 | 手工 5 |
| 6 | 打开设置 → 「设置新快捷键」→ 按当前组合 `Ctrl+Alt+B` | 捕获窗口显示 `Ctrl+Alt+B`（不是 `Ctrl+Alt+System`，证明 `HotkeyKeyNormalizer` 生效；`Suspend` 生效不触发隐藏） | 手工 8 / A8 / B2 |
| 7 | 捕获成功改为 `Ctrl+Shift+N` → 保存 → 按新旧组合 | 新组合立即生效；旧组合无反应；检查 `%LOCALAPPDATA%\BashCommandManager\settings.json` 中 `Key`/`Modifiers` 已更新且字段仍是 `Enabled`/`Key`/`Modifiers` 三个 | 手工 6 / G5 / G8 |
| 8 | 用 AutoHotkey 或另一实例占用目标组合后保存 | 弹出「快捷键更新失败，可能已被其他程序占用（已保留原快捷键）」；设置窗口不关闭；`settings.json` 文件内容与修改时间未变；旧组合仍生效 | 手工 7 / G5 |
| 9 | 打开设置窗口（保持打开），按热键 | 主窗口不被隐藏，应用被拉到前台（模态分支） | 手工 9 |
| 10 | 托盘菜单「退出」→ 重新启动 → 立即按热键 | 进程结束；重启后同一组合可立即注册成功（无 `1409` 残留） | 手工 10 / G6 |
| 11 | 前台开一个普通应用（记事本）；再用一个全屏应用（如全屏视频/游戏）占前台，按热键 | 本应用确实到前台，光标可输入，而不是只闪任务栏 | 手工 11 / A6 |
| 12 | 关闭「启用全局快捷键」→ 保存 → 按热键；再勾选启用 → 保存 → 按热键 | 关闭后热键无反应；重新启用后立即恢复（走 `UpdateHotkey` 注册） | G8 反向路径 |
| 13 | 捕获 `Alt+F1` 组合 → 保存 → 生效；取消捕获 → 原热键恢复 | 捕获窗口显示 `Alt+F1`（Alt 组合走 `WM_SYSKEYDOWN`，仍是 `F1` 不是 `System`）；保存后按 `Alt+F1` 能切换窗口；`settings.json` 里 `Key` 是 `112`（`F1` 的虚拟键 `0x70`）而不是 `156`；随后打开捕获窗口按「取消」，原热键立即可用 | 手工 8 / B2 / G5 |
| 14 | 保存新组合（如 `Ctrl+Shift+N`）后**重启应用**，再按新组合 | 重启后新组合立即生效（`settings.json` 已正确落盘、启动注册走 `App.OnStartup`） | 手工 6 后半句 |
| 15 | 打开捕获窗口后直接按「取消」，再按原热键 | 原热键恢复（`finally` 里的 `Resume()` 成功）；若 `Resume` 失败应弹出「原快捷键恢复失败，可能已被其他程序占用（保存时会重新注册）」，而不是静默失效 | 手工 8 后半句 / A8 |

验收前后可用下面两条命令核对 `settings.json`（路径来自 `SettingsService`：`%LOCALAPPDATA%\BashCommandManager\settings.json`）：

```powershell
$p = Join-Path $env:LOCALAPPDATA "BashCommandManager\settings.json"
Get-Item $p | Select-Object FullName, LastWriteTime
Get-Content $p -Raw
```

- 第 8 行（组合被占用）验收时，`LastWriteTime` 必须与操作前一致，文件内容必须仍是旧的组合。
- 第 13 行验收时，`Key` 必须是 `112`（`F1`），`Modifiers` 必须是 `1`（仅 Alt），绝不能出现 `156`（`Key.System` 的枚举值）。

- [ ] **Step 5: 更新 README**

在 `README.md` 的「### ⌨️ 全局快捷键」小节（现有 3 条 bullet）末尾追加一条，变为：

```markdown
### ⌨️ 全局快捷键
- 支持配置全局快捷键
- 一键显示/隐藏应用窗口
- 即使窗口最小化到托盘也能快速唤出
- 点关闭按钮隐藏到托盘后，快捷键依然有效
```

- [ ] **Step 6: 更新 CLAUDE.md**

在 `CLAUDE.md` 的「### 系统托盘」小节之后、「## 开发计划」之前插入：

```markdown
### 全局快捷键

- 热键宿主是进程级的 message-only 窗口（`Core/Services/HotkeyMessageWindow.cs`，`HwndSource` + `Parent=HWND_MESSAGE`），与主窗口 `Show/Hide` 无关
- 注册在 `App.OnStartup`，生命周期的注销只在 `App.OnExit`；`MainWindow` 不再持有 `IGlobalHotkeyService`
- 「快照 → 决策 → 执行」是可单测的纯逻辑：`WindowTogglePolicy`、`HotkeyToggleCoordinator`；Win32 只在 `HotkeyNativeMethods` 与 `ForegroundWindowActivator` 两个薄层
- 点 X 隐藏到托盘走 `MainWindow.OnClosing`（`e.Cancel = true`），不会注销热键
```

- [ ] **Step 7: 提交（含设计文档与计划）**

```bash
git add README.md CLAUDE.md docs/superpowers/specs/2026-11-08-hotkey-tray-design.md docs/superpowers/plans/2026-11-08-hotkey-tray.md docs/superpowers/reviews/2026-11-08-hotkey-tray-plan-review.md
git commit -m "feat(hotkey): 策略分层与测试 — 验收记录与热键生命周期文档"
```

`docs/superpowers/reviews/2026-11-08-hotkey-tray-plan-review.md` 是评审报告（含本轮修订记录），与计划同属 #8 的设计产物，故并入本次文档提交。

---

## Self-Review

### 1. spec 目标（G1–G8）覆盖检查

| 目标 | 由哪些任务实现 | 由什么验证 |
|---|---|---|
| G1 托盘隐藏（X / 最小化 / 任何 `Hide()`）期间热键有效 | Task 6（宿主与窗口无关）+ Task 8（生命周期移出 `Loaded`/`Closing`） | 手工 2、3、4、5；grep 步骤 Task 10 Step 3 |
| G2 宿主与主窗口彻底解耦（隐藏/显示/取消 `Closing`/将来重建都不影响注册） | Task 6（message-only HWND）+ Task 8（`MainWindow` 不再出现 `IGlobalHotkeyService`/`HotkeyPressed`/`Unregister`） | Task 10 Step 3 第一条 grep 无输出 |
| G3 隐藏时按热键：显示、`Normal`、前台且有焦点 | Task 7 `ShowAndActivate()`（`Show → Normal → Activate → TryActivate` 顺序固定）+ Task 4 决策表 | `WindowTogglePolicyTests`（隐藏/最小化 → `ShowAndActivate`）；手工 1、2 |
| G4 可见且前台时按热键：最小化到托盘 | Task 3 决策表 `IsActive → HideToTray`、Task 7 `HideToTray()` | `WindowTogglePolicyTests`、`HotkeyToggleCoordinatorTests`；手工 1 |
| G5 改键立即生效；被占用时保留旧组合且不污染配置 | Task 9（先注册后落盘、失败 `return` 不写文件、捕获键归一化 + `Resume` 失败提示）+ Task 8（`UpdateHotkey` 同组合短路、1409 保留旧注册） | 手工 6、7、8、13；`HotkeyRegistrationResultTests`（1409→Conflict）、`HotkeyKeyNormalizerTests`（`Key.System` 不再落盘） |
| G6 真正退出释放注册与消息窗口 | Task 8 `App.OnExit`（退订 → `Unregister` → `Dispose` → `_host.Dispose()`），Task 6 `Dispose` 可重复 | grep 步骤（唯一注销点）；手工 10 |
| G7 决策为纯逻辑可单测，Win32 走薄层 | Task 3、4、5 全部纯逻辑 + Task 9 的 `HotkeyKeyNormalizer` + `WindowTogglePolicyTests`/`HotkeyToggleCoordinatorTests`/`HotkeyRegistrationResultTests`/`HotkeyNativeMethodsTests`/`HotkeyIdAllocatorTests`/`HotkeyKeyNormalizerTests`；Win32 收敛到 Task 5 的 `HotkeyNativeMethods`、Task 7 的 `ForegroundWindowActivator` | `dotnet test`（无任何测试创建 `Window`/`HwndSource`） |
| G8 不改用户可见语义、不新增配置项、不改 `settings.json` 字段 | Task 9 只改注册/落盘顺序，不动 `HotkeySettings` 的 3 个字段；`SaveHotkeySettings` 签名不变；`Conflict` 文案逐字沿用 spec，`InvalidCombination`/`HostUnavailable` 分档（见 §5，属有意细化） | 手工 7（检查 json 仍是 3 个字段）、`HotkeySettingsTests`、diff 复核 |

补充覆盖 A5（宿主降级）：Task 6 `EnsureCreated()` 先 `HWND_MESSAGE` 再降级 `IntPtr.Zero` 的 0×0 隐藏窗口；A6（`SetForegroundWindow` 被拒）：Task 7 `AttachThreadInput` + `try/finally` 解挂 + 结果 `Debug.WriteLine`；A7（旧版本可回读）：`settings.json` 形状不变，Task 10 手工 7 检查；A8（捕获期挂起）：Task 9 `Suspend`/`finally Resume`，`Resume` 失败时 `Debug.WriteLine` + Growl 提示（不静默）；B2（Alt 组合）：Task 9 `HotkeyKeyNormalizer.Normalize` + 手工 13。

### 2. spec 验收标准（9 条）逐条映射

| 验收标准 | 覆盖 |
|---|---|
| X 关闭到托盘后按热键能显示并激活 | Task 6 + Task 8；手工 2 |
| 最小化/托盘恢复/反复切换后热键始终有效 | Task 6 + Task 8；手工 3、4、5 |
| 热键不再由 `Loaded`/`Closing` 决定；`MainWindow.xaml.cs` 不再出现相关符号 | Task 8 Step 3/Step 7；Task 10 Step 3 |
| 全进程只有 `App.OnExit` 一处生命周期注销，退出无残留 | Task 8；Task 10 Step 3 第二条 grep（恰好 2 处，含 `SettingsViewModel` 的用户意图注销，理由见下文「口径说明」） |
| 隐藏时按热键：显示/`Normal`/前台/有焦点；前台时按热键：最小化到托盘 | Task 7；手工 1、2、11 |
| 运行时改键立即生效；占用时提示并保留旧组合，配置不被污染 | Task 9；手工 6、7、8、13、14 |
| `WindowTogglePolicy` 与 `HotkeyToggleCoordinator` 单测全绿且不依赖 Win32/WPF 窗口实例 | Task 3、4；`dotnet test` |
| 捕获期间当前组合可被捕获，关闭后热键恢复 | Task 9（`HotkeyKeyNormalizer` + `Suspend`/`finally Resume`）；`HotkeyKeyNormalizerTests`；手工 6、13、15 |
| 启动注册失败只提示一次、应用可用；类名与路径与 spec 一致、无占位符 | Task 8（只在 `OnStartup` 一次 `Growl.Warning`）；本计划 File Structure 与 spec 组件清单对照（4 处偏差已单列并说明） |

**口径说明（避免误判验收失败）：** spec 写「全进程只有 `App.OnExit` 一处注销热键」，但 spec:217 又要求 `SettingsViewModel.Save` 在「未启用」时调 `Unregister()`。本计划按字面同时满足两者的方式是：`App.OnExit` 是唯一**生命周期**注销点；`SettingsViewModel` 的调用是**用户意图**注销。Task 10 Step 3 因此把 grep 预期定为「恰好 2 处」而不是 1 处。

### 3. 类型与方法命名一致性（跨任务核对）

| 名称 | 定义任务 | 定义处 | 使用方 |
|---|---|---|---|
| `HotkeyRegistrationResult` / `HotkeyRegistrationStatus` | Task 5 | `Core/Models/HotkeyRegistrationResult.cs` | Task 5 测试、Task 8 `GlobalHotkeyService`、Task 9 `SettingsViewModel`（`result.IsSuccess`） |
| `HotkeyRegistrationResult.Success/InvalidCombination/HostUnavailable/NotRegistered/FromRegisterError` | Task 5 | 同上 | Task 8 的 5 个返回点，名称与 Task 5 测试逐字一致 |
| `HotkeyNativeMethods.WmHotkey/ToWin32Modifiers/ToVirtualKey/RegisterHotKey/UnregisterHotKey` | Task 5 | `Core/Services/HotkeyNativeMethods.cs` | Task 6 `WndProc`（`WmHotkey`）、Task 8 `RegisterNative`/`UnregisterNative` |
| `HotkeyKeyNormalizer.Normalize(Key key, Key systemKey) → Key` | Task 9 | `Core/Services/HotkeyKeyNormalizer.cs` | Task 9 `Views/HotkeyCaptureWindow.xaml.cs`（`Window_KeyDown`）、`HotkeyKeyNormalizerTests` |
| `HotkeyIdAllocator.Next()` | Task 5 | `Core/Services/HotkeyIdAllocator.cs` | Task 8 `TryRegister`（`_idAllocator.Next()`） |
| `HotkeyMessageWindow.EnsureCreated()/Handle/HotkeyIdReceived/Dispose()` | Task 6 | `Core/Services/HotkeyMessageWindow.cs` | Task 8（`EnsureCreated`、`_host.Handle`、`_host.HotkeyIdReceived += OnHotkeyIdReceived`、`_host.Dispose()`） |
| `WindowActivitySnapshot(IsAppModalOpen, IsVisible, IsMinimized, IsActive)` | Task 3 | `Core/Services/WindowTogglePolicy.cs` | Task 4 假控制器与协调器、Task 7 `MainWindowVisibilityController.Capture`、Task 3/4 测试 |
| `WindowToggleAction.ShowAndActivate/HideToTray/ActivateExisting` | Task 3 | 同上 | Task 4 协调器 switch 与测试断言、Task 7 控制器方法名一一对应 |
| `IWindowVisibilityController.Capture/ShowAndActivate/HideToTray/ActivateExisting` | Task 4 | `Core/Services/IWindowVisibilityController.cs` | Task 4 假控制器、Task 7 真实现、Task 4 测试的 `nameof(...)` 断言 |
| `HotkeyToggleCoordinator.Execute() → WindowToggleAction` | Task 4 | `Core/Services/HotkeyToggleCoordinator.cs` | Task 8 `App.OnHotkeyPressed` |
| `IGlobalHotkeyService.TryRegister/UpdateHotkey/Suspend/Resume/Unregister/IsRegistered/HotkeyPressed` | Task 8 | `Core/Services/IGlobalHotkeyService.cs` | Task 8 `App`、Task 8/9 `SettingsViewModel` |
| `IForegroundWindowActivator.TryActivate(IntPtr) → bool` | Task 7 | `Infrastructure/Interop/ForegroundWindowActivator.cs` | Task 7 `MainWindowVisibilityController`、Task 7 测试 |
| `MainWindowVisibilityController(Window, IForegroundWindowActivator)` | Task 7 | `Infrastructure/MainWindowVisibilityController.cs` | Task 8 `App.OnStartup` 构造 |
| `HotkeySettings.Clone()` | Task 2 | `Core/Models/HotkeySettings.cs` | Task 9 构造函数与 `SaveHotkeySettings(HotkeySettings.Clone())` |

核对结论：无「Task N 定义 A、Task M 调用 B」的错配；`WindowToggleAction` 的 3 个成员与 `IWindowVisibilityController` 的 3 个动作方法同名，协调器 switch 不需要映射表。

### 4. 占位符扫描

已逐节检查本文件（含本轮新增的 Task 9 Step 1–10 与 Self-Review §7）：不存在未完成标记、未填写的待办项，也不存在把细节推给「上文/其它任务」的偷懒引用；每个实现步骤都给出可直接粘贴的完整代码（含 `HotkeyKeyNormalizer`、`HotkeyCaptureWindow.xaml.cs`、`SettingsViewModel.cs` 全文），每个测试步骤都给出完整测试文件内容与可复制的验证命令，且「期望失败信息」写到了具体编译器错误码（`CS0246`/`CS1061`/`CS0579`/`CS1501`）。

（本轮修订特意把原先用于「声明没有占位」的那几个字面缩写的举例写法去掉，避免执行者的占位符 grep 误报——本文件正文里不出现这些字面词。）

### 5. 与 spec 的偏差复述（上文 File Structure 已列，此处给结论）

| 偏差 | 是否影响 G1–G8 或验收标准 | 结论 |
|---|---|---|
| 新增 `HotkeyNativeMethods.cs`、`HotkeyIdAllocator.cs` | 不影响；增强 G7 的可测性 | 采纳 |
| 新增 `HotkeyKeyNormalizer.cs`，`Views/HotkeyCaptureWindow.xaml.cs` 改用它 | 修复 G5「不污染配置」的功能缺陷（B2）；不影响其余条目 | 采纳 |
| `Resume()` 返回 `HotkeyRegistrationResult` | 不影响；只是让 spec 文字里的 `NotRegistered` 可表达 | 采纳 |
| `SaveHotkeySettings(HotkeySettings.Clone())` | 增强 A7 与 spec:218 的「服务内仍是旧值」 | 采纳 |
| 改键失败按 `Status` 分档文案：`Conflict` 用 spec:217 原句，`InvalidCombination`/`HostUnavailable` 改为「快捷键无效或系统不可用（已保留原快捷键）」 | 不影响 G5/验收标准；避免把「无效组合」误报成「被占用」而让用户去关别的程序（spec:217 是单句实现示例，这里是文案细化，偏离已登记） | 采纳 |
| 任务数 10（把纯逻辑与宿主拆开、把适配器前移、把服务与接线合并） | 不影响功能覆盖；让每个提交都可构建 | 采纳 |

### 6. 执行风险（按严重度）

1. **`HotkeyMessageWindow` 在个别环境建窗失败**（spec 风险表「中」）：Task 6 已实现两级降级；若两级都失败，`TryRegister` 返回 `HostUnavailable`，`App.OnStartup` 只 Growl 一次，应用其余功能不受影响。执行时若手工步骤 1 完全无反应，先看 `Debug.WriteLine` 输出（DebugView）确认是宿主还是注册失败。
2. **`ForegroundWindowActivator` 的 `AttachThreadInput` 未解挂会耦合输入队列**（spec 风险表「中」）：Task 7 用 `try/finally` 强制解挂，且只在前台锁拒绝时进入；手工步骤 11 就是针对它的验收。
3. **TDD 例外带来的盲区**：Task 6、7（控制器部分）、8、9（UI 接线部分）没有自动化测试，只能靠 Task 10 的 15 条手工步骤。这是 spec 测试策略的既定取舍，不是本计划的疏漏；本轮已把 Task 9 的键位归一化抽成纯函数补上 2 个用例（总用例 42），把「Alt 组合静默失效」这条最危险的路径从纯手工覆盖里拿了出来。执行者若跳过 Task 10 的手工步骤，等于放弃 G1–G6 的验证。
4. **测试工程首次 restore**：Task 1 若在本机缓存被清理后执行，需要联网还原 xunit 2.5.3 / Microsoft.NET.Test.Sdk 17.8.0 / xunit.runner.visualstudio 2.5.3；已核实这三个版本在本机 NuGet 缓存中，可离线完成。
5. **`SettingsViewModel` 在根容器解析（`AddScoped` 但由 `App.ServiceProvider` 解析）实际上是单例**：因此编辑副本在 VM 生命周期内保留用户未保存的编辑（与现状一致，且 spec 未要求复位）；也正因为是单例，Task 9 的 `Save` 成功路径必须复位 `HasError`/`ErrorMessage`，否则旧红字会残留到下一次打开设置页。若将来把 `SettingsViewModel` 改成每次新建，这两条行为仍然正确。

### 7. 与 DECISIONS 的偏差登记（B4）

`DECISIONS.md` 第 4 行要求「实现阶段必须遵守本文件；与本文件冲突时以本文件为准，并在提交说明里记录偏差」。本节把本计划对 D5.3、D5.4 的偏离一次性登记，并在 Task 6/7/8/9 的 commit 步骤里用第二段 `-m` 复述，满足该要求。

| 决策 | 计划的做法 | 为什么（理由） | 影响面 | 留痕位置 |
|---|---|---|---|---|
| **D5.3** 不删除、不改名任何已有公开类/方法（只允许新增） | 删除 `IGlobalHotkeyService.Register(Window window, Key key, ModifierKeys modifiers)`；把 `UpdateHotkey` 的返回类型由 `bool` 改为 `HotkeyRegistrationResult` | D4（`DECISIONS.md:74`）已把 `IGlobalHotkeyService` 的形状裁给 #8，spec:151-164 又明确定义了 `HotkeyRegistrationResult` 与 5 个状态。`Register(Window, …)` 本身就是 G2 要消灭的「热键认识主窗口」耦合，保留它等于保留 Issue #8 的根因；`bool` 无法区分 `Conflict`/`InvalidCombination`/`HostUnavailable`，会让 B2 这类无效键被误报成「被占用」，也无法让 `Resume()` 表达 `NotRegistered` | 源码级破坏性变更，但调用方只有 3 个且已全部并入 Task 8 的同一个提交：`MainWindow.xaml.cs`（删除 `Register(this, …)` 调用）、`App.xaml.cs`（改为 `TryRegister(key, modifiers)`）、`ViewModels/SettingsViewModel.cs`（`bool registered = …` → `result.IsSuccess`）。仓库内无其它实现/调用方（`grep IGlobalHotkeyService` 只命中这 3 个文件 + 接口与实现本体），因此不存在编译断点残留 | Task 8 Step 9 的第二段 `-m`：`DECISIONS-D5.3: IGlobalHotkeyService 接口签名变更…` |
| **D5.4** 每个任务都必须先有失败测试，再有实现 | Task 6（`HotkeyMessageWindow`）、Task 7 的 `MainWindowVisibilityController`、Task 8（`GlobalHotkeyService` 整体）、Task 9 的 UI 接线（`SettingsViewModel` 与 `HotkeyCaptureWindow`）没有先行失败测试；Task 7 的 `ForegroundWindowActivator` 与 Task 9 的 `HotkeyKeyNormalizer` 仍然先写失败测试 | 这四处都需要 xunit 进程内不具备的东西：Task 6 需要 STA + 消息泵才能验证「收 `WM_HOTKEY`」；Task 7 的控制器需要真实 `Window`（`Show`/`Hide`/`WindowState`/`IsActive`/`OwnedWindows`）；Task 8 的每个方法都直接 `RegisterHotKey`/`UnregisterHotKey` 且要观察真实消息队列；Task 9 的 `Growl` 需要 `Application`、捕获窗口需要真实键盘输入管线。spec 的测试策略（spec:240-246）也把这些路径归入「集成与手工验证」。所有可抽出的纯逻辑（1409 映射、修饰键换算、id 回绕、结果语义、决策表、键位归一化）都已拆成纯类并用 42 个用例覆盖，避免「豁免」变成「整条链没有断言」 | 验证责任转移到 Task 10 的 15 条手工步骤（1–12 覆盖宿主/生命周期/模态/冲突，13–15 覆盖 Alt 组合、重启后生效、取消捕获后恢复）。若这些手工步骤被跳过，等于这四处没有任何验证 | Task 6 Step 4、Task 7 Step 7、Task 8 Step 9、Task 9 Step 10 的第二段 `-m`（均以 `DECISIONS-D5.4:` 开头） |

**用例计数口径：** 全量 `通过数 = 42`（冒烟 1 + `HotkeySettings` 3 + 决策策略 10 + 协调器 7 + 注册结果 6 + 修饰键 10 + id 分配 2 + 键位归一化 2 + 前台激活器 1）。Task 5 结束时的累计仍是 39，Task 7 结束时是 40，Task 9 结束时是 42；Task 10 Step 2 的验收口径固定为 42。若执行时采用了评审建议里「给 Task 6 加 STA 建窗测试」的可选方案（本轮未采纳，见评审报告修订记录），则该数字还要 +1 并同步 Task 6/7/9/10 四处。

---

## 回滚

无数据迁移、无 `settings.json` 字段变化、无数据库变更。回滚按提交逆序 `git revert`（或整体 revert Task 8/9 两个提交并删除 Task 5/6/7/9 新增文件）：恢复 `MainWindow.xaml.cs`/`App.xaml.cs`/`GlobalHotkeyService.cs`/`ViewModels/SettingsViewModel.cs`/`Views/HotkeyCaptureWindow.xaml.cs`，并删除 `Core/Services/HotkeyKeyNormalizer.cs` 与 `tests/BashCommandManager.Tests/HotkeyKeyNormalizerTests.cs`，即可回到旧行为；热键注册随进程退出自动释放，无需额外清理。测试工程可以保留（Task 1 的 csproj 排除与测试与回滚无关），`DefaultItemExcludes` 里的 `BashCommandManager.Cli\**` 也建议保留（#10 会用到）。
