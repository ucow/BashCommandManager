# 执行终端可配置化实施计划（Issue #9）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让用户可在设置中选择 cmd / Windows PowerShell / PowerShell 7 / Windows Terminal / 自定义程序执行批处理，参数模板、保持窗口打开、默认提权可配置，并保证旧 `settings.json` 无损迁移、wt/pwsh 缺失时降级 cmd 不崩溃。

**Architecture:** 数据模型集中在 `Core/Models`（纯 POCO，`SettingsService` 用嵌套 `AppSettings` 落盘并负责旧格式迁移）；命令行构造抽成纯函数 `TerminalCommandBuilder.Build`（不碰文件系统，可单测）；可执行文件探测独立为 `ExecutableLocator`；`CommandExecutor` 只做「现取配置 → 探测 → 构造 → `Process.Start` → 回写状态」，接口只追加 `ExecuteWithResult`；UI 在 `SettingsWindow` 增加「执行」Tab，`SettingsViewModel` 用编辑缓冲 + 内联校验，保存后下一次执行立即生效。

**Tech Stack:** .NET 8（SDK 10.0.301）、WPF、`net8.0-windows10.0.19041.0`、CommunityToolkit.Mvvm 8.2.2、HandyControl 3.5.1、System.Text.Json（BCL）、xunit 2.5.3（测试项目由 #8 创建）。

**Spec:** `docs/superpowers/specs/2026-11-08-execution-terminal-design.md`（权威需求）

**关联文档（冲突时以 DECISIONS.md 为准）:**
- `docs/superpowers/DECISIONS.md`（D1 串行顺序、D2 测试基础设施、D3 settings.json 唯一形状、D4 接口归属、D5 不变量的 7 条）
- `docs/superpowers/specs/2026-11-08-hotkey-tray-design.md` 的「接线变更」小节（#8 会先落地，`SettingsViewModel` 的最终形态以它为准）

---

## Global Constraints

以下约束对**每个任务**都生效，任务内的步骤不再重复：

- **目标框架**：`net8.0-windows10.0.19041.0`，`Nullable=enable`，`ImplicitUsings=enable`。不升级 TargetFramework，不加 `LangVersion`。
- **依赖**：不新增任何 NuGet 包（xunit 只存在于测试项目）。不引入 `System.Windows.Forms`。
- **测试项目只追加**：`tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj` 由 #8 创建（xunit 2.5.3 / Microsoft.NET.Test.Sdk 17.8.0 / xunit.runner.visualstudio 2.5.3，`UseWPF=true`，`ProjectReference` 主工程）。**本计划只往该目录追加 `.cs` 文件，禁止新建/修改任何 `.csproj`，禁止 `dotnet new`。**
- **主工程排除**：主 csproj 应有 `<DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>`（由 #8 按 DECISIONS D2 添加，`tests\**` 与 `BashCommandManager.Cli\**` 缺一不可）。本计划**不得**新增 `<Compile Remove>`，**不得**修改主 csproj；若 Task 0 只看到 `tests\**`，**停止并报告**（由 #8 补全，不要在本计划里改 csproj）。
- **settings.json 路径**：`%LOCALAPPDATA%\BashCommandManager\settings.json`。`SettingsService` 默认构造必须用同一字面路径（`Environment.SpecialFolder.LocalApplicationData` + `"BashCommandManager"`）。**不得**提前引入 #10 的 `DatabasePathResolver` 类型。
- **JSON 形状**：`{ "Hotkey": {...}, "Execution": {...} }`，枚举写**数字**（不注册 `JsonStringEnumConverter`）。序列化选项：`WriteIndented=true`、`PropertyNameCaseInsensitive=true`、`AllowTrailingCommas=true`、`ReadCommentHandling=Skip`。
- **不变量（DECISIONS D5）**：不删除/不改名已有 public 成员，只允许新增；`SettingsViewModel` 的 `SaveCommand`/`CancelCommand` 名称不变；`HotkeySettings` 不修改（#8 已给它加 `Clone()`）；`Command` 实体与数据库零改动；不做输出捕获。
- **默认行为等价**：默认配置必须是 `Cmd` + `/k {file}` + `KeepWindowOpen=true`，与现状逐字节一致。
- **中文编码**：所有新增注释/字符串一律 UTF-8，不得出现 `?` 或 `鏂` 之类乱码；提交前用 `git diff --stat` 目检文件编码正常。
- **TDD**：每个任务先写失败测试（C# 新类型场景允许以编译错误作为 RED），再写最小实现，测试全绿后才 commit。测试文件里禁止写真实 UAC 弹窗或长时间驻留窗口。
- **验证命令**（按任务使用，不必每步全量）：
  - 聚焦测试：`dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~<测试类名>"`
  - 全量测试：`dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`
  - 主工程构建（**只在 Task 8 执行一次**，全量 Release 约 2.5 分钟）：`dotnet build -c Release`
- **提交**：每个任务末尾 commit，前缀用 `feat(...)` / `test(...)` / `docs(...)`；只 `git add` 本任务涉及的文件，不 `git add -A`。
- **顺序依赖**：Task 0 必须通过（#8 已落地）才能开始；Task 1→2→3→4→5→6→7 顺序执行，不并行。
- **本计划不修改 `HotkeySettings.cs`、不改 `MainWindow.xaml`（「运行」「管理员运行」按钮绑定保持原样）。**

---

## File Structure

| 文件 | 动作 | 责任 |
|---|---|---|
| `Core/Models/TerminalKind.cs` | 新增 | 终端枚举 + `GetDisplayName()` 中文名 |
| `Core/Models/ExecutionSettings.cs` | 新增 | 全局执行配置（`Clone()` / `WithTerminal()`） |
| `Core/Models/AppSettings.cs` | 新增 | `settings.json` 根对象（`Hotkey` + `Execution`） |
| `Core/Models/TerminalLaunchPlan.cs` | 新增 | 一次启动的 `ProcessStartInfo` 映射（纯记录，不依赖 WPF） |
| `Core/Services/ExecutionResult.cs` | 新增 | `ExecutionOptions` / `ExecutionResult` 两个执行 DTO |
| `Core/Services/TerminalCommandBuilder.cs` | 新增 | 纯函数：默认模板、`Build`、`ValidateTemplate`；`TerminalConfigurationException` |
| `Core/Services/ExecutableLocator.cs` | 新增 | `IExecutableLocator` + 固定探测顺序 + `FindOnPath` 纯函数 |
| `Core/Services/SettingsService.cs` | 重写 | 嵌套 `AppSettings` 读写、旧格式迁移、原子写、备份、损坏回退 |
| `Core/Services/CommandExecutor.cs` | 修改 | 追加 `ExecuteWithResult`，`Execute` 委托它；加测试缝 `StartProcess` |
| `ViewModels/CommandListViewModel.cs` | 修改 | 两处执行调用合并为私有 `ExecuteCoreAsync`（命令名/按钮不变） |
| `ViewModels/SettingsViewModel.cs` | 重写 | 执行 Tab 的字段/命令/校验（基于 #8 的最终形态，含 `Suspend/Resume` 与先注册后落盘） |
| `Views/SettingsWindow.xaml` | 重写 | `Height=560 Width=560`，两个 Tab，错误提示移出 Tab 共用 |
| `App.xaml.cs` | 修改 | `ISettingsService` 工厂；新增 `IExecutableLocator` 注册 |
| `tests/BashCommandManager.Tests/ExecutionSettingsTests.cs` | 新增 | T1 模型默认值与 Clone 语义 |
| `tests/BashCommandManager.Tests/TerminalCommandBuilderTests.cs` | 新增 | B1–B18 + `ValidateTemplate` |
| `tests/BashCommandManager.Tests/ExecutableLocatorTests.cs` | 新增 | 探测顺序 + `FindOnPath` |
| `tests/BashCommandManager.Tests/SettingsServiceTests.cs` | 新增 | S1–S9（临时目录注入） |
| `tests/BashCommandManager.Tests/CommandExecutorTests.cs` | 新增 | 降级、自定义缺失、UAC 1223、配置无效、runas 装配 |
| `tests/BashCommandManager.Tests/CommandExecutorSmokeTests.cs` | 新增 | 环境变量门控的真实启动冒烟 |
| `tests/BashCommandManager.Tests/SettingsViewModelTests.cs` | 新增 | 模板联动 + 保存校验（不触发 Growl） |
| `tests/BashCommandManager.Tests/Fakes/FakeSettingsService.cs` | 新增 | `ISettingsService` 假实现 + 调用计数 |
| `tests/BashCommandManager.Tests/Fakes/FakeExecutableLocator.cs` | 新增 | 按终端类型返回预设路径 + 调用记录 |
| `tests/BashCommandManager.Tests/Fakes/FakeGlobalHotkeyService.cs` | 新增 | `IGlobalHotkeyService` 假实现（#8 接口） |
| `README.md` | 修改 | 功能特性 + 开发计划勾选 |
| `CLAUDE.md` | 修改 | 常用命令加 `dotnet test`；关键设计决策加终端执行配置；开发计划勾选 |

**归属说明（对设计文档文件清单的两处微调，均不改语义）：**
1. 设计文档 §1 把 `ExecutionOptions`/`ExecutionResult` 列在数据模型段，但文件表写 `Core/Services/ExecutionResult.cs`。本计划以文件表为准，两者都放 `Core/Services/ExecutionResult.cs`，命名空间 `BashCommandManager.Core.Services`。
2. `TerminalLaunchPlan` 单独成文件 `Core/Models/TerminalLaunchPlan.cs`（设计文档把它和 `ExecutionResult` 写在同一行）。#10 链接 `TerminalCommandBuilder.cs` 时必须**同时链接** `TerminalLaunchPlan.cs` 与 `ExecutionResult.cs`，否则编译失败；已写入本计划 Task 8 的交接说明。

---

### Task 0: 前置检查（#8 已落地）

**Files:**
- Read: `tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`
- Read: `ViewModels/SettingsViewModel.cs`
- Read: `Core/Services/IGlobalHotkeyService.cs`（#8 抽出的接口文件；若 #8 把它留在 `GlobalHotkeyService.cs` 顶部，则读该文件）
- Read: `Core/Models/HotkeySettings.cs`
- Read: `BashCommandManager.csproj`
- Modify: 无（本任务不写任何文件）

**Interfaces:**
- Consumes: #8 的产物（测试项目、`HotkeySettings.Clone()`、`IGlobalHotkeyService` 新签名、主 csproj 排除项）
- Produces: 无。本任务是门禁，不通过就停止。

- [ ] **Step 1: 确认测试项目存在且属性符合 D2**

Run:
```powershell
Test-Path tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
Select-String -Path tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj -Pattern "TargetFramework|UseWPF|IsTestProject|ImplicitUsings|Nullable|ProjectReference"
```
Expected: 第一条输出 `True`。若输出 `False`，**立即停止**，向主代理报告「#8 未落地：`tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj` 不存在，本计划不能继续」。不要自行创建项目。
第二条应能看到 `net8.0-windows10.0.19041.0`、`UseWPF` 为 `true`、`IsTestProject` 为 `true`、`ImplicitUsings` 为 `enable`、`Nullable` 为 `enable`、`ProjectReference` 指向 `..\..\BashCommandManager.csproj`。若 `ImplicitUsings` 未启用，停止并报告（本计划所有测试文件依赖隐式 `System`/`System.Collections.Generic`）。

- [ ] **Step 2: 确认测试基线全绿**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
```
Expected: 输出以 `Passed!` 结尾（`Failed: 0`）。若失败，停止并报告失败用例名。

- [ ] **Step 3: 确认 #8 对 `HotkeySettings` 与热键服务做了本计划依赖的改造**

Run:
```powershell
Select-String -Path Core\Models\HotkeySettings.cs -Pattern "Clone"
Select-String -Path Core\Services\IGlobalHotkeyService.cs -Pattern "UpdateHotkey|Suspend|Resume|HotkeyRegistrationResult"
Select-String -Path BashCommandManager.csproj -Pattern "DefaultItemExcludes"
```
Expected: 三条命令都有命中，且能看到：
- `HotkeySettings.Clone()` 存在；
- `HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers);`
- `void Suspend();` `HotkeyRegistrationResult Resume();`（`IGlobalHotkeyService : IDisposable`；`Resume()` **不是** `void`，与 #8 的接口文件逐字一致）
- `HotkeyRegistrationResult` 有 `bool IsSuccess`；且 `HotkeyRegistrationResult` / `HotkeyRegistrationStatus` 的命名空间是 `BashCommandManager.Core.Models`（测试 Fake 必须 `using BashCommandManager.Core.Models;`）；
- `DefaultItemExcludes` **同时**含 `tests\**` 与 `BashCommandManager.Cli\**`（#8 按 DECISIONS D2 落地；缺任一项都算门禁失败：**停止并报告**，不要自行改主 csproj）。

若 `IGlobalHotkeyService.cs` 不存在（接口仍在 `GlobalHotkeyService.cs` 内），改读 `Core\Services\GlobalHotkeyService.cs` 做同样确认。若签名与上述不一致，**停止并报告具体签名差异**，不要猜测重命名。

- [ ] **Step 4: 确认工作区干净**

Run:
```powershell
git status --short
git branch --show-current
```
Expected: `git status --short` 除 `docs/superpowers/` 未跟踪外无其他改动；分支为 `feat/v1.3.0`。若有与本计划无关的未提交改动，停止并报告。

- [ ] **Step 5: 记录基线（不提交）**

在任务执行记录里写下一行：`#8 基线：dotnet test 全绿，测试项目存在，HotkeySettings.Clone 与 IGlobalHotkeyService.Suspend/Resume 可用`。本任务无 commit。

---

### Task 1: 数据模型（TerminalKind / ExecutionSettings / AppSettings / TerminalLaunchPlan / ExecutionResult）

**Files:**
- Create: `Core/Models/TerminalKind.cs`
- Create: `Core/Models/ExecutionSettings.cs`
- Create: `Core/Models/AppSettings.cs`
- Create: `Core/Models/TerminalLaunchPlan.cs`
- Create: `Core/Services/ExecutionResult.cs`
- Test: `tests/BashCommandManager.Tests/ExecutionSettingsTests.cs`

**Interfaces:**
- Consumes: #8 的 `HotkeySettings`（`Enabled`/`Key`/`Modifiers`/`IsValid(HotkeySettings)`/`Clone()`）。
- Produces:
  - `public enum TerminalKind { Cmd = 0, WindowsPowerShell = 1, PowerShell7 = 2, WindowsTerminal = 3, Custom = 4 }`
  - `public static class TerminalKindDisplay { public static string GetDisplayName(this TerminalKind kind); }`
  - `public class ExecutionSettings { TerminalKind Terminal; string CustomExecutablePath; string ArgumentsTemplate; bool KeepWindowOpen; bool RunAsAdministrator; ExecutionSettings Clone(); ExecutionSettings WithTerminal(TerminalKind kind); }`
  - `public class AppSettings { HotkeySettings Hotkey; ExecutionSettings Execution; }`
  - `public sealed record TerminalLaunchPlan(string FileName, string Arguments, string? WorkingDirectory, bool UseShellExecute, string? Verb) { public const string RunAsVerb = "runas"; }`
  - `public sealed record ExecutionOptions(bool RunAsAdmin = false);`
  - `public sealed record ExecutionResult(bool Started, TerminalKind EffectiveTerminal, bool FallbackApplied, string? Notice, Exception? Error);`

- [ ] **Step 1: 写失败测试（模型默认值与 Clone 语义）**

Create `tests/BashCommandManager.Tests/ExecutionSettingsTests.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class ExecutionSettingsTests
{
    [Fact]
    public void 默认执行设置_与现有行为等价()
    {
        var settings = new ExecutionSettings();

        Assert.Equal(TerminalKind.Cmd, settings.Terminal);
        Assert.Equal(string.Empty, settings.CustomExecutablePath);
        Assert.Equal(string.Empty, settings.ArgumentsTemplate);
        Assert.True(settings.KeepWindowOpen);
        Assert.False(settings.RunAsAdministrator);
    }

    [Fact]
    public void Clone_修改副本不影响原对象()
    {
        var original = new ExecutionSettings
        {
            Terminal = TerminalKind.WindowsTerminal,
            KeepWindowOpen = false
        };

        var clone = original.Clone();
        clone.Terminal = TerminalKind.Custom;
        clone.ArgumentsTemplate = "{fileRaw}";
        clone.KeepWindowOpen = true;
        clone.RunAsAdministrator = true;

        Assert.Equal(TerminalKind.WindowsTerminal, original.Terminal);
        Assert.Equal(string.Empty, original.ArgumentsTemplate);
        Assert.False(original.KeepWindowOpen);
        Assert.False(original.RunAsAdministrator);
    }

    [Fact]
    public void WithTerminal_返回新实例且原对象不变()
    {
        var original = new ExecutionSettings { Terminal = TerminalKind.Cmd };

        var switched = original.WithTerminal(TerminalKind.PowerShell7);

        Assert.Equal(TerminalKind.PowerShell7, switched.Terminal);
        Assert.Equal(TerminalKind.Cmd, original.Terminal);
        Assert.NotSame(original, switched);
    }

    [Fact]
    public void AppSettings_默认包含快捷键与执行两段()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.Hotkey);
        Assert.NotNull(settings.Execution);
        Assert.Equal(TerminalKind.Cmd, settings.Execution.Terminal);
        Assert.Equal(Key.B, settings.Hotkey.Key);
    }

    [Theory]
    [InlineData(TerminalKind.Cmd, "cmd")]
    [InlineData(TerminalKind.WindowsPowerShell, "PowerShell")]
    [InlineData(TerminalKind.PowerShell7, "pwsh")]
    [InlineData(TerminalKind.WindowsTerminal, "wt")]
    [InlineData(TerminalKind.Custom, "自定义")]
    public void GetDisplayName_返回中文名称(TerminalKind kind, string expectedPart)
    {
        var display = kind.GetDisplayName();

        Assert.False(string.IsNullOrWhiteSpace(display));
        Assert.Contains(expectedPart, display);
    }

    [Fact]
    public void ExecutionResult_记录值相等()
    {
        var error = new InvalidOperationException("boom");
        var first = new ExecutionResult(false, TerminalKind.WindowsTerminal, true, "已降级", error);
        var second = new ExecutionResult(false, TerminalKind.WindowsTerminal, true, "已降级", error);

        Assert.Equal(first, second);
        Assert.Equal(TerminalKind.WindowsTerminal, first.EffectiveTerminal);
        Assert.True(first.FallbackApplied);
    }

    [Fact]
    public void TerminalLaunchPlan_运行身份常量固定为_runas()
    {
        Assert.Equal("runas", TerminalLaunchPlan.RunAsVerb);
    }
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~ExecutionSettingsTests"
```
Expected: 编译失败（exit code 非 0），错误为 `error CS0246: 未能找到类型或命名空间名"ExecutionSettings"` / `"TerminalKind"` / `"TerminalLaunchPlan"` / `"ExecutionResult"` 之一。若测试是通过的，说明实现已存在，停止并报告。

- [ ] **Step 3: 写最小实现（5 个文件）**

Create `Core/Models/TerminalKind.cs`:

```csharp
namespace BashCommandManager.Core.Models;

/// <summary>执行批处理脚本时使用的终端类型。</summary>
public enum TerminalKind
{
    Cmd = 0,
    WindowsPowerShell = 1,
    PowerShell7 = 2,
    WindowsTerminal = 3,
    Custom = 4
}

/// <summary>终端类型的中文显示名，供设置界面与 Growl 提示复用。</summary>
public static class TerminalKindDisplay
{
    public static string GetDisplayName(this TerminalKind kind) => kind switch
    {
        TerminalKind.Cmd => "命令提示符 (cmd)",
        TerminalKind.WindowsPowerShell => "Windows PowerShell",
        TerminalKind.PowerShell7 => "PowerShell 7 (pwsh)",
        TerminalKind.WindowsTerminal => "Windows Terminal (wt)",
        TerminalKind.Custom => "自定义程序",
        _ => "命令提示符 (cmd)"
    };
}
```

Create `Core/Models/ExecutionSettings.cs`:

```csharp
namespace BashCommandManager.Core.Models;

/// <summary>全局「执行/终端」配置，持久化在 settings.json 的 Execution 段。</summary>
public class ExecutionSettings
{
    public TerminalKind Terminal { get; set; } = TerminalKind.Cmd;

    public string CustomExecutablePath { get; set; } = string.Empty;

    /// <summary>参数模板；空白表示使用 TerminalCommandBuilder 的默认模板。</summary>
    public string ArgumentsTemplate { get; set; } = string.Empty;

    public bool KeepWindowOpen { get; set; } = true;

    public bool RunAsAdministrator { get; set; }

    public ExecutionSettings Clone() => (ExecutionSettings)MemberwiseClone();

    public ExecutionSettings WithTerminal(TerminalKind kind)
    {
        var clone = Clone();
        clone.Terminal = kind;
        return clone;
    }
}
```

Create `Core/Models/AppSettings.cs`:

```csharp
namespace BashCommandManager.Core.Models;

/// <summary>settings.json 的根对象：快捷键段 + 执行段。</summary>
public class AppSettings
{
    public HotkeySettings Hotkey { get; set; } = new();

    public ExecutionSettings Execution { get; set; } = new();
}
```

Create `Core/Models/TerminalLaunchPlan.cs`:

```csharp
namespace BashCommandManager.Core.Models;

/// <summary>一次终端启动所需的全部 ProcessStartInfo 字段，由纯函数构造。</summary>
public sealed record TerminalLaunchPlan(
    string FileName,
    string Arguments,
    string? WorkingDirectory,
    bool UseShellExecute,
    string? Verb)
{
    public const string RunAsVerb = "runas";
}
```

Create `Core/Services/ExecutionResult.cs`:

```csharp
using BashCommandManager.Core.Models;

namespace BashCommandManager.Core.Services;

/// <summary>单次执行的附加选项。</summary>
public sealed record ExecutionOptions(bool RunAsAdmin = false);

/// <summary>执行结果；Started=false 时调用方按 Notice/Error 给用户提示。</summary>
public sealed record ExecutionResult(
    bool Started,
    TerminalKind EffectiveTerminal,
    bool FallbackApplied,
    string? Notice,
    Exception? Error);
```

- [ ] **Step 4: 运行测试确认 GREEN**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~ExecutionSettingsTests"
```
Expected: `Passed!`，`Failed: 0`，`Passed: 11`（6 个 Fact + 5 条 `InlineData` 用例）。

- [ ] **Step 5: 提交**

```powershell
git add Core/Models/TerminalKind.cs Core/Models/ExecutionSettings.cs Core/Models/AppSettings.cs Core/Models/TerminalLaunchPlan.cs Core/Services/ExecutionResult.cs tests/BashCommandManager.Tests/ExecutionSettingsTests.cs
git commit -m "feat(execution): 终端可配置化数据模型"
```

---

### Task 2: TerminalCommandBuilder（纯函数，B1–B18）

**Files:**
- Create: `Core/Services/TerminalCommandBuilder.cs`
- Test: `tests/BashCommandManager.Tests/TerminalCommandBuilderTests.cs`

**Interfaces:**
- Consumes: `TerminalKind`、`ExecutionSettings`、`TerminalLaunchPlan`、`Command`（`Core/Models`）。
- Produces:
  - `public class TerminalConfigurationException : Exception { public TerminalConfigurationException(string message); }`
  - `public static class TerminalCommandBuilder {`
    `public static IReadOnlyList<string> AllowedPlaceholders { get; }`
    `public static string GetDefaultTemplate(TerminalKind kind, bool keepWindowOpen);`
    `public static TerminalLaunchPlan Build(ExecutionSettings settings, Command command, string? resolvedExecutable = null);`
    `public static IReadOnlyList<string> ValidateTemplate(string template); }`

- [ ] **Step 1: 写失败测试（B1–B9）**

Create `tests/BashCommandManager.Tests/TerminalCommandBuilderTests.cs`（先写这一批，Step 3 再追加）：

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests;

public class TerminalCommandBuilderTests
{
    private static Command MakeCommand(string filePath) => new() { Id = 1, Name = "demo", FilePath = filePath };

    private static TerminalLaunchPlan Build(ExecutionSettings settings, string filePath)
        => TerminalCommandBuilder.Build(settings, MakeCommand(filePath));

    [Fact]
    public void B1_Cmd_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal(@"/k ""C:\work\demo.bat""", plan.Arguments);
        Assert.Equal(@"C:\work", plan.WorkingDirectory);
        Assert.True(plan.UseShellExecute);
        Assert.Null(plan.Verb);
    }

    [Fact]
    public void B2_Cmd_中文与空格路径()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\工作 目录\我的 脚本.bat");

        Assert.Equal(@"/k ""C:\工作 目录\我的 脚本.bat""", plan.Arguments);
        Assert.Equal(@"C:\工作 目录", plan.WorkingDirectory);
    }

    [Fact]
    public void B3_Cmd_不保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, KeepWindowOpen = false };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal(@"/c ""C:\work\demo.bat""", plan.Arguments);
    }

    [Fact]
    public void B4_Cmd_管理员运行()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, RunAsAdministrator = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("runas", plan.Verb);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void B5_WindowsPowerShell_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsPowerShell, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("powershell.exe", plan.FileName);
        Assert.Equal(@"-NoExit -Command ""& 'C:\work\demo.bat'""", plan.Arguments);
        Assert.Equal(@"C:\work", plan.WorkingDirectory);
    }

    [Fact]
    public void B6_PowerShell7_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.PowerShell7, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("pwsh.exe", plan.FileName);
        Assert.Equal(@"-NoExit -Command ""& 'C:\work\demo.bat'""", plan.Arguments);
    }

    [Fact]
    public void B7_PowerShell_路径含单引号时翻倍()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsPowerShell, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\it's.bat");

        Assert.Equal(@"-NoExit -Command ""& 'C:\work\it''s.bat'""", plan.Arguments);
    }

    [Fact]
    public void B8_WindowsTerminal_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work 目录\demo.bat");

        Assert.Equal("wt.exe", plan.FileName);
        Assert.Equal(@"-d ""C:\work 目录"" cmd.exe /k ""C:\work 目录\demo.bat""", plan.Arguments);
        Assert.Equal(@"C:\work 目录", plan.WorkingDirectory);
    }

    [Fact]
    public void B9_WindowsTerminal_不保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, KeepWindowOpen = false };

        var plan = Build(settings, @"C:\work 目录\demo.bat");

        Assert.Equal(@"-d ""C:\work 目录"" cmd.exe /c ""C:\work 目录\demo.bat""", plan.Arguments);
    }
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~TerminalCommandBuilderTests"
```
Expected: 编译失败，`error CS0246: 未能找到类型或命名空间名"TerminalCommandBuilder"`（`TerminalConfigurationException` 同批）。exit code 非 0。

- [ ] **Step 3: 追加剩余测试（B10–B18 + ValidateTemplate）**

在 `TerminalCommandBuilderTests.cs` 的最后一个 `}` 之前追加：

```csharp
    [Fact]
    public void B10_WindowsTerminal_管理员运行()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, RunAsAdministrator = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("runas", plan.Verb);
    }

    [Fact]
    public void B11_WindowsTerminal_分号路径被引号包住()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\a;b\demo.bat");

        Assert.Equal(@"-d ""C:\a;b"" cmd.exe /k ""C:\a;b\demo.bat""", plan.Arguments);
        Assert.Equal(@"C:\a;b", plan.WorkingDirectory);
    }

    [Fact]
    public void B12_自定义程序_使用自定义路径与模板()
    {
        var settings = new ExecutionSettings
        {
            Terminal = TerminalKind.Custom,
            CustomExecutablePath = @"D:\Apps\ConEmu\ConEmu64.exe",
            ArgumentsTemplate = "-run {dir} {file}"
        };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal(@"D:\Apps\ConEmu\ConEmu64.exe", plan.FileName);
        Assert.Equal(@"-run ""C:\work"" ""C:\work\demo.bat""", plan.Arguments);
    }

    [Fact]
    public void B13_自定义程序_fileRaw_原样输出()
    {
        var settings = new ExecutionSettings
        {
            Terminal = TerminalKind.Custom,
            CustomExecutablePath = @"D:\Apps\ConEmu\ConEmu64.exe",
            ArgumentsTemplate = "-run {fileRaw}"
        };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal(@"-run C:\work\demo.bat", plan.Arguments);
    }

    [Fact]
    public void B14_传入已探测的可执行文件时优先使用()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal };
        var command = MakeCommand(@"C:\work\demo.bat");

        var plan = TerminalCommandBuilder.Build(settings, command, @"C:\Fake\wt.exe");

        Assert.Equal(@"C:\Fake\wt.exe", plan.FileName);
    }

    [Fact]
    public void B15_非法模板抛配置异常()
    {
        var unknownPlaceholder = new ExecutionSettings
        {
            Terminal = TerminalKind.Cmd,
            ArgumentsTemplate = "-Command \"& '{foo}'\""
        };
        var withoutFilePlaceholder = new ExecutionSettings
        {
            Terminal = TerminalKind.Cmd,
            ArgumentsTemplate = "-Command echo hi"
        };
        var unknownBesideFile = new ExecutionSettings
        {
            Terminal = TerminalKind.Cmd,
            ArgumentsTemplate = "{file} {foo}"
        };

        var first = Assert.Throws<TerminalConfigurationException>(() => Build(unknownPlaceholder, @"C:\work\demo.bat"));
        Assert.Contains("参数模板", first.Message);

        var second = Assert.Throws<TerminalConfigurationException>(() => Build(withoutFilePlaceholder, @"C:\work\demo.bat"));
        Assert.Contains("参数模板必须包含", second.Message);

        var third = Assert.Throws<TerminalConfigurationException>(() => Build(unknownBesideFile, @"C:\work\demo.bat"));
        Assert.Contains("{foo}", third.Message);
    }

    [Fact]
    public void B16_脚本路径含双引号时抛配置异常()
    {
        var command = MakeCommand("C:\\wo\"rk\\demo.bat");

        Assert.Throws<TerminalConfigurationException>(
            () => TerminalCommandBuilder.Build(new ExecutionSettings(), command));
    }

    [Fact]
    public void B17_磁盘不存在的路径仍返回计划()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd };

        var plan = Build(settings, @"Z:\nope\x.bat");

        Assert.Equal(@"Z:\nope", plan.WorkingDirectory);
        Assert.Equal(@"/k ""Z:\nope\x.bat""", plan.Arguments);
    }

    [Fact]
    public void B18_根目录文件的工作目录为盘符根()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd };

        var plan = Build(settings, @"C:\demo.bat");

        Assert.Equal(@"C:\", plan.WorkingDirectory);
    }

    [Fact]
    public void 相同输入两次调用返回相等结果()
    {
        var settings = new ExecutionSettings
        {
            Terminal = TerminalKind.WindowsTerminal,
            KeepWindowOpen = false,
            RunAsAdministrator = true
        };

        var first = Build(settings, @"C:\工作 目录\demo.bat");
        var second = Build(settings, @"C:\工作 目录\demo.bat");

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ValidateTemplate_默认模板无错误()
    {
        foreach (var kind in new[]
                 {
                     TerminalKind.Cmd, TerminalKind.WindowsPowerShell, TerminalKind.PowerShell7,
                     TerminalKind.WindowsTerminal, TerminalKind.Custom
                 })
        {
            Assert.Empty(TerminalCommandBuilder.ValidateTemplate(
                TerminalCommandBuilder.GetDefaultTemplate(kind, true)));
            Assert.Empty(TerminalCommandBuilder.ValidateTemplate(
                TerminalCommandBuilder.GetDefaultTemplate(kind, false)));
        }
    }

    [Fact]
    public void ValidateTemplate_空白模板视为合法()
    {
        Assert.Empty(TerminalCommandBuilder.ValidateTemplate(string.Empty));
        Assert.Empty(TerminalCommandBuilder.ValidateTemplate("   "));
    }

    [Fact]
    public void ValidateTemplate_未知占位符报错()
    {
        // 模板必须同时带 {file}：否则实现循环结束后还会追加「必须包含 {file}…」，错误数会变成 2 条。
        var errors = TerminalCommandBuilder.ValidateTemplate("{file} {foo}");

        Assert.Single(errors);
        Assert.Contains("{foo}", errors[0]);
    }

    [Fact]
    public void ValidateTemplate_缺少文件类占位符报错()
    {
        var errors = TerminalCommandBuilder.ValidateTemplate("-Command echo hi");

        Assert.Single(errors);
        Assert.Contains("{file}", errors[0]);
    }

    [Fact]
    public void ValidateTemplate_花括号不配对报错()
    {
        var missingClose = TerminalCommandBuilder.ValidateTemplate("-Command \"{file\"");
        Assert.Contains("不配对", Assert.Single(missingClose));

        var extraClose = TerminalCommandBuilder.ValidateTemplate("-Command \"} {file}\"");
        Assert.Contains("不配对", Assert.Single(extraClose));
    }
```

- [ ] **Step 4: 再次运行确认 RED（仍是类型缺失）**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~TerminalCommandBuilderTests"
```
Expected: 仍为 `error CS0246`（未实现），不是测试逻辑错误。

- [ ] **Step 5: 写实现**

Create `Core/Services/TerminalCommandBuilder.cs`:

```csharp
using BashCommandManager.Core.Models;
using System.Text;

namespace BashCommandManager.Core.Services;

/// <summary>终端参数模板非法时抛出，消息面向用户可直接展示。</summary>
public class TerminalConfigurationException : Exception
{
    public TerminalConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>把 ExecutionSettings + Command 映射为 TerminalLaunchPlan 的纯函数集合。</summary>
public static class TerminalCommandBuilder
{
    private static readonly string[] AllPlaceholders =
    {
        "{file}", "{dir}", "{filePs}", "{dirPs}", "{fileRaw}", "{dirRaw}"
    };

    private static readonly string[] FilePlaceholders = { "{file}", "{filePs}", "{fileRaw}" };

    public static IReadOnlyList<string> AllowedPlaceholders => AllPlaceholders;

    public static string GetDefaultTemplate(TerminalKind kind, bool keepWindowOpen) => kind switch
    {
        TerminalKind.Cmd => keepWindowOpen ? "/k {file}" : "/c {file}",
        TerminalKind.WindowsPowerShell => keepWindowOpen
            ? "-NoExit -Command \"& '{filePs}'\""
            : "-Command \"& '{filePs}'\"",
        TerminalKind.PowerShell7 => keepWindowOpen
            ? "-NoExit -Command \"& '{filePs}'\""
            : "-Command \"& '{filePs}'\"",
        TerminalKind.WindowsTerminal => keepWindowOpen
            ? "-d {dir} cmd.exe /k {file}"
            : "-d {dir} cmd.exe /c {file}",
        TerminalKind.Custom => "{file}",
        _ => keepWindowOpen ? "/k {file}" : "/c {file}"
    };

    public static TerminalLaunchPlan Build(ExecutionSettings settings, Command command, string? resolvedExecutable = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(command);

        var kind = Enum.IsDefined(settings.Terminal) ? settings.Terminal : TerminalKind.Cmd;
        var keepWindowOpen = settings.KeepWindowOpen;
        var template = string.IsNullOrWhiteSpace(settings.ArgumentsTemplate)
            ? GetDefaultTemplate(kind, keepWindowOpen)
            : settings.ArgumentsTemplate;

        if (!ContainsFilePlaceholder(template))
        {
            throw new TerminalConfigurationException(
                $"参数模板必须包含 {string.Join("、", FilePlaceholders)} 之一，当前模板：{template}");
        }

        var filePath = command.FilePath ?? string.Empty;
        if (filePath.Contains('"'))
        {
            throw new TerminalConfigurationException($"脚本路径包含非法字符 \"：{filePath}");
        }

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var arguments = Expand(template, filePath, directory);

        string fileName;
        if (kind == TerminalKind.Custom)
        {
            var customPath = resolvedExecutable ?? settings.CustomExecutablePath;
            if (string.IsNullOrWhiteSpace(customPath))
            {
                throw new TerminalConfigurationException("未配置自定义终端程序路径");
            }

            fileName = customPath.Trim();
        }
        else
        {
            fileName = resolvedExecutable ?? GetLogicalFileName(kind);
        }

        return new TerminalLaunchPlan(
            fileName,
            arguments,
            string.IsNullOrEmpty(directory) ? null : directory,
            UseShellExecute: true,
            Verb: settings.RunAsAdministrator ? TerminalLaunchPlan.RunAsVerb : null);
    }

    public static IReadOnlyList<string> ValidateTemplate(string template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            return errors;
        }

        var hasFilePlaceholder = false;
        var index = 0;
        while (index < template.Length)
        {
            var current = template[index];

            if (current == '}')
            {
                errors.Add("参数模板中的花括号不配对：存在多余的 }");
                return errors;
            }

            if (current != '{')
            {
                index++;
                continue;
            }

            var closeIndex = template.IndexOf('}', index + 1);
            if (closeIndex < 0)
            {
                errors.Add("参数模板中的花括号不配对：缺少 }");
                return errors;
            }

            var token = template.Substring(index, closeIndex - index + 1);
            if (Array.IndexOf(AllPlaceholders, token) < 0)
            {
                errors.Add($"未知占位符 {token}，允许的占位符：{string.Join(" ", AllPlaceholders)}");
            }
            else if (Array.IndexOf(FilePlaceholders, token) >= 0)
            {
                hasFilePlaceholder = true;
            }

            index = closeIndex + 1;
        }

        if (!hasFilePlaceholder)
        {
            errors.Add($"参数模板必须包含 {string.Join("、", FilePlaceholders)} 之一");
        }

        return errors;
    }

    private static string GetLogicalFileName(TerminalKind kind) => kind switch
    {
        TerminalKind.Cmd => "cmd.exe",
        TerminalKind.WindowsPowerShell => "powershell.exe",
        TerminalKind.PowerShell7 => "pwsh.exe",
        TerminalKind.WindowsTerminal => "wt.exe",
        _ => "cmd.exe"
    };

    private static bool ContainsFilePlaceholder(string template)
    {
        foreach (var placeholder in FilePlaceholders)
        {
            if (template.Contains(placeholder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Expand(string template, string filePath, string directory)
    {
        var builder = new StringBuilder(template.Length + filePath.Length + 8);
        var index = 0;
        while (index < template.Length)
        {
            var current = template[index];
            if (current != '{')
            {
                builder.Append(current);
                index++;
                continue;
            }

            var closeIndex = template.IndexOf('}', index + 1);
            if (closeIndex < 0)
            {
                throw new TerminalConfigurationException("参数模板中的花括号不配对：缺少 }");
            }

            var token = template.Substring(index, closeIndex - index + 1);
            builder.Append(ExpandToken(token, filePath, directory));
            index = closeIndex + 1;
        }

        return builder.ToString();
    }

    private static string ExpandToken(string token, string filePath, string directory) => token switch
    {
        "{file}" => Quote(filePath),
        "{dir}" => Quote(directory),
        "{filePs}" => EscapePowerShellLiteral(filePath),
        "{dirPs}" => EscapePowerShellLiteral(directory),
        "{fileRaw}" => filePath,
        "{dirRaw}" => directory,
        _ => throw new TerminalConfigurationException(
            $"未知占位符 {token}，允许的占位符：{string.Join(" ", AllPlaceholders)}")
    };

    private static string Quote(string value) => $"\"{value}\"";

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");
}
```

- [ ] **Step 6: 运行测试确认 GREEN**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~TerminalCommandBuilderTests"
```
Expected: `Passed!`，`Failed: 0`。若 B15/B16 失败，检查异常类型是否 `TerminalConfigurationException`（而不是 `ArgumentException`）。

- [ ] **Step 7: 提交**

```powershell
git add Core/Services/TerminalCommandBuilder.cs tests/BashCommandManager.Tests/TerminalCommandBuilderTests.cs
git commit -m "feat(execution): 终端命令行构造纯函数"
```

---

### Task 3: ExecutableLocator（探测顺序 + FindOnPath）

**Files:**
- Create: `Core/Services/ExecutableLocator.cs`
- Test: `tests/BashCommandManager.Tests/ExecutableLocatorTests.cs`

**Interfaces:**
- Consumes: `TerminalKind`。
- Produces:
  - `public interface IExecutableLocator { string? Resolve(TerminalKind kind, string customExecutablePath); }`
  - `public class ExecutableLocator : IExecutableLocator { public ExecutableLocator(); public ExecutableLocator(Func<string, string?>? getEnvironmentVariable, Func<string, bool>? fileExists); public static string? FindOnPath(string fileName, string? pathVariable, Func<string, bool> fileExists); }`

- [ ] **Step 1: 写失败测试**

Create `tests/BashCommandManager.Tests/ExecutableLocatorTests.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests;

public class ExecutableLocatorTests
{
    private static ExecutableLocator CreateLocator(
        Dictionary<string, string?> environment,
        Func<string, bool> fileExists)
        => new(name => environment.TryGetValue(name, out var value) ? value : null, fileExists);

    [Fact]
    public void FindOnPath_命中第一个存在的文件()
    {
        var found = ExecutableLocator.FindOnPath(
            "tool.exe",
            @"C:\empty;C:\tools;C:\other",
            candidate => string.Equals(candidate, @"C:\tools\tool.exe", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"C:\tools\tool.exe", found);
    }

    [Fact]
    public void FindOnPath_未命中返回_null()
    {
        var found = ExecutableLocator.FindOnPath("tool.exe", @"C:\a;C:\b", _ => false);

        Assert.Null(found);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindOnPath_PATH_为空返回_null(string? pathVariable)
    {
        Assert.Null(ExecutableLocator.FindOnPath("tool.exe", pathVariable, _ => true));
    }

    [Fact]
    public void FindOnPath_忽略带引号的目录与空条目()
    {
        var probed = new List<string>();

        var found = ExecutableLocator.FindOnPath(
            "tool.exe",
            "\"C:\\quoted\";;C:\\good",
            candidate =>
            {
                probed.Add(candidate);
                return string.Equals(candidate, @"C:\good\tool.exe", StringComparison.OrdinalIgnoreCase);
            });

        Assert.Equal(@"C:\good\tool.exe", found);
        Assert.DoesNotContain(probed, candidate => candidate.StartsWith(@"C:\quoted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cmd_优先使用_COMSPEC()
    {
        var environment = new Dictionary<string, string?> { ["COMSPEC"] = @"C:\Fake\cmd.exe" };
        var locator = CreateLocator(environment, _ => false);

        var resolved = locator.Resolve(TerminalKind.Cmd, string.Empty);

        Assert.Equal(@"C:\Fake\cmd.exe", resolved);
    }

    [Fact]
    public void Cmd_没有_COMSPEC_时回退_System32()
    {
        var environment = new Dictionary<string, string?>
        {
            ["COMSPEC"] = null,
            ["SystemRoot"] = @"C:\Windows"
        };
        var locator = CreateLocator(environment, candidate => candidate == @"C:\Windows\System32\cmd.exe");

        var resolved = locator.Resolve(TerminalKind.Cmd, string.Empty);

        Assert.Equal(@"C:\Windows\System32\cmd.exe", resolved);
    }

    [Fact]
    public void Cmd_与_WindowsPowerShell_永不返回_null()
    {
        var locator = CreateLocator(new Dictionary<string, string?>(), _ => false);

        Assert.Equal("cmd.exe", locator.Resolve(TerminalKind.Cmd, string.Empty));
        Assert.Equal("powershell.exe", locator.Resolve(TerminalKind.WindowsPowerShell, string.Empty));
    }

    [Fact]
    public void WindowsPowerShell_优先使用_System32_全路径()
    {
        var environment = new Dictionary<string, string?> { ["SystemRoot"] = @"C:\Windows" };
        var locator = CreateLocator(
            environment,
            candidate => candidate == @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        var resolved = locator.Resolve(TerminalKind.WindowsPowerShell, string.Empty);

        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", resolved);
    }

    [Fact]
    public void PowerShell7_按_ProgramFiles_到_PATH_顺序探测()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ProgramFiles"] = @"C:\PF",
            ["ProgramFiles(x86)"] = @"C:\PF86",
            ["LOCALAPPDATA"] = @"C:\LAD",
            ["PATH"] = @"C:\bin"
        };

        var locator = CreateLocator(environment, candidate => candidate == @"C:\PF\PowerShell\7\pwsh.exe");
        Assert.Equal(@"C:\PF\PowerShell\7\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\PF86\PowerShell\7\pwsh.exe");
        Assert.Equal(@"C:\PF86\PowerShell\7\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\LAD\Microsoft\WindowsApps\pwsh.exe");
        Assert.Equal(@"C:\LAD\Microsoft\WindowsApps\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\bin\pwsh.exe");
        Assert.Equal(@"C:\bin\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, _ => false);
        Assert.Null(locator.Resolve(TerminalKind.PowerShell7, string.Empty));
    }

    [Fact]
    public void WindowsTerminal_先查_WindowsApps_再查_PATH()
    {
        var environment = new Dictionary<string, string?>
        {
            ["LOCALAPPDATA"] = @"C:\LAD",
            ["PATH"] = @"C:\bin"
        };

        var locator = CreateLocator(environment, candidate => candidate == @"C:\LAD\Microsoft\WindowsApps\wt.exe");
        Assert.Equal(@"C:\LAD\Microsoft\WindowsApps\wt.exe", locator.Resolve(TerminalKind.WindowsTerminal, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\bin\wt.exe");
        Assert.Equal(@"C:\bin\wt.exe", locator.Resolve(TerminalKind.WindowsTerminal, string.Empty));

        locator = CreateLocator(environment, _ => false);
        Assert.Null(locator.Resolve(TerminalKind.WindowsTerminal, string.Empty));
    }

    [Fact]
    public void Custom_文件存在返回路径_不存在返回_null()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BashCommandManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var existing = Path.Combine(directory, "term.exe");
        File.WriteAllText(existing, string.Empty);

        try
        {
            var locator = new ExecutableLocator();

            Assert.Equal(existing, locator.Resolve(TerminalKind.Custom, existing));
            Assert.Null(locator.Resolve(TerminalKind.Custom, Path.Combine(directory, "missing.exe")));
            Assert.Null(locator.Resolve(TerminalKind.Custom, "   "));
            Assert.Null(locator.Resolve(TerminalKind.Custom, string.Empty));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~ExecutableLocatorTests"
```
Expected: 编译失败，`error CS0246: 未能找到类型或命名空间名"ExecutableLocator"`。

- [ ] **Step 3: 写实现**

Create `Core/Services/ExecutableLocator.cs`:

```csharp
using BashCommandManager.Core.Models;

namespace BashCommandManager.Core.Services;

/// <summary>把终端类型解析为可直接启动的可执行文件路径。</summary>
public interface IExecutableLocator
{
    string? Resolve(TerminalKind kind, string customExecutablePath);
}

/// <summary>按固定探测顺序查找终端程序；环境变量与文件存在性可注入以便单测。</summary>
public class ExecutableLocator : IExecutableLocator
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;

    public ExecutableLocator() : this(null, null)
    {
    }

    public ExecutableLocator(Func<string, string?>? getEnvironmentVariable, Func<string, bool>? fileExists)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
    }

    public string? Resolve(TerminalKind kind, string customExecutablePath) => kind switch
    {
        TerminalKind.Cmd => ResolveCmd(),
        TerminalKind.WindowsPowerShell => ResolveWindowsPowerShell(),
        TerminalKind.PowerShell7 => ResolvePowerShell7(),
        TerminalKind.WindowsTerminal => ResolveWindowsTerminal(),
        TerminalKind.Custom => ResolveCustom(customExecutablePath),
        _ => ResolveCmd()
    };

    /// <summary>在 PATH 中查找文件名；忽略空条目与包含引号的目录。</summary>
    public static string? FindOnPath(string fileName, string? pathVariable, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator))
        {
            var directory = entry.Trim();
            if (directory.Length == 0)
            {
                continue;
            }

            if (directory.Contains('"'))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string ResolveCmd()
    {
        var comspec = _getEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrWhiteSpace(comspec))
        {
            return comspec!;
        }

        var systemRoot = _getEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            var candidate = Path.Combine(systemRoot!, "System32", "cmd.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return "cmd.exe";
    }

    private string ResolveWindowsPowerShell()
    {
        var systemRoot = _getEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            var candidate = Path.Combine(systemRoot!, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return "powershell.exe";
    }

    private string? ResolvePowerShell7()
    {
        foreach (var candidate in GetWellKnownPwshPaths())
        {
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("pwsh.exe", _getEnvironmentVariable("PATH"), _fileExists);
    }

    private IEnumerable<string> GetWellKnownPwshPaths()
    {
        var programFiles = _getEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles!, "PowerShell", "7", "pwsh.exe");
        }

        var programFilesX86 = _getEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86!, "PowerShell", "7", "pwsh.exe");
        }

        var localAppData = _getEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData!, "Microsoft", "WindowsApps", "pwsh.exe");
        }
    }

    private string? ResolveWindowsTerminal()
    {
        var localAppData = _getEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var candidate = Path.Combine(localAppData!, "Microsoft", "WindowsApps", "wt.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("wt.exe", _getEnvironmentVariable("PATH"), _fileExists);
    }

    private string? ResolveCustom(string customExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(customExecutablePath))
        {
            return null;
        }

        var path = customExecutablePath.Trim();
        return _fileExists(path) ? path : null;
    }
}
```

- [ ] **Step 4: 运行测试确认 GREEN**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~ExecutableLocatorTests"
```
Expected: `Passed!`，`Failed: 0`。

- [ ] **Step 5: 提交**

```powershell
git add Core/Services/ExecutableLocator.cs tests/BashCommandManager.Tests/ExecutableLocatorTests.cs
git commit -m "feat(execution): 终端可执行文件探测"
```

---

### Task 4: SettingsService 重写（嵌套格式 / 迁移 / 原子写 / 备份）

**Files:**
- Modify: `Core/Services/SettingsService.cs`（整文件替换，原 1–70 行）
- Modify: `App.xaml.cs`（`ConfigureServices` 方法里 `ISettingsService` 的注册处，即现有 `services.AddSingleton<ISettingsService, SettingsService>();` 那一行：改为工厂注册，配合新构造函数）
- Test: `tests/BashCommandManager.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: `AppSettings`、`HotkeySettings`（含 `Clone()`）、`ExecutionSettings`、`TerminalCommandBuilder.GetDefaultTemplate(TerminalKind, bool)`。
- Produces:
  - `public interface ISettingsService { HotkeySettings GetHotkeySettings(); void SaveHotkeySettings(HotkeySettings settings); event EventHandler? HotkeySettingsChanged; ExecutionSettings GetExecutionSettings(); void SaveExecutionSettings(ExecutionSettings settings); event EventHandler? ExecutionSettingsChanged; }`
  - `public class SettingsService : ISettingsService { public SettingsService(string? settingsDirectory = null); }`

- [ ] **Step 1: 写失败测试（S1–S4）**

Create `tests/BashCommandManager.Tests/SettingsServiceTests.cs`（先写这一批）：

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BashCommandManager.Tests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    [Fact]
    public void S1_旧格式可读且执行段为默认()
    {
        WriteSettings("{\"Enabled\":true,\"Key\":66,\"Modifiers\":3}");

        var service = new SettingsService(_directory);

        Assert.True(service.GetHotkeySettings().Enabled);
        Assert.Equal(Key.B, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, service.GetHotkeySettings().Modifiers);
        Assert.Equal(TerminalKind.Cmd, service.GetExecutionSettings().Terminal);
        Assert.True(service.GetExecutionSettings().KeepWindowOpen);
        Assert.Equal("/k {file}", service.GetExecutionSettings().ArgumentsTemplate);
        Assert.Equal(string.Empty, service.GetExecutionSettings().CustomExecutablePath);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.v1.bak")));
    }

    [Fact]
    public void S2_旧格式的自定义快捷键原样保留()
    {
        WriteSettings("{\"Enabled\":false,\"Key\":116,\"Modifiers\":4}");

        var service = new SettingsService(_directory);

        Assert.False(service.GetHotkeySettings().Enabled);
        Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Shift, service.GetHotkeySettings().Modifiers);
    }

    [Fact]
    public void S3_新格式字段一致_缺段用默认_未知字段忽略()
    {
        WriteSettings(
            "{\"Hotkey\":{\"Enabled\":false,\"Key\":116,\"Modifiers\":4}," +
            "\"Execution\":{\"Terminal\":3,\"CustomExecutablePath\":\"\",\"ArgumentsTemplate\":\"-d {dir} cmd.exe /k {file}\",\"KeepWindowOpen\":true,\"RunAsAdministrator\":true}," +
            "\"Unknown\":123}");

        var service = new SettingsService(_directory);

        Assert.False(service.GetHotkeySettings().Enabled);
        Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Shift, service.GetHotkeySettings().Modifiers);
        Assert.Equal(TerminalKind.WindowsTerminal, service.GetExecutionSettings().Terminal);
        Assert.Equal("-d {dir} cmd.exe /k {file}", service.GetExecutionSettings().ArgumentsTemplate);
        Assert.True(service.GetExecutionSettings().RunAsAdministrator);

        WriteSettings("{\"Hotkey\":{\"Enabled\":true,\"Key\":66,\"Modifiers\":3}}");
        var missingExecution = new SettingsService(_directory);

        Assert.Equal(TerminalKind.Cmd, missingExecution.GetExecutionSettings().Terminal);
        Assert.Equal("/k {file}", missingExecution.GetExecutionSettings().ArgumentsTemplate);
    }

    [Fact]
    public void S4_损坏_json_不抛异常并备份为_corrupt_bak()
    {
        const string corrupt = "{ this is not json";
        WriteSettings(corrupt);

        var service = new SettingsService(_directory);

        var backupPath = Path.Combine(_directory, "settings.json.corrupt.bak");
        Assert.True(File.Exists(backupPath));
        Assert.Equal(corrupt, File.ReadAllText(backupPath));
        Assert.Equal(Key.B, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, service.GetHotkeySettings().Modifiers);
        Assert.Equal(TerminalKind.Cmd, service.GetExecutionSettings().Terminal);
    }
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsServiceTests"
```
Expected: 编译失败，`error CS1061: "ISettingsService"未包含"GetExecutionSettings"的定义` 与 `error CS1729: "SettingsService"不包含采用 1 个参数的构造函数`（或 `CS1739`）。

- [ ] **Step 3: 追加测试（S5–S9）**

在 `SettingsServiceTests.cs` 的最后一个 `}` 之前追加：

```csharp
    [Fact]
    public void S5_两次保存互不抹掉另一段()
    {
        var service = new SettingsService(_directory);
        var hotkeyChanged = 0;
        var executionChanged = 0;
        service.HotkeySettingsChanged += (_, _) => hotkeyChanged++;
        service.ExecutionSettingsChanged += (_, _) => executionChanged++;

        service.SaveExecutionSettings(new ExecutionSettings
        {
            Terminal = TerminalKind.WindowsTerminal,
            ArgumentsTemplate = "-d {dir} cmd.exe /k {file}"
        });
        service.SaveHotkeySettings(new HotkeySettings
        {
            Enabled = false,
            Key = Key.F5,
            Modifiers = ModifierKeys.Shift
        });

        var reloaded = new SettingsService(_directory);
        Assert.Equal(TerminalKind.WindowsTerminal, reloaded.GetExecutionSettings().Terminal);
        Assert.Equal(Key.F5, reloaded.GetHotkeySettings().Key);
        Assert.Equal(1, hotkeyChanged);
        Assert.Equal(1, executionChanged);

        reloaded.SaveHotkeySettings(new HotkeySettings
        {
            Enabled = true,
            Key = Key.C,
            Modifiers = ModifierKeys.Control
        });

        var second = new SettingsService(_directory);
        Assert.Equal(Key.C, second.GetHotkeySettings().Key);
        Assert.Equal(TerminalKind.WindowsTerminal, second.GetExecutionSettings().Terminal);
    }

    [Fact]
    public void S6_旧格式首次保存生成_v1_bak_且文件变为嵌套格式()
    {
        const string legacyJson = "{\"Enabled\":true,\"Key\":66,\"Modifiers\":3}";
        WriteSettings(legacyJson);
        var service = new SettingsService(_directory);

        service.SaveHotkeySettings(service.GetHotkeySettings());

        var backupPath = Path.Combine(_directory, "settings.json.v1.bak");
        Assert.True(File.Exists(backupPath));
        Assert.Equal(legacyJson, File.ReadAllText(backupPath));

        var savedJson = File.ReadAllText(SettingsPath);
        // 原子写：File.Move 成功后 tmp 会被带走，不残留；settings.json 能被 JsonDocument 整体解析即证明是完整 JSON。
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.tmp")));
        using var document = JsonDocument.Parse(savedJson);
        Assert.True(document.RootElement.TryGetProperty("Hotkey", out _));
        Assert.True(document.RootElement.TryGetProperty("Execution", out _));
        Assert.False(document.RootElement.TryGetProperty("Enabled", out _));
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("Hotkey").GetProperty("Key").ValueKind);
    }

    [Fact]
    public void S7_非法终端与空白模板被归一化()
    {
        WriteSettings(
            "{\"Hotkey\":{\"Enabled\":true,\"Key\":66,\"Modifiers\":3}," +
            "\"Execution\":{\"Terminal\":99,\"CustomExecutablePath\":\"   \",\"ArgumentsTemplate\":\"  \",\"KeepWindowOpen\":true,\"RunAsAdministrator\":false}}");

        var service = new SettingsService(_directory);

        Assert.Equal(TerminalKind.Cmd, service.GetExecutionSettings().Terminal);
        Assert.Equal("/k {file}", service.GetExecutionSettings().ArgumentsTemplate);
        Assert.Equal(string.Empty, service.GetExecutionSettings().CustomExecutablePath);
    }

    [Fact]
    public void S8_写入失败时抛异常但内存值已更新()
    {
        const string originalJson = "{\"Enabled\":true,\"Key\":66,\"Modifiers\":3}";
        WriteSettings(originalJson);

        var service = new SettingsService(_directory);
        var tempPath = Path.Combine(_directory, "settings.json.tmp");

        // 用 FileShare.None 占用 tmp，让 File.WriteAllText 抛可预期的 IOException。
        // 不要用「把 settings.json 建成目录」制造失败：那种情况 File.Move 抛的是
        // UnauthorizedAccessException，它不继承 IOException，Assert.ThrowsAny<IOException> 必红。
        using var locked = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var newHotkey = new HotkeySettings
        {
            Enabled = false,
            Key = Key.F5,
            Modifiers = ModifierKeys.Shift
        };

        Assert.ThrowsAny<IOException>(() => service.SaveHotkeySettings(newHotkey));

        // 内存值先更新、落盘失败不回滚（假设 Q2）。
        Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
        Assert.False(service.GetHotkeySettings().Enabled);
        // 写失败不破坏原文件内容（原子写语义）。
        Assert.Equal(originalJson, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void S9_保存成功后_tmp_不残留且文件为完整_json()
    {
        var service = new SettingsService(_directory);

        service.SaveExecutionSettings(new ExecutionSettings
        {
            Terminal = TerminalKind.PowerShell7,
            KeepWindowOpen = false
        });

        var tempPath = Path.Combine(_directory, "settings.json.tmp");
        Assert.False(File.Exists(tempPath));

        var savedJson = File.ReadAllText(SettingsPath);
        Assert.EndsWith("}", savedJson.TrimEnd());

        using var document = JsonDocument.Parse(savedJson);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("Hotkey", out _));
        Assert.True(document.RootElement.TryGetProperty("Execution", out var execution));
        Assert.Equal(JsonValueKind.Number, execution.GetProperty("Terminal").ValueKind);
        Assert.False(execution.GetProperty("KeepWindowOpen").GetBoolean());
    }
```

- [ ] **Step 4: 再次运行确认 RED（仍是接口/构造函数缺失）**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsServiceTests"
```
Expected: 仍为 `CS1061`/`CS1729` 编译错误。

- [ ] **Step 5: 重写实现并改 DI 注册**

Replace `Core/Services/SettingsService.cs` 全部内容为：

```csharp
using BashCommandManager.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BashCommandManager.Core.Services;

public interface ISettingsService
{
    HotkeySettings GetHotkeySettings();
    void SaveHotkeySettings(HotkeySettings settings);
    event EventHandler? HotkeySettingsChanged;

    ExecutionSettings GetExecutionSettings();
    void SaveExecutionSettings(ExecutionSettings settings);
    event EventHandler? ExecutionSettingsChanged;
}

/// <summary>settings.json 的唯一读写入口：嵌套 AppSettings、旧格式迁移、原子写与备份。</summary>
public class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string TempFileName = "settings.json.tmp";
    private const string LegacyBackupFileName = "settings.json.v1.bak";
    private const string CorruptBackupFileName = "settings.json.corrupt.bak";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private AppSettings _appSettings;
    private bool _needsMigration;

    public event EventHandler? HotkeySettingsChanged;
    public event EventHandler? ExecutionSettingsChanged;

    public SettingsService(string? settingsDirectory = null)
    {
        _settingsDirectory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BashCommandManager");
        _settingsPath = Path.Combine(_settingsDirectory, SettingsFileName);

        if (!Directory.Exists(_settingsDirectory))
        {
            Directory.CreateDirectory(_settingsDirectory);
        }

        (_appSettings, _needsMigration) = LoadAll();
    }

    public HotkeySettings GetHotkeySettings() => _appSettings.Hotkey;

    public ExecutionSettings GetExecutionSettings() => _appSettings.Execution;

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        _appSettings.Hotkey = settings ?? new HotkeySettings();
        SaveAll();
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveExecutionSettings(ExecutionSettings settings)
    {
        _appSettings.Execution = settings ?? new ExecutionSettings();
        SaveAll();
        ExecutionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private (AppSettings Settings, bool NeedsMigration) LoadAll()
    {
        if (!File.Exists(_settingsPath))
        {
            return Normalize(new AppSettings(), false);
        }

        string json;
        try
        {
            json = File.ReadAllText(_settingsPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取 settings.json 失败: {ex.Message}");
            BackupCorrupt();
            return Normalize(new AppSettings(), false);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = document.RootElement;
            if (HasProperty(root, "Hotkey") || HasProperty(root, "Execution"))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
                return Normalize(settings, false);
            }

            if (HasProperty(root, "Enabled") || HasProperty(root, "Key") || HasProperty(root, "Modifiers"))
            {
                var hotkey = JsonSerializer.Deserialize<HotkeySettings>(json, SerializerOptions) ?? new HotkeySettings();
                var settings = new AppSettings { Hotkey = hotkey };
                return Normalize(settings, true);
            }

            return Normalize(new AppSettings(), false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解析 settings.json 失败: {ex.Message}");
            BackupCorrupt();
            return Normalize(new AppSettings(), false);
        }
    }

    private static (AppSettings Settings, bool NeedsMigration) Normalize(AppSettings settings, bool needsMigration)
    {
        if (settings.Hotkey is null || !HotkeySettings.IsValid(settings.Hotkey))
        {
            settings.Hotkey = new HotkeySettings();
        }

        if (settings.Execution is null)
        {
            settings.Execution = new ExecutionSettings();
        }

        if (!Enum.IsDefined(settings.Execution.Terminal))
        {
            settings.Execution.Terminal = TerminalKind.Cmd;
        }

        settings.Execution.CustomExecutablePath =
            (settings.Execution.CustomExecutablePath ?? string.Empty).Trim();

        var template = settings.Execution.ArgumentsTemplate;
        settings.Execution.ArgumentsTemplate = string.IsNullOrWhiteSpace(template)
            ? TerminalCommandBuilder.GetDefaultTemplate(
                settings.Execution.Terminal,
                settings.Execution.KeepWindowOpen)
            : template.Trim();

        return (settings, needsMigration);
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void SaveAll()
    {
        Directory.CreateDirectory(_settingsDirectory);

        var migrating = _needsMigration;
        if (migrating)
        {
            BackupLegacy();
        }

        var json = JsonSerializer.Serialize(_appSettings, SerializerOptions);
        var tempPath = Path.Combine(_settingsDirectory, TempFileName);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);

        if (migrating)
        {
            _needsMigration = false;
        }
    }

    private void BackupLegacy()
    {
        var backupPath = Path.Combine(_settingsDirectory, LegacyBackupFileName);
        if (File.Exists(backupPath) || !File.Exists(_settingsPath))
        {
            return;
        }

        File.Copy(_settingsPath, backupPath, overwrite: false);
    }

    private void BackupCorrupt()
    {
        var backupPath = Path.Combine(_settingsDirectory, CorruptBackupFileName);
        if (File.Exists(backupPath) || !File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            File.Copy(_settingsPath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"备份损坏的 settings.json 失败: {ex.Message}");
        }
    }
}
```

在 `App.xaml.cs` 的 `ConfigureServices` 中，找到现有这一行（#8 注释「新增：设置服务（单例）」下面那一行）：

```csharp
        services.AddSingleton<ISettingsService, SettingsService>();
```

改为：

```csharp
        services.AddSingleton<ISettingsService>(_ => new SettingsService());
```

- [ ] **Step 6: 运行测试确认 GREEN**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsServiceTests"
```
Expected: `Passed!`，`Failed: 0`（S1–S9 共 9 个用例）。若 S8 报「未抛出异常」，检查 `FileShare.None` 的 tmp 占用是否在 `SaveHotkeySettings` 之前建立；若 S8 报别的异常类型，说明实现没有把写盘异常向外抛。注意：S8 靠占用 `settings.json.tmp` 制造可预期的 `IOException`，**不要**改回「把 `settings.json` 建成目录」——`File.Move` 到目录抛的是 `UnauthorizedAccessException`，它不是 `IOException`，断言会必红。

- [ ] **Step 7: 提交**

```powershell
git add Core/Services/SettingsService.cs App.xaml.cs tests/BashCommandManager.Tests/SettingsServiceTests.cs
git commit -m "feat(settings): 执行设置持久化与旧格式迁移"
```

---

### Task 5: CommandExecutor.ExecuteWithResult（降级 / 自定义缺失 / UAC 取消 / 配置无效）

**Files:**
- Modify: `Core/Services/CommandExecutor.cs`（整文件替换，原 1–64 行）
- Test: `tests/BashCommandManager.Tests/CommandExecutorTests.cs`
- Test: `tests/BashCommandManager.Tests/CommandExecutorSmokeTests.cs`
- Test: `tests/BashCommandManager.Tests/Fakes/FakeSettingsService.cs`
- Test: `tests/BashCommandManager.Tests/Fakes/FakeExecutableLocator.cs`

**Interfaces:**
- Consumes: `ISettingsService.GetExecutionSettings()`、`IExecutableLocator.Resolve(...)`、`TerminalCommandBuilder.Build(...)`、`ExecutionOptions`/`ExecutionResult`、`CommandStatus`。
- Produces:
  - `public interface ICommandExecutor { void Execute(Command command, bool runAsAdmin = false); ExecutionResult ExecuteWithResult(Command command, ExecutionOptions options); bool IsRunning(int commandId); void RegisterProcess(int commandId, Process process); void UnregisterProcess(int commandId); }`
  - `public class CommandExecutor : ICommandExecutor { public CommandExecutor(ISettingsService settingsService, IExecutableLocator executableLocator); protected virtual void StartProcess(Process process); }`

- [ ] **Step 1: 写 Fake 与失败测试**

Create `tests/BashCommandManager.Tests/Fakes/FakeSettingsService.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeSettingsService : ISettingsService
{
    public HotkeySettings Hotkey { get; set; } = new();

    public ExecutionSettings Execution { get; set; } = new();

    public int SaveHotkeyCalls { get; private set; }

    public int SaveExecutionCalls { get; private set; }

    public event EventHandler? HotkeySettingsChanged;
    public event EventHandler? ExecutionSettingsChanged;

    public HotkeySettings GetHotkeySettings() => Hotkey;

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        SaveHotkeyCalls++;
        Hotkey = settings;
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ExecutionSettings GetExecutionSettings() => Execution;

    public void SaveExecutionSettings(ExecutionSettings settings)
    {
        SaveExecutionCalls++;
        Execution = settings;
        ExecutionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

Create `tests/BashCommandManager.Tests/Fakes/FakeExecutableLocator.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeExecutableLocator : IExecutableLocator
{
    public Dictionary<TerminalKind, string?> Results { get; } = new();

    public List<TerminalKind> Calls { get; } = new();

    public string? Resolve(TerminalKind kind, string customExecutablePath)
    {
        Calls.Add(kind);
        return Results.TryGetValue(kind, out var value) ? value : null;
    }
}
```

Create `tests/BashCommandManager.Tests/CommandExecutorTests.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests;

public class CommandExecutorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BashCommandManager.Tests",
        Guid.NewGuid().ToString("N"));

    public CommandExecutorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string CreateScript(string name = "demo.bat")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "@echo off\r\nexit /b 0\r\n");
        return path;
    }

    [Theory]
    [InlineData(TerminalKind.PowerShell7, "PowerShell 7")]
    [InlineData(TerminalKind.WindowsTerminal, "Windows Terminal")]
    public void 终端缺失时降级_cmd_并给出提示(TerminalKind missing, string displayPart)
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = missing }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[missing] = null;
        locator.Results[TerminalKind.Cmd] = @"C:\Windows\System32\cmd.exe";

        var executor = new NotStartingCommandExecutor(settings, locator);
        var command = new Command { Id = 7, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal(TerminalKind.Cmd, result.EffectiveTerminal);
        Assert.True(result.FallbackApplied);
        Assert.NotNull(result.Notice);
        Assert.Contains(displayPart, result.Notice!);
        Assert.Contains("cmd", result.Notice!);
        Assert.Equal(new[] { missing, TerminalKind.Cmd }, locator.Calls);
        Assert.Equal(CommandStatus.Running, command.Status);
        Assert.True(executor.IsRunning(command.Id));
    }

    [Fact]
    public void 终端存在时不降级()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.PowerShell7 }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.PowerShell7] = @"C:\Fake\pwsh.exe";

        var executor = new CapturingCommandExecutor(settings, locator);
        var command = new Command { Id = 8, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal(TerminalKind.PowerShell7, result.EffectiveTerminal);
        Assert.False(result.FallbackApplied);
        Assert.Null(result.Notice);
        Assert.Equal(@"C:\Fake\pwsh.exe", executor.StartInfo!.FileName);
    }

    [Fact]
    public void Cmd_探测为_null_时直接使用逻辑名且不降级()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;

        var executor = new CapturingCommandExecutor(settings, locator);
        var command = new Command { Id = 9, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.False(result.FallbackApplied);
        Assert.Null(result.Notice);
        Assert.Equal("cmd.exe", executor.StartInfo!.FileName);
    }

    [Fact]
    public void 自定义程序不存在时返回错误且不启动()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Custom,
                CustomExecutablePath = @"C:\nope\term.exe"
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Custom] = null;

        var executor = new NotStartingCommandExecutor(settings, locator);
        var command = new Command { Id = 10, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.False(result.Started);
        var error = Assert.IsType<FileNotFoundException>(result.Error);
        Assert.Contains(@"C:\nope\term.exe", error.Message);
        Assert.False(result.FallbackApplied);
        Assert.Equal(CommandStatus.Failed, command.Status);
        Assert.False(executor.IsRunning(command.Id));
    }

    [Fact]
    public void 取消_UAC_时状态回到_Idle_并返回取消提示()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var executor = new ThrowingCommandExecutor(settings, locator, new Win32Exception(1223));
        var command = new Command { Id = 11, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions(RunAsAdmin: true));

        Assert.False(result.Started);
        Assert.Null(result.Error);
        Assert.Equal("已取消管理员授权", result.Notice);
        Assert.Equal(CommandStatus.Idle, command.Status);
        Assert.False(executor.IsRunning(command.Id));
    }

    [Fact]
    public void 其它启动失败时状态为_Failed()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var executor = new ThrowingCommandExecutor(settings, locator, new Win32Exception(2, "系统找不到指定的文件。"));
        var command = new Command { Id = 12, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.False(result.Started);
        Assert.NotNull(result.Error);
        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void 脚本不存在时抛_FileNotFoundException_且状态为_Failed()
    {
        var settings = new FakeSettingsService();
        var executor = new NotStartingCommandExecutor(settings, new FakeExecutableLocator());
        var command = new Command
        {
            Id = 13,
            Name = "missing",
            FilePath = Path.Combine(_directory, "missing.bat")
        };

        Assert.Throws<FileNotFoundException>(
            () => executor.ExecuteWithResult(command, new ExecutionOptions()));
        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void 模板非法时返回配置异常且不启动()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Cmd,
                ArgumentsTemplate = "{file} {foo}"
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var executor = new NotStartingCommandExecutor(settings, locator);
        var command = new Command { Id = 14, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.False(result.Started);
        Assert.IsType<TerminalConfigurationException>(result.Error);
        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void 本次管理员运行只作用于本次且不改动全局配置()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd, RunAsAdministrator = false }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;
        var executor = new CapturingCommandExecutor(settings, locator);
        var script = CreateScript();
        var command = new Command { Id = 15, Name = "demo", FilePath = script };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions(RunAsAdmin: true));

        Assert.True(result.Started);
        Assert.Equal("runas", executor.StartInfo!.Verb);
        Assert.True(executor.StartInfo.UseShellExecute);
        Assert.Equal("cmd.exe", executor.StartInfo.FileName);
        Assert.Equal(@"/k """ + script + @"""", executor.StartInfo.Arguments);
        Assert.Equal(_directory, executor.StartInfo.WorkingDirectory);
        Assert.False(settings.Execution.RunAsAdministrator);
    }

    [Fact]
    public void 全局管理员运行配置直接作用于启动计划()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd, RunAsAdministrator = true }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;
        var executor = new CapturingCommandExecutor(settings, locator);
        var command = new Command { Id = 16, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal("runas", executor.StartInfo!.Verb);
    }

    [Fact]
    public void Execute_成功时不抛异常_取消时抛_OperationCanceledException()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var command = new Command { Id = 17, Name = "demo", FilePath = CreateScript() };

        var success = new NotStartingCommandExecutor(settings, locator);
        success.Execute(command, runAsAdmin: false);
        Assert.Equal(CommandStatus.Running, command.Status);

        var cancelledCommand = new Command { Id = 18, Name = "demo", FilePath = CreateScript("demo2.bat") };
        var cancelled = new ThrowingCommandExecutor(settings, locator, new Win32Exception(1223));
        Assert.Throws<OperationCanceledException>(() => cancelled.Execute(cancelledCommand, runAsAdmin: true));
        Assert.Equal(CommandStatus.Idle, cancelledCommand.Status);
    }

    private sealed class NotStartingCommandExecutor : CommandExecutor
    {
        public NotStartingCommandExecutor(ISettingsService settingsService, IExecutableLocator locator)
            : base(settingsService, locator)
        {
        }

        protected override void StartProcess(Process process)
        {
        }
    }

    private sealed class CapturingCommandExecutor : CommandExecutor
    {
        public CapturingCommandExecutor(ISettingsService settingsService, IExecutableLocator locator)
            : base(settingsService, locator)
        {
        }

        public ProcessStartInfo? StartInfo { get; private set; }

        protected override void StartProcess(Process process)
        {
            StartInfo = process.StartInfo;
        }
    }

    private sealed class ThrowingCommandExecutor : CommandExecutor
    {
        private readonly Exception _error;

        public ThrowingCommandExecutor(
            ISettingsService settingsService,
            IExecutableLocator locator,
            Exception error)
            : base(settingsService, locator)
        {
            _error = error;
        }

        protected override void StartProcess(Process process) => throw _error;
    }
}
```

Create `tests/BashCommandManager.Tests/CommandExecutorSmokeTests.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests;

public class CommandExecutorSmokeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BashCommandManager.Tests",
        Guid.NewGuid().ToString("N"));

    public CommandExecutorSmokeTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void 真实启动_cmd_并在脚本退出后置为_Completed()
    {
        if (Environment.GetEnvironmentVariable("BCM_EXECUTOR_SMOKE") != "1")
        {
            return;
        }

        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Cmd,
                KeepWindowOpen = false
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;

        var executor = new CommandExecutor(settings, locator);
        var script = Path.Combine(_directory, "smoke.bat");
        File.WriteAllText(script, "@echo off\r\nexit /b 0\r\n");
        var command = new Command { Id = 99, Name = "smoke", FilePath = script };

        using var completed = new ManualResetEventSlim();
        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Command.Status) && command.Status == CommandStatus.Completed)
            {
                completed.Set();
            }
        };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), $"实际状态: {command.Status}");
    }
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~CommandExecutorTests"
```
Expected: 编译失败，`error CS0246: 未能找到类型或命名空间名"ExecutionOptions"`（若 Task 1 未完成）或 `error CS1061: "ICommandExecutor"未包含"ExecuteWithResult"的定义` / `error CS1729: "CommandExecutor"不包含采用 2 个参数的构造函数`。

- [ ] **Step 3: 重写实现**

Replace `Core/Services/CommandExecutor.cs` 全部内容为：

```csharp
using BashCommandManager.Core.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BashCommandManager.Core.Services;

public interface ICommandExecutor
{
    void Execute(Command command, bool runAsAdmin = false);

    ExecutionResult ExecuteWithResult(Command command, ExecutionOptions options);

    bool IsRunning(int commandId);
    void RegisterProcess(int commandId, Process process);
    void UnregisterProcess(int commandId);
}

/// <summary>
/// 启动批处理脚本；终端选择、模板展开与降级由 TerminalCommandBuilder / ExecutableLocator 承担，
/// 本类只负责组装 ProcessStartInfo、启动进程与回写 CommandStatus。
/// </summary>
public class CommandExecutor : ICommandExecutor
{
    private readonly Dictionary<int, Process> _runningProcesses = new();
    private readonly ISettingsService _settingsService;
    private readonly IExecutableLocator _executableLocator;

    public CommandExecutor(ISettingsService settingsService, IExecutableLocator executableLocator)
    {
        _settingsService = settingsService;
        _executableLocator = executableLocator;
    }

    public void Execute(Command command, bool runAsAdmin = false)
    {
        var result = ExecuteWithResult(command, new ExecutionOptions(runAsAdmin));
        if (result.Started)
        {
            return;
        }

        if (result.Error is not null)
        {
            throw result.Error;
        }

        throw new OperationCanceledException(result.Notice ?? "执行已取消");
    }

    public ExecutionResult ExecuteWithResult(Command command, ExecutionOptions options)
    {
        if (!File.Exists(command.FilePath))
        {
            command.Status = CommandStatus.Failed;
            throw new FileNotFoundException($"文件不存在: {command.FilePath}");
        }

        var settings = _settingsService.GetExecutionSettings();
        if (options.RunAsAdmin && !settings.RunAsAdministrator)
        {
            settings = settings.Clone();
            settings.RunAsAdministrator = true;
        }

        var resolved = _executableLocator.Resolve(settings.Terminal, settings.CustomExecutablePath);
        string? notice = null;
        var fallbackApplied = false;

        if (resolved is null && IsFallbackTerminal(settings.Terminal))
        {
            var originalTerminal = settings.Terminal;
            settings = settings.WithTerminal(TerminalKind.Cmd);
            resolved = _executableLocator.Resolve(TerminalKind.Cmd, settings.CustomExecutablePath);
            fallbackApplied = true;
            notice = $"未检测到 {originalTerminal.GetDisplayName()}，已改用 cmd 启动";
        }
        else if (resolved is null && settings.Terminal == TerminalKind.Custom)
        {
            command.Status = CommandStatus.Failed;
            return new ExecutionResult(
                false,
                TerminalKind.Custom,
                false,
                null,
                new FileNotFoundException($"自定义程序不存在: {settings.CustomExecutablePath}"));
        }

        TerminalLaunchPlan plan;
        try
        {
            plan = TerminalCommandBuilder.Build(settings, command, resolved);
        }
        catch (TerminalConfigurationException ex)
        {
            command.Status = CommandStatus.Failed;
            return new ExecutionResult(false, settings.Terminal, fallbackApplied, notice, ex);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            Arguments = plan.Arguments,
            WorkingDirectory = plan.WorkingDirectory ?? string.Empty,
            UseShellExecute = plan.UseShellExecute
        };

        if (plan.Verb is not null)
        {
            startInfo.Verb = plan.Verb;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            UnregisterProcess(command.Id);
            command.Status = process.ExitCode == 0 ? CommandStatus.Completed : CommandStatus.Failed;
        };

        try
        {
            StartProcess(process);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            command.Status = CommandStatus.Idle;
            return new ExecutionResult(false, settings.Terminal, fallbackApplied, "已取消管理员授权", null);
        }
        catch (Exception ex)
        {
            command.Status = CommandStatus.Failed;
            return new ExecutionResult(false, settings.Terminal, fallbackApplied, notice, ex);
        }

        RegisterProcess(command.Id, process);
        command.Status = CommandStatus.Running;

        return new ExecutionResult(true, settings.Terminal, fallbackApplied, notice, null);
    }

    public bool IsRunning(int commandId) => _runningProcesses.ContainsKey(commandId);

    public void RegisterProcess(int commandId, Process process)
    {
        _runningProcesses[commandId] = process;
    }

    public void UnregisterProcess(int commandId)
    {
        _runningProcesses.Remove(commandId);
    }

    /// <summary>真实启动进程；测试重写此方法以拦截 Process.Start，避免弹窗与 UAC。</summary>
    protected virtual void StartProcess(Process process) => process.Start();

    private static bool IsFallbackTerminal(TerminalKind kind) =>
        kind is TerminalKind.PowerShell7 or TerminalKind.WindowsTerminal;
}
```

方法名固定为 `StartProcess`，与 `CommandExecutorTests` 里的 `protected override void StartProcess(Process process)` 一一对应，不要改名。

- [ ] **Step 4: 运行测试确认 GREEN**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~CommandExecutorTests"
```
Expected: `Passed!`，`Failed: 0`（含 2 条 Theory 数据行）。

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "Category=Smoke"
```
Expected: `Passed!`，且**没有弹出 cmd 窗口**（未设置 `BCM_EXECUTOR_SMOKE` 时直接返回）。

- [ ] **Step 5: 提交**

```powershell
git add Core/Services/CommandExecutor.cs tests/BashCommandManager.Tests/CommandExecutorTests.cs tests/BashCommandManager.Tests/CommandExecutorSmokeTests.cs tests/BashCommandManager.Tests/Fakes/FakeSettingsService.cs tests/BashCommandManager.Tests/Fakes/FakeExecutableLocator.cs
git commit -m "feat(execution): 执行结果与终端降级"
```

**本任务结束时应用还无法启动**：`CommandExecutor` 需要 `IExecutableLocator`，但 DI 注册在 Task 6 补。不要在此任务执行 `dotnet run`。

---

### Task 6: CommandListViewModel 接线与 DI 注册

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs:179-213`
- Modify: `App.xaml.cs`（`ConfigureServices` 的 `// Services` 区块，`services.AddSingleton<ICommandExecutor, CommandExecutor>();` 之后新增 `IExecutableLocator` 注册）
- Test: 无（UI 接线，属 DECISIONS D5.4 的显式豁免：由 Task 8 的 build + 手工冒烟覆盖；`Growl` 需要已显示窗口，单元测试无法可靠断言）。豁免理由与手工验证条目登记在 Self-Review 第 5 节第 11 条。

**Interfaces:**
- Consumes: `ICommandExecutor.ExecuteWithResult(Command, ExecutionOptions)`、`ExecutionResult`（`Started`/`EffectiveTerminal`/`FallbackApplied`/`Notice`/`Error`）、`TerminalConfigurationException`。
- Produces: 私有方法 `private async Task ExecuteCoreAsync(Command command, bool runAsAdmin)`；`services.AddSingleton<IExecutableLocator, ExecutableLocator>();`。

- [ ] **Step 1: 确认接线前的失败基线**

本任务不加测试。先记录当前两个命令的生成名（供 XAML 绑定不变）：`ExecuteCommandAsync` → `ExecuteCommand`，`ExecuteAsAdminAsync` → `ExecuteAsAdmin`。

- [ ] **Step 2: 替换两个执行方法**

把 `ViewModels/CommandListViewModel.cs` 的 179–213 行：

```csharp
    [RelayCommand]
    private async Task ExecuteCommandAsync(Command command)
    {
        try
        {
            _executor.Execute(command, runAsAdmin: false);
            await _commandService.RecordExecutionAsync(command.Id);
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"执行失败: {ex.Message}",
                WaitTime = 3
            });
        }
    }

    [RelayCommand]
    private async Task ExecuteAsAdminAsync(Command command)
    {
        try
        {
            _executor.Execute(command, runAsAdmin: true);
            await _commandService.RecordExecutionAsync(command.Id);
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"执行失败: {ex.Message}",
                WaitTime = 3
            });
        }
    }
```

替换为：

```csharp
    [RelayCommand]
    private Task ExecuteCommandAsync(Command command) => ExecuteCoreAsync(command, runAsAdmin: false);

    [RelayCommand]
    private Task ExecuteAsAdminAsync(Command command) => ExecuteCoreAsync(command, runAsAdmin: true);

    private async Task ExecuteCoreAsync(Command command, bool runAsAdmin)
    {
        ExecutionResult result;
        try
        {
            result = _executor.ExecuteWithResult(command, new ExecutionOptions(RunAsAdmin: runAsAdmin));
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"执行失败: {ex.Message}",
                WaitTime = 3
            });
            return;
        }

        if (result.Notice is not null)
        {
            Growl.Warning(new GrowlInfo
            {
                Message = result.Notice,
                WaitTime = 5
            });
        }

        if (!result.Started)
        {
            if (result.Error is TerminalConfigurationException configurationError)
            {
                Growl.Error(new GrowlInfo
                {
                    Message = $"执行配置无效: {configurationError.Message}",
                    WaitTime = 5
                });
            }
            else if (result.Error is not null)
            {
                Growl.Error(new GrowlInfo
                {
                    Message = $"执行失败: {result.Error.Message}",
                    WaitTime = 5
                });
            }

            return;
        }

        await _commandService.RecordExecutionAsync(command.Id);
    }
```

`CommandListViewModel.cs` 顶部已 `using BashCommandManager.Core.Services;`（第 3 行），`ExecutionResult`/`ExecutionOptions`/`TerminalConfigurationException` 都在该命名空间，无需新增 using。

- [ ] **Step 3: 注册 IExecutableLocator**

在 `App.xaml.cs` 的 `ConfigureServices` 中，`// Services` 区块里，在：

```csharp
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
```

之后插入一行：

```csharp
        services.AddSingleton<IExecutableLocator, ExecutableLocator>();
```

最终该区块应为：

```csharp
        // Services
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<ICommandService, CommandService>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddSingleton<IExecutableLocator, ExecutableLocator>();
```

- [ ] **Step 4: 编译验证（不跑 GUI）**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~CommandExecutorTests"
```
Expected: `Passed!`，`Failed: 0`（编译通过即证明 `CommandListViewModel` 引用符合新接口；测试项目会编译主工程）。若报 `error CS1061: "ICommandExecutor"未包含"ExecuteWithResult"`，说明 Task 5 未落地。

- [ ] **Step 5: 提交**

```powershell
git add ViewModels/CommandListViewModel.cs App.xaml.cs
git commit -m "feat(execution): 接线执行入口与依赖注入"
```

---

### Task 7: 设置界面「执行」Tab 与 SettingsViewModel

**Files:**
- Modify: `Views/SettingsWindow.xaml`（整文件替换）
- Modify: `ViewModels/SettingsViewModel.cs`（整文件替换）
- Test: `tests/BashCommandManager.Tests/SettingsViewModelTests.cs`
- Test: `tests/BashCommandManager.Tests/Fakes/FakeGlobalHotkeyService.cs`

**Interfaces:**
- Consumes: `ISettingsService.GetExecutionSettings()/SaveExecutionSettings()/SaveHotkeySettings()`、`ExecutionSettings`、`TerminalKind.GetDisplayName()`、`TerminalCommandBuilder.GetDefaultTemplate()/ValidateTemplate()`、#8 的 `IGlobalHotkeyService.Suspend()/Resume()/Unregister()/UpdateHotkey(Key, ModifierKeys)`（返回 `HotkeyRegistrationResult`，有 `IsSuccess`）、`HotkeySettings.Clone()`。
- Produces（供 XAML 绑定，名字固定不可改）：
  - `public sealed record TerminalOption(TerminalKind Kind, string DisplayName);`（`BashCommandManager.ViewModels`）
  - `SettingsViewModel.Terminal`（`TerminalKind`）、`CustomExecutablePath`（`string`）、`ArgumentsTemplate`（`string`）、`KeepWindowOpen`（`bool`）、`RunAsAdministrator`（`bool`）、`IsCustomTerminal`（`bool`，只读）、`TerminalOptions`（`IReadOnlyList<TerminalOption>`）
  - 命令：`BrowseCustomExecutableCommand`、`ResetTemplateCommand`（`SaveCommand`/`CancelCommand`/`SetHotkeyCommand` 名字不变）

- [ ] **Step 1: 写 Fake 与第一批失败测试（模板联动）**

Create `tests/BashCommandManager.Tests/Fakes/FakeGlobalHotkeyService.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.Windows.Input;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
{
    public HotkeyRegistrationResult UpdateResult { get; set; } =
        new(HotkeyRegistrationStatus.Success, Key.B, ModifierKeys.Control | ModifierKeys.Alt);

    public int SuspendCount { get; private set; }

    public int ResumeCount { get; private set; }

    public bool UnregisterCalled { get; private set; }

    public bool IsRegistered { get; private set; }

    public event EventHandler? HotkeyPressed;

    public HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers) => UpdateResult;

    public HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers) => UpdateResult;

    public void Suspend() => SuspendCount++;

    public HotkeyRegistrationResult Resume()
    {
        ResumeCount++;
        // 与 #8 的真实实现一致：Resume() 返回「上次注册的结果」。
        return UpdateResult;
    }

    public void Unregister()
    {
        UnregisterCalled = true;
        IsRegistered = false;
    }

    public void Dispose()
    {
    }

    private void RaiseHotkeyPressed() => HotkeyPressed?.Invoke(this, EventArgs.Empty);
}
```

> 若 #8 的 `IGlobalHotkeyService` 还有本文件未实现的其他成员，编译器会报 `CS0535`，按 **Task 0 Step 3 记录的真实签名**补齐空实现（仍只允许改测试 Fake，不改生产接口）。

Create `tests/BashCommandManager.Tests/SettingsViewModelTests.cs`:

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using BashCommandManager.ViewModels;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(
        FakeSettingsService? settings = null,
        FakeGlobalHotkeyService? hotkey = null)
        => new(settings ?? new FakeSettingsService(), hotkey ?? new FakeGlobalHotkeyService());

    [Fact]
    public void 构造时载入执行设置的编辑副本()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.WindowsTerminal,
                CustomExecutablePath = @"D:\Apps\term.exe",
                ArgumentsTemplate = "-d {dir} cmd.exe /c {file}",
                KeepWindowOpen = false,
                RunAsAdministrator = true
            }
        };

        var viewModel = CreateViewModel(settings);

        Assert.Equal(TerminalKind.WindowsTerminal, viewModel.Terminal);
        Assert.Equal(@"D:\Apps\term.exe", viewModel.CustomExecutablePath);
        Assert.Equal("-d {dir} cmd.exe /c {file}", viewModel.ArgumentsTemplate);
        Assert.False(viewModel.KeepWindowOpen);
        Assert.True(viewModel.RunAsAdministrator);
        Assert.False(viewModel.IsCustomTerminal);
    }

    [Fact]
    public void 终端选项为五个且顺序固定()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(5, viewModel.TerminalOptions.Count);
        Assert.Equal(TerminalKind.Cmd, viewModel.TerminalOptions[0].Kind);
        Assert.Equal(TerminalKind.WindowsPowerShell, viewModel.TerminalOptions[1].Kind);
        Assert.Equal(TerminalKind.PowerShell7, viewModel.TerminalOptions[2].Kind);
        Assert.Equal(TerminalKind.WindowsTerminal, viewModel.TerminalOptions[3].Kind);
        Assert.Equal(TerminalKind.Custom, viewModel.TerminalOptions[4].Kind);
        Assert.All(viewModel.TerminalOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
    }

    [Fact]
    public void 切换终端_模板仍为默认时自动重写()
    {
        var viewModel = CreateViewModel();
        Assert.Equal("/k {file}", viewModel.ArgumentsTemplate);

        viewModel.Terminal = TerminalKind.WindowsTerminal;
        Assert.Equal("-d {dir} cmd.exe /k {file}", viewModel.ArgumentsTemplate);

        viewModel.KeepWindowOpen = false;
        Assert.Equal("-d {dir} cmd.exe /c {file}", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 手改模板后切换终端不覆盖用户输入()
    {
        var viewModel = CreateViewModel();
        viewModel.ArgumentsTemplate = "/k \"{file}\" /wait";

        viewModel.Terminal = TerminalKind.WindowsTerminal;

        Assert.Equal("/k \"{file}\" /wait", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 恢复默认模板后重新随终端联动()
    {
        var viewModel = CreateViewModel();
        viewModel.ArgumentsTemplate = "/c {file} /custom";
        viewModel.Terminal = TerminalKind.Cmd;
        Assert.Equal("/c {file} /custom", viewModel.ArgumentsTemplate);

        viewModel.ResetTemplateCommand.Execute(null);
        Assert.Equal("/k {file}", viewModel.ArgumentsTemplate);

        viewModel.KeepWindowOpen = false;
        Assert.Equal("/c {file}", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 切到自定义终端时显示路径输入区()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.IsCustomTerminal);

        viewModel.Terminal = TerminalKind.Custom;

        Assert.True(viewModel.IsCustomTerminal);
        Assert.Equal("{file}", viewModel.ArgumentsTemplate);
    }
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests"
```
Expected: 编译失败，`error CS1061: "SettingsViewModel"未包含"Terminal"的定义` / `"TerminalOptions"` / `"ResetTemplateCommand"`。

- [ ] **Step 3: 追加保存校验测试（不触发 Growl 的路径）**

在 `SettingsViewModelTests.cs` 的最后一个 `}` 之前追加：

```csharp
    [Fact]
    public void 保存_自定义路径为空_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.Terminal = TerminalKind.Custom;
        viewModel.CustomExecutablePath = "   ";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("自定义终端程序路径不能为空", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveExecutionCalls);
        Assert.Equal(0, settings.SaveHotkeyCalls);
    }

    [Fact]
    public void 保存_自定义路径不存在_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.Terminal = TerminalKind.Custom;
        viewModel.CustomExecutablePath = @"C:\nope\nope.exe";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("自定义程序不存在", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveExecutionCalls);
    }

    [Fact]
    public void 保存_模板缺少文件占位符_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.ArgumentsTemplate = "-Command echo hi";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("参数模板必须包含", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveExecutionCalls);
    }

    [Fact]
    public void 保存_模板花括号不配对_内联报错()
    {
        var viewModel = CreateViewModel();
        viewModel.ArgumentsTemplate = "-Command \"{file\"";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("不配对", viewModel.ErrorMessage);
    }

    [Fact]
    public void 保存_快捷键无效_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.HotkeySettings.Modifiers = ModifierKeys.None;

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("快捷键设置无效", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveHotkeyCalls);
    }
```

- [ ] **Step 4: 再次运行确认 RED（仍是 VM 成员缺失）**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests"
```
Expected: 仍为 `CS1061`。

- [ ] **Step 5: 重写 SettingsViewModel**

Replace `ViewModels/SettingsViewModel.cs` 全部内容为（注意：本文件同时 `using HandyControl.Controls;` 与 `using System.Windows;`，两者都有 `Window` 类型，`Save`/`Cancel` 的参数必须写成全限定的 `System.Windows.Window?`，否则编译报 CS0104；#8 的最终形态也是这么写的，不要「简化」成 `Window?`）：

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace BashCommandManager.ViewModels;

/// <summary>终端下拉项的展示模型。</summary>
public sealed record TerminalOption(TerminalKind Kind, string DisplayName);

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private bool _templateIsDefault;

    [ObservableProperty]
    private HotkeySettings _hotkeySettings = new();

    [ObservableProperty]
    private string _hotkeySettingsDisplay = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private TerminalKind _terminal;

    [ObservableProperty]
    private string _customExecutablePath = string.Empty;

    [ObservableProperty]
    private string _argumentsTemplate = string.Empty;

    [ObservableProperty]
    private bool _keepWindowOpen = true;

    [ObservableProperty]
    private bool _runAsAdministrator;

    public IReadOnlyList<TerminalOption> TerminalOptions { get; } = new List<TerminalOption>
    {
        new(TerminalKind.Cmd, TerminalKind.Cmd.GetDisplayName()),
        new(TerminalKind.WindowsPowerShell, TerminalKind.WindowsPowerShell.GetDisplayName()),
        new(TerminalKind.PowerShell7, TerminalKind.PowerShell7.GetDisplayName()),
        new(TerminalKind.WindowsTerminal, TerminalKind.WindowsTerminal.GetDisplayName()),
        new(TerminalKind.Custom, TerminalKind.Custom.GetDisplayName())
    };

    public bool IsCustomTerminal => Terminal == TerminalKind.Custom;

    public SettingsViewModel(ISettingsService settingsService, IGlobalHotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;

        HotkeySettings = _settingsService.GetHotkeySettings().Clone();

        var execution = _settingsService.GetExecutionSettings().Clone();
        _terminal = Enum.IsDefined(execution.Terminal) ? execution.Terminal : TerminalKind.Cmd;
        _customExecutablePath = execution.CustomExecutablePath ?? string.Empty;
        _keepWindowOpen = execution.KeepWindowOpen;
        _runAsAdministrator = execution.RunAsAdministrator;
        _argumentsTemplate = string.IsNullOrWhiteSpace(execution.ArgumentsTemplate)
            ? TerminalCommandBuilder.GetDefaultTemplate(_terminal, _keepWindowOpen)
            : execution.ArgumentsTemplate.Trim();
        _templateIsDefault = string.Equals(
            _argumentsTemplate,
            TerminalCommandBuilder.GetDefaultTemplate(_terminal, _keepWindowOpen),
            StringComparison.Ordinal);

        UpdateDisplayString();
    }

    partial void OnHotkeySettingsChanged(HotkeySettings value) => UpdateDisplayString();

    partial void OnTerminalChanged(TerminalKind value)
    {
        OnPropertyChanged(nameof(IsCustomTerminal));

        if (_templateIsDefault)
        {
            ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(value, KeepWindowOpen);
        }
    }

    partial void OnKeepWindowOpenChanged(bool value)
    {
        if (_templateIsDefault)
        {
            ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(Terminal, value);
        }
    }

    partial void OnArgumentsTemplateChanged(string value)
    {
        _templateIsDefault = string.Equals(
            value,
            TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen),
            StringComparison.Ordinal);
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
    private void BrowseCustomExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择终端程序",
            Filter = "可执行文件|*.exe;*.bat;*.cmd|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            CustomExecutablePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void ResetTemplate()
    {
        _templateIsDefault = true;
        ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen);
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

        if (Terminal == TerminalKind.Custom)
        {
            if (string.IsNullOrWhiteSpace(CustomExecutablePath))
            {
                ErrorMessage = "自定义终端程序路径不能为空";
                HasError = true;
                return;
            }

            if (!File.Exists(CustomExecutablePath))
            {
                ErrorMessage = $"自定义程序不存在：{CustomExecutablePath}";
                HasError = true;
                return;
            }
        }

        var templateErrors = TerminalCommandBuilder.ValidateTemplate(ArgumentsTemplate);
        if (templateErrors.Count > 0)
        {
            ErrorMessage = string.Join("；", templateErrors);
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

        var execution = new ExecutionSettings
        {
            Terminal = Terminal,
            CustomExecutablePath = (CustomExecutablePath ?? string.Empty).Trim(),
            ArgumentsTemplate = (ArgumentsTemplate ?? string.Empty).Trim(),
            KeepWindowOpen = KeepWindowOpen,
            RunAsAdministrator = RunAsAdministrator
        };

        try
        {
            // 传克隆体，避免服务内的内存配置与界面编辑副本互为别名（#8 Task 9 的语义）。
            _settingsService.SaveHotkeySettings(HotkeySettings.Clone());
            _settingsService.SaveExecutionSettings(execution);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"设置保存失败：{ex.Message}";
            HasError = true;
            Growl.Error(new GrowlInfo
            {
                Message = $"设置保存失败：{ex.Message}",
                WaitTime = 5
            });
            return;
        }

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

    private void UpdateDisplayString() => HotkeySettingsDisplay = HotkeySettings.ToDisplayString();
}
```

- [ ] **Step 6: 运行测试确认 GREEN**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests"
```
Expected: `Passed!`，`Failed: 0`。若「恢复默认模板后重新随终端联动」失败，检查 `ResetTemplate` 是否先把 `_templateIsDefault` 置 true 再赋值（顺序不能反）。

- [ ] **Step 7: 重写 SettingsWindow.xaml**

Replace `Views/SettingsWindow.xaml` 全部内容为：

```xml
<Window x:Class="BashCommandManager.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="设置"
        Height="560"
        Width="560"
        WindowStartupLocation="CenterOwner">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   Text="设置"
                   FontSize="20"
                   FontWeight="Bold"
                   Margin="0,0,0,20"/>

        <TabControl Grid.Row="1">
            <TabItem Header="快捷键">
                <StackPanel Margin="10">
                    <CheckBox Content="启用全局快捷键"
                              IsChecked="{Binding HotkeySettings.Enabled}"
                              Margin="0,0,0,20"/>

                    <GroupBox Header="当前快捷键"
                              IsEnabled="{Binding HotkeySettings.Enabled}">
                        <StackPanel Margin="10">
                            <TextBlock Text="{Binding HotkeySettingsDisplay}"
                                       FontSize="24"
                                       FontWeight="Bold"
                                       HorizontalAlignment="Center"
                                       Margin="0,10"/>

                            <Button Content="设置新快捷键"
                                    Command="{Binding SetHotkeyCommand}"
                                    HorizontalAlignment="Center"
                                    Margin="0,10"/>

                            <TextBlock Text="按下快捷键可以显示或隐藏应用窗口"
                                       Foreground="Gray"
                                       TextWrapping="Wrap"
                                       Margin="0,10,0,0"/>
                        </StackPanel>
                    </GroupBox>
                </StackPanel>
            </TabItem>

            <TabItem Header="执行">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel Margin="10">
                        <TextBlock Text="终端程序" Margin="0,0,0,6"/>
                        <ComboBox ItemsSource="{Binding TerminalOptions}"
                                  DisplayMemberPath="DisplayName"
                                  SelectedValuePath="Kind"
                                  SelectedValue="{Binding Terminal, Mode=TwoWay}"
                                  Margin="0,0,0,12"/>

                        <StackPanel Margin="0,0,0,12"
                                    Visibility="{Binding IsCustomTerminal, Converter={StaticResource BoolToVisibility}}">
                            <TextBlock Text="自定义程序路径" Margin="0,0,0,6"/>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBox Grid.Column="0"
                                         Text="{Binding CustomExecutablePath, UpdateSourceTrigger=PropertyChanged}"
                                         Margin="0,0,8,0"/>
                                <Button Grid.Column="1"
                                        Content="浏览..."
                                        Padding="12,2"
                                        Command="{Binding BrowseCustomExecutableCommand}"/>
                            </Grid>
                        </StackPanel>

                        <TextBlock Text="启动参数模板" Margin="0,0,0,6"/>
                        <TextBox Text="{Binding ArgumentsTemplate, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="0,0,0,6"/>
                        <TextBlock Text="占位符：{file} {dir} {filePs} {dirPs} {fileRaw} {dirRaw}；展开值自带引号，请勿再加"
                                   Foreground="Gray"
                                   TextWrapping="Wrap"
                                   Margin="0,0,0,12"/>

                        <Button Content="恢复默认模板"
                                Command="{Binding ResetTemplateCommand}"
                                HorizontalAlignment="Left"
                                Padding="12,2"
                                Margin="0,0,0,12"/>

                        <CheckBox Content="保持终端窗口打开（/k、-NoExit）"
                                  IsChecked="{Binding KeepWindowOpen}"
                                  Margin="0,0,0,8"/>
                        <CheckBox Content="默认以管理员身份运行（每次运行都会弹出 UAC）"
                                  IsChecked="{Binding RunAsAdministrator}"
                                  Margin="0,0,0,12"/>

                        <TextBlock Text="提示：手动修改参数模板后，「保持终端窗口打开」不再自动改写模板；可点「恢复默认模板」还原。"
                                   Foreground="Gray"
                                   TextWrapping="Wrap"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>
        </TabControl>

        <TextBlock Grid.Row="2"
                   Text="{Binding ErrorMessage}"
                   Foreground="Red"
                   TextWrapping="Wrap"
                   Visibility="{Binding HasError, Converter={StaticResource BoolToVisibility}}"
                   Margin="0,8,0,0"/>

        <StackPanel Grid.Row="3"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,20,0,0">
            <Button Content="保存"
                    Command="{Binding SaveCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Margin="0,0,10,0"/>
            <Button Content="取消"
                    Command="{Binding CancelCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{StaticResource ButtonDefault}"/>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 8: 构建验证 XAML 与绑定（不启动 GUI）**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~SettingsViewModelTests"
```
Expected: `Passed!`，`Failed: 0`（测试项目会编译主工程，XAML 编译错误会在此暴露）。若报 `MC3074: 不存在标记"BoolToVisibility"`，检查 `App.xaml` 里的资源键是否仍为 `BoolToVisibility`。

- [ ] **Step 9: 提交**

```powershell
git add Views/SettingsWindow.xaml ViewModels/SettingsViewModel.cs tests/BashCommandManager.Tests/SettingsViewModelTests.cs tests/BashCommandManager.Tests/Fakes/FakeGlobalHotkeyService.cs
git commit -m "feat(settings): 执行设置界面"
```

---

### Task 8: 全量验证与文档更新

**Files:**
- Modify: `README.md`（功能特性第 42–46 行之后插入；开发计划清单第 231 行后）
- Modify: `CLAUDE.md`（常用命令第 28 行后；关键设计决策第 112 行后；开发计划第 147 行后）
- Test: 全量 `dotnet test`（不改测试文件）

**Interfaces:**
- Consumes: Task 1–7 的全部产物。
- Produces: 文档说明；交接给 #10 的链接清单。

- [ ] **Step 1: 跑全量单元测试**

Run:
```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
```
Expected: `Passed!`，`Failed: 0`，`Skipped: 0`（若显示 `Skipped: 1` 也正确——那是 Category=Smoke 之外没有跳过项；本计划的 Smoke 测试不产生 Skipped）。若有失败，**停止**，回到对应任务修复，不要继续改文档。

- [ ] **Step 2: 主工程 Release 构建（只跑这一次）**

Run:
```powershell
dotnet build -c Release
```
Expected: `Build succeeded`，`0 Warning(s)`，`0 Error(s)`，用时约 2–3 分钟。若出现 warning，按 warning 内容修到 0；若出现 `error CS0246` 或 `error CS0103`，说明测试目录/CLI 目录未被排除——检查主 csproj 的 `DefaultItemExcludes`，但**不要**改成 `<Compile Remove>`。

- [ ] **Step 3: 更新 README.md**

在 `README.md` 的「### ⚡ 快速执行」小节之后（第 46 行 `- 命令执行状态清晰反馈` 之后空行后）插入：

```markdown
### 🖥️ 终端可配置
- 执行终端可选：命令提示符 (cmd)、Windows PowerShell、PowerShell 7 (pwsh)、Windows Terminal (wt)、自定义程序
- 参数模板支持占位符：`{file}` `{dir}` `{filePs}` `{dirPs}` `{fileRaw}` `{dirRaw}`
- 支持「保持窗口打开」与「默认以管理员身份运行」
- wt / pwsh 未安装时自动降级到 cmd 并提示；自定义程序缺失时拦截保存并给出内联错误
```

在「## 📝 开发计划」的清单中，把：

```markdown
- [x] 全局快捷键
- [x] 常用命令
```

改为：

```markdown
- [x] 全局快捷键
- [x] 常用命令
- [x] 终端可配置（cmd / PowerShell / Windows Terminal / 自定义）
```

- [ ] **Step 4: 更新 CLAUDE.md**

在「### 开发运行」的代码块中，紧跟现有的 `# 构建 Release` 注释行之前插入两行（保持原有缩进与注释风格）：

```bash
# 运行单元测试
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
```

在「#### 5. 虚拟节点模式」小节之后、「### 文件组织约定」之前插入：

```markdown
#### 6. 终端执行配置

- 全局终端配置存于 `%LOCALAPPDATA%\BashCommandManager\settings.json` 的 `Execution` 段，根对象为 `{ "Hotkey": {...}, "Execution": {...} }`；旧扁平 `{"Enabled":...}` 格式首次保存前自动备份为 `settings.json.v1.bak`。
- 命令行构造集中在纯函数 `Core/Services/TerminalCommandBuilder.Build`（占位符 `{file}` `{dir}` `{filePs}` `{dirPs}` `{fileRaw}` `{dirRaw}`），可执行文件探测在 `Core/Services/ExecutableLocator`；`CommandExecutor` 只负责 `Process.Start` 与状态回写。
- wt/pwsh 缺失时降级 cmd 并 Growl 警告；自定义程序缺失时保存被拦截、执行报错但不崩溃；取消 UAC（Win32 1223）时状态回到 `Idle`。
```

在「## 开发计划」的「已完成：」清单末尾追加一行：

```markdown
- 终端可配置（cmd/PowerShell/wt/自定义，settings.json 嵌套格式与旧格式迁移）
```

- [ ] **Step 5: 检查改动范围与编码**

Run:
```powershell
git status --short
git diff --stat
```
Expected: 只出现本计划列出的文件（`Core/Models/*.cs`、`Core/Services/*.cs`、`ViewModels/*.cs`、`Views/SettingsWindow.xaml`、`App.xaml.cs`、`tests/BashCommandManager.Tests/**`、`README.md`、`CLAUDE.md`），**没有** `BashCommandManager.csproj`、`HotkeySettings.cs`、`MainWindow.xaml` 的改动。

再检查中文是否乱码：

```powershell
Select-String -Path README.md,CLAUDE.md -Pattern "\uFFFD|鏂|\?\?"
```
Expected: 无命中（若命中，说明写入编码错误，用 UTF-8 重写对应片段）。

- [ ] **Step 6: 提交**

```powershell
git add README.md CLAUDE.md
git commit -m "docs(execution): 补充终端配置文档"
```

- [ ] **Step 7: 记录交接信息（不提交）**

在任务记录里写下：`#10 链接源码时必须同时链接 Core/Models/TerminalKind.cs、Core/Models/ExecutionSettings.cs、Core/Models/TerminalLaunchPlan.cs、Core/Services/ExecutionResult.cs、Core/Services/TerminalCommandBuilder.cs、Core/Services/ExecutableLocator.cs；不要链接 AppSettings.cs / SettingsService.cs / CommandExecutor.cs。`

---

## Self-Review（对照设计文档逐条核查）

### 1. 目标 1–5 覆盖

| 规格目标 | 落地任务 | 证据 |
|---|---|---|
| 1. 全局「执行/终端」配置（5 种终端、参数模板、保持打开、管理员） | Task 1（模型）、Task 2（模板/引号规则）、Task 7（UI） | `ExecutionSettings` 5 个字段；`GetDefaultTemplate` 五分支；设置页「执行」Tab |
| 2. 存 `%LOCALAPPDATA%\BashCommandManager\settings.json`，旧扁平格式可读、无损迁移 | Task 4 | S1/S2/S6；`BackupLegacy` 生成 `settings.json.v1.bak` |
| 3. `TerminalCommandBuilder.Build` 纯函数、可单测 | Task 2 | B1–B18 + 纯度测试（B17 不碰文件系统、相同输入两次相等） |
| 4. 设置窗口新增「执行」分组；保存后下次立即生效；内联校验 | Task 7 + Task 5 | UI 绑定；`CommandExecutor` 每次执行现取 `GetExecutionSettings()`；`HasError`/`ErrorMessage` 共用提示 |
| 5. `ICommandExecutor` 原成员语义不变，仅新增带结果方法；wt/pwsh 缺失降级 + Growl；不崩溃 | Task 5 + Task 6 | 接口 4 个旧成员签名原样；`ExecuteWithResult`；降级 Theory；`CommandListViewModel` Growl.Warning |

### 2. 验收标准 1–6 覆盖

| 验收标准 | 落地任务 |
|---|---|
| 1. 设置窗口有「执行」Tab（终端下拉、自定义路径+浏览、模板、保持打开、管理员），保存后下次执行生效 | Task 7（XAML 全部控件齐全）；Task 5（每次现取配置） |
| 2. 旧 `settings.json` 启动无异常、快捷键不丢；首次保存生成 `.v1.bak` 且新文件嵌套 | Task 4（S1、S2、S6） |
| 3. B1–B18 覆盖空格/中文/引号/wt/自定义/runas，且同输入两次结果相等 | Task 2（`B1_…` 到 `B18_…` 命名与用例表一一对应，另有「相同输入两次调用返回相等结果」） |
| 4. wt/pwsh 缺失降级 + Growl.Warning 不崩溃；自定义不存在时保存拦截、运行时报错不崩溃 | Task 5（降级 Theory、自定义缺失 Fact）；Task 6（Notice → Growl.Warning）；Task 7（保存校验 Fact） |
| 5. `ICommandExecutor` 原 4 个成员签名与语义不变；「运行」「管理员运行」按钮行为不变 | Task 5（接口只追加）；Task 6（生成命令名 `ExecuteCommand`/`ExecuteAsAdmin` 不变，`MainWindow.xaml` 未改） |
| 6. 主项目正常编译（tests 已排除），`dotnet test` 全绿 | Task 8 Step 1–2 |

### 3. 用例表覆盖

| 用例 | 测试方法 | 所在任务 |
|---|---|---|
| B1 | `B1_Cmd_保持窗口打开` | Task 2 |
| B2 | `B2_Cmd_中文与空格路径` | Task 2 |
| B3 | `B3_Cmd_不保持窗口打开` | Task 2 |
| B4 | `B4_Cmd_管理员运行` | Task 2 |
| B5 | `B5_WindowsPowerShell_保持窗口打开` | Task 2 |
| B6 | `B6_PowerShell7_保持窗口打开` | Task 2 |
| B7 | `B7_PowerShell_路径含单引号时翻倍` | Task 2 |
| B8 | `B8_WindowsTerminal_保持窗口打开` | Task 2 |
| B9 | `B9_WindowsTerminal_不保持窗口打开` | Task 2 |
| B10 | `B10_WindowsTerminal_管理员运行` | Task 2 |
| B11 | `B11_WindowsTerminal_分号路径被引号包住` | Task 2 |
| B12 | `B12_自定义程序_使用自定义路径与模板` | Task 2 |
| B13 | `B13_自定义程序_fileRaw_原样输出` | Task 2 |
| B14 | `B14_传入已探测的可执行文件时优先使用` | Task 2 |
| B15 | `B15_非法模板抛配置异常` | Task 2 |
| B16 | `B16_脚本路径含双引号时抛配置异常` | Task 2 |
| B17 | `B17_磁盘不存在的路径仍返回计划` | Task 2 |
| B18 | `B18_根目录文件的工作目录为盘符根` | Task 2 |
| S1 | `S1_旧格式可读且执行段为默认` | Task 4 |
| S2 | `S2_旧格式的自定义快捷键原样保留` | Task 4 |
| S3 | `S3_新格式字段一致_缺段用默认_未知字段忽略` | Task 4 |
| S4 | `S4_损坏_json_不抛异常并备份为_corrupt_bak` | Task 4 |
| S5 | `S5_两次保存互不抹掉另一段` | Task 4 |
| S6 | `S6_旧格式首次保存生成_v1_bak_且文件变为嵌套格式` | Task 4 |
| S7 | `S7_非法终端与空白模板被归一化` | Task 4 |
| S8 | `S8_写入失败时抛异常但内存值已更新` | Task 4（设计文档表格只列到 S7；S8 按假设 Q2「写失败不回滚内存值并 Growl.Error」补充，偏差见本 Self-Review 第 5 节） |
| S9 | `S9_保存成功后_tmp_不残留且文件为完整_json` | Task 4（spec §2 的 tmp+Move 原子写直接断言：成功路径 `settings.json.tmp` 不残留、`settings.json` 为完整 JSON） |

`ExecutableLocator` 用例（设计文档「测试策略」段）：`FindOnPath_命中/未命中/PATH 为空/带引号目录`、`Cmd_优先 COMSPEC`、`Cmd_没有 COMSPEC 回退 System32`、`Cmd_与_WindowsPowerShell_永不返回_null`、`PowerShell7_按 ProgramFiles 到 PATH 顺序探测`、`WindowsTerminal_先查 WindowsApps 再查 PATH`、`Custom_文件存在/不存在` 全部在 Task 3。

### 4. 类名 / 方法签名一致性检查

| 名字 | 定义任务 | 使用任务 | 一致 |
|---|---|---|---|
| `TerminalKind`（`Cmd=0` … `Custom=4`） | T1 `Core/Models/TerminalKind.cs` | T2/T3/T5/T6/T7 | 是 |
| `TerminalKind.GetDisplayName()` | T1 `TerminalKindDisplay` 扩展方法 | T5 降级提示、T7 下拉项 | 是 |
| `ExecutionSettings.Clone()/WithTerminal(TerminalKind)` | T1 | T5（`WithTerminal`）、T7（`Clone`） | 是 |
| `TerminalLaunchPlan(FileName, Arguments, WorkingDirectory, UseShellExecute, Verb)` + `RunAsVerb` | T1 | T2 返回、T5 消费 | 是 |
| `ExecutionOptions(bool RunAsAdmin = false)` / `ExecutionResult(bool Started, TerminalKind EffectiveTerminal, bool FallbackApplied, string? Notice, Exception? Error)` | T1 `Core/Services/ExecutionResult.cs` | T5/T6 | 是 |
| `TerminalCommandBuilder.GetDefaultTemplate(TerminalKind, bool)` | T2 | T4 归一化、T7 模板联动 | 是 |
| `TerminalCommandBuilder.Build(ExecutionSettings, Command, string?)` | T2 | T5 | 是 |
| `TerminalCommandBuilder.ValidateTemplate(string)` → `IReadOnlyList<string>` | T2 | T7 保存校验 | 是 |
| `TerminalConfigurationException(string)` | T2 | T5 捕获、T6 Growl 分派 | 是 |
| `IExecutableLocator.Resolve(TerminalKind, string customExecutablePath)` | T3 | T5、Fake | 是 |
| `ExecutableLocator.FindOnPath(string, string?, Func<string,bool>)` | T3 | T3 测试 | 是 |
| `ISettingsService.GetExecutionSettings/SaveExecutionSettings/ExecutionSettingsChanged` | T4 | T5（读）、T7（读写） | 是 |
| `SettingsService(string? settingsDirectory = null)` | T4 | T4 测试、App 工厂 | 是 |
| `ICommandExecutor.ExecuteWithResult(Command, ExecutionOptions)` | T5 | T6、测试 | 是 |
| `CommandExecutor(ISettingsService, IExecutableLocator)` + `protected virtual void StartProcess(Process)` | T5 | 测试子类 | 是 |
| `ICommandExecutor.Execute(Command, bool runAsAdmin = false)` 等 4 个旧成员 | 保留 | T6、既有 XAML | 是 |
| `SettingsViewModel.Terminal/CustomExecutablePath/ArgumentsTemplate/KeepWindowOpen/RunAsAdministrator` | T7 | T7 XAML + 测试 | 是 |
| `SettingsViewModel.IsCustomTerminal/TerminalOptions` | T7 | T7 XAML + 测试 | 是 |
| `BrowseCustomExecutableCommand/ResetTemplateCommand` | T7 | T7 XAML | 是 |
| `FakeSettingsService` / `FakeExecutableLocator` / `FakeGlobalHotkeyService` | T5/T7 | T5/T7 | 是 |

### 5. 显式记录的偏差与决策

1. **S8/S9 为补充用例**：设计文档用例表只到 S7；S8 覆盖假设 Q2「写失败不回滚内存值」（并顺带断言原 `settings.json` 未被破坏），S9 覆盖 spec §2 的 tmp+Move 原子写成功路径（tmp 不残留、文件为完整 JSON），两者都属于新增测试而非新增行为。
2. **`ExecutionOptions`/`ExecutionResult`/`TerminalLaunchPlan` 的文件归属**：按设计文档文件表放 `Core/Services/ExecutionResult.cs`；`TerminalLaunchPlan` 单独成文件放 `Core/Models/`（纯记录、无 WPF 依赖），便于 #10 链接。
3. **`CommandExecutor.StartProcess` 测试缝**：设计文档未提，但为了在没有真实 UAC/窗口的情况下测 1223 与启动失败，新增 `protected virtual` 方法。它不改变任何公开签名与行为，DI 注册也不受影响（构造函数仍是两个依赖）。
4. **Growl 分支不做自动化断言**：`Growl` 依赖 HandyControl 的已显示窗口，单元测试环境不可靠；T5 断言 `Notice`/`Error`/状态，T6 断言分派代码，最终由 Task 8 的人工冒烟覆盖 4 条提示路径。
5. **`B15` 断言的是「均为 `TerminalConfigurationException` 且消息含 `参数模板`」**，不绑定具体是哪一条错误（`{foo}` 模板既缺 file 占位符又是未知占位符，设计文档只要求抛同类异常）。
6. **默认模板字符串与设计文档逐字符一致**：`/k {file}`、`-NoExit -Command "& '{filePs}'"`、`-d {dir} cmd.exe /k {file}`，`SettingsService` 归一化后即写回该字面值（S1/S7 断言）。
7. **`ValidateTemplate` 对空白模板返回空列表**（合法，表示用默认模板），避免了「用户清空输入被拦」的假失败。
8. **VM 的错误前缀**：设计文档 §5 的 VM 代码片段统一用「执行失败: {msg}」，但 §7 错误表要求模板非法时提示「执行配置无效：…」。本计划在 `CommandListViewModel.ExecuteCoreAsync` 对 `TerminalConfigurationException` 单独分派为「执行配置无效: {msg}」，其余错误仍是「执行失败: {msg}」，两条要求同时满足。
9. **测试目录排除方式**：设计文档 §测试策略写的是 `<Compile Remove="tests/**/*.cs" />`，DECISIONS D2 已改为 `DefaultItemExcludes`（同时覆盖 `Compile`/`None`/`Page`）且 #8 已落地。本计划**不修改 csproj**，以 DECISIONS 为准（冲突时以 DECISIONS 为准，D1 已声明）。
10. **`Smoke` 测试默认不启动真实进程**：用 `BCM_EXECUTOR_SMOKE=1` 显式启用，CI 不设置该变量即等价跳过；真实启动的受控冒烟由 Task 8 的人工冒烟清单第 2/3 项覆盖。
11. **T6（`CommandListViewModel` 接线）无自动化测试 — D5.4 显式豁免**：`ExecuteCoreAsync` 只做「`ExecutionResult` → Growl」的分发，`Growl` 需要已显示的 HandyControl 窗口，单测无法可靠断言；这是本计划唯一没有对应自动化用例的功能性任务（Task 0 是门禁、Task 8 是验证与文档，均不产生产品代码）。理由：分支输入已由 T5 的 `终端缺失时降级_cmd_并给出提示`、`模板非法时返回配置异常且不启动` 用例断言（`Notice`/`Error`/状态），T6 只负责把这些字段映射成提示文案。手工验证条目：Task 8 人工冒烟第 3 条（降级 Growl.Warning）、第 4 条（自定义程序缺失的内联错误）、第 5 条（取消 UAC 的 Growl）；「执行配置无效」分派由 T5 的 `模板非法…` 用例 + 代码复核确认。
12. **保留 #8 Task 9 的 `SaveHotkeySettings(HotkeySettings.Clone())` 落盘语义**：本计划整体替换 `SettingsViewModel`，只在 #8 的 `Save` 上新增执行段逻辑；落盘前仍传 `HotkeySettings.Clone()`，避免编辑缓冲区与服务内存对象变成同一实例（否则「改键后点取消」会污染服务内配置，破坏 #8 的 A7 与 spec:218）。
13. **`SettingsViewModel` 的 `[ObservableProperty] private HotkeySettings _hotkeySettings = new();` 带初始化器**：`Nullable=enable` 下避免 CS8618；本计划保留该初始化器（#8 Task 9 的同一字段没有初始化器，属 #8 自身待办，本计划不改 #8）。

### 6. 计划执行前自检清单

- [ ] Task 0 的四个门禁全部通过（测试项目存在、基线全绿、#8 签名可用、工作区干净）。
- [ ] 全文无占位描述（每个代码步骤都是完整可运行代码，没有「先留空」「参照前面的代码」这类占位）。
- [ ] 每个任务的测试代码与本任务的实现代码一一对应，没有「先实现后补测试」顺序。
- [ ] 每个 commit 只包含该任务的文件；主 csproj、`HotkeySettings.cs`、`MainWindow.xaml` 零改动。
- [ ] Task 8 结束时 `dotnet build -c Release` 为 0 warning 0 error、`dotnet test` 全绿。
- [ ] 人工冒烟 6 项（旧配置启动、wt 打开并执行、改名 wt 后降级、自定义路径填错被拦、取消 UAC 回 Idle、保存快捷键后 Execution 段未丢）在真实桌面环境执行并记录。

---

## 手工冒烟清单（Task 8 之后，由人工在真实桌面执行）

| # | 操作 | 期望 |
|---|---|---|
| 1 | 保留现有 `%LOCALAPPDATA%\BashCommandManager\settings.json`（旧扁平格式）启动应用 | 应用正常启动、无异常弹窗；原快捷键仍生效；文件暂未改写 |
| 2 | 设置 →「执行」选 Windows Terminal 保存；运行一个中文+空格路径的脚本 | wt 在脚本目录打开并执行；关窗后命令状态变 `Completed`；`settings.json` 出现嵌套 `Hotkey`/`Execution`，并生成 `settings.json.v1.bak` |
| 3 | 临时把 `wt.exe` 改名（或选 PowerShell 7 且本机未装）后运行 | 弹出 Growl 警告「未检测到 …，已改用 cmd 启动」，cmd 正常打开并执行，应用不崩溃 |
| 4 | 设置 →「执行」选自定义程序并填入不存在的路径，点保存 | 窗口不关闭，TabControl 下方红字显示「自定义程序不存在：…」 |
| 5 | 勾选「默认以管理员身份运行」后运行脚本，在 UAC 弹窗点「否」 | Growl 提示「已取消管理员授权」，命令状态回到 `Idle`，执行次数未增加 |
| 6 | 在「快捷键」Tab 保存一次快捷键，再打开 `settings.json` | `Execution` 段仍在且值未变（两次保存不互相抹除） |
