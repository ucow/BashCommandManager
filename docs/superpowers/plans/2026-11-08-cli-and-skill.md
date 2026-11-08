# CLI 调用层与操作 Skill 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增独立控制台程序 `bcm.exe`（19 条子命令，覆盖分组/命令/运行/配置/打开 GUI），与 GUI 共用同一 SQLite 数据库与路径迁移逻辑，并把可被 AI 代理使用的操作说明落成 `.claude/skills/bash-command-manager/SKILL.md`。

**Architecture:** 新建 `BashCommandManager.Cli`（`net8.0-windows`、`OutputType=Exe`、`AssemblyName=bcm`），用**源码链接**引用主工程中 WPF-free 的数据层文件（**不** `ProjectReference` 主工程，避免把 WindowsDesktop 运行时拖进 CLI）。流程固定为「手写解析器 → `DatabaseSession`（按需创建）→ `CliDispatcher` → `Output/` 统一输出 → 退出码 0/1/2/3」。数据库位置由 GUI 与 CLI 共用的 `Infrastructure/DatabasePathResolver` + `DatabaseMigrator` 决定：规范位置 `%LOCALAPPDATA%\BashCommandManager\data\app.db`，旧 `{应用目录}\data\app.db` 只作迁移源（只复制不删除），失败回退旧库并告警。

**Tech Stack:** .NET 8（`net8.0-windows`，C# 12）、System.Text.Json（`JsonNode`/`Utf8JsonWriter` 语义）、Dapper 2.1.35、System.Data.SQLite.Core 1.0.115.5、CommunityToolkit.Mvvm 8.2.2、xUnit 2.5.3 + Microsoft.NET.Test.Sdk 17.8.0、Inno Setup 6。

**Spec:** `docs/superpowers/specs/2026-11-08-cli-and-skill-design.md`
**跨特性决策（冲突时以此为准）:** `docs/superpowers/DECISIONS.md`
**前置 Spec:** `docs/superpowers/specs/2026-11-08-hotkey-tray-design.md`(#8)、`docs/superpowers/specs/2026-11-08-execution-terminal-design.md`(#9)

## Global Constraints

- 平台：Windows 10/11 x64；CLI 目标框架固定 `net8.0-windows`（**不加** `UseWPF`、**不加** `UseWindowsForms`），`AssemblyName=bcm`，`<Version>1.3.0</Version>`（D6 要求三处版本统一为 `1.3.0`：CLI csproj、两个 `.iss` 的 `MyAppVersion`、README）。
- **D2.2（硬约束）**：CLI **不得**链接 `Core/Services/CommandExecutor.cs`、`CommandService.cs`、`SettingsService.cs`、`Core/Models/HotkeySettings.cs`、`SingleInstanceService.cs`、`GlobalHotkeyService.cs`；`run` 子命令自行实现启动逻辑，但命令构造必须复用 `Core/Services/TerminalCommandBuilder.cs`。
- CLI 允许链接（均 WPF-free，以 `DECISIONS.md` D2.2 第 3 条为准）：`Core/Models/Command.cs`、`Group.cs`、`SortOptions.cs`、`TerminalKind.cs`、`ExecutionSettings.cs`、`Core/Services/TerminalCommandBuilder.cs`、`ExecutionResult.cs`（含 `TerminalLaunchPlan`/`ExecutionOptions`/`ExecutionResult`）、`ExecutableLocator.cs`、`Core/Repositories/CommandRepository.cs`、`GroupRepository.cs`、`Core/Services/DatabaseService.cs`、`GroupService.cs`、`Infrastructure/DatabaseInitializer.cs`、`Infrastructure/DatabasePathResolver.cs`。
- 不新增第三方 NuGet 依赖（D5.5）；CLI 仅 `Dapper`、`System.Data.SQLite.Core`、`CommunityToolkit.Mvvm`（版本与主工程一致），测试项目仅 `Microsoft.NET.Test.Sdk 17.8.0`、`xunit 2.5.3`、`xunit.runner.visualstudio 2.5.3`。
- 绝不交互：CLI 不读 stdin、不弹窗、不调用 `Dialog`/`ReadLine`/`ReadKey`；`--admin` 的 UAC 属 OS 行为。
- 输出：UTF-8 **无 BOM**（`new UTF8Encoding(false)`），JSON 用 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`（中文不转 `\uXXXX`）、`WriteIndented=false`；成功写 **stdout**，失败写 **stderr**；`lastExecutedAt` 为本地时间 `yyyy-MM-ddTHH:mm:ss`，空为 `null`。
- 退出码：`0` 成功；`1` 参数/用法（未知命令或选项、缺值、非法枚举、缺 `--force`、`--admin` 与 `--wait` 冲突、`--timeout` 单独用、`--db` 指向目录、未找到配置键）；`2` 未找到（命令/分组/GUI 可执行文件）；`3` 运行时失败（数据库打开/写入/被占用、脚本文件缺失、子进程非 0、UAC 取消、超时）。
- 错误码集合：`E_USAGE`、`E_INVALID_VALUE`、`E_AMBIGUOUS`、`E_CONFIRM_REQUIRED`、`E_ROOT_GROUP`、`E_NOT_FOUND`、`E_FILE_NOT_FOUND`、`E_GUI_NOT_FOUND`、`E_CONFIG_KEY`、`E_DB_ERROR`、`E_DB_BUSY`、`E_COMMAND_FAILED`、`E_TIMEOUT`、`E_ELEVATION_CANCELLED`、`E_INTERNAL`（本计划新增的兜底码，见「偏差记录 VD-5」）。
- 主工程 `BashCommandManager.csproj` 必须存在 `DefaultItemExcludes` 覆盖 `tests\**;BashCommandManager.Cli\**`（D2）——本计划只**验证**，缺失才补（T1 步骤 2）。
- 现有 GUI 行为不回归：`dotnet publish` 原命令仍只发布主工程、产物名为 `BashCommandManager.exe`、`Inno` 的 `OutputBaseFilename` 不变；「运行」默认仍是 `cmd.exe /k "<脚本>"`。
- 测试隔离：任何测试/冒烟**不得**读写真实安装库 `D:\Program Files\BashCommandManager\data\app.db` 与真实 `%LOCALAPPDATA%\BashCommandManager\settings.json`；一律用 `--db <临时文件>`、`BASHCOMMANDMANAGER_DB`、`BASHCOMMANDMANAGER_SETTINGS_DIR` 指向临时目录（见 VD-6）。
- 每个任务先写失败测试再写实现（D5.4）；提交信息前缀 `feat(cli):`/`test(cli):`/`chore(cli):`/`docs(cli):`。
- 新代码 UTF-8，中文不得出现 `?` 或 `鏂` 类乱码（D5.6）；新增公开类型写一行中文职责注释（D5.7）。

---

## 0. 执行前置条件（务必先读）

### 0.1 顺序与分支

- D1 规定实施顺序 **#8 热键 → #9 执行终端 → #10 CLI + skill**，严格串行。本计划假设 #8、#9 已合入当前分支。
- 分支：`feat/v1.3.0`（已从 `master` 拉出），在本分支直接执行，不额外建 worktree。

### 0.2 本计划写作时的仓库核实结果（与任务说明的差异）

| 事实 | 核实命令 | 写作时结果 | 影响 |
|---|---|---|---|
| 主 csproj 有 `DefaultItemExcludes` | `Select-String -Path BashCommandManager.csproj -Pattern DefaultItemExcludes` | **无匹配**（28 行 csproj 无该 ItemGroup） | #8 尚未落地；T1 步骤 2 会**补上**并说明 |
| `tests/BashCommandManager.Tests/` 存在 | `Test-Path tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj` | **False** | 同第一行：#8 未落地，T1 步骤 2 一并创建（内容取 D2 的 csproj 定义） |
| #9 产物存在 | `Core\Models\TerminalKind.cs`、`ExecutionSettings.cs`、`Core\Services\TerminalCommandBuilder.cs`、`ExecutionResult.cs`、`ExecutableLocator.cs` | **全部 False** | CLI 无法编译；T1 步骤 1 硬性拦截：缺失即停，先完成 #9 |
| `BashCommandManager.Cli/` 存在 | `Test-Path BashCommandManager.Cli` | False | 本计划从零创建 |
| 真实安装库 | `D:\Program Files\BashCommandManager\data\app.db` | 存在（旧库） | 所有测试用临时库，禁止指向该路径 |
| SDK | `dotnet --list-sdks` | `10.0.301` | 可构建 `net8.0-windows`；xunit 2.5.3 本机可离线还原（已验证） |
| `DECISIONS.md` D2.2 第 3 条 | `git diff docs/superpowers/DECISIONS.md` | 写作期间被更新：可链接清单新增 `Core/Services/ExecutionResult.cs`（含 `TerminalLaunchPlan`/`ExecutionOptions`/`ExecutionResult`） | 本计划已按更新后的清单链接该文件（T1 的 `SharedSources`） |

> 结论：本计划可以照抄执行，但 **T1 步骤 1 必须真实跑一遍**。若 #9 未完成，执行者应停下来报告 `#9 未合入，CLI 依赖的 TerminalCommandBuilder/ExecutionSettings/TerminalKind/ExecutableLocator 不存在`，而不是在 CLI 里复制一份命令构造逻辑（那会违反 D2.2 的第 4 条「`--dry-run` 必须复用 `TerminalCommandBuilder`」）。

### 0.3 与 spec 的偏差记录（每条都在提交说明里引用编号）

| 编号 | spec 原文 | 本计划做法 | 理由 |
|---|---|---|---|
| VD-1 | 版本号 `1.2.1`（spec 1.1 节 csproj 片段） | 统一 `1.3.0` | `DECISIONS.md` D6 明确「版本号统一为 1.3.0」，冲突以 DECISIONS 为准 |
| VD-2 | 「脚本文件缺失」列在退出码 3；命令表 `commands add` 文件不存在 → 1 | `commands add/edit --file` 缺失 → `E_FILE_NOT_FOUND` **退出 1**；`run` 目标脚本缺失 → `E_FILE_NOT_FOUND` **退出 3** | 命令表更具体（参数校验 vs 运行时），故按位置区分。`CliErrors.FileNotFound(msg, exitCode:)` 显式携带退出码，测试逐条断言 |
| VD-3 | `run` 默认（非 `--wait`）JSON 含 `"pid":1234` | `pid` 为 `int?`：`--wait` 下为真实进程 id；默认分支 `UseShellExecute=true`（与 GUI 逐字节一致）时 Windows 的 `Process.Start` 返回 `null`，此时 `pid` 为 `null` | 保持 A7「与 GUI 一致、可见窗口、立即返回」优先；拿 pid 必须 `UseShellExecute=false`，会把子进程挂到调用者控制台，破坏「立即返回 + 独立窗口」语义 |
| VD-4 | spec 5 节要求 `open` 返回 `meta.warning` 提示旧 GUI | 不返回该 warning | 旧 GUI 不讲管道协议，CLI 无法探测对端版本；改为 SKILL.md 写明「GUI 需与 CLI 同版本」。`open` 语义仍满足验收 9（启动或经 NamedPipe 激活，不产生第二个窗口） |
| VD-5 | 「错误码固定集合」14 个 | 增加 `E_INTERNAL`（退出 3），仅用于兜底未预期异常与导出文件写入失败 | 否则只能把内部错误伪装成 `E_DB_ERROR`（语义错误）。正常路径不会出现该码；有专门测试 |
| VD-6 | spec 未提供 settings 目录覆盖手段 | 新增环境变量 `BASHCOMMANDMANAGER_SETTINGS_DIR`（仅 CLI 读取，用于测试/便携） | 冒烟测试要执行 `config set`，绝不能改写开发机真实 `settings.json`；默认值仍等于 `%LOCALAPPDATA%\BashCommandManager`（验收 3 不受影响） |
| VD-7 | spec 2 节 `DatabasePathResolver.Resolve` 只有 3 参 | 追加两个**可选**参数 `settingsDirectory`、`legacyDatabasePath` | D4.1 要求「注入目录与文件系统委托」的迁移单测；3 参调用完全兼容 |
| VD-8 | `groups create` 未定义同名处理 | 同级同名分组 → `E_INVALID_VALUE` 退出 1（与 GUI `CheckGroupNameExistsAsync` 一致）；`groups rename` 同规则（排除自身） | 避免 CLI 造出 GUI 会拒绝的数据 |
| VD-9 | `groups delete` 回报 `deletedGroupIds` 未定义是否含自身 | 含自身 + 全部后代，**升序** | 可被测试断言 |

### 0.4 执行期间的固定命令

```powershell
# CLI 构建（约 20-40 秒）
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release

# CLI 单测（全部）
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release

# CLI 单测（按类过滤）
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandLineParserTests"

# GUI 侧单测（#8 创建的项目，本计划在其子目录追加文件；约 30-60 秒）
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj

# 手工运行 CLI（Release 构建产物）
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe --help
```

## File Structure

### 新增：CLI 项目（31 个文件）

| 文件 | 职责 |
|---|---|
| `BashCommandManager.Cli/BashCommandManager.Cli.csproj` | 项目定义、源码链接清单、包引用、版本 |
| `BashCommandManager.Cli/Program.cs` | 唯一 `Main`：设置 UTF-8 控制台编码后转调 `CliHost.Run` |
| `BashCommandManager.Cli/CliHost.cs` | 解析 → 按需建 `DatabaseSession` → 分发 → 输出/退出码；唯一 catch 边界 |
| `BashCommandManager.Cli/CliDispatcher.cs` | `CommandPath` → 处理器的唯一映射表 |
| `BashCommandManager.Cli/CliServices.cs` | 处理器依赖容器（会话、settings、进程、GUI、文件系统委托） |
| `BashCommandManager.Cli/CommandLine/CommandSpec.cs` | `CommandSpec`/`OptionSpec` 记录类型 |
| `BashCommandManager.Cli/CommandLine/CommandCatalog.cs` | 19 条命令的**唯一**元数据表（用法/摘要/选项/参数数/示例） |
| `BashCommandManager.Cli/CommandLine/ParsedCommand.cs` | 解析结果（命令路径、选项、开关、位置参数、`--json`、`--db`） |
| `BashCommandManager.Cli/CommandLine/CommandLineParser.cs` | 手写解析器 + 语义校验（冲突/必填/枚举/数值/`--db` 目录） |
| `BashCommandManager.Cli/CommandLine/HelpCatalog.cs` | 由 `CommandCatalog` **生成**的全局帮助与 19 条命令帮助 |
| `BashCommandManager.Cli/CommandLine/CliVersion.cs` | 程序集版本 → `1.3.0` |
| `BashCommandManager.Cli/Infrastructure/CliFailureException.cs` | `CliError`/`CliFailureException`/`CliErrorCodes`/`CliErrors` |
| `BashCommandManager.Cli/Infrastructure/CliPaths.cs` | settings 目录（含 `BASHCOMMANDMANAGER_SETTINGS_DIR` 覆盖） |
| `BashCommandManager.Cli/Infrastructure/GuiLauncher.cs` | GUI 路径查找 + 启动（`--command` 透传） |
| `BashCommandManager.Cli/Output/CliResult.cs` | 处理器返回值（`Data`/`Meta`/`HumanText`） |
| `BashCommandManager.Cli/Output/JsonEnvelope.cs` | 信封构造与序列化选项（中文不转义） |
| `BashCommandManager.Cli/Output/JsonShapes.cs` | 分组/命令 → JSON 对象（字段名与格式的唯一来源） |
| `BashCommandManager.Cli/Output/OutputWriter.cs` | 成功→stdout、失败→stderr、人类可读渲染 |
| `BashCommandManager.Cli/Output/TableWriter.cs` | 等宽表格（含 CJK 宽度） |
| `BashCommandManager.Cli/Application/CliCommandStore.cs` | 分组/命令 CRUD、移动、执行记录、Busy 重试、子树统计 |
| `BashCommandManager.Cli/Application/CliSettingsStore.cs` | `settings.json` 的 `JsonNode` 就地读写（扁平 + 嵌套两种形状） |
| `BashCommandManager.Cli/Application/HotkeySpec.cs` | `Ctrl+Shift+N` ↔ `{Key,Modifiers,Display}` |
| `BashCommandManager.Cli/Application/HotkeyKeyMap.cs` | 键名 ↔ Win32/WPF 虚拟键码（A=44…） |
| `BashCommandManager.Cli/Operations/DatabaseSession.cs` | 打开/初始化数据库、`created`、`source`、迁移告警 |
| `BashCommandManager.Cli/Operations/CommandResolver.cs` | id / 名称 / 分组路径 / 歧义 / 批量列表 |
| `BashCommandManager.Cli/Operations/GroupCommands.cs` | `groups list/create/rename/delete` |
| `BashCommandManager.Cli/Operations/CommandCommands.cs` | `commands list/search/show/add/edit/delete/move/import/export/frequent` |
| `BashCommandManager.Cli/Operations/RunCommand.cs` | `run`（默认 / `--wait` / `--admin` / `--timeout` / `--dry-run`） |
| `BashCommandManager.Cli/Operations/ProcessRunner.cs` | `IProcessRunner` + 真实实现（启动 / 等待 / 逐字节转发 stderr） |
| `BashCommandManager.Cli/Operations/ConfigCommand.cs` | `config get/set` |
| `BashCommandManager.Cli/Operations/OpenCommand.cs` | `open` |

### 新增：CLI 测试项目（33 个文件）

`tests/BashCommandManager.Cli.Tests/BashCommandManager.Cli.Tests.csproj`、`TestSupport/{TempDatabase,CliServicesFactory,FakeSettingsStore,FakeProcessRunner,FakeGuiLauncher,FakeExecutableLocator,CliProcess}.cs`、`CommandLine/{CliVersionTests,HelpCatalogTests,CliHostHelpVersionTests,ParserGlobalOptionTests,ParserCommandShapeTests,ParserValidationTests,CliHostDatabaseTests}.cs`、`Infrastructure/{ExitCodesTests,GuiLauncherTests}.cs`、`Output/{JsonEnvelopeTests,OutputWriterTests,TableWriterTests}.cs`、`Application/{CliCommandStoreTests,CliSettingsStoreTests,HotkeySpecTests}.cs`、`Operations/{DatabaseSessionTests,CommandResolverTests,GroupCommandsTests,CommandCommandsTests,ImportExportTests,RunCommandTests,ProcessRunnerTests,ConfigCommandTests,OpenCommandTests}.cs`、`Smoke/CliSmokeTests.cs`。

### 新增/修改：GUI 侧（11 个文件）

| 文件 | 变更 |
|---|---|
| `Infrastructure/DatabasePathResolver.cs` | **新增**：`DatabasePathSource`/`DatabasePathResolution`/`DatabasePathResolver`/`DatabasePreparationResult`/`DatabaseMigrator` |
| `Infrastructure/DatabaseInitializer.cs` | **修改**：构造函数接受 `explicitDatabasePath`；走解析+迁移；新增 `DatabasePath`/`MigrationWarning`/`Source`/`Resolution`；连接串加 `Foreign Keys=True` |
| `Core/Services/CommandService.cs` | **修改**：接口与实现新增 `Task<Command?> GetByIdAsync(int id)` |
| `Core/Services/StartupArgumentParser.cs` | **新增**：`--command <id>` 纯解析 |
| `Core/Services/SingleInstanceMessage.cs` | **新增**：`ACTIVATE` / `ACTIVATE\t["--command","7"]` 编解码 |
| `Core/Services/SingleInstanceService.cs` | **修改**：`Initialize(Action<string[]> onActivate, string[] startupArgs)`，管道转发参数，兼容旧 `ACTIVATE` |
| `App.xaml.cs` | **修改**：传 `e.Args`；`OnActivate` 回调带参数；`Show()` 后 `ApplyStartupArguments`；迁移告警 `Debug.WriteLine` |
| `MainWindow.xaml.cs` | **修改**：`ApplyStartupArguments`/挂起未加载参数 |
| `ViewModels/MainViewModel.cs` | **修改**：注入 `ICommandService`；新增 `SelectCommandAsync(int)` |
| `ViewModels/GroupTreeViewModel.cs` | **修改**：新增 `FindGroupById(int)` |
| `BashCommandManager.csproj` | **验证/补**：`DefaultItemExcludes`（T1） |

### 新增/修改：GUI 测试（5 个文件）

`tests/BashCommandManager.Tests/Infrastructure/DatabasePathResolverTests.cs`、`Infrastructure/DatabaseMigratorTests.cs`、`Infrastructure/DatabaseInitializerTests.cs`、`Core/StartupArgumentParserTests.cs`、`Core/SingleInstanceMessageTests.cs`。

### 新增/修改：打包与文档（6 个文件）

`.claude/skills/bash-command-manager/SKILL.md`（新增）、`Packaging/InnoSetup/BashCommandManager-Full.iss`、`Packaging/InnoSetup/BashCommandManager-Lite.iss`、`docs/PACKAGING.md`、`CLAUDE.md`、`README.md`。

---

### Task 1: 前置校验、CLI 项目骨架、帮助/版本/退出码

**Files:**
- 验证/补：`BashCommandManager.csproj`（`DefaultItemExcludes`）
- 验证/建：`tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`（#8 应已创建）
- Create: `BashCommandManager.Cli/BashCommandManager.Cli.csproj`
- Create: `tests/BashCommandManager.Cli.Tests/BashCommandManager.Cli.Tests.csproj`
- Create: `BashCommandManager.Cli/Program.cs`
- Create: `BashCommandManager.Cli/CommandLine/CliVersion.cs`
- Create: `BashCommandManager.Cli/CommandLine/CommandSpec.cs`
- Create: `BashCommandManager.Cli/CommandLine/CommandCatalog.cs`
- Create: `BashCommandManager.Cli/CommandLine/HelpCatalog.cs`
- Create: `BashCommandManager.Cli/Infrastructure/CliFailureException.cs`
- Create: `BashCommandManager.Cli/Output/JsonEnvelope.cs`
- Create: `BashCommandManager.Cli/CliHost.cs`
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/CliVersionTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/HelpCatalogTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Infrastructure/ExitCodesTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/CliHostHelpVersionTests.cs`

**Interfaces:**
- Consumes: #9 的 `Core/Models/TerminalKind.cs`、`Core/Models/ExecutionSettings.cs`、`Core/Services/TerminalCommandBuilder.cs`、`Core/Services/ExecutableLocator.cs`（仅源码链接，不调用）
- Produces:
  - `public static class CliVersion { public static string Value { get; } }` → `"1.3.0"`
  - `public sealed record OptionSpec(string Name, bool TakesValue, string Description, bool Required = false, IReadOnlyList<string>? AllowedValues = null)`
  - `public sealed record CommandSpec(string Path, string Verb, string? SubVerb, string Usage, string Summary, IReadOnlyList<OptionSpec> Options, int MinArguments, int MaxArguments, IReadOnlyList<string> Examples)`
  - `public static class CommandCatalog { public static IReadOnlyList<CommandSpec> Commands { get; } public static CommandSpec? Find(string path); public static CommandSpec? Find(string verb, string? subVerb); }`
  - `public static class HelpCatalog { public static string Global(); public static string For(string commandPath); }`
  - `public sealed class CliError { string Code; string Message; int ExitCode; string? Hint; JsonObject? Details; }`
  - `public sealed class CliFailureException : Exception { public CliError Error { get; } }`
  - `public static class CliErrorCodes { 15 个 const；public static int ExitCodeFor(string code); }`
  - `public static class CliErrors { Usage/InvalidValue/Ambiguous/ConfirmRequired/RootGroup/NotFound/FileNotFound/GuiNotFound/ConfigKey/DbError/DbBusy/CommandFailed/Timeout/ElevationCancelled/Internal }`
  - `public static class JsonEnvelope { public static JsonSerializerOptions SerializerOptions; public static JsonObject Success(JsonNode? data, JsonObject? meta); public static JsonObject Failure(CliError error); public static string Serialize(JsonNode node); }`
  - `public static class CliHost { public static int Run(string[] args); public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null); }`

- [ ] **Step 1: 跑前置校验，缺失 #9 产物就停**

```powershell
Select-String -Path BashCommandManager.csproj -Pattern 'DefaultItemExcludes'
Test-Path tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
'Core\Models\TerminalKind.cs','Core\Models\ExecutionSettings.cs','Core\Services\TerminalCommandBuilder.cs','Core\Services\ExecutionResult.cs','Core\Services\ExecutableLocator.cs' |
  ForEach-Object { "$_ => " + (Test-Path $_) }
git status --short
git branch --show-current
```

期望：分支为 `feat/v1.3.0`；5 个 #9 文件全部 `True`。
若任一为 `False`：**停止**，回报 `#9 未合入，缺少 <文件名>；按 D1 必须先完成 #9`，不要创建 CLI 项目（否则 CLI 只能复制一份命令构造逻辑，违反 D2.2）。

- [ ] **Step 2: 补主工程排除项与 #8 测试项目（缺失才做）**

若步骤 1 的 `Select-String` 无匹配，在 `BashCommandManager.csproj` 的 `</Project>` 前追加（内容取自 D2，**只加一次**）：

```xml
  <ItemGroup>
    <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
  </ItemGroup>
```

若 `tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj` 不存在（#8 未落地），按 D2 创建：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\BashCommandManager.csproj" />
  </ItemGroup>
</Project>
```

并在提交说明里写明「T1 步骤 2 因 #8 未落地而补建」。

- [ ] **Step 3: 创建两个项目文件（此时还没有任何 CLI 源码）**

`BashCommandManager.Cli/BashCommandManager.Cli.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <AssemblyName>bcm</AssemblyName>
    <RootNamespace>BashCommandManager.Cli</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.3.0</Version>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <SatelliteResourceLanguages>zh-Hans;en</SatelliteResourceLanguages>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Dapper" Version="2.1.35" />
    <PackageReference Include="System.Data.SQLite.Core" Version="1.0.115.5" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
  </ItemGroup>

  <!-- D2.2：只链接 WPF-free 文件；禁止改成 ProjectReference 主工程 -->
  <ItemGroup Label="SharedSources">
    <Compile Include="..\Core\Models\Command.cs" Link="Shared\Core\Models\Command.cs" />
    <Compile Include="..\Core\Models\Group.cs" Link="Shared\Core\Models\Group.cs" />
    <Compile Include="..\Core\Models\SortOptions.cs" Link="Shared\Core\Models\SortOptions.cs" />
    <Compile Include="..\Core\Models\TerminalKind.cs" Link="Shared\Core\Models\TerminalKind.cs" />
    <Compile Include="..\Core\Models\ExecutionSettings.cs" Link="Shared\Core\Models\ExecutionSettings.cs" />
    <Compile Include="..\Core\Repositories\CommandRepository.cs" Link="Shared\Core\Repositories\CommandRepository.cs" />
    <Compile Include="..\Core\Repositories\GroupRepository.cs" Link="Shared\Core\Repositories\GroupRepository.cs" />
    <Compile Include="..\Core\Services\DatabaseService.cs" Link="Shared\Core\Services\DatabaseService.cs" />
    <Compile Include="..\Core\Services\GroupService.cs" Link="Shared\Core\Services\GroupService.cs" />
    <Compile Include="..\Core\Services\TerminalCommandBuilder.cs" Link="Shared\Core\Services\TerminalCommandBuilder.cs" />
    <Compile Include="..\Core\Services\ExecutionResult.cs" Link="Shared\Core\Services\ExecutionResult.cs" />
    <Compile Include="..\Core\Services\ExecutableLocator.cs" Link="Shared\Core\Services\ExecutableLocator.cs" />
    <Compile Include="..\Infrastructure\DatabaseInitializer.cs" Link="Shared\Infrastructure\DatabaseInitializer.cs" />
    <!-- DatabasePathResolver.cs 在 Task 5 创建后再加入本 ItemGroup（见 Task 5 步骤 3） -->
  </ItemGroup>
</Project>
```

`tests/BashCommandManager.Cli.Tests/BashCommandManager.Cli.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\BashCommandManager.Cli\BashCommandManager.Cli.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: 写失败测试（3 个文件）**

`tests/BashCommandManager.Cli.Tests/CommandLine/CliVersionTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class CliVersionTests
{
    [Fact]
    public void Version_matches_plan_version()
    {
        // 版本三处同步（D6）：csproj、两个 .iss、README 都是 1.3.0
        Assert.Equal("1.3.0", CliVersion.Value);
    }
}
```

`tests/BashCommandManager.Cli.Tests/Infrastructure/ExitCodesTests.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.Infrastructure;

public class ExitCodesTests
{
    [Theory]
    [InlineData("E_USAGE", 1)]
    [InlineData("E_INVALID_VALUE", 1)]
    [InlineData("E_AMBIGUOUS", 1)]
    [InlineData("E_CONFIRM_REQUIRED", 1)]
    [InlineData("E_ROOT_GROUP", 1)]
    [InlineData("E_CONFIG_KEY", 1)]
    [InlineData("E_FILE_NOT_FOUND", 1)]
    [InlineData("E_NOT_FOUND", 2)]
    [InlineData("E_GUI_NOT_FOUND", 2)]
    [InlineData("E_DB_ERROR", 3)]
    [InlineData("E_DB_BUSY", 3)]
    [InlineData("E_COMMAND_FAILED", 3)]
    [InlineData("E_TIMEOUT", 3)]
    [InlineData("E_ELEVATION_CANCELLED", 3)]
    [InlineData("E_INTERNAL", 3)]
    public void Maps_error_code_to_exit_code(string code, int expected)
    {
        Assert.Equal(expected, CliErrorCodes.ExitCodeFor(code));
    }

    [Fact]
    public void FileNotFound_can_override_exit_code_to_three()
    {
        var error = CliErrors.FileNotFound("脚本文件不存在: D:\\x.bat", exitCode: 3).Error;
        Assert.Equal("E_FILE_NOT_FOUND", error.Code);
        Assert.Equal(3, error.ExitCode);
    }

    [Fact]
    public void CommandFailed_carries_child_exit_code_in_details()
    {
        var error = CliErrors.CommandFailed(7).Error;
        Assert.Equal("E_COMMAND_FAILED", error.Code);
        Assert.Equal(3, error.ExitCode);
        Assert.Equal(7, error.Details!["exitCode"]!.GetValue<int>());
        Assert.Equal("命令退出码 7", error.Message);
    }
}
```

`tests/BashCommandManager.Cli.Tests/CommandLine/HelpCatalogTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class HelpCatalogTests
{
    [Fact]
    public void Catalog_defines_exactly_19_commands()
    {
        Assert.Equal(19, CommandCatalog.Commands.Count);
    }

    [Fact]
    public void Global_help_lists_every_command_usage_and_summary()
    {
        var help = HelpCatalog.Global();

        foreach (var spec in CommandCatalog.Commands)
        {
            if (spec.Path == "version")
            {
                Assert.Contains("--version", help);
            }
            else
            {
                Assert.Contains(spec.Path.Replace('.', ' '), help);
            }
            Assert.Contains(spec.Summary, help);
        }
        Assert.Contains("退出码", help);
        Assert.Contains("0 成功", help);
    }

    [Fact]
    public void Global_help_documents_global_options()
    {
        var help = HelpCatalog.Global();

        Assert.Contains("--json", help);
        Assert.Contains("--db <path>", help);
        Assert.Contains("--help", help);
        Assert.Contains("--version", help);
    }

    [Theory]
    [InlineData("groups.list")]
    [InlineData("groups.create")]
    [InlineData("groups.rename")]
    [InlineData("groups.delete")]
    [InlineData("commands.list")]
    [InlineData("commands.search")]
    [InlineData("commands.show")]
    [InlineData("commands.add")]
    [InlineData("commands.edit")]
    [InlineData("commands.delete")]
    [InlineData("commands.move")]
    [InlineData("commands.import")]
    [InlineData("commands.export")]
    [InlineData("commands.frequent")]
    [InlineData("run")]
    [InlineData("config.get")]
    [InlineData("config.set")]
    [InlineData("open")]
    [InlineData("version")]
    public void Each_command_help_contains_usage_and_example(string path)
    {
        var spec = CommandCatalog.Find(path);
        Assert.NotNull(spec);

        var help = HelpCatalog.For(path);

        Assert.Contains(spec!.Usage, help);
        Assert.Contains(spec.Summary, help);
        Assert.Contains(spec.Examples[0], help);
        if (spec.Options.Count > 0)
        {
            Assert.Contains(spec.Options[0].HelpLine, help);
        }
    }
}
```

`tests/BashCommandManager.Cli.Tests/CommandLine/CliHostHelpVersionTests.cs`：

```csharp
using BashCommandManager.Cli;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class CliHostHelpVersionTests
{
    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliHost.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void No_arguments_prints_help_and_exits_zero()
    {
        var result = Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bcm groups list", result.Stdout);
        Assert.Equal("", result.Stderr);
    }

    [Fact]
    public void Help_flag_prints_help_and_exits_zero()
    {
        var result = Run("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("用法", result.Stdout);
    }

    [Fact]
    public void Version_flag_prints_bare_version()
    {
        var result = Run("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1.3.0", result.Stdout.Trim());
    }

    [Fact]
    public void Version_with_json_wraps_in_envelope()
    {
        var result = Run("--version", "--json");

        Assert.Equal(0, result.ExitCode);
        var root = JsonNode.Parse(result.Stdout)!.AsObject();
        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal("1.3.0", root["data"]!["version"]!.GetValue<string>());
        Assert.Null(root["meta"]);
    }

    [Fact]
    public void Unknown_command_exits_one_with_usage_error_on_stderr()
    {
        var result = Run("bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        Assert.Contains("[E_USAGE]", result.Stderr);
        Assert.Contains("bogus", result.Stderr);
    }

    [Fact]
    public void Unknown_command_with_json_emits_json_error_on_stderr()
    {
        var result = Run("bogus", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        var root = JsonNode.Parse(result.Stderr)!.AsObject();
        Assert.False(root["ok"]!.GetValue<bool>());
        Assert.Equal("E_USAGE", root["error"]!["code"]!.GetValue<string>());
        Assert.NotNull(root["error"]!["hint"]);
    }
}
```

- [ ] **Step 5: 跑测试，确认因缺少类型而失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：编译失败，错误含 `CS0246: 未能找到类型或命名空间名"CommandCatalog"`（以及 `CliHost`/`CliErrors`/`CliVersion`）。若报的是 NuGet 还原失败，先处理还原再继续。

- [ ] **Step 6: 实现错误类型与退出码映射**

`BashCommandManager.Cli/Infrastructure/CliFailureException.cs`：

```csharp
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Infrastructure;

/// <summary>CLI 的统一错误对象：错误码、消息、提示、明细与退出码。</summary>
public sealed class CliError
{
    public CliError(string code, string message, int exitCode, string? hint = null, JsonObject? details = null)
    {
        Code = code;
        Message = message;
        ExitCode = exitCode;
        Hint = hint;
        Details = details;
    }

    public string Code { get; }
    public string Message { get; }
    public int ExitCode { get; }
    public string? Hint { get; }
    public JsonObject? Details { get; }
}

/// <summary>携带 CliError 的异常；CliHost 是唯一捕获点。</summary>
public sealed class CliFailureException : Exception
{
    public CliFailureException(CliError error) : base(error.Message) => Error = error;

    public CliError Error { get; }
}

/// <summary>固定错误码集合与默认退出码映射。</summary>
public static class CliErrorCodes
{
    public const string Usage = "E_USAGE";
    public const string InvalidValue = "E_INVALID_VALUE";
    public const string Ambiguous = "E_AMBIGUOUS";
    public const string ConfirmRequired = "E_CONFIRM_REQUIRED";
    public const string RootGroup = "E_ROOT_GROUP";
    public const string NotFound = "E_NOT_FOUND";
    public const string FileNotFound = "E_FILE_NOT_FOUND";
    public const string GuiNotFound = "E_GUI_NOT_FOUND";
    public const string ConfigKey = "E_CONFIG_KEY";
    public const string DbError = "E_DB_ERROR";
    public const string DbBusy = "E_DB_BUSY";
    public const string CommandFailed = "E_COMMAND_FAILED";
    public const string Timeout = "E_TIMEOUT";
    public const string ElevationCancelled = "E_ELEVATION_CANCELLED";
    public const string Internal = "E_INTERNAL";

    public static int ExitCodeFor(string code) => code switch
    {
        Usage or InvalidValue or Ambiguous or ConfirmRequired or RootGroup or ConfigKey or FileNotFound => 1,
        NotFound or GuiNotFound => 2,
        _ => 3
    };
}

/// <summary>错误工厂：所有消息文案集中在此，便于测试逐字断言。</summary>
public static class CliErrors
{
    public static CliFailureException Usage(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.Usage, message, 1, hint));

    public static CliFailureException InvalidValue(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.InvalidValue, message, 1, hint));

    public static CliFailureException Ambiguous(string what, JsonArray candidates)
        => new(new CliError(CliErrorCodes.Ambiguous, $"{what}不唯一，请改用 id 或 --group",
            CliErrorCodes.ExitCodeFor(CliErrorCodes.Ambiguous),
            "用 bcm commands list --json 或 bcm groups list --json 查看 id",
            new JsonObject { ["candidates"] = candidates }));

    public static CliFailureException ConfirmRequired(string message)
        => new(new CliError(CliErrorCodes.ConfirmRequired, message, 1,
            "确认后加 --force 重新执行；代理必须先向人类确认"));

    public static CliFailureException RootGroup()
        => new(new CliError(CliErrorCodes.RootGroup, "根分组不允许删除（Id=1）", 1,
            "如需清空，请删除根分组的子分组"));

    public static CliFailureException NotFound(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.NotFound, message, 2, hint));

    public static CliFailureException FileNotFound(string message, string? hint = null, int exitCode = 1)
        => new(new CliError(CliErrorCodes.FileNotFound, message, exitCode, hint));

    public static CliFailureException GuiNotFound(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.GuiNotFound, message, 2, hint));

    public static CliFailureException ConfigKey(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.ConfigKey, message, 1, hint));

    public static CliFailureException DbError(string message)
        => new(new CliError(CliErrorCodes.DbError, $"数据库错误: {message}", 3));

    public static CliFailureException DbBusy(string message)
        => new(new CliError(CliErrorCodes.DbBusy, $"数据库被占用，请稍后重试: {message}", 3));

    public static CliFailureException CommandFailed(int childExitCode)
        => new(new CliError(CliErrorCodes.CommandFailed, $"命令退出码 {childExitCode}", 3, null,
            new JsonObject { ["exitCode"] = childExitCode }));

    public static CliFailureException Timeout(int seconds)
        => new(new CliError(CliErrorCodes.Timeout, $"命令执行超时（{seconds} 秒），已终止进程树", 3,
            null, new JsonObject { ["timeoutSeconds"] = seconds }));

    public static CliFailureException ElevationCancelled()
        => new(new CliError(CliErrorCodes.ElevationCancelled, "已取消管理员授权", 3));

    public static CliFailureException Internal(string message)
        => new(new CliError(CliErrorCodes.Internal, $"内部错误: {message}", 3));
}
```

- [ ] **Step 7: 实现版本、命令元数据表、帮助生成**

`BashCommandManager.Cli/CommandLine/CliVersion.cs`：

```csharp
using System.Reflection;

namespace BashCommandManager.Cli.CommandLine;

/// <summary>程序集版本（csproj 的 Version），--version 与帮助头部的唯一来源。</summary>
public static class CliVersion
{
    public static string Value { get; } = Read();

    private static string Read()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
```

`BashCommandManager.Cli/CommandLine/CommandSpec.cs`：

```csharp
namespace BashCommandManager.Cli.CommandLine;

/// <summary>一个命令行选项：是否取值、帮助描述、是否必填、可选值集合。</summary>
public sealed record OptionSpec(
    string Name,
    bool TakesValue,
    string Description,
    bool Required = false,
    IReadOnlyList<string>? AllowedValues = null)
{
    public string ValuePlaceholder => Name switch
    {
        "name" => "名称",
        "parent" => "id|路径",
        "group" => "id|路径",
        "to" => "id|路径",
        "file" => "路径",
        "description" => "描述",
        "sort" => "name|last|count",
        "order" => "asc|desc",
        "limit" => "n",
        "format" => "json|csv",
        "output" => "文件",
        "timeout" => "秒",
        "command" => "id",
        "gui" => "路径",
        _ => "值"
    };

    public string HelpLine => TakesValue ? $"--{Name} <{ValuePlaceholder}>" : $"--{Name}";
}

/// <summary>一条命令的完整契约：用法、摘要、选项、位置参数个数、示例。</summary>
public sealed record CommandSpec(
    string Path,
    string Verb,
    string? SubVerb,
    string Usage,
    string Summary,
    IReadOnlyList<OptionSpec> Options,
    int MinArguments,
    int MaxArguments,
    IReadOnlyList<string> Examples);
```

`BashCommandManager.Cli/CommandLine/CommandCatalog.cs`（19 条命令，顺序即帮助顺序）：

```csharp
namespace BashCommandManager.Cli.CommandLine;

/// <summary>19 条命令的唯一元数据表：解析器、帮助、处理器分发都从这里取。</summary>
public static class CommandCatalog
{
    private static readonly OptionSpec ForceOption = new("force", false, "显式确认删除（缺省时退出 1）", Required: true);

    public static IReadOnlyList<CommandSpec> Commands { get; } = new List<CommandSpec>
    {
        new("groups.list", "groups", "list",
            "bcm groups list [--tree] [--db <path>] [--json]",
            "列出分组",
            new[] { new OptionSpec("tree", false, "以树形结构输出（children 嵌套）") },
            0, 0,
            new[] { "bcm groups list --json", "bcm groups list --tree" }),

        new("groups.create", "groups", "create",
            "bcm groups create --name <名称> [--parent <id|路径>] [--db <path>] [--json]",
            "新建分组",
            new[]
            {
                new OptionSpec("name", true, "分组名称", Required: true),
                new OptionSpec("parent", true, "父分组 id 或路径；none 表示顶级（默认 1 根分组）")
            },
            0, 0,
            new[] { "bcm groups create --name 运维 --parent 1", "bcm groups create --name 临时 --parent none" }),

        new("groups.rename", "groups", "rename",
            "bcm groups rename <id|路径> --name <新名称> [--db <path>] [--json]",
            "重命名分组",
            new[] { new OptionSpec("name", true, "新名称", Required: true) },
            1, 1,
            new[] { "bcm groups rename 2 --name 生产运维" }),

        new("groups.delete", "groups", "delete",
            "bcm groups delete <id|路径> --force [--db <path>] [--json]",
            "删除分组（级联删除子分组与命令，需 --force）",
            new[] { ForceOption },
            1, 1,
            new[] { "bcm groups delete 运维 --force" }),

        new("commands.list", "commands", "list",
            "bcm commands list [--group <id|路径>] [--sort name|last|count] [--order asc|desc] [--limit <n>] [--db <path>] [--json]",
            "列出命令",
            new[]
            {
                new OptionSpec("group", true, "只列出该分组的直属命令（不含子分组）"),
                new OptionSpec("sort", true, "排序字段，默认 name", AllowedValues: new[] { "name", "last", "count" }),
                new OptionSpec("order", true, "排序方向，默认 asc", AllowedValues: new[] { "asc", "desc" }),
                new OptionSpec("limit", true, "最多返回条数，0 表示全部（默认 0）")
            },
            0, 0,
            new[] { "bcm commands list --json", "bcm commands list --group 运维 --sort count --order desc" }),

        new("commands.search", "commands", "search",
            "bcm commands search <关键字> [--group <id|路径>] [--sort name|last|count] [--order asc|desc] [--limit <n>] [--db <path>] [--json]",
            "搜索命令（名称或描述包含关键字）",
            new[]
            {
                new OptionSpec("group", true, "只在该分组内搜索"),
                new OptionSpec("sort", true, "排序字段，默认 name", AllowedValues: new[] { "name", "last", "count" }),
                new OptionSpec("order", true, "排序方向，默认 asc", AllowedValues: new[] { "asc", "desc" }),
                new OptionSpec("limit", true, "最多返回条数，0 表示全部（默认 0）")
            },
            1, 1,
            new[] { "bcm commands search 备份 --json" }),

        new("commands.show", "commands", "show",
            "bcm commands show <id|名称> [--group <id|路径>] [--db <path>] [--json]",
            "显示单条命令",
            new[] { new OptionSpec("group", true, "在该分组内查找") },
            1, 1,
            new[] { "bcm commands show 7 --json" }),

        new("commands.add", "commands", "add",
            "bcm commands add --name <名称> --file <路径> [--description <描述>] [--group <id|路径>] [--db <path>] [--json]",
            "添加命令",
            new[]
            {
                new OptionSpec("name", true, "显示名称", Required: true),
                new OptionSpec("file", true, "批处理文件路径（必须存在）", Required: true),
                new OptionSpec("description", true, "描述，默认空"),
                new OptionSpec("group", true, "所属分组，默认根分组 1")
            },
            0, 0,
            new[] { "bcm commands add --name 备份 --file D:\\bat\\备份.bat --group 运维" }),

        new("commands.edit", "commands", "edit",
            "bcm commands edit <id|名称> [--name <新名称>] [--description <描述>] [--file <路径>] [--group <id|路径>] [--db <path>] [--json]",
            "编辑命令（至少指定一项）",
            new[]
            {
                new OptionSpec("name", true, "新名称"),
                new OptionSpec("description", true, "新描述（空串表示清空）"),
                new OptionSpec("file", true, "新文件路径（必须存在）"),
                new OptionSpec("group", true, "移动到该分组")
            },
            1, 1,
            new[] { "bcm commands edit 7 --description \"每日 2:00\"" }),

        new("commands.delete", "commands", "delete",
            "bcm commands delete <id|名称[,..]> --force [--db <path>] [--json]",
            "删除命令（需 --force）",
            new[] { ForceOption },
            1, 1,
            new[] { "bcm commands delete 7,8 --force" }),

        new("commands.move", "commands", "move",
            "bcm commands move <id|名称[,..]> --to <id|路径> [--db <path>] [--json]",
            "移动命令到其他分组",
            new[] { new OptionSpec("to", true, "目标分组 id 或路径", Required: true) },
            1, 1,
            new[] { "bcm commands move 7,8 --to 生产运维" }),

        new("commands.import", "commands", "import",
            "bcm commands import --group <id|路径> <路径...> [--recursive] [--allow-duplicates] [--dry-run] [--db <path>] [--json]",
            "导入 .bat/.cmd 文件",
            new[]
            {
                new OptionSpec("group", true, "导入目标分组", Required: true),
                new OptionSpec("recursive", false, "目录递归展开"),
                new OptionSpec("allow-duplicates", false, "允许同一 FilePath 重复导入"),
                new OptionSpec("dry-run", false, "只报告将导入的内容，不写库")
            },
            1, int.MaxValue,
            new[] { "bcm commands import --group 运维 D:\\bat --recursive" }),

        new("commands.export", "commands", "export",
            "bcm commands export [--group <id|路径>] [--recursive] [--format json|csv] [--output <文件>] [--db <path>] [--json]",
            "导出命令为 JSON/CSV",
            new[]
            {
                new OptionSpec("group", true, "只导出该分组"),
                new OptionSpec("recursive", false, "包含子分组的命令"),
                new OptionSpec("format", true, "导出格式，默认 json", AllowedValues: new[] { "json", "csv" }),
                new OptionSpec("output", true, "写入文件；缺省时输出到 stdout 的 data")
            },
            0, 0,
            new[] { "bcm commands export --format json --output backup.json", "bcm commands export --format csv --output backup.csv" }),

        new("commands.frequent", "commands", "frequent",
            "bcm commands frequent [--limit <n>] [--db <path>] [--json]",
            "列出常用命令（按执行次数降序）",
            new[] { new OptionSpec("limit", true, "返回条数，1-1000，默认 10") },
            0, 0,
            new[] { "bcm commands frequent --limit 5 --json" }),

        new("run", "run", null,
            "bcm run <id|名称> [--group <id|路径>] [--admin] [--wait] [--timeout <秒>] [--dry-run] [--db <path>] [--json]",
            "运行命令",
            new[]
            {
                new OptionSpec("group", true, "在该分组内查找命令"),
                new OptionSpec("admin", false, "以管理员身份启动（UAC）"),
                new OptionSpec("wait", false, "等待结束并返回退出码（用 /c）"),
                new OptionSpec("timeout", true, "等待超时秒数，仅与 --wait 同用"),
                new OptionSpec("dry-run", false, "只打印将启动的命令行，不执行、不写库")
            },
            1, 1,
            new[] { "bcm run 备份 --wait", "bcm run 7 --dry-run --json" }),

        new("config.get", "config", "get",
            "bcm config get [key] [--db <path>] [--json]",
            "读取配置",
            Array.Empty<OptionSpec>(),
            0, 1,
            new[] { "bcm config get --json", "bcm config get db.path --json" }),

        new("config.set", "config", "set",
            "bcm config set <hotkey|hotkey.enabled> <值> [--db <path>] [--json]",
            "写入配置",
            Array.Empty<OptionSpec>(),
            2, 2,
            new[] { "bcm config set hotkey \"Ctrl+Shift+N\"", "bcm config set hotkey.enabled false" }),

        new("open", "open", null,
            "bcm open [--command <id>] [--gui <路径>] [--db <path>] [--json]",
            "打开 GUI（可定位命令）",
            new[]
            {
                new OptionSpec("command", true, "打开后定位到该命令 id"),
                new OptionSpec("gui", true, "指定 BashCommandManager.exe 路径")
            },
            0, 0,
            new[] { "bcm open --command 7" }),

        new("version", "version", null,
            "bcm --version [--json]",
            "显示版本",
            Array.Empty<OptionSpec>(),
            0, 0,
            new[] { "bcm --version", "bcm --version --json" })
    };

    public static CommandSpec? Find(string path)
        => Commands.FirstOrDefault(spec => string.Equals(spec.Path, path, StringComparison.Ordinal));

    public static CommandSpec? Find(string verb, string? subVerb)
        => Commands.FirstOrDefault(spec =>
            string.Equals(spec.Verb, verb, StringComparison.Ordinal)
            && string.Equals(spec.SubVerb, subVerb, StringComparison.Ordinal));
}
```

> 注：`--db`/`--json`/`--help`/`--version` 是全局选项，由解析器单独处理，**不**写进任何命令的 `Options`（帮助文本会单独列出）。

`BashCommandManager.Cli/CommandLine/HelpCatalog.cs`：

```csharp
using System.Text;

namespace BashCommandManager.Cli.CommandLine;

/// <summary>从 CommandCatalog 生成帮助文本，保证帮助与解析器永不漂移。</summary>
public static class HelpCatalog
{
    public static string Global()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"bcm - BashCommandManager 命令行工具 {CliVersion.Value}");
        builder.AppendLine();
        builder.AppendLine("用法:");
        builder.AppendLine("  bcm [--json] [--db <path>] <命令> [命令选项] [位置参数]");
        builder.AppendLine("  bcm --help");
        builder.AppendLine("  bcm --version");
        builder.AppendLine();
        builder.AppendLine("命令:");
        foreach (var spec in CommandCatalog.Commands)
        {
            var label = spec.Path == "version" ? "bcm --version" : "bcm " + spec.Path.Replace('.', ' ');
            builder.AppendLine("  " + label.PadRight(26) + spec.Summary);
        }

        builder.AppendLine();
        builder.AppendLine("全局选项:");
        builder.AppendLine("  --json                      以 JSON 输出（成功写 stdout，失败写 stderr）");
        builder.AppendLine("  --db <path>                 指定数据库文件路径（默认 %LOCALAPPDATA%\\BashCommandManager\\data\\app.db）");
        builder.AppendLine("  --help                      显示帮助（可与任意命令组合）");
        builder.AppendLine("  --version                   显示版本");
        builder.AppendLine();
        builder.AppendLine("退出码:");
        builder.AppendLine("  0 成功  1 参数/用法错误  2 未找到  3 运行时失败");
        builder.AppendLine();
        builder.AppendLine("示例:");
        builder.AppendLine("  bcm groups list --json");
        builder.AppendLine("  bcm commands search 备份 --json");
        builder.AppendLine("  bcm run 7 --wait");
        builder.AppendLine("  bcm open --command 7");
        return builder.ToString();
    }

    public static string For(string commandPath)
    {
        var spec = CommandCatalog.Find(commandPath)
                   ?? throw new InvalidOperationException($"未知命令: {commandPath}");
        var builder = new StringBuilder();
        builder.AppendLine($"用法: {spec.Usage}");
        builder.AppendLine();
        builder.AppendLine(spec.Summary);
        builder.AppendLine();
        builder.AppendLine("选项:");
        foreach (var option in spec.Options)
        {
            var suffix = string.IsNullOrEmpty(option.Description) ? string.Empty : "  " + option.Description;
            builder.AppendLine("  " + option.HelpLine.PadRight(26) + suffix.TrimStart());
        }

        if (spec.Options.Count == 0)
        {
            builder.AppendLine("  （本命令没有专属选项）");
        }

        builder.AppendLine("  --db <path>                 指定数据库文件路径");
        builder.AppendLine("  --json                      以 JSON 输出");
        builder.AppendLine("  --help                      显示本帮助");
        builder.AppendLine();
        builder.AppendLine("示例:");
        foreach (var example in spec.Examples)
        {
            builder.AppendLine("  " + example);
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 8: 实现 JSON 信封、Program 与 CliHost 骨架**

`BashCommandManager.Cli/Output/JsonEnvelope.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>统一 JSON 信封：{ok:true,data,meta} / {ok:false,error}。</summary>
public static class JsonEnvelope
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonObject Success(JsonNode? data, JsonObject? meta)
    {
        var root = new JsonObject { ["ok"] = true };
        root["data"] = data?.DeepClone();
        if (meta is not null)
        {
            root["meta"] = meta;
        }
        return root;
    }

    public static JsonObject Failure(CliError error)
    {
        var node = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message
        };
        if (error.Hint is not null)
        {
            node["hint"] = error.Hint;
        }
        if (error.Details is not null)
        {
            node["details"] = error.Details.DeepClone();
        }
        return new JsonObject { ["ok"] = false, ["error"] = node };
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(SerializerOptions);
}
```

`BashCommandManager.Cli/Program.cs`：

```csharp
using BashCommandManager.Cli;
using System.Text;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return CliHost.Run(args);
    }
}
```

`BashCommandManager.Cli/CliHost.cs`（T1 版本；T2/T4 会继续扩展）：

```csharp
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli;

/// <summary>CLI 的唯一入口编排与异常边界。</summary>
public static class CliHost
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error, Console.OpenStandardError());

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null)
    {
        var wantsJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.Ordinal));
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                stdout.Write(HelpCatalog.Global());
                return 0;
            }

            if (args.Contains("--version"))
            {
                if (wantsJson)
                {
                    var payload = new JsonObject { ["version"] = CliVersion.Value };
                    stdout.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Success(payload, null)));
                }
                else
                {
                    stdout.WriteLine(CliVersion.Value);
                }
                return 0;
            }

            throw CliErrors.Usage($"未知命令: {args[0]}", "用 bcm --help 查看可用命令");
        }
        catch (CliFailureException ex)
        {
            WriteError(stderr, ex.Error, wantsJson);
            return ex.Error.ExitCode;
        }
    }

    private static void WriteError(TextWriter stderr, CliError error, bool json)
    {
        if (json)
        {
            stderr.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Failure(error)));
            return;
        }

        stderr.WriteLine($"错误 [{error.Code}] {error.Message}");
        if (error.Hint is not null)
        {
            stderr.WriteLine($"提示: {error.Hint}");
        }
    }
}
```

- [ ] **Step 9: 构建并跑测试**

```powershell
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：构建 `0 个警告 0 个错误`；`dotnet test` 末行汇总为 `Failed: 0`（总数以实际汇总为准）。

- [ ] **Step 10: 手工冒烟帮助与版本**

```powershell
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe --help
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe --version
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe bogus
$LASTEXITCODE
```

期望：第一条输出 19 行命令清单与「退出码」段、退出码 0；第二条只输出 `1.3.0`；第三条 stderr 输出 `错误 [E_USAGE] 未知命令: bogus`，`$LASTEXITCODE == 1`。

- [ ] **Step 11: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests BashCommandManager.csproj tests\BashCommandManager.Tests
git commit -m "feat(cli): 建立 bcm 项目骨架、19 条命令元数据表与帮助/版本/退出码"
```

---

### Task 2: CommandLineParser（全局选项 + 动词 + 位置参数 + `--help` 路由）

**Files:**
- Create: `BashCommandManager.Cli/CommandLine/ParsedCommand.cs`
- Create: `BashCommandManager.Cli/CommandLine/CommandLineParser.cs`
- Modify: `BashCommandManager.Cli/CliHost.cs`（改为调用解析器）
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/ParserGlobalOptionTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/ParserCommandShapeTests.cs`

**Interfaces:**
- Consumes: `CommandCatalog`、`CliErrors`、`CliVersion`、`HelpCatalog`
- Produces:
  - `public sealed class ParsedCommand { string CommandPath; string Verb; IReadOnlyDictionary<string,string> Values; IReadOnlySet<string> Flags; IReadOnlyList<string> Arguments; bool Json; string? DatabasePath; bool HasFlag(string); string? GetValue(string); bool HasValue(string); }`
  - `public static class CommandLineParser { public static ParsedCommand Parse(string[] args); public static ParsedCommand Parse(string[] args, Func<string,bool> directoryExists); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/CommandLine/ParserCommandShapeTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class ParserCommandShapeTests
{
    public static IEnumerable<object[]> Shapes => new[]
    {
        new object[] { new[] { "groups", "list" }, "groups.list" },
        new object[] { new[] { "groups", "list", "--tree" }, "groups.list" },
        new object[] { new[] { "groups", "create", "--name", "运维", "--parent", "1" }, "groups.create" },
        new object[] { new[] { "groups", "rename", "2", "--name", "生产运维" }, "groups.rename" },
        new object[] { new[] { "groups", "delete", "运维", "--force" }, "groups.delete" },
        new object[] { new[] { "commands", "list", "--group", "运维" }, "commands.list" },
        new object[] { new[] { "commands", "search", "备份" }, "commands.search" },
        new object[] { new[] { "commands", "show", "7" }, "commands.show" },
        new object[] { new[] { "commands", "add", "--name", "备份", "--file", @"D:\bat\备份.bat" }, "commands.add" },
        new object[] { new[] { "commands", "edit", "7", "--description", "每日 2:00" }, "commands.edit" },
        new object[] { new[] { "commands", "delete", "7,8", "--force" }, "commands.delete" },
        new object[] { new[] { "commands", "move", "7,8", "--to", "生产运维" }, "commands.move" },
        new object[] { new[] { "commands", "import", "--group", "运维", @"D:\bat", "--recursive" }, "commands.import" },
        new object[] { new[] { "commands", "export", "--format", "json", "--output", "backup.json" }, "commands.export" },
        new object[] { new[] { "commands", "frequent", "--limit", "5" }, "commands.frequent" },
        new object[] { new[] { "run", "备份", "--wait" }, "run" },
        new object[] { new[] { "config", "get", "db.path" }, "config.get" },
        new object[] { new[] { "config", "set", "hotkey", "Ctrl+Shift+N" }, "config.set" },
        new object[] { new[] { "open", "--command", "7" }, "open" },
        new object[] { new[] { "--version" }, "version" }
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Parses_command_shape(string[] args, string expectedPath)
    {
        var parsed = CommandLineParser.Parse(args, _ => false);

        Assert.Equal(expectedPath, parsed.CommandPath);
    }

    [Fact]
    public void Groups_create_keeps_name_with_spaces()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "create", "--name", "生产 运维", "--parent", "1" }, _ => false);

        Assert.Equal("生产 运维", parsed.GetValue("name"));
        Assert.Equal("1", parsed.GetValue("parent"));
        Assert.Empty(parsed.Arguments);
    }

    [Fact]
    public void Flags_and_values_are_separated()
    {
        var parsed = CommandLineParser.Parse(new[] { "run", "备份", "--wait", "--timeout", "30", "--dry-run" }, _ => false);

        Assert.True(parsed.HasFlag("wait"));
        Assert.True(parsed.HasFlag("dry-run"));
        Assert.False(parsed.HasFlag("admin"));
        Assert.Equal("30", parsed.GetValue("timeout"));
        Assert.Null(parsed.GetValue("wait"));
        Assert.Single(parsed.Arguments);
        Assert.Equal("备份", parsed.Arguments[0]);
    }

    [Fact]
    public void Supports_inline_value_with_equals()
    {
        var parsed = CommandLineParser.Parse(new[] { "commands", "add", "--name=备份", "--file=D:\\bat\\a.cmd" }, _ => false);

        Assert.Equal("备份", parsed.GetValue("name"));
        Assert.Equal(@"D:\bat\a.cmd", parsed.GetValue("file"));
    }

    [Fact]
    public void Global_options_may_appear_before_or_after_the_command()
    {
        var before = CommandLineParser.Parse(new[] { "--json", "--db", @"C:\tmp\a.db", "groups", "list" }, _ => false);
        var after = CommandLineParser.Parse(new[] { "groups", "list", "--json", "--db", @"C:\tmp\a.db" }, _ => false);

        Assert.True(before.Json);
        Assert.True(after.Json);
        Assert.Equal(@"C:\tmp\a.db", before.DatabasePath);
        Assert.Equal(before.DatabasePath, after.DatabasePath);
        Assert.Equal("groups.list", before.CommandPath);
        Assert.Equal("groups.list", after.CommandPath);
    }

    [Fact]
    public void Help_flag_targets_the_specific_command()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "delete", "--help" }, _ => false);

        Assert.Equal("help.groups.delete", parsed.CommandPath);
    }

    [Fact]
    public void Bare_help_means_global_help()
    {
        Assert.Equal("help", CommandLineParser.Parse(new[] { "--help" }, _ => false).CommandPath);
        Assert.Equal("help", CommandLineParser.Parse(Array.Empty<string>(), _ => false).CommandPath);
    }

    [Fact]
    public void Unknown_verb_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "bogus" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Equal(1, error.ExitCode);
        Assert.Contains("bogus", error.Message);
    }

    [Fact]
    public void Unknown_subcommand_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "groups", "bogus" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("groups bogus", error.Message);
    }

    [Fact]
    public void Unknown_option_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "groups", "list", "--bogus" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--bogus", error.Message);
    }

    [Fact]
    public void Option_missing_value_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "commands", "add", "--name" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--name", error.Message);
    }
}
```

`tests/BashCommandManager.Cli.Tests/CommandLine/ParserGlobalOptionTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class ParserGlobalOptionTests
{
    [Fact]
    public void Json_alone_without_command_means_help()
    {
        var parsed = CommandLineParser.Parse(new[] { "--json" }, _ => false);

        Assert.Equal("help", parsed.CommandPath);
        Assert.True(parsed.Json);
    }

    [Fact]
    public void Version_wins_over_help()
    {
        var parsed = CommandLineParser.Parse(new[] { "--help", "--version" }, _ => false);

        Assert.Equal("version", parsed.CommandPath);
    }

    [Fact]
    public void Database_path_is_preserved_verbatim()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "list", "--db", @"..\tmp\x.db" }, _ => false);

        Assert.Equal(@"..\tmp\x.db", parsed.DatabasePath);
    }

    [Fact]
    public void Duplicate_global_option_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(
            () => CommandLineParser.Parse(new[] { "groups", "list", "--json", "--json" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--json", error.Message);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~Parser"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"CommandLineParser"`。

- [ ] **Step 3: 实现 ParsedCommand**

`BashCommandManager.Cli/CommandLine/ParsedCommand.cs`：

```csharp
namespace BashCommandManager.Cli.CommandLine;

/// <summary>解析结果：命令路径、选项值、布尔开关、位置参数与全局选项。</summary>
public sealed class ParsedCommand
{
    public ParsedCommand(
        string commandPath,
        string verb,
        IReadOnlyDictionary<string, string> values,
        IReadOnlySet<string> flags,
        IReadOnlyList<string> arguments,
        bool json,
        string? databasePath)
    {
        CommandPath = commandPath;
        Verb = verb;
        Values = values;
        Flags = flags;
        Arguments = arguments;
        Json = json;
        DatabasePath = databasePath;
    }

    public string CommandPath { get; }
    public string Verb { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public IReadOnlySet<string> Flags { get; }
    public IReadOnlyList<string> Arguments { get; }
    public bool Json { get; }
    public string? DatabasePath { get; }

    public bool HasFlag(string name) => Flags.Contains(name);

    public string? GetValue(string name) => Values.TryGetValue(name, out var value) ? value : null;

    public bool HasValue(string name) => Values.ContainsKey(name);
}
```

- [ ] **Step 4: 实现 CommandLineParser（T2 只做结构解析，语义校验留给 T3）**

`BashCommandManager.Cli/CommandLine/CommandLineParser.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.CommandLine;

/// <summary>手写参数解析器：全局选项任意位置、两级动词、选项表来自 CommandCatalog。</summary>
public static class CommandLineParser
{
    private static readonly HashSet<string> GlobalFlags = new(StringComparer.Ordinal) { "json", "help", "version" };

    private static readonly Dictionary<string, bool> OptionArity = BuildOptionArity();

    public static ParsedCommand Parse(string[] args) => Parse(args, Directory.Exists);

    public static ParsedCommand Parse(string[] args, Func<string, bool> directoryExists)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var optionSeen = new HashSet<string>(StringComparer.Ordinal);
        var positionals = new List<string>();
        string? databasePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var raw = args[index] ?? string.Empty;
            if (!raw.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(raw);
                continue;
            }

            var name = raw[2..];
            string? inlineValue = null;
            var equalsAt = name.IndexOf('=');
            if (equalsAt >= 0)
            {
                inlineValue = name[(equalsAt + 1)..];
                name = name[..equalsAt];
            }

            if (name.Length == 0)
            {
                throw CliErrors.Usage("选项名不能为空");
            }

            if (GlobalFlags.Contains(name))
            {
                if (inlineValue is not null)
                {
                    throw CliErrors.Usage($"选项 --{name} 不接受值", "用 bcm --help 查看用法");
                }
                if (!flags.Add(name))
                {
                    throw CliErrors.Usage($"选项重复: --{name}");
                }
                continue;
            }

            if (string.Equals(name, "db", StringComparison.Ordinal))
            {
                if (!optionSeen.Add(name))
                {
                    throw CliErrors.Usage("选项重复: --db");
                }
                databasePath = inlineValue ?? TakeNextValue(args, ref index, name);
                if (string.IsNullOrWhiteSpace(databasePath))
                {
                    throw CliErrors.InvalidValue("--db 不能为空");
                }
                continue;
            }

            if (!OptionArity.TryGetValue(name, out var takesValue))
            {
                throw CliErrors.Usage($"未知选项: --{name}", "用 bcm --help 查看全部选项");
            }

            if (!optionSeen.Add(name))
            {
                throw CliErrors.Usage($"选项重复: --{name}");
            }

            if (takesValue)
            {
                values[name] = inlineValue ?? TakeNextValue(args, ref index, name);
            }
            else
            {
                if (inlineValue is not null)
                {
                    throw CliErrors.Usage($"选项 --{name} 不接受值");
                }
                flags.Add(name);
            }
        }

        if (flags.Contains("version"))
        {
            return new ParsedCommand("version", "version", values, flags, Array.Empty<string>(), flags.Contains("json"), databasePath);
        }

        var verb = positionals.Count > 0 ? positionals[0] : null;
        if (verb is null)
        {
            return new ParsedCommand("help", "help", values, flags, Array.Empty<string>(), flags.Contains("json"), databasePath);
        }

        CommandSpec? spec;
        var consumed = 1;
        if (NeedsSubVerb(verb))
        {
            var subVerb = positionals.Count > 1 ? positionals[1] : null;
            if (subVerb is null)
            {
                if (flags.Contains("help"))
                {
                    return new ParsedCommand("help", verb, values, flags, Array.Empty<string>(), flags.Contains("json"), databasePath);
                }
                throw CliErrors.Usage($"缺少子命令: bcm {verb} <{string.Join("|", SubVerbs(verb))}>");
            }
            spec = CommandCatalog.Find(verb, subVerb);
            if (spec is null)
            {
                throw CliErrors.Usage($"未知子命令: bcm {verb} {subVerb}",
                    $"可用子命令: {string.Join(", ", SubVerbs(verb))}");
            }
            consumed = 2;
        }
        else
        {
            if (CommandCatalog.Find(verb, null) is null)
            {
                throw CliErrors.Usage($"未知命令: {verb}", "用 bcm --help 查看可用命令");
            }
            spec = CommandCatalog.Find(verb, null);
        }

        var arguments = positionals.Skip(consumed).ToList();
        EnsureOptionsBelongTo(spec!, values, flags);
        var commandPath = spec!.Path;

        if (flags.Contains("help"))
        {
            return new ParsedCommand("help." + commandPath, spec.Verb, values, flags, arguments, flags.Contains("json"), databasePath);
        }

        if (databasePath is not null)
        {
            var fullPath = Path.GetFullPath(databasePath);
            if (directoryExists(fullPath))
            {
                throw CliErrors.Usage($"--db 指向目录: {databasePath}", "请指定 .db 文件路径");
            }
            databasePath = fullPath;
        }

        return new ParsedCommand(commandPath, spec.Verb, values, flags, arguments, flags.Contains("json"), databasePath);
    }

    private static string TakeNextValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw CliErrors.Usage($"选项 --{name} 缺少值", "用 bcm --help 查看用法");
        }

        index++;
        return args[index];
    }

    private static void EnsureOptionsBelongTo(CommandSpec spec, IReadOnlyDictionary<string, string> values, IReadOnlySet<string> flags)
    {
        foreach (var name in values.Keys.Concat(flags))
        {
            if (name is "json" or "help" or "version")
            {
                continue;
            }

            if (!spec.Options.Any(option => string.Equals(option.Name, name, StringComparison.Ordinal)))
            {
                throw CliErrors.Usage($"命令 {spec.Path} 不支持选项 --{name}",
                    spec.Options.Count == 0 ? "该命令没有专属选项" : $"可用选项: {string.Join(", ", spec.Options.Select(option => "--" + option.Name))}");
            }
        }
    }

    private static Dictionary<string, bool> BuildOptionArity()
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var option in CommandCatalog.Commands.SelectMany(spec => spec.Options))
        {
            if (map.TryGetValue(option.Name, out var existing) && existing != option.TakesValue)
            {
                throw new InvalidOperationException($"选项 --{option.Name} 在命令表中的取值语义不一致");
            }
            map[option.Name] = option.TakesValue;
        }
        return map;
    }

    private static bool NeedsSubVerb(string verb) => CommandCatalog.Commands.Any(spec => spec.Verb == verb && spec.SubVerb is not null);

    private static IEnumerable<string> SubVerbs(string verb)
        => CommandCatalog.Commands.Where(spec => spec.Verb == verb && spec.SubVerb is not null).Select(spec => spec.SubVerb!);
}
```

- [ ] **Step 5: CliHost 改用解析器（保留 T1 的行为）**

`BashCommandManager.Cli/CliHost.cs` 中把 T1 的 `args.Length == 0 || args.Contains("--help")` / `args.Contains("--version")` 段替换为：

```csharp
            var parsed = CommandLineParser.Parse(args);
            if (parsed.CommandPath == "help")
            {
                stdout.Write(HelpCatalog.Global());
                return 0;
            }

            if (parsed.CommandPath.StartsWith("help.", StringComparison.Ordinal))
            {
                stdout.Write(HelpCatalog.For(parsed.CommandPath["help.".Length..]));
                return 0;
            }

            if (parsed.CommandPath == "version")
            {
                if (parsed.Json)
                {
                    var payload = new JsonObject { ["version"] = CliVersion.Value };
                    stdout.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Success(payload, null)));
                }
                else
                {
                    stdout.WriteLine(CliVersion.Value);
                }
                return 0;
            }

            throw CliErrors.Usage($"未知命令: {parsed.CommandPath}", "用 bcm --help 查看可用命令");
```

- [ ] **Step 6: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed（含 T1 的 6 个 `CliHostHelpVersionTests`——它们只断言输出，重构后行为不变）。

- [ ] **Step 7: 手工验证帮助路由**

```powershell
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe groups delete --help
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe groups
$LASTEXITCODE
```

期望：第一条输出 `用法: bcm groups delete <id|路径> --force ...` 与 `--force` 说明，退出码 0；第二条 stderr 输出 `错误 [E_USAGE] 缺少子命令: bcm groups <list|create|rename|delete>`，`$LASTEXITCODE == 1`。

- [ ] **Step 8: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 手写参数解析器支持全局选项与 19 条命令形态"
```

---

### Task 3: 参数语义校验（必填/枚举/冲突/数值/`--db` 目录/`--force`）

**Files:**
- Modify: `BashCommandManager.Cli/CommandLine/CommandLineParser.cs`
- Create: `BashCommandManager.Cli/Application/HotkeyKeyMap.cs`
- Create: `BashCommandManager.Cli/Application/HotkeySpec.cs`
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/ParserValidationTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Application/HotkeySpecTests.cs`

**Interfaces:**
- Consumes: `CommandLineParser.Parse`、`CliErrors`
- Produces:
  - `public static class HotkeyKeyMap { public static bool TryGetValue(string name, out int virtualKey); public static int GetValue(string name); public static string GetName(int virtualKey); public static IReadOnlyCollection<string> Names { get; } }`
  - `public sealed class HotkeySpec { public int Key; public int Modifiers; public string KeyName; public string Display; public static HotkeySpec Create(int key, int modifiers); public static HotkeySpec Parse(string text); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Application/HotkeySpecTests.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.Application;

public class HotkeySpecTests
{
    [Theory]
    [InlineData("A", 44)]
    [InlineData("Z", 69)]
    [InlineData("N", 57)]
    [InlineData("D0", 34)]
    [InlineData("D9", 43)]
    [InlineData("F1", 90)]
    [InlineData("F24", 113)]
    [InlineData("Space", 18)]
    [InlineData("Enter", 6)]
    [InlineData("Tab", 3)]
    [InlineData("Escape", 13)]
    [InlineData("Insert", 31)]
    [InlineData("Delete", 32)]
    [InlineData("Home", 22)]
    [InlineData("End", 21)]
    [InlineData("PageUp", 19)]
    [InlineData("PageDown", 20)]
    [InlineData("Left", 23)]
    [InlineData("Up", 24)]
    [InlineData("Right", 25)]
    [InlineData("Down", 26)]
    public void Maps_key_names_to_virtual_keys(string name, int expected)
    {
        Assert.True(HotkeyKeyMap.TryGetValue(name, out var virtualKey));
        Assert.Equal(expected, virtualKey);
        Assert.Equal(expected, HotkeyKeyMap.GetValue(name.ToLowerInvariant()));
    }

    [Fact]
    public void Parses_ctrl_shift_n_like_the_gui_writes_it()
    {
        var spec = HotkeySpec.Parse("Ctrl+Shift+N");

        Assert.Equal(57, spec.Key);
        Assert.Equal(6, spec.Modifiers);
        Assert.Equal("Ctrl+Shift+N", spec.Display);
    }

    [Fact]
    public void Parses_ctrl_alt_b()
    {
        var spec = HotkeySpec.Parse("ctrl+alt+b");

        Assert.Equal(45, spec.Key);
        Assert.Equal(3, spec.Modifiers);
        Assert.Equal("Ctrl+Alt+B", spec.Display);
    }

    [Fact]
    public void Modifier_order_is_normalized()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+N", HotkeySpec.Parse("Win+Shift+Alt+Ctrl+N").Display);
    }

    [Fact]
    public void Missing_modifier_is_invalid()
    {
        var error = Assert.Throws<CliFailureException>(() => HotkeySpec.Parse("N")).Error;

        Assert.Equal("E_INVALID_VALUE", error.Code);
        Assert.Equal(1, error.ExitCode);
    }

    [Fact]
    public void Unknown_key_is_invalid()
    {
        var error = Assert.Throws<CliFailureException>(() => HotkeySpec.Parse("Ctrl+Shift+Nope")).Error;

        Assert.Equal("E_INVALID_VALUE", error.Code);
        Assert.Contains("Nope", error.Message);
    }

    [Fact]
    public void Two_main_keys_are_invalid()
    {
        var error = Assert.Throws<CliFailureException>(() => HotkeySpec.Parse("Ctrl+N+M")).Error;

        Assert.Equal("E_INVALID_VALUE", error.Code);
    }

    [Fact]
    public void Create_round_trips_a_gui_written_pair()
    {
        var spec = HotkeySpec.Create(57, 6);

        Assert.Equal("N", spec.KeyName);
        Assert.Equal("Ctrl+Shift+N", spec.Display);
    }
}
```

`tests/BashCommandManager.Cli.Tests/CommandLine/ParserValidationTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class ParserValidationTests
{
    private static CliError Fails(params string[] args)
        => Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(args, _ => false)).Error;

    [Fact]
    public void Missing_required_option_lists_an_example()
    {
        var error = Fails("groups", "create", "--parent", "1");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--name", error.Message);
        Assert.Contains("bcm groups create", error.Hint);
    }

    [Fact]
    public void Delete_without_force_requires_confirmation()
    {
        var groups = Fails("groups", "delete", "运维");
        var commands = Fails("commands", "delete", "7");

        Assert.Equal("E_CONFIRM_REQUIRED", groups.Code);
        Assert.Equal(1, groups.ExitCode);
        Assert.Contains("--force", groups.Hint);
        Assert.Equal("E_CONFIRM_REQUIRED", commands.Code);
    }

    [Fact]
    public void Too_many_positionals_is_usage_error()
    {
        var error = Fails("groups", "rename", "2", "3", "--name", "x");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("位置参数", error.Message);
    }

    [Fact]
    public void Missing_positional_is_usage_error()
    {
        var error = Fails("commands", "show");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("bcm commands show", error.Message);
    }

    [Fact]
    public void Illegal_enum_value_is_invalid_value()
    {
        var error = Fails("commands", "list", "--sort", "size");

        Assert.Equal("E_INVALID_VALUE", error.Code);
        Assert.Contains("size", error.Message);
        Assert.Contains("name,last,count", error.Hint);
    }

    [Fact]
    public void Negative_limit_is_invalid_value()
    {
        // 值 "-1" 不以 "--" 开头，会被当作 --limit 的值，随后在语义校验里被判非法
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "list", "--limit", "-1").Code);
    }

    [Fact]
    public void Option_value_starting_with_double_dash_is_treated_as_missing()
    {
        var error = Fails("commands", "search", "--limit", "--json", "备份");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--limit", error.Message);
    }

    [Fact]
    public void Non_numeric_limit_is_invalid_value()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "list", "--limit", "abc").Code);
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "frequent", "--limit", "0").Code);
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "frequent", "--limit", "1001").Code);
    }

    [Fact]
    public void Admin_with_wait_is_usage_error()
    {
        var error = Fails("run", "备份", "--admin", "--wait");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--admin", error.Message);
    }

    [Fact]
    public void Timeout_without_wait_is_usage_error()
    {
        var error = Fails("run", "备份", "--timeout", "30");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--timeout", error.Message);
    }

    [Fact]
    public void Non_positive_timeout_is_invalid_value()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("run", "备份", "--wait", "--timeout", "0").Code);
    }

    [Fact]
    public void Db_pointing_to_directory_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() =>
            CommandLineParser.Parse(new[] { "groups", "list", "--db", @"C:\temp" }, path => path.EndsWith("temp", StringComparison.OrdinalIgnoreCase))).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--db", error.Message);
    }

    [Fact]
    public void Db_path_is_made_absolute()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "list", "--db", "relative.db" }, _ => false);

        Assert.True(Path.IsPathRooted(parsed.DatabasePath));
        Assert.EndsWith("relative.db", parsed.DatabasePath);
    }

    [Fact]
    public void Edit_without_any_change_is_usage_error()
    {
        var error = Fails("commands", "edit", "7");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--name", error.Message);
    }

    [Fact]
    public void Csv_export_requires_output()
    {
        var error = Fails("commands", "export", "--format", "csv");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--output", error.Message);
    }

    [Fact]
    public void Unknown_config_key_is_config_key_error()
    {
        Assert.Equal("E_CONFIG_KEY", Fails("config", "get", "nope").Code);
        Assert.Equal("E_CONFIG_KEY", Fails("config", "set", "nope", "x").Code);
    }

    [Fact]
    public void Config_set_hotkey_enabled_requires_boolean()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("config", "set", "hotkey.enabled", "yes").Code);
    }

    [Fact]
    public void Open_command_id_must_be_positive_integer()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("open", "--command", "abc").Code);
        Assert.Equal("E_INVALID_VALUE", Fails("open", "--command", "0").Code);
    }

    [Fact]
    public void Option_not_belonging_to_command_is_usage_error()
    {
        var error = Fails("groups", "list", "--force");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("groups.list", error.Message);
    }

    [Fact]
    public void Delete_help_bypasses_force_requirement()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "delete", "--help" }, _ => false);

        Assert.Equal("help.groups.delete", parsed.CommandPath);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ParserValidationTests|FullyQualifiedName~HotkeySpecTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"HotkeySpec"`（`ParserValidationTests` 也会因行为未实现而失败，待实现后转绿）。

- [ ] **Step 3: 实现 HotkeyKeyMap**

`BashCommandManager.Cli/Application/HotkeyKeyMap.cs`：

```csharp
namespace BashCommandManager.Cli.Application;

/// <summary>键名与虚拟键码映射（与 System.Windows.Input.Key 数值一致，CLI 不引用 WPF）。</summary>
public static class HotkeyKeyMap
{
    private static readonly Dictionary<string, int> ByName = Build();
    private static readonly Dictionary<int, string> ByValue = BuildReverse();

    public static IReadOnlyCollection<string> Names => ByName.Keys;

    public static bool TryGetValue(string name, out int virtualKey)
        => ByName.TryGetValue(Normalize(name), out virtualKey);

    public static int GetValue(string name)
        => TryGetValue(name, out var virtualKey)
            ? virtualKey
            : throw new ArgumentOutOfRangeException(nameof(name), name, "未知按键");

    public static string GetName(int virtualKey)
        => ByValue.TryGetValue(virtualKey, out var name) ? name : virtualKey.ToString();

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static Dictionary<string, int> Build()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["space"] = 18,
            ["tab"] = 3,
            ["enter"] = 6,
            ["escape"] = 13,
            ["insert"] = 31,
            ["delete"] = 32,
            ["home"] = 22,
            ["end"] = 21,
            ["pageup"] = 19,
            ["pagedown"] = 20,
            ["left"] = 23,
            ["up"] = 24,
            ["right"] = 25,
            ["down"] = 26
        };

        for (var key = 44; key <= 69; key++)
        {
            map[((char)('A' + key - 44)).ToString().ToLowerInvariant()] = key;
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            map[$"d{digit}"] = 34 + digit;
            map[digit.ToString()] = 34 + digit;
        }

        for (var function = 1; function <= 24; function++)
        {
            map[$"f{function}"] = 89 + function;
        }

        return map;
    }

    private static Dictionary<int, string> BuildReverse()
    {
        var map = new Dictionary<int, string>();
        for (var letter = 0; letter < 26; letter++)
        {
            map[44 + letter] = ((char)('A' + letter)).ToString();
        }
        for (var digit = 0; digit <= 9; digit++)
        {
            map[34 + digit] = "D" + digit;
        }
        for (var function = 1; function <= 24; function++)
        {
            map[89 + function] = "F" + function;
        }
        map[18] = "Space";
        map[3] = "Tab";
        map[6] = "Enter";
        map[13] = "Escape";
        map[31] = "Insert";
        map[32] = "Delete";
        map[22] = "Home";
        map[21] = "End";
        map[19] = "PageUp";
        map[20] = "PageDown";
        map[23] = "Left";
        map[24] = "Up";
        map[25] = "Right";
        map[26] = "Down";
        return map;
    }
}
```

- [ ] **Step 4: 实现 HotkeySpec**

`BashCommandManager.Cli/Application/HotkeySpec.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using System.Text;

namespace BashCommandManager.Cli.Application;

/// <summary>快捷键规格：Key/Modifiers 与 WPF 枚举数值一致，Display 为规范文本。</summary>
public sealed class HotkeySpec
{
    public const int ModifierAlt = 1;
    public const int ModifierControl = 2;
    public const int ModifierShift = 4;
    public const int ModifierWindows = 8;

    private HotkeySpec(int key, int modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public int Key { get; }
    public int Modifiers { get; }
    public string KeyName => HotkeyKeyMap.GetName(Key);
    public string Display => FormatModifiers(Modifiers) + "+" + KeyName;

    public static HotkeySpec Create(int key, int modifiers) => new(key, modifiers);

    public static HotkeySpec Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw CliErrors.InvalidValue("快捷键不能为空", "示例: Ctrl+Shift+N");
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw CliErrors.InvalidValue($"快捷键缺少修饰键: {text}", "示例: Ctrl+Shift+N");
        }

        var modifiers = 0;
        string? keyName = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierControl;
                    break;
                case "alt":
                    modifiers |= ModifierAlt;
                    break;
                case "shift":
                    modifiers |= ModifierShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierWindows;
                    break;
                default:
                    if (keyName is not null)
                    {
                        throw CliErrors.InvalidValue($"快捷键只能包含一个主键: {text}");
                    }
                    if (!HotkeyKeyMap.TryGetValue(part, out _))
                    {
                        throw CliErrors.InvalidValue($"无法识别的按键: {part}",
                            "可用按键: A-Z、D0-D9、F1-F24、Space、Tab、Enter、Escape、Insert、Delete、Home、End、PageUp、PageDown、方向键");
                    }
                    keyName = part;
                    break;
            }
        }

        if (modifiers == 0)
        {
            throw CliErrors.InvalidValue($"快捷键至少需要一个修饰键: {text}", "示例: Ctrl+Shift+N");
        }

        if (keyName is null)
        {
            throw CliErrors.InvalidValue($"快捷键缺少主键: {text}", "示例: Ctrl+Shift+N");
        }

        return new HotkeySpec(HotkeyKeyMap.GetValue(keyName), modifiers);
    }

    public static string FormatModifiers(int modifiers)
    {
        var builder = new StringBuilder();
        if ((modifiers & ModifierControl) != 0)
        {
            builder.Append("Ctrl+");
        }
        if ((modifiers & ModifierAlt) != 0)
        {
            builder.Append("Alt+");
        }
        if ((modifiers & ModifierShift) != 0)
        {
            builder.Append("Shift+");
        }
        if ((modifiers & ModifierWindows) != 0)
        {
            builder.Append("Win+");
        }
        return builder.ToString();
    }
}
```

- [ ] **Step 5: 在解析器中加入语义校验**

在 `CommandLineParser.Parse` 里、`--db` 目录校验**之前**插入下面三行调用（此时 `spec`/`values`/`flags`/`arguments` 都已就绪）：

```csharp
        ValidateRequiredOptions(spec!, values, flags);
        ValidateArgumentCount(spec!, arguments);
        ValidateSemantics(spec!, values, flags, arguments);
```

并在同一文件的类末尾追加 `ConfigKeys` 字段与三个校验方法：

```csharp
    private static readonly HashSet<string> ConfigKeys = new(StringComparer.Ordinal)
    {
        "hotkey", "hotkey.enabled", "db.path", "db.source", "settings.path", "gui.path", "version"
    };

    private static void ValidateRequiredOptions(CommandSpec spec, IReadOnlyDictionary<string, string> values, IReadOnlySet<string> flags)
    {
        foreach (var option in spec.Options.Where(option => option.Required))
        {
            var present = values.ContainsKey(option.Name) || flags.Contains(option.Name);
            if (present)
            {
                continue;
            }

            if (option.Name == "force")
            {
                throw CliErrors.ConfirmRequired($"命令 {spec.Path} 删除数据需要显式确认（--force）");
            }

            throw CliErrors.Usage($"缺少必填选项 --{option.Name}: {spec.Usage}", $"示例: {spec.Examples[0]}");
        }
    }

    private static void ValidateArgumentCount(CommandSpec spec, IReadOnlyList<string> arguments)
    {
        if (arguments.Count < spec.MinArguments)
        {
            throw CliErrors.Usage($"缺少位置参数: {spec.Usage}", $"示例: {spec.Examples[0]}");
        }

        if (arguments.Count > spec.MaxArguments)
        {
            throw CliErrors.Usage($"位置参数过多（最多 {spec.MaxArguments} 个）: {spec.Usage}");
        }
    }
```

`ValidateSemantics` 的完整实现（同样追加到类末尾）：

```csharp
    private static void ValidateSemantics(
        CommandSpec spec,
        IReadOnlyDictionary<string, string> values,
        IReadOnlySet<string> flags,
        IReadOnlyList<string> arguments)
    {
        foreach (var option in spec.Options)
        {
            if (option.AllowedValues is null || !values.TryGetValue(option.Name, out var value))
            {
                continue;
            }

            if (!option.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                throw CliErrors.InvalidValue($"--{option.Name} 的值非法: {value}",
                    $"可选值: {string.Join(",", option.AllowedValues)}");
            }
        }

        switch (spec.Path)
        {
            case "commands.list":
            case "commands.search":
                if (values.TryGetValue("limit", out var limitText) && (!int.TryParse(limitText, out var limit) || limit < 0))
                {
                    throw CliErrors.InvalidValue($"--limit 需要非负整数: {limitText}");
                }
                break;

            case "commands.frequent":
                if (values.TryGetValue("limit", out var frequentText) &&
                    (!int.TryParse(frequentText, out var frequentLimit) || frequentLimit < 1 || frequentLimit > 1000))
                {
                    throw CliErrors.InvalidValue($"--limit 需要 1-1000 的整数: {frequentText}");
                }
                break;

            case "commands.edit":
                var hasChange = spec.Options.Any(option => values.ContainsKey(option.Name) || flags.Contains(option.Name));
                if (!hasChange)
                {
                    throw CliErrors.Usage("commands edit 至少需要一项修改: --name/--description/--file/--group",
                        "示例: bcm commands edit 7 --description \"每日 2:00\"");
                }
                break;

            case "commands.export":
                if (values.TryGetValue("format", out var format) &&
                    string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) &&
                    !values.ContainsKey("output"))
                {
                    throw CliErrors.Usage("--format csv 需要同时指定 --output <文件>",
                        "示例: bcm commands export --format csv --output backup.csv");
                }
                break;

            case "run":
                var wait = flags.Contains("wait");
                if (flags.Contains("admin") && wait)
                {
                    throw CliErrors.Usage("--admin 不能与 --wait 同时使用", "提权窗口不支持等待退出码");
                }
                if (values.TryGetValue("timeout", out var timeoutText))
                {
                    if (!wait)
                    {
                        throw CliErrors.Usage("--timeout 只能与 --wait 一起使用");
                    }
                    if (!int.TryParse(timeoutText, out var seconds) || seconds < 1)
                    {
                        throw CliErrors.InvalidValue($"--timeout 需要正整数秒: {timeoutText}");
                    }
                }
                break;

            case "config.get":
                if (arguments.Count == 1 && !ConfigKeys.Contains(arguments[0]))
                {
                    throw CliErrors.ConfigKey($"未知配置键: {arguments[0]}", $"可用键: {string.Join(",", ConfigKeys)}");
                }
                break;

            case "config.set":
                var configKey = arguments[0];
                if (configKey is not ("hotkey" or "hotkey.enabled"))
                {
                    throw CliErrors.ConfigKey($"未知配置键: {configKey}", "可用键: hotkey,hotkey.enabled");
                }
                if (configKey == "hotkey.enabled" && !bool.TryParse(arguments[1], out _))
                {
                    throw CliErrors.InvalidValue($"hotkey.enabled 需要 true 或 false: {arguments[1]}");
                }
                if (configKey == "hotkey")
                {
                    HotkeySpec.Parse(arguments[1]);
                }
                break;

            case "open":
                if (values.TryGetValue("command", out var commandText) &&
                    (!int.TryParse(commandText, out var commandId) || commandId < 1))
                {
                    throw CliErrors.InvalidValue($"--command 需要正整数 id: {commandText}");
                }
                break;
        }
    }
```

并在 `CommandLineParser.cs` 文件头加一行 `using BashCommandManager.Cli.Application;`（`HotkeySpec` 在解析期做格式校验）。

- [ ] **Step 6: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed。重点确认 `Delete_without_force_requires_confirmation`、`Admin_with_wait_is_usage_error`、`Db_pointing_to_directory_is_usage_error` 三条通过。

- [ ] **Step 7: 手工验证三类错误**

```powershell
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
& $exe groups delete 运维 ; $LASTEXITCODE
& $exe run 备份 --admin --wait ; $LASTEXITCODE
& $exe commands list --sort size ; $LASTEXITCODE
```

期望：依次为 `错误 [E_CONFIRM_REQUIRED] ...` → 1、`错误 [E_USAGE] --admin 不能与 --wait 同时使用` → 1、`错误 [E_INVALID_VALUE] --sort 的值非法: size` 且 `提示: 可选值: name,last,count` → 1。

- [ ] **Step 8: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 参数语义校验覆盖必填/枚举/冲突/超时与 --force"
```

### Task 4: Output 层（CliResult / JsonShapes / TableWriter / OutputWriter）

**Files:**
- Create: `BashCommandManager.Cli/Output/CliResult.cs`
- Create: `BashCommandManager.Cli/Output/JsonShapes.cs`
- Create: `BashCommandManager.Cli/Output/TableWriter.cs`
- Create: `BashCommandManager.Cli/Output/OutputWriter.cs`
- Modify: `BashCommandManager.Cli/CliHost.cs`（成功/失败都改走 `OutputWriter`）
- Test: `tests/BashCommandManager.Cli.Tests/Output/JsonEnvelopeTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Output/TableWriterTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Output/OutputWriterTests.cs`

**Interfaces:**
- Consumes: `CliError`/`CliFailureException`、`JsonEnvelope`、`BashCommandManager.Core.Models.Group`/`Command`
- Produces:
  - `public sealed record CliResult(JsonNode? Data, JsonObject? Meta = null, string? HumanText = null) { public static CliResult Object(JsonObject data, string? humanText = null); public static CliResult Items(JsonArray items, string? humanText = null); }`
  - `public static class JsonShapes { public static JsonObject Group(Group group, int commandCount); public static JsonObject Command(Command command, string? groupName, bool includeFileExists, Func<string,bool> fileExists); public static string FormatTimestamp(DateTime? value); }`
  - `public static class TableWriter { public static string Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows); public static int DisplayWidth(string text); }`
  - `public interface IOutputWriter { void WriteResult(CliResult result, JsonObject? meta); void WriteError(CliError error); }`
  - `public sealed class OutputWriter : IOutputWriter { public OutputWriter(TextWriter stdout, TextWriter stderr, bool json); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Output/TableWriterTests.cs`：

```csharp
using BashCommandManager.Cli.Output;

namespace BashCommandManager.Cli.Tests.Output;

public class TableWriterTests
{
    [Fact]
    public void Renders_aligned_columns_with_two_space_gap()
    {
        var text = TableWriter.Render(
            new[] { "Id", "Name" },
            new IReadOnlyList<string>[]
            {
                new[] { "7", "备份" },
                new[] { "10", "deploy" }
            });

        var lines = text.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("Id  Name", lines[0]);
        Assert.Equal("7   备份", lines[1]);
        Assert.Equal("10  deploy", lines[2]);
    }

    [Fact]
    public void Counts_cjk_characters_as_two_columns()
    {
        Assert.Equal(2, TableWriter.DisplayWidth("备"));
        Assert.Equal(6, TableWriter.DisplayWidth("备份"));
        Assert.Equal(3, TableWriter.DisplayWidth("abc"));
    }

    [Fact]
    public void Empty_rows_render_only_headers()
    {
        var text = TableWriter.Render(new[] { "Id" }, Array.Empty<IReadOnlyList<string>>());

        Assert.Equal("Id", text);
    }
}
```

`tests/BashCommandManager.Cli.Tests/Output/JsonEnvelopeTests.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Output;

public class JsonEnvelopeTests
{
    [Fact]
    public void Success_envelope_has_ok_data_and_meta()
    {
        var data = new JsonObject { ["id"] = 1 };
        var meta = new JsonObject { ["count"] = 1 };

        var text = JsonEnvelope.Serialize(JsonEnvelope.Success(data, meta));
        var root = JsonNode.Parse(text)!.AsObject();

        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal(1, root["data"]!["id"]!.GetValue<int>());
        Assert.Equal(1, root["meta"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void Success_envelope_omits_meta_when_null()
    {
        var text = JsonEnvelope.Serialize(JsonEnvelope.Success(new JsonObject(), null));

        Assert.DoesNotContain("\"meta\"", text);
    }

    [Fact]
    public void Failure_envelope_carries_code_message_hint_and_details()
    {
        var error = CliErrors.Ambiguous("命令", new JsonArray { new JsonObject { ["id"] = 3 } }).Error;

        var text = JsonEnvelope.Serialize(JsonEnvelope.Failure(error));
        var root = JsonNode.Parse(text)!.AsObject();
        var node = root["error"]!.AsObject();

        Assert.False(root["ok"]!.GetValue<bool>());
        Assert.Equal("E_AMBIGUOUS", node["code"]!.GetValue<string>());
        Assert.Contains("不唯一", node["message"]!.GetValue<string>());
        Assert.NotNull(node["hint"]);
        Assert.Equal(3, node["details"]!["candidates"]![0]!["id"]!.GetValue<int>());
    }

    [Fact]
    public void Chinese_is_not_escaped()
    {
        var text = JsonEnvelope.Serialize(JsonEnvelope.Success(new JsonObject { ["name"] = "备份库" }, null));

        Assert.Contains("备份库", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void Command_shape_has_expected_fields_and_null_timestamp()
    {
        var command = new Command
        {
            Id = 7,
            Name = "备份",
            Description = null,
            FilePath = @"D:\bat\备份.bat",
            GroupId = 2,
            SortOrder = 0,
            ExecutionCount = 3,
            LastExecutedAt = null
        };

        var node = JsonShapes.Command(command, "运维", includeFileExists: true, _ => true);

        Assert.Equal(7, node["id"]!.GetValue<int>());
        Assert.Equal(string.Empty, node["description"]!.GetValue<string>());
        Assert.Equal("运维", node["groupName"]!.GetValue<string>());
        Assert.Null(node["lastExecutedAt"]);
        Assert.True(node["fileExists"]!.GetValue<bool>());
        Assert.Equal(new[] { "id", "name", "description", "filePath", "groupId", "groupName", "sortOrder", "executionCount", "lastExecutedAt", "fileExists" },
            node.Select(pair => pair.Key).ToArray());
    }

    [Fact]
    public void Timestamp_is_local_iso_without_fraction_or_offset()
    {
        var value = new DateTime(2026, 11, 8, 20, 47, 32, 123);

        Assert.Equal("2026-11-08T20:47:32", JsonShapes.FormatTimestamp(value));
        Assert.Null(JsonShapes.FormatTimestamp(null));
    }

    [Fact]
    public void Group_shape_has_command_count_and_null_parent()
    {
        var group = new Group { Id = 1, Name = "根分组", ParentId = null, SortOrder = 0 };

        var node = JsonShapes.Group(group, 2);

        Assert.Equal(1, node["id"]!.GetValue<int>());
        Assert.Null(node["parentId"]);
        Assert.Equal(2, node["commandCount"]!.GetValue<int>());
        Assert.Equal(new[] { "id", "name", "parentId", "sortOrder", "commandCount" }, node.Select(pair => pair.Key).ToArray());
    }
}
```

`tests/BashCommandManager.Cli.Tests/Output/OutputWriterTests.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Output;

public class OutputWriterTests
{
    private static (OutputWriter Writer, StringWriter Out, StringWriter Err) Create(bool json)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        return (new OutputWriter(stdout, stderr, json), stdout, stderr);
    }

    [Fact]
    public void Json_success_goes_to_stdout_only()
    {
        var (writer, stdout, stderr) = Create(json: true);
        var data = new JsonArray(new JsonObject { ["id"] = 1, ["name"] = "备份" });

        writer.WriteResult(CliResult.Items(data!), new JsonObject { ["dbPath"] = "x", ["created"] = false, ["count"] = 1 });

        Assert.Empty(stderr.ToString());
        var root = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal(1, root["meta"]!["count"]!.GetValue<int>());
        Assert.Contains("备份", stdout.ToString());
    }

    [Fact]
    public void Json_error_goes_to_stderr_with_empty_stdout()
    {
        var (writer, stdout, stderr) = Create(json: true);

        writer.WriteError(CliErrors.NotFound("未找到命令: 备份", "用 bcm commands list --json 查看可用 id").Error);

        Assert.Empty(stdout.ToString());
        var root = JsonNode.Parse(stderr.ToString())!.AsObject();
        Assert.False(root["ok"]!.GetValue<bool>());
        Assert.Equal("E_NOT_FOUND", root["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Human_error_line_matches_spec_format()
    {
        var (writer, _, stderr) = Create(json: false);

        writer.WriteError(CliErrors.NotFound("未找到命令: 备份", "用 bcm commands list --json 查看可用 id").Error);

        var lines = stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("错误 [E_NOT_FOUND] 未找到命令: 备份", lines[0]);
        Assert.Equal("提示: 用 bcm commands list --json 查看可用 id", lines[1]);
    }

    [Fact]
    public void Human_object_renders_key_value_lines()
    {
        var (writer, stdout, _) = Create(json: false);
        var data = new JsonObject { ["id"] = 7, ["name"] = "备份", ["lastExecutedAt"] = null };

        writer.WriteResult(CliResult.Object(data), null);

        var text = stdout.ToString();
        Assert.Contains("id: 7", text);
        Assert.Contains("name: 备份", text);
        Assert.Contains("lastExecutedAt: ", text);
    }

    [Fact]
    public void Human_array_renders_table_with_mapped_headers()
    {
        var (writer, stdout, _) = Create(json: false);
        var data = new JsonArray
        {
            new JsonObject
            {
                ["id"] = 7,
                ["name"] = "备份",
                ["groupName"] = "运维",
                ["filePath"] = @"D:\bat\备份.bat"
            }
        };

        writer.WriteResult(CliResult.Items(data), null);

        var lines = stdout.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id  Name  Group  FilePath", lines[0]);
        Assert.Contains("备份", lines[1]);
        Assert.Contains(@"D:\bat\备份.bat", lines[1]);
    }

    [Fact]
    public void Human_array_uses_explicit_human_text_when_provided()
    {
        var (writer, stdout, _) = Create(json: false);

        writer.WriteResult(CliResult.Object(new JsonObject { ["id"] = 7 }, "自定义输出"), null);

        Assert.Equal("自定义输出", stdout.ToString().Trim());
    }

    [Fact]
    public void Empty_human_array_prints_placeholder()
    {
        var (writer, stdout, _) = Create(json: false);

        writer.WriteResult(CliResult.Items(new JsonArray()), null);

        Assert.Equal("(空)", stdout.ToString().Trim());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~Output"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"TableWriter"` / `"JsonShapes"` / `"OutputWriter"`。

- [ ] **Step 3: 实现 CliResult 与 JsonShapes**

`BashCommandManager.Cli/Output/CliResult.cs`：

```csharp
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>处理器返回值：data 必填、meta 追加字段可选、HumanText 覆盖人类可读渲染。</summary>
public sealed record CliResult(JsonNode? Data, JsonObject? Meta = null, string? HumanText = null)
{
    public static CliResult Object(JsonObject data, string? humanText = null) => new(data, null, humanText);

    public static CliResult Items(JsonArray items, string? humanText = null) => new(items, null, humanText);
}
```

`BashCommandManager.Cli/Output/JsonShapes.cs`：

```csharp
using BashCommandManager.Core.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>分组/命令的 JSON 形状：字段名与时间格式的唯一来源。</summary>
public static class JsonShapes
{
    public static JsonObject Group(Group group, int commandCount) => new()
    {
        ["id"] = group.Id,
        ["name"] = group.Name,
        ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null,
        ["sortOrder"] = group.SortOrder,
        ["commandCount"] = commandCount
    };

    public static JsonObject GroupNode(Group group) => new()
    {
        ["id"] = group.Id,
        ["name"] = group.Name,
        ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null,
        ["sortOrder"] = group.SortOrder
    };

    public static JsonObject Command(Command command, string? groupName, bool includeFileExists, Func<string, bool> fileExists)
    {
        var node = new JsonObject
        {
            ["id"] = command.Id,
            ["name"] = command.Name,
            ["description"] = command.Description ?? string.Empty,
            ["filePath"] = command.FilePath,
            ["groupId"] = command.GroupId,
            ["groupName"] = groupName ?? string.Empty,
            ["sortOrder"] = command.SortOrder,
            ["executionCount"] = command.ExecutionCount,
            ["lastExecutedAt"] = command.LastExecutedAt.HasValue
                ? JsonValue.Create(FormatTimestamp(command.LastExecutedAt))
                : null
        };

        if (includeFileExists)
        {
            node["fileExists"] = fileExists(command.FilePath);
        }

        return node;
    }

    public static string? FormatTimestamp(DateTime? value)
        => value?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: 实现 TableWriter**

`BashCommandManager.Cli/Output/TableWriter.cs`：

```csharp
using System.Text;

namespace BashCommandManager.Cli.Output;

/// <summary>等宽表格渲染；CJK 字符按 2 列计宽。</summary>
public static class TableWriter
{
    public static string Render(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var widths = headers.Select(DisplayWidth).ToArray();
        foreach (var row in rows)
        {
            for (var column = 0; column < widths.Length && column < row.Count; column++)
            {
                widths[column] = Math.Max(widths[column], DisplayWidth(row[column]));
            }
        }

        var builder = new StringBuilder();
        AppendRow(builder, headers, widths);
        foreach (var row in rows)
        {
            AppendRow(builder, row, widths);
        }

        return builder.ToString().TrimEnd('\n');
    }

    public static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var character in text)
        {
            width += character >= 0x2E80 ? 2 : 1;
        }
        return width;
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells, int[] widths)
    {
        for (var column = 0; column < cells.Count; column++)
        {
            builder.Append(cells[column]);
            if (column < cells.Count - 1)
            {
                builder.Append(' ', Math.Max(1, widths[column] - DisplayWidth(cells[column])) + 2);
            }
        }
        builder.Append('\n');
    }
}
```

> 表格统一用 `\n` 作行分隔（不用 `AppendLine`），这样测试可以按 `'\n'` 精确切分，也不会在 Windows 上留下 `\r`。

- [ ] **Step 5: 实现 OutputWriter**

`BashCommandManager.Cli/Output/OutputWriter.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>输出契约：成功写 stdout、失败写 stderr；JSON 或人类可读两种模式。</summary>
public interface IOutputWriter
{
    void WriteResult(CliResult result, JsonObject? meta);

    void WriteError(CliError error);
}

/// <summary>IOutputWriter 的默认实现。</summary>
public sealed class OutputWriter : IOutputWriter
{
    private static readonly Dictionary<string, string> HeaderLabels = new(StringComparer.Ordinal)
    {
        ["id"] = "Id",
        ["name"] = "Name",
        ["parentId"] = "ParentId",
        ["sortOrder"] = "SortOrder",
        ["commandCount"] = "命令数",
        ["groupId"] = "GroupId",
        ["groupName"] = "Group",
        ["description"] = "Description",
        ["filePath"] = "FilePath",
        ["executionCount"] = "执行次数",
        ["lastExecutedAt"] = "上次执行",
        ["fileExists"] = "文件存在",
        ["enabled"] = "Enabled",
        ["key"] = "Key",
        ["keyName"] = "KeyName",
        ["modifiers"] = "Modifiers",
        ["display"] = "Display",
        ["path"] = "Path",
        ["format"] = "Format",
        ["count"] = "Count"
    };

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _json;

    public OutputWriter(TextWriter stdout, TextWriter stderr, bool json)
    {
        _stdout = stdout;
        _stderr = stderr;
        _json = json;
    }

    public void WriteResult(CliResult result, JsonObject? meta)
    {
        if (_json)
        {
            _stdout.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Success(result.Data, meta)));
            return;
        }

        var text = result.HumanText ?? RenderHuman(result.Data);
        if (text.Length > 0)
        {
            _stdout.WriteLine(text);
        }
    }

    public void WriteError(CliError error)
    {
        if (_json)
        {
            _stderr.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Failure(error)));
            return;
        }

        _stderr.WriteLine($"错误 [{error.Code}] {error.Message}");
        if (error.Hint is not null)
        {
            _stderr.WriteLine($"提示: {error.Hint}");
        }
    }

    private static string RenderHuman(JsonNode? data) => data switch
    {
        null => string.Empty,
        JsonObject node => RenderObject(node, string.Empty),
        JsonArray array => RenderArray(array),
        _ => data.ToJsonString(JsonEnvelope.SerializerOptions)
    };

    private static string RenderObject(JsonObject node, string prefix)
    {
        var lines = new List<string>();
        foreach (var pair in node)
        {
            var label = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
            if (pair.Value is JsonObject nested)
            {
                lines.AddRange(RenderObject(nested, label).Split(Environment.NewLine));
                continue;
            }

            lines.Add($"{label}: {ScalarText(pair.Value)}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderArray(JsonArray array)
    {
        if (array.Count == 0)
        {
            return "(空)";
        }

        if (array.All(item => item is JsonObject))
        {
            var headers = ((JsonObject)array[0]!).Select(pair => HeaderLabel(pair.Key)).ToList();
            var rows = array
                .Select(item => (IReadOnlyList<string>)((JsonObject)item!)
                    .Select(pair => ScalarText(pair.Value))
                    .ToList())
                .ToList();
            return TableWriter.Render(headers, rows);
        }

        return string.Join(Environment.NewLine, array.Select(item => item?.ToJsonString(JsonEnvelope.SerializerOptions) ?? "null"));
    }

    private static string HeaderLabel(string key) => HeaderLabels.TryGetValue(key, out var label) ? label : key;

    private static string ScalarText(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToJsonString(JsonEnvelope.SerializerOptions);
    }
}
```

- [ ] **Step 6: CliHost 改用 OutputWriter（完整文件）**

```csharp
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Data.SQLite;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli;

/// <summary>CLI 的唯一入口编排与异常边界。</summary>
public static class CliHost
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error, Console.OpenStandardError());

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null)
    {
        var wantsJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.Ordinal));
        try
        {
            var parsed = CommandLineParser.Parse(args);
            var output = new OutputWriter(stdout, stderr, parsed.Json);

            if (parsed.CommandPath == "help")
            {
                stdout.Write(HelpCatalog.Global());
                return 0;
            }

            if (parsed.CommandPath.StartsWith("help.", StringComparison.Ordinal))
            {
                stdout.Write(HelpCatalog.For(parsed.CommandPath["help.".Length..]));
                return 0;
            }

            if (parsed.CommandPath == "version")
            {
                output.WriteResult(new CliResult(new JsonObject { ["version"] = CliVersion.Value }, null, CliVersion.Value), null);
                return 0;
            }

            throw CliErrors.Usage($"命令尚未接线: {parsed.CommandPath}", "T7 起由 CliDispatcher 处理");
        }
        catch (CliFailureException ex)
        {
            new OutputWriter(stdout, stderr, wantsJson).WriteError(ex.Error);
            return ex.Error.ExitCode;
        }
        catch (SQLiteException ex)
        {
            var error = CliErrors.DbError(ex.Message);
            new OutputWriter(stdout, stderr, wantsJson).WriteError(error);
            return error.ExitCode;
        }
        catch (Exception ex)
        {
            var error = CliErrors.Internal(ex.Message);
            new OutputWriter(stdout, stderr, wantsJson).WriteError(error);
            return error.ExitCode;
        }
    }
}
```

- [ ] **Step 7: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed（T1/T2/T3 的 `CliHostHelpVersionTests` 仍全绿，因为输出契约未变）。

- [ ] **Step 8: 手工验证表格输出**

```powershell
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe --version
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe --version --json
```

期望：第一条输出 `1.3.0`；第二条输出单行紧凑 JSON `{"ok":true,"data":{"version":"1.3.0"}}`（无 `meta`）。

- [ ] **Step 9: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 统一 JSON 信封与人类可读输出层"
```

---

### Task 5: DatabasePathResolver + DatabaseMigrator（GUI 与 CLI 共用）

**Files:**
- Create: `Infrastructure/DatabasePathResolver.cs`
- Test: `tests/BashCommandManager.Tests/Infrastructure/DatabasePathResolverTests.cs`
- Test: `tests/BashCommandManager.Tests/Infrastructure/DatabaseMigratorTests.cs`

**Interfaces:**
- Consumes: `System.Data.SQLite.SQLiteConnection.BackupDatabase`
- Produces:
  - `public enum DatabasePathSource { Argument, Environment, LocalAppData, LegacyApplicationDirectory }`
  - `public sealed record DatabasePathResolution(string DatabasePath, DatabasePathSource Source, string? LegacyPath, bool RequiresMigration)`
  - `public sealed record DatabasePreparationResult(string DatabasePath, bool Migrated, string? Warning)`
  - `public static class DatabasePathResolver { public const string EnvironmentVariableName = "BASHCOMMANDMANAGER_DB"; public static string SettingsDirectory { get; } public static string CanonicalDatabasePath { get; } public static string LegacyDatabasePath { get; } public static DatabasePathResolution Resolve(string? explicitPath, string? environmentValue, Func<string,bool> fileExists, string? settingsDirectory = null, string? legacyDatabasePath = null); public static DatabasePathResolution ResolveForCurrentProcess(string? explicitPath = null); }`
  - `public static class DatabaseMigrator { public static DatabasePreparationResult Prepare(DatabasePathResolution resolution, Action<string,string>? copyDatabase = null, Action<string>? createDirectory = null); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Tests/Infrastructure/DatabasePathResolverTests.cs`：

```csharp
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Tests.Infrastructure;

public class DatabasePathResolverTests
{
    private const string SettingsDir = @"C:\Users\tester\AppData\Local\BashCommandManager";
    private const string LegacyDir = @"C:\Apps\BashCommandManager";

    private static string Canonical => Path.Combine(SettingsDir, "data", "app.db");
    private static string Legacy => Path.Combine(LegacyDir, "data", "app.db");

    [Fact]
    public void Explicit_path_wins_and_is_absolutized()
    {
        var resolution = DatabasePathResolver.Resolve(@"relative\my.db", null, _ => true, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.Argument, resolution.Source);
        Assert.Equal(Path.GetFullPath(@"relative\my.db"), resolution.DatabasePath);
        Assert.False(resolution.RequiresMigration);
        Assert.Null(resolution.LegacyPath);
    }

    [Fact]
    public void Environment_variable_used_when_no_explicit_path()
    {
        var resolution = DatabasePathResolver.Resolve(null, @"D:\portable\data.db", _ => true, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.Environment, resolution.Source);
        Assert.Equal(Path.GetFullPath(@"D:\portable\data.db"), resolution.DatabasePath);
        Assert.False(resolution.RequiresMigration);
    }

    [Fact]
    public void Canonical_existing_database_is_used_without_migration()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, path => path == Canonical, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.LocalAppData, resolution.Source);
        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.Equal(Legacy, resolution.LegacyPath);
        Assert.False(resolution.RequiresMigration);
    }

    [Fact]
    public void Legacy_only_requires_migration_to_canonical()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, path => path == Legacy, SettingsDir, Legacy);

        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.Equal(Legacy, resolution.LegacyPath);
        Assert.True(resolution.RequiresMigration);
    }

    [Fact]
    public void Neither_exists_uses_canonical_without_migration()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, _ => false, SettingsDir, Legacy);

        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.False(resolution.RequiresMigration);
        Assert.Equal(DatabasePathSource.LocalAppData, resolution.Source);
    }

    [Fact]
    public void Blank_environment_value_is_ignored()
    {
        var resolution = DatabasePathResolver.Resolve(null, "   ", path => path == Canonical, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.LocalAppData, resolution.Source);
    }

    [Fact]
    public void Canonical_path_matches_decision_document()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BashCommandManager",
            "data",
            "app.db");

        Assert.Equal(expected, DatabasePathResolver.CanonicalDatabasePath);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data", "app.db"), DatabasePathResolver.LegacyDatabasePath);
        Assert.Equal("BASHCOMMANDMANAGER_DB", DatabasePathResolver.EnvironmentVariableName);
    }

    [Fact]
    public void Never_points_to_the_foreign_legacy_file_name()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, _ => false, SettingsDir, Legacy);

        Assert.DoesNotContain("BashCommandManager.db", resolution.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"data\app.db", resolution.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }
}
```

`tests/BashCommandManager.Tests/Infrastructure/DatabaseMigratorTests.cs`：

```csharp
using BashCommandManager.Infrastructure;
using System.Data.SQLite;

namespace BashCommandManager.Tests.Infrastructure;

public class DatabaseMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bcm-migrator-" + Guid.NewGuid().ToString("N"));

    public DatabaseMigratorTests() => Directory.CreateDirectory(_root);

    private string LegacyPath => Path.Combine(_root, "legacy", "data", "app.db");
    private string CanonicalPath => Path.Combine(_root, "canonical", "data", "app.db");

    private DatabasePathResolution Resolution(bool requiresMigration = true)
        => new(CanonicalPath, DatabasePathSource.LocalAppData, LegacyPath, requiresMigration);

    private void CreateLegacyDatabase(string table)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        using var connection = new SQLiteConnection($"Data Source={LegacyPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand($"CREATE TABLE {table} (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO {table} (Id, Name) VALUES (1, '备份');", connection);
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Migrates_legacy_database_and_keeps_it()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(Resolution());

        Assert.True(result.Migrated);
        Assert.Null(result.Warning);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        Assert.True(File.Exists(CanonicalPath));
        Assert.True(File.Exists(LegacyPath));
        using var connection = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand("SELECT COUNT(*) FROM Commands", connection);
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Leaves_no_migration_temp_files()
    {
        CreateLegacyDatabase("Commands");

        DatabaseMigrator.Prepare(Resolution());

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CanonicalPath)!, "*.migrating-*"));
    }

    [Fact]
    public void Existing_target_is_treated_as_concurrent_success()
    {
        CreateLegacyDatabase("Commands");
        Directory.CreateDirectory(Path.GetDirectoryName(CanonicalPath)!);
        using (var seed = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;"))
        {
            seed.Open();
            using var command = new SQLiteCommand("CREATE TABLE Marker (Id INTEGER); INSERT INTO Marker (Id) VALUES (42);", seed);
            command.ExecuteNonQuery();
        }

        var result = DatabaseMigrator.Prepare(Resolution());

        Assert.True(result.Migrated);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        using var connection = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand("SELECT COUNT(*) FROM Marker", connection);
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Copy_failure_falls_back_to_legacy_with_warning()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(
            Resolution(),
            copyDatabase: (_, _) => throw new IOException("磁盘已满"),
            createDirectory: _ => { });

        Assert.False(result.Migrated);
        Assert.Equal(LegacyPath, result.DatabasePath);
        Assert.NotNull(result.Warning);
        Assert.Contains("磁盘已满", result.Warning!);
        Assert.False(File.Exists(CanonicalPath));
    }

    [Fact]
    public void Directory_creation_failure_falls_back_to_legacy()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(
            Resolution(),
            copyDatabase: (_, _) => { },
            createDirectory: _ => throw new UnauthorizedAccessException("拒绝访问"));

        Assert.Equal(LegacyPath, result.DatabasePath);
        Assert.Contains("拒绝访问", result.Warning!);
    }

    [Fact]
    public void No_migration_when_not_required()
    {
        var copied = false;

        var result = DatabaseMigrator.Prepare(Resolution(requiresMigration: false), (_, _) => copied = true);

        Assert.False(copied);
        Assert.False(result.Migrated);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        Assert.Null(result.Warning);
    }

    public void Dispose()
    {
        SQLiteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~Infrastructure"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"DatabasePathResolver"`。

- [ ] **Step 3: 实现解析器与迁移器**

`Infrastructure/DatabasePathResolver.cs`：

```csharp
using System.Data.SQLite;
using System.IO;

namespace BashCommandManager.Infrastructure;

/// <summary>数据库路径来源。</summary>
public enum DatabasePathSource
{
    Argument,
    Environment,
    LocalAppData,
    LegacyApplicationDirectory
}

/// <summary>一次解析结果：最终路径、来源、旧库路径、是否需要迁移。</summary>
public sealed record DatabasePathResolution(
    string DatabasePath,
    DatabasePathSource Source,
    string? LegacyPath,
    bool RequiresMigration);

/// <summary>迁移结果：最终使用的路径、是否发生迁移、失败告警。</summary>
public sealed record DatabasePreparationResult(string DatabasePath, bool Migrated, string? Warning);

/// <summary>GUI 与 CLI 共用的数据库位置解析（D4.1）。</summary>
public static class DatabasePathResolver
{
    public const string EnvironmentVariableName = "BASHCOMMANDMANAGER_DB";
    public const string DatabaseFileName = "app.db";

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BashCommandManager");

    public static string CanonicalDatabasePath => Path.Combine(SettingsDirectory, "data", DatabaseFileName);

    public static string LegacyDatabasePath => Path.Combine(AppContext.BaseDirectory, "data", DatabaseFileName);

    public static DatabasePathResolution Resolve(
        string? explicitPath,
        string? environmentValue,
        Func<string, bool> fileExists,
        string? settingsDirectory = null,
        string? legacyDatabasePath = null)
    {
        var settingsDir = settingsDirectory ?? SettingsDirectory;
        var canonical = Path.Combine(settingsDir, "data", DatabaseFileName);
        var legacy = legacyDatabasePath ?? LegacyDatabasePath;

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return new DatabasePathResolution(Path.GetFullPath(explicitPath), DatabasePathSource.Argument, null, false);
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new DatabasePathResolution(Path.GetFullPath(environmentValue), DatabasePathSource.Environment, null, false);
        }

        if (fileExists(canonical))
        {
            return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, false);
        }

        if (fileExists(legacy))
        {
            return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, true);
        }

        return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, false);
    }

    public static DatabasePathResolution ResolveForCurrentProcess(string? explicitPath = null)
        => Resolve(explicitPath, Environment.GetEnvironmentVariable(EnvironmentVariableName), File.Exists);
}

/// <summary>把旧库一次性复制到规范位置；失败回退旧库并给告警。</summary>
public static class DatabaseMigrator
{
    public static DatabasePreparationResult Prepare(
        DatabasePathResolution resolution,
        Action<string, string>? copyDatabase = null,
        Action<string>? createDirectory = null)
    {
        if (!resolution.RequiresMigration || string.IsNullOrWhiteSpace(resolution.LegacyPath))
        {
            return new DatabasePreparationResult(resolution.DatabasePath, false, null);
        }

        var legacyPath = resolution.LegacyPath!;
        var targetPath = resolution.DatabasePath;
        var targetDirectory = Path.GetDirectoryName(targetPath);
        var tempPath = targetPath + ".migrating-" + Guid.NewGuid().ToString("N");
        var copy = copyDatabase ?? BackupCopy;
        var ensureDirectory = createDirectory ?? (directory => Directory.CreateDirectory(directory));

        try
        {
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                ensureDirectory(targetDirectory);
            }

            copy(legacyPath, tempPath);

            if (File.Exists(targetPath))
            {
                TryDelete(tempPath);
                return new DatabasePreparationResult(targetPath, true, null);
            }

            try
            {
                File.Move(tempPath, targetPath);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                TryDelete(tempPath);
                return new DatabasePreparationResult(targetPath, true, null);
            }

            return new DatabasePreparationResult(targetPath, true, null);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return new DatabasePreparationResult(legacyPath, false, $"数据库迁移失败，已回退使用旧库: {ex.Message}");
        }
    }

    // 一致性快照复制：不用 File.Copy，避免热库撕裂
    private static void BackupCopy(string sourcePath, string targetPath)
    {
        using var source = new SQLiteConnection($"Data Source={sourcePath};Version=3;Read Only=True;");
        using var target = new SQLiteConnection($"Data Source={targetPath};Version=3;");
        source.Open();
        target.Open();
        source.BackupDatabase(target, "main", "main", -1, null, 0);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
```

- [ ] **Step 4: 把解析器/迁移器加入 CLI 的源码链接清单**

在 `BashCommandManager.Cli/BashCommandManager.Cli.csproj` 的 `SharedSources` ItemGroup 内（`DatabaseInitializer.cs` 那一行之后）追加：

```xml
    <Compile Include="..\Infrastructure\DatabasePathResolver.cs" Link="Shared\Infrastructure\DatabasePathResolver.cs" />
```

- [ ] **Step 5: 跑测试确认通过（CLI 也一起构建，确认链接无副作用）**

```powershell
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~Infrastructure"
```

期望：CLI 构建 `0 个错误`；测试 `Passed! - Failed: 0`。若 `Migrates_legacy_database_and_keeps_it` 报 `BackupDatabase` 参数错误，核对签名是 `BackupDatabase(SQLiteConnection destination, string destinationName, string sourceName, int pages, SQLiteBackupCallback callback, int retryMilliseconds)`。

- [ ] **Step 6: 提交**

```powershell
git add Infrastructure\DatabasePathResolver.cs BashCommandManager.Cli\BashCommandManager.Cli.csproj tests\BashCommandManager.Tests\Infrastructure
git commit -m "feat(cli): 新增 GUI/CLI 共用的数据库路径解析与迁移器"
```

---

### Task 6: DatabaseInitializer 改造（注入路径、`Foreign Keys=True`、告警）

**Files:**
- Modify: `Infrastructure/DatabaseInitializer.cs`
- Modify: `App.xaml.cs`（把迁移告警写 `Debug.WriteLine`）
- Test: `tests/BashCommandManager.Tests/Infrastructure/DatabaseInitializerTests.cs`

**Interfaces:**
- Consumes: `DatabasePathResolver.ResolveForCurrentProcess`、`DatabaseMigrator.Prepare`
- Produces:
  - `public DatabaseInitializer(string? explicitDatabasePath = null)`
  - `public string DatabasePath { get; }`
  - `public string? MigrationWarning { get; }`
  - `public DatabasePathSource Source { get; }`
  - `public DatabasePathResolution Resolution { get; }`
  - `public string ConnectionString`（含 `Foreign Keys=True`）
  - `public void Initialize()`（签名不变）

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Tests/Infrastructure/DatabaseInitializerTests.cs`：

```csharp
using BashCommandManager.Infrastructure;
using System.Data.SQLite;

namespace BashCommandManager.Tests.Infrastructure;

public class DatabaseInitializerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bcm-initializer-" + Guid.NewGuid().ToString("N"));

    public DatabaseInitializerTests() => Directory.CreateDirectory(_root);

    private string DatabasePath => Path.Combine(_root, "nested", "data", "app.db");

    [Fact]
    public void Uses_explicit_path_and_creates_directories()
    {
        var initializer = new DatabaseInitializer(DatabasePath);

        Assert.Equal(DatabasePath, initializer.DatabasePath);
        Assert.Null(initializer.MigrationWarning);
        Assert.Equal(DatabasePathSource.Argument, initializer.Source);

        initializer.Initialize();

        Assert.True(File.Exists(DatabasePath));
        Assert.True(Directory.Exists(Path.GetDirectoryName(DatabasePath)));
    }

    [Fact]
    public void Connection_string_enables_foreign_keys()
    {
        var initializer = new DatabaseInitializer(DatabasePath);

        Assert.Contains("Foreign Keys=True", initializer.ConnectionString);

        initializer.Initialize();
        using var connection = new SQLiteConnection(initializer.ConnectionString);
        connection.Open();
        using var command = new SQLiteCommand("PRAGMA foreign_keys;", connection);
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Initialize_creates_schema_and_root_group_idempotently()
    {
        var initializer = new DatabaseInitializer(DatabasePath);

        initializer.Initialize();
        initializer.Initialize();

        using var connection = new SQLiteConnection(initializer.ConnectionString);
        connection.Open();
        using var groups = new SQLiteCommand("SELECT COUNT(*) FROM Groups WHERE Id = 1 AND Name = '根分组'", connection);
        Assert.Equal(1L, Convert.ToInt64(groups.ExecuteScalar()));
        using var tables = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Groups','Commands')", connection);
        Assert.Equal(2L, Convert.ToInt64(tables.ExecuteScalar()));
    }

    [Fact]
    public void Deleting_group_cascades_to_commands()
    {
        var initializer = new DatabaseInitializer(DatabasePath);
        initializer.Initialize();

        using var connection = new SQLiteConnection(initializer.ConnectionString);
        connection.Open();
        using (var seed = new SQLiteCommand(@"
            INSERT INTO Groups (Id, Name, ParentId, SortOrder) VALUES (5, '运维', 1, 0);
            INSERT INTO Commands (Name, FilePath, GroupId, SortOrder) VALUES ('备份', 'D:\bat\a.bat', 5, 0);", connection))
        {
            seed.ExecuteNonQuery();
        }

        using (var delete = new SQLiteCommand("DELETE FROM Groups WHERE Id = 5", connection))
        {
            delete.ExecuteNonQuery();
        }

        using var count = new SQLiteCommand("SELECT COUNT(*) FROM Commands WHERE GroupId = 5", connection);
        Assert.Equal(0L, Convert.ToInt64(count.ExecuteScalar()));
    }

    public void Dispose()
    {
        SQLiteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~DatabaseInitializerTests"
```

期望：`Failed`——`Assert.Contains("Foreign Keys=True", ...)` 失败（现有连接串没有该项），且 `DatabaseInitializer(DatabasePath)` 无法编译（没有该构造函数）。

- [ ] **Step 3: 改造 DatabaseInitializer**

`Infrastructure/DatabaseInitializer.cs`（替换文件头与构造函数，`Initialize()`/`AddColumnIfNotExists` 保持不变）：

```csharp
using System.Data.SQLite;
using System.IO;

namespace BashCommandManager.Infrastructure;

/// <summary>数据库初始化：解析路径 → 必要时迁移 → 幂等建表。</summary>
public class DatabaseInitializer
{
    public DatabaseInitializer(string? explicitDatabasePath = null)
    {
        Resolution = DatabasePathResolver.ResolveForCurrentProcess(explicitDatabasePath);
        var prepared = DatabaseMigrator.Prepare(Resolution);
        DatabasePath = prepared.DatabasePath;
        MigrationWarning = prepared.Warning;
        Source = prepared.Warning is null ? Resolution.Source : DatabasePathSource.LegacyApplicationDirectory;

        var dataDirectory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    public string DatabasePath { get; }

    public string? MigrationWarning { get; }

    public DatabasePathSource Source { get; }

    public DatabasePathResolution Resolution { get; }

    public string ConnectionString => $"Data Source={DatabasePath};Version=3;Foreign Keys=True;";

    public void Initialize()
    {
        // 原实现整体保留：using var connection = new SQLiteConnection(ConnectionString); ...
    }

    private void AddColumnIfNotExists(SQLiteConnection connection, string table, string column, string type)
    {
        // 原实现整体保留
    }
}
```

> `Initialize()` 与 `AddColumnIfNotExists` 的**方法体逐字节保留**现有实现（`CREATE TABLE IF NOT EXISTS` 建表 + 索引 + 根分组 + `AddColumnIfNotExists` 两次调用），只把文件头 `using System.Data;`/`using System.Reflection;` 清理为本文件实际使用的引用。

- [ ] **Step 4: App.xaml.cs 记录迁移告警**

在 `App.OnStartup` 里把

```csharp
        var initializer = new DatabaseInitializer();
        initializer.Initialize();
```

改为

```csharp
        var initializer = new DatabaseInitializer();
        if (initializer.MigrationWarning is not null)
        {
            System.Diagnostics.Debug.WriteLine($"[Database] {initializer.MigrationWarning}");
        }
        initializer.Initialize();
```

- [ ] **Step 5: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
```

期望：全部 Passed（`Foreign Keys=True` 与级联删除两条为本次核心）。

- [ ] **Step 6: GUI 构建冒烟（不改行为）**

```powershell
dotnet build -c Release
```

期望：`0 个警告 0 个错误`。随后确认 `bin\Release\net8.0-windows10.0.19041.0\win-x64\BashCommandManager.exe` 时间戳更新，且目录中**没有** `bcm.exe`（验收 1 的前半部分）。

- [ ] **Step 7: 提交**

```powershell
git add Infrastructure\DatabaseInitializer.cs App.xaml.cs tests\BashCommandManager.Tests\Infrastructure\DatabaseInitializerTests.cs
git commit -m "feat(cli): DatabaseInitializer 支持注入路径、外键级联与迁移告警"
```

---

### Task 7: DatabaseSession + CliCommandStore（CRUD/移动/常用/执行记录/重试）

**Files:**
- Create: `BashCommandManager.Cli/Operations/DatabaseSession.cs`
- Create: `BashCommandManager.Cli/Infrastructure/CliPaths.cs`
- Create: `BashCommandManager.Cli/Application/CliCommandStore.cs`
- Create: `tests/BashCommandManager.Cli.Tests/TestSupport/TempDatabase.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/DatabaseSessionTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Application/CliCommandStoreTests.cs`

**Interfaces:**
- Consumes: `DatabaseInitializer`、`CommandRepository`、`GroupRepository`、`CliErrors`
- Produces:
  - `public sealed class DatabaseSession : IDisposable { public DatabaseSession(string? explicitDatabasePath); public string DatabasePath; public string? MigrationWarning; public DatabasePathSource Source; public bool Created; public string ConnectionString; public IDbConnection Connection; public DatabasePathResolution Resolution; public void Dispose(); }`
  - `public sealed class CliCommandStore { public CliCommandStore(IDbConnection db); 方法见 Step 4 完整代码 }`
  - `public static class CliPaths { public const string SettingsDirectoryVariable = "BASHCOMMANDMANAGER_SETTINGS_DIR"; public static string SettingsDirectory { get; } }`
  - `internal sealed class TempDatabase : IDisposable`（测试支持）

> `CliServices`（处理器依赖容器）在 **Task 10** 一次性引入：它依赖 T8 的 `CommandResolver` 与 T17/T21 的 `IProcessRunner`/`IGuiLauncher`/`IExecutableLocator`，过早创建会反复改签名。本任务的 `CliHost` 仍只处理 help/version/未接线错误。

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/TestSupport/TempDatabase.cs`：

```csharp
using BashCommandManager.Cli.Operations;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>每个测试一个临时库目录；Dispose 时清理。</summary>
internal sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Root = Path.Combine(Path.GetTempPath(), "bcm-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatabasePath = Path.Combine(Root, "app.db");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public DatabaseSession Open() => new(DatabasePath);

    public string CreateScript(string fileName, string content = "@echo off\r\nexit /b 0\r\n")
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
```

`tests/BashCommandManager.Cli.Tests/Operations/DatabaseSessionTests.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Infrastructure;
using Dapper;

namespace BashCommandManager.Cli.Tests.Operations;

public class DatabaseSessionTests
{
    [Fact]
    public void Creates_missing_database_and_reports_created_true()
    {
        using var temp = new TempDatabase();

        using var session = new DatabaseSession(temp.DatabasePath);

        Assert.True(session.Created);
        Assert.True(File.Exists(temp.DatabasePath));
        Assert.Equal(DatabasePathSource.Argument, session.Source);
        Assert.Null(session.MigrationWarning);
    }

    [Fact]
    public void Existing_database_reports_created_false()
    {
        using var temp = new TempDatabase();
        using (var first = new DatabaseSession(temp.DatabasePath))
        {
            Assert.True(first.Created);
        }

        using var second = new DatabaseSession(temp.DatabasePath);

        Assert.False(second.Created);
    }

    [Fact]
    public void Schema_and_root_group_are_ready()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);

        var rootGroups = session.Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Groups WHERE Id = 1");

        Assert.Equal(1L, rootGroups);
    }

    [Fact]
    public void Busy_timeout_is_configured()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);

        var timeout = session.Connection.ExecuteScalar<long>("PRAGMA busy_timeout;");

        Assert.Equal(3000L, timeout);
    }

    [Fact]
    public void Session_uses_resolver_when_no_explicit_path()
    {
        using var temp = new TempDatabase();
        var previous = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, temp.DatabasePath);
        try
        {
            using var session = new DatabaseSession(null);

            Assert.Equal(Path.GetFullPath(temp.DatabasePath), session.DatabasePath);
            Assert.Equal(DatabasePathSource.Environment, session.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, previous);
        }
    }
}
```

`tests/BashCommandManager.Cli.Tests/Application/CliCommandStoreTests.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;

namespace BashCommandManager.Cli.Tests.Application;

public class CliCommandStoreTests
{
    private static CliCommandStore Store(DatabaseSession session) => new(session.Connection);

    [Fact]
    public async Task Creates_groups_with_monotonic_sort_order()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);

        var first = await store.CreateGroupAsync("运维", 1);
        var second = await store.CreateGroupAsync("测试", 1);

        var groups = await store.GetGroupsAsync();
        Assert.Equal(1, groups.Single(group => group.Id == first).SortOrder);
        Assert.Equal(2, groups.Single(group => group.Id == second).SortOrder);
    }

    [Fact]
    public async Task Finds_child_group_by_name_case_insensitively()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var id = await store.CreateGroupAsync("Ops", 1);

        var found = await store.FindChildGroupByNameAsync(1, "ops");
        var missing = await store.FindChildGroupByNameAsync(1, "nope");

        Assert.Equal(id, found!.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Commands_round_trip_with_sort_and_limit()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var groupId = await store.CreateGroupAsync("运维", 1);
        await store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = groupId, SortOrder = 0 });
        await store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = groupId, SortOrder = 1 });

        var byName = await store.GetCommandsAsync(groupId, SortOption.Name, SortDirection.Ascending, 0);
        var limited = await store.GetCommandsAsync(groupId, SortOption.Name, SortDirection.Ascending, 1);

        Assert.Equal(new[] { "a", "b" }, byName.Select(command => command.Name).ToArray());
        Assert.Single(limited);
        Assert.Equal("a", limited[0].Name);
    }

    [Fact]
    public async Task Search_matches_name_or_description_and_respects_group_scope()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        await store.CreateCommandAsync(new Command { Name = "备份", Description = "每日", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "部署", Description = "备份后部署", FilePath = @"D:\b.bat", GroupId = test });

        var all = await store.SearchCommandsAsync("备份", null, SortOption.Name, SortDirection.Ascending, 0);
        var scoped = await store.SearchCommandsAsync("备份", ops, SortOption.Name, SortDirection.Ascending, 0);

        Assert.Equal(2, all.Count);
        Assert.Single(scoped);
        Assert.Equal("备份", scoped[0].Name);
    }

    [Fact]
    public async Task Moves_commands_between_groups()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        var id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });

        await store.MoveCommandsAsync(new[] { id }, test);

        var moved = await store.GetCommandAsync(id);
        Assert.Equal(test, moved!.GroupId);
    }

    [Fact]
    public async Task Records_execution_count_and_timestamp()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });

        await store.RecordExecutionAsync(id);
        await store.RecordExecutionAsync(id);

        var command = await store.GetCommandAsync(id);
        Assert.Equal(2, command!.ExecutionCount);
        Assert.NotNull(command.LastExecutedAt);
    }

    [Fact]
    public async Task Deleting_group_cascades_and_reports_subtree_ids()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        var child = await store.CreateGroupAsync("生产", ops);
        var commandInOps = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var commandInChild = await store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = child });

        var subtree = await store.GetGroupSubtreeIdsAsync(ops);
        var commandIds = await store.GetCommandIdsInGroupsAsync(subtree);
        await store.DeleteGroupAsync(ops);

        Assert.Equal(new[] { ops, child }.OrderBy(id => id), subtree.OrderBy(id => id));
        Assert.Equal(new[] { commandInOps, commandInChild }.OrderBy(id => id), commandIds.OrderBy(id => id));
        Assert.Null(await store.GetGroupAsync(child));
        Assert.Null(await store.GetCommandAsync(commandInChild));
    }

    [Fact]
    public async Task Counts_commands_and_groups()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "恢复", FilePath = @"D:\b.bat", GroupId = ops });

        Assert.Equal(2, await store.CountCommandsInGroupAsync(ops));
        var counts = await store.GetCommandCountsAsync();
        Assert.Equal(2, counts[ops]);
        var names = await store.FillGroupNamesAsync(await store.GetCommandsAsync(ops, SortOption.Name, SortDirection.Ascending, 0));
        Assert.All(names, command => Assert.Equal("运维", command.GroupName));
    }

    [Fact]
    public async Task Frequent_returns_only_executed_commands_ordered_by_count()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var first = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = 1 });
        await store.CreateCommandAsync(new Command { Name = "没用过", FilePath = @"D:\c.bat", GroupId = 1 });
        await store.RecordExecutionAsync(first);
        await store.RecordExecutionAsync(second);
        await store.RecordExecutionAsync(second);

        var frequent = await store.GetFrequentlyUsedAsync(10);

        Assert.Equal(new[] { "发布", "备份" }, frequent.Select(command => command.Name).ToArray());
        Assert.All(frequent, command => Assert.False(string.IsNullOrEmpty(command.GroupName)));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~DatabaseSessionTests|FullyQualifiedName~CliCommandStoreTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"DatabaseSession"`。

- [ ] **Step 3: 实现 CliPaths 与 DatabaseSession**

`BashCommandManager.Cli/Infrastructure/CliPaths.cs`：

```csharp
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli.Infrastructure;

/// <summary>CLI 使用的目录：默认与 GUI 相同的 settings 目录，可用环境变量覆盖以便测试/便携。</summary>
public static class CliPaths
{
    public const string SettingsDirectoryVariable = "BASHCOMMANDMANAGER_SETTINGS_DIR";

    public static string SettingsDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(SettingsDirectoryVariable);
            return string.IsNullOrWhiteSpace(overridden)
                ? DatabasePathResolver.SettingsDirectory
                : Path.GetFullPath(overridden);
        }
    }
}
```

`BashCommandManager.Cli/Operations/DatabaseSession.cs`：

```csharp
using BashCommandManager.Infrastructure;
using Dapper;
using System.Data;
using System.Data.SQLite;

namespace BashCommandManager.Cli.Operations;

/// <summary>CLI 的数据库会话：初始化 schema、记录 created/source/迁移告警，并保持单一连接。</summary>
public sealed class DatabaseSession : IDisposable
{
    private readonly SQLiteConnection _connection;

    public DatabaseSession(string? explicitDatabasePath)
    {
        var initializer = new DatabaseInitializer(explicitDatabasePath);
        Resolution = initializer.Resolution;
        DatabasePath = initializer.DatabasePath;
        MigrationWarning = initializer.MigrationWarning;
        Source = initializer.MigrationWarning is null
            ? initializer.Source
            : DatabasePathSource.LegacyApplicationDirectory;
        Created = !File.Exists(DatabasePath);

        initializer.Initialize();

        ConnectionString = initializer.ConnectionString;
        _connection = new SQLiteConnection(ConnectionString);
        _connection.Open();
        _connection.Execute("PRAGMA busy_timeout = 3000;");
    }

    public DatabasePathResolution Resolution { get; }

    public string DatabasePath { get; }

    public string? MigrationWarning { get; }

    public DatabasePathSource Source { get; }

    public bool Created { get; }

    public string ConnectionString { get; }

    public IDbConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
```

- [ ] **Step 4: 实现 CliCommandStore**

`BashCommandManager.Cli/Application/CliCommandStore.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;
using System.Data;
using System.Data.SQLite;

namespace BashCommandManager.Cli.Application;

/// <summary>CLI 的数据访问层：复用仓库，补齐 CLI 需要的查询、子树统计与 Busy 重试。</summary>
public sealed class CliCommandStore
{
    private static readonly int[] RetryDelays = { 200, 500, 1000 };

    private readonly IGroupRepository _groups;
    private readonly ICommandRepository _commands;

    public CliCommandStore(IDbConnection db)
    {
        _groups = new GroupRepository(db);
        _commands = new CommandRepository(db);
    }

    public Task<IReadOnlyList<Group>> GetGroupsAsync()
        => Retry(async () => (IReadOnlyList<Group>)(await _groups.GetAllAsync()).ToList());

    public Task<Group?> GetGroupAsync(int id)
        => Retry(() => _groups.GetByIdAsync(id));

    public async Task<Group?> FindChildGroupByNameAsync(int? parentId, string name)
    {
        var all = await GetGroupsAsync();
        return all.FirstOrDefault(group =>
            group.ParentId == parentId
            && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> CreateGroupAsync(string name, int? parentId)
    {
        var all = await GetGroupsAsync();
        var sortOrder = all.Count(group => group.ParentId == parentId);
        return await Retry(() => _groups.CreateAsync(new Group
        {
            Name = name,
            ParentId = parentId,
            SortOrder = sortOrder
        }));
    }

    public async Task RenameGroupAsync(int id, string name)
    {
        var group = await Retry(() => _groups.GetByIdAsync(id))
                    ?? throw CliErrors.NotFound($"未找到分组: {id}");
        group.Name = name;
        await Retry(() => _groups.UpdateAsync(group));
    }

    public Task DeleteGroupAsync(int id) => Retry(() => _groups.DeleteAsync(id));

    public async Task<IReadOnlyList<int>> GetGroupSubtreeIdsAsync(int rootGroupId)
    {
        var all = await GetGroupsAsync();
        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootGroupId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var child in all.Where(group => group.ParentId == current))
            {
                queue.Enqueue(child.Id);
            }
        }
        return result.OrderBy(id => id).ToList();
    }

    public async Task<IReadOnlyList<Command>> GetCommandsAsync(int? groupId, SortOption sort, SortDirection direction, int limit)
    {
        var items = groupId is int scope
            ? await Retry(async () => (IReadOnlyList<Command>)(await _commands.GetByGroupIdWithSortAsync(scope, sort, direction)).ToList())
            : await Retry(async () => (IReadOnlyList<Command>)(await _commands.GetAllWithSortAsync(sort, direction)).ToList());
        return limit > 0 ? items.Take(limit).ToList() : items;
    }

    public async Task<IReadOnlyList<Command>> SearchCommandsAsync(string keyword, int? groupId, SortOption sort, SortDirection direction, int limit)
    {
        var items = groupId is int scope
            ? await Retry(async () => (IReadOnlyList<Command>)(await _commands.SearchInGroupAsync(keyword, scope, sort, direction)).ToList())
            : await Retry(async () => (IReadOnlyList<Command>)(await _commands.SearchWithSortAsync(keyword, sort, direction)).ToList());
        return limit > 0 ? items.Take(limit).ToList() : items;
    }

    public Task<Command?> GetCommandAsync(int id) => Retry(() => _commands.GetByIdAsync(id));

    public Task<IReadOnlyList<Command>> GetAllCommandsAsync()
        => Retry(async () => (IReadOnlyList<Command>)(await _commands.GetAllAsync()).ToList());

    public async Task<IReadOnlyList<Command>> FindCommandsByNameAsync(string name, int? groupId)
    {
        var all = await GetAllCommandsAsync();
        return all
            .Where(command => string.Equals(command.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(command => groupId is null || command.GroupId == groupId)
            .ToList();
    }

    public Task<int> CreateCommandAsync(Command command) => Retry(() => _commands.CreateAsync(command));

    public Task UpdateCommandAsync(Command command) => Retry(() => _commands.UpdateAsync(command));

    public async Task DeleteCommandsAsync(IReadOnlyList<int> ids)
    {
        foreach (var id in ids)
        {
            await Retry(() => _commands.DeleteAsync(id));
        }
    }

    public async Task MoveCommandsAsync(IReadOnlyList<int> ids, int targetGroupId)
    {
        foreach (var id in ids)
        {
            await Retry(() => _commands.MoveToGroupAsync(id, targetGroupId));
        }
    }

    public Task<IReadOnlyList<Command>> GetFrequentlyUsedAsync(int limit)
        => Retry(async () => (IReadOnlyList<Command>)(await _commands.GetFrequentlyUsedAsync(limit)).ToList());

    public async Task RecordExecutionAsync(int id)
    {
        var command = await Retry(() => _commands.GetByIdAsync(id));
        if (command is null)
        {
            return;
        }

        command.ExecutionCount += 1;
        command.LastExecutedAt = DateTime.Now;
        await Retry(() => _commands.UpdateAsync(command));
    }

    public async Task<int> CountCommandsInGroupAsync(int groupId)
        => (await Retry(async () => (await _commands.GetByGroupIdAsync(groupId)).ToList())).Count;

    public async Task<IReadOnlyDictionary<int, int>> GetCommandCountsAsync()
    {
        var all = await GetAllCommandsAsync();
        return all.GroupBy(command => command.GroupId).ToDictionary(group => group.Key, group => group.Count());
    }

    public async Task<IReadOnlyList<int>> GetCommandIdsInGroupsAsync(IReadOnlyList<int> groupIds)
    {
        var scope = groupIds.ToHashSet();
        var all = await GetAllCommandsAsync();
        return all.Where(command => scope.Contains(command.GroupId)).Select(command => command.Id).OrderBy(id => id).ToList();
    }

    public async Task<IReadOnlyList<Command>> FillGroupNamesAsync(IReadOnlyList<Command> commands)
    {
        var names = (await GetGroupsAsync()).ToDictionary(group => group.Id, group => group.Name);
        foreach (var command in commands)
        {
            command.GroupName = names.TryGetValue(command.GroupId, out var name) ? name : string.Empty;
        }
        return commands;
    }

    private static async Task<T> Retry<T>(Func<Task<T>> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SQLiteException ex) when (IsBusy(ex) && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt]);
            }
            catch (SQLiteException ex) when (IsBusy(ex))
            {
                throw CliErrors.DbBusy(ex.Message);
            }
            catch (SQLiteException ex)
            {
                throw CliErrors.DbError(ex.Message);
            }
        }
    }

    private static async Task Retry(Func<Task> operation)
    {
        await Retry(async () =>
        {
            await operation();
            return true;
        });
    }

    private static bool IsBusy(SQLiteException ex)
        => ex.ResultCode is SQLiteErrorCode.Busy or SQLiteErrorCode.Locked;
}
```

> `CliCommandStore` 只依赖 `IDbConnection` 与两个仓库，不直接写 SQL（`Repository` 已封装），因此不需要 `using Dapper;`。

- [ ] **Step 5: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed（含 `Deleting_group_cascades_and_reports_subtree_ids`——它同时验证 T6 的 `Foreign Keys=True`）。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 新增 DatabaseSession 与 CliCommandStore 数据访问层"
```

### Task 8: CommandResolver（id / 名称 / 分组路径 / 歧义 / 批量）

**Files:**
- Create: `BashCommandManager.Cli/Operations/CommandResolver.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/CommandResolverTests.cs`

**Interfaces:**
- Consumes: `CliCommandStore`（T7）、`CliErrors`、`JsonShapes`（仅 `JsonObject` 组装）
- Produces: `public sealed class CommandResolver { public CommandResolver(CliCommandStore store); public Task<Group> ResolveGroupAsync(string token); public Task<Group?> FindGroupAsync(string token); public Task<Command> ResolveCommandAsync(string token, string? groupToken); public Task<IReadOnlyList<Command>> ResolveCommandsAsync(string token, string? groupToken); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/CommandResolverTests.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;

namespace BashCommandManager.Cli.Tests.Operations;

public class CommandResolverTests
{
    private static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver, int OpsId, int ProdId)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var ops = await store.CreateGroupAsync("运维", 1);
        var prod = await store.CreateGroupAsync("生产", ops);
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = prod });
        return (temp, session, store, new CommandResolver(store), ops, prod);
    }

    [Fact]
    public async Task Resolves_group_by_id()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var group = await arranged.Resolver.ResolveGroupAsync(arranged.OpsId.ToString());

        Assert.Equal(arranged.OpsId, group.Id);
    }

    [Fact]
    public async Task Resolves_group_by_path()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var group = await arranged.Resolver.ResolveGroupAsync("运维/生产");

        Assert.Equal(arranged.ProdId, group.Id);
    }

    [Fact]
    public async Task Resolves_group_by_name_case_insensitively()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        Assert.Equal("运维", (await arranged.Resolver.ResolveGroupAsync(" 运维 ")).Name);
    }

    [Fact]
    public async Task Missing_group_is_not_found_with_exit_two()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() => arranged.Resolver.ResolveGroupAsync("不存在"));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Ambiguous_group_lists_candidates_and_exits_one()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var first = await store.CreateGroupAsync("同名", 1);
        var second = await store.CreateGroupAsync("其他", 1);
        await store.CreateGroupAsync("同名", first);
        var resolver = new CommandResolver(store);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveGroupAsync("同名"));

        Assert.Equal("E_AMBIGUOUS", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
        var candidates = error.Error.Details!["candidates"]!.AsArray();
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public async Task Resolves_command_by_id_and_name()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var commands = await arranged.Store.GetCommandsAsync(null, SortOption.Name, SortDirection.Ascending, 0);
        var backup = commands.Single(command => command.Name == "备份");

        Assert.Equal(backup.Id, (await arranged.Resolver.ResolveCommandAsync(backup.Id.ToString(), null)).Id);
        Assert.Equal(backup.Id, (await arranged.Resolver.ResolveCommandAsync("备份", null)).Id);
        Assert.Equal(backup.Id, (await arranged.Resolver.ResolveCommandAsync(" 备份 ", null)).Id);
    }

    [Fact]
    public async Task Group_scope_narrows_name_lookup()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        var inOps = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var resolver = new CommandResolver(store);

        var resolved = await resolver.ResolveCommandAsync("备份", "运维");
        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveCommandAsync("备份", "测试"));

        Assert.Equal(inOps, resolved.Id);
        Assert.Equal("E_NOT_FOUND", error.Error.Code);
    }

    [Fact]
    public async Task Duplicate_command_name_is_ambiguous()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\b.bat", GroupId = test });
        var resolver = new CommandResolver(store);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveCommandAsync("备份", null));

        Assert.Equal("E_AMBIGUOUS", error.Error.Code);
        Assert.Equal(2, error.Error.Details!["candidates"]!.AsArray().Count);
    }

    [Fact]
    public async Task Comma_separated_list_resolves_each_entry()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var commands = await arranged.Store.GetCommandsAsync(null, SortOption.Name, SortDirection.Ascending, 0);
        var ids = string.Join(",", commands.OrderBy(command => command.Id).Select(command => command.Id));

        var resolved = await arranged.Resolver.ResolveCommandsAsync(ids, null);

        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public async Task Comma_separated_names_are_split_so_use_ids_for_such_names()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        await store.CreateCommandAsync(new Command { Name = "a,b", FilePath = @"D:\a.bat", GroupId = 1 });
        var resolver = new CommandResolver(store);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveCommandsAsync("a,b", null));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandResolverTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"CommandResolver"`。

- [ ] **Step 3: 实现 CommandResolver**

`BashCommandManager.Cli/Operations/CommandResolver.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>把「id / 名称 / 父/子 分组路径 / 逗号列表」解析成实体，并落实 0 条→2、多条→1 的规则。</summary>
public sealed class CommandResolver
{
    private const string GroupHint = "用 bcm groups list --json 查看可用分组";
    private const string CommandHint = "用 bcm commands list --json 查看可用 id";

    private readonly CliCommandStore _store;

    public CommandResolver(CliCommandStore store) => _store = store;

    public async Task<Group> ResolveGroupAsync(string token)
    {
        var matches = await FindGroupsAsync(token);
        return matches.Count switch
        {
            0 => throw CliErrors.NotFound($"未找到分组: {token}", GroupHint),
            1 => matches[0],
            _ => throw CliErrors.Ambiguous("分组", GroupCandidates(matches))
        };
    }

    public async Task<Group?> FindGroupAsync(string token)
    {
        var matches = await FindGroupsAsync(token);
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<Command> ResolveCommandAsync(string token, string? groupToken)
    {
        var text = token.Trim();
        int? scopeGroupId = null;
        if (!string.IsNullOrWhiteSpace(groupToken))
        {
            scopeGroupId = (await ResolveGroupAsync(groupToken)).Id;
        }

        if (int.TryParse(text, out var id))
        {
            var byId = await _store.GetCommandAsync(id)
                       ?? throw CliErrors.NotFound($"未找到命令: {text}", CommandHint);
            if (scopeGroupId is int scope && byId.GroupId != scope)
            {
                throw CliErrors.NotFound($"命令 {id} 不在指定分组内", CommandHint);
            }
            return byId;
        }

        var candidates = await _store.FindCommandsByNameAsync(text, scopeGroupId);
        return candidates.Count switch
        {
            0 => throw CliErrors.NotFound($"未找到命令: {text}", CommandHint),
            1 => candidates[0],
            _ => throw CliErrors.Ambiguous("命令", CommandCandidates(candidates))
        };
    }

    public async Task<IReadOnlyList<Command>> ResolveCommandsAsync(string token, string? groupToken)
    {
        var parts = token.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw CliErrors.InvalidValue("命令列表为空", "用逗号分隔 id 或名称，例如 7,8");
        }

        var result = new List<Command>();
        foreach (var part in parts)
        {
            result.Add(await ResolveCommandAsync(part, groupToken));
        }
        return result;
    }

    private async Task<IReadOnlyList<Group>> FindGroupsAsync(string token)
    {
        var groups = await _store.GetGroupsAsync();
        var text = token.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<Group>();
        }

        if (int.TryParse(text, out var id))
        {
            return groups.Where(group => group.Id == id).ToList();
        }

        var segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return Array.Empty<Group>();
        }

        var current = groups
            .Where(group => string.Equals(group.Name.Trim(), segments[0], StringComparison.OrdinalIgnoreCase))
            .ToList();
        for (var index = 1; index < segments.Length && current.Count > 0; index++)
        {
            var parents = current.Select(group => group.Id).ToHashSet();
            var segment = segments[index];
            current = groups
                .Where(group => group.ParentId is int parentId
                                && parents.Contains(parentId)
                                && string.Equals(group.Name.Trim(), segment, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return current;
    }

    private static JsonArray GroupCandidates(IReadOnlyList<Group> groups)
    {
        var array = new JsonArray();
        foreach (var group in groups.OrderBy(group => group.Id))
        {
            array.Add(new JsonObject
            {
                ["id"] = group.Id,
                ["name"] = group.Name,
                ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null
            });
        }
        return array;
    }

    private static JsonArray CommandCandidates(IReadOnlyList<Command> commands)
    {
        var array = new JsonArray();
        foreach (var command in commands.OrderBy(command => command.Id))
        {
            array.Add(new JsonObject
            {
                ["id"] = command.Id,
                ["name"] = command.Name,
                ["groupId"] = command.GroupId
            });
        }
        return array;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandResolverTests"
```

期望：`Passed! - Failed: 0, Passed: 11`。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli\Operations\CommandResolver.cs tests\BashCommandManager.Cli.Tests\Operations\CommandResolverTests.cs
git commit -m "feat(cli): 命令/分组解析器支持 id、名称、路径与歧义候选"
```

---

### Task 9: ProcessRunner（启动 / 等待 / 超时 / stderr 逐字节转发）

**Files:**
- Create: `BashCommandManager.Cli/Operations/ProcessRunner.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/ProcessRunnerTests.cs`

**Interfaces:**
- Consumes: 无（BLC 内部）
- Produces:
  - `public sealed record ProcessRunRequest(string FileName, string Arguments, string? WorkingDirectory, bool UseShellExecute, string? Verb, bool RedirectOutput, int? TimeoutSeconds)`
  - `public sealed record ProcessRunResult(bool Started, int? ProcessId, int? ExitCode, bool TimedOut)`
  - `public interface IProcessRunner { ProcessRunResult Start(ProcessRunRequest request); ProcessRunResult RunWithResult(ProcessRunRequest request); }`
  - `public sealed class ProcessRunner : IProcessRunner { public ProcessRunner(Stream? stderrBytes = null); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/ProcessRunnerTests.cs`：

```csharp
using BashCommandManager.Cli.Operations;

namespace BashCommandManager.Cli.Tests.Operations;

public class ProcessRunnerTests
{
    private static ProcessRunRequest Cmd(string arguments, bool redirect = false, int? timeout = null)
        => new("cmd.exe", arguments, Path.GetTempPath(), UseShellExecute: false, Verb: null, RedirectOutput: redirect, TimeoutSeconds: timeout);

    [Fact]
    public void RunWithResult_returns_child_exit_code_and_pid()
    {
        var runner = new ProcessRunner();

        var result = runner.RunWithResult(Cmd("/c exit 5"));

        Assert.True(result.Started);
        Assert.False(result.TimedOut);
        Assert.NotNull(result.ProcessId);
        Assert.True(result.ProcessId > 0);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public void RunWithResult_forwards_child_stderr_bytes_to_provided_stream()
    {
        using var buffer = new MemoryStream();
        var runner = new ProcessRunner(buffer);

        var result = runner.RunWithResult(Cmd("/c echo marker 1>&2"));

        Assert.Equal(0, result.ExitCode);
        var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        Assert.Contains("marker", text);
    }

    [Fact]
    public void RunWithResult_reports_timeout_and_kills_process()
    {
        var runner = new ProcessRunner();

        var result = runner.RunWithResult(Cmd("/c ping -n 6 127.0.0.1 > nul", timeout: 1));

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void Start_with_shell_execute_returns_started()
    {
        var runner = new ProcessRunner();

        var result = runner.Start(Cmd("/c exit 0"));

        Assert.True(result.Started);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void Start_with_missing_file_throws_win32_exception()
    {
        var runner = new ProcessRunner();

        var request = new ProcessRunRequest(
            @"Z:\definitely-missing\bcm-missing.exe", string.Empty, null, UseShellExecute: true, Verb: null, RedirectOutput: false, TimeoutSeconds: null);

        Assert.ThrowsAny<System.ComponentModel.Win32Exception>(() => runner.Start(request));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ProcessRunnerTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"ProcessRunner"`。

- [ ] **Step 3: 实现 ProcessRunner**

`BashCommandManager.Cli/Operations/ProcessRunner.cs`：

```csharp
using System.ComponentModel;
using System.Diagnostics;

namespace BashCommandManager.Cli.Operations;

/// <summary>一次进程启动请求（与 TerminalLaunchPlan 的字段一一对应）。</summary>
public sealed record ProcessRunRequest(
    string FileName,
    string Arguments,
    string? WorkingDirectory,
    bool UseShellExecute,
    string? Verb,
    bool RedirectOutput,
    int? TimeoutSeconds);

/// <summary>进程启动结果：是否启动、pid、退出码、是否超时。</summary>
public sealed record ProcessRunResult(bool Started, int? ProcessId, int? ExitCode, bool TimedOut);

/// <summary>进程启动端口，便于在单测中断言参数而不真的起进程。</summary>
public interface IProcessRunner
{
    ProcessRunResult Start(ProcessRunRequest request);

    ProcessRunResult RunWithResult(ProcessRunRequest request);
}

/// <summary>真实实现：ShellExecute 启动或重定向等待；子进程输出逐字节转发到本进程 stderr。</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly Stream? _stderrBytes;

    public ProcessRunner(Stream? stderrBytes = null) => _stderrBytes = stderrBytes;

    public ProcessRunResult Start(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = true,
            Verb = request.Verb ?? string.Empty
        };

        var process = Process.Start(startInfo);
        return new ProcessRunResult(true, process?.Id, null, false);
    }

    public ProcessRunResult RunWithResult(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!string.IsNullOrEmpty(request.Verb))
        {
            startInfo.Verb = request.Verb;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new ProcessRunResult(false, null, null, false);
        }

        var processId = process.Id;
        var stdoutTask = Task.Run(() => Forward(process.StandardOutput.BaseStream));
        var stderrTask = Task.Run(() => Forward(process.StandardError.BaseStream));

        bool exited;
        if (request.TimeoutSeconds is int seconds)
        {
            exited = process.WaitForExit(seconds * 1000);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                Task.WaitAll(new[] { stdoutTask, stderrTask }, 5000);
                return new ProcessRunResult(true, processId, null, true);
            }
        }
        else
        {
            process.WaitForExit();
        }

        Task.WaitAll(new[] { stdoutTask, stderrTask }, 5000);
        return new ProcessRunResult(true, processId, process.ExitCode, false);
    }

    private void Forward(Stream source)
    {
        try
        {
            if (_stderrBytes is null)
            {
                source.CopyTo(Stream.Null);
                return;
            }

            source.CopyTo(_stderrBytes);
            _stderrBytes.Flush();
        }
        catch (IOException)
        {
            // 子进程被 Kill 后管道关闭，属预期
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
```

> `System.ComponentModel` 只在测试里用于 `Win32Exception` 断言；实现文件不需要该 using（`Process.Start` 抛的异常类型在测试项目里引用即可）。

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ProcessRunnerTests"
```

期望：`Passed! - Failed: 0, Passed: 5`（`RunWithResult_reports_timeout_and_kills_process` 约 1-2 秒）。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli\Operations\ProcessRunner.cs tests\BashCommandManager.Cli.Tests\Operations\ProcessRunnerTests.cs
git commit -m "feat(cli): 新增进程启动端口，支持等待、超时与 stderr 逐字节转发"
```

---

### Task 10: CliSettingsStore（settings.json 的 JsonNode 就地读写）

**Files:**
- Create: `BashCommandManager.Cli/Application/CliSettingsStore.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Application/CliSettingsStoreTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/TestSupport/FakeSettingsStore.cs`

**Interfaces:**
- Consumes: `CliPaths`、`CliErrors`、`BashCommandManager.Core.Models.ExecutionSettings`/`TerminalKind`
- Produces:
  - `public sealed record HotkeyConfig(bool Enabled, int Key, int Modifiers)`
  - `public interface ICliSettingsStore { string SettingsPath { get; } HotkeyConfig GetHotkey(); void SetHotkey(HotkeyConfig hotkey); void SetHotkeyEnabled(bool enabled); ExecutionSettings GetExecutionSettings(); }`
  - `public sealed class CliSettingsStore : ICliSettingsStore { public CliSettingsStore(string settingsDirectory, Func<string,bool>? fileExists = null, Func<string,string>? readAllText = null, Action<string,string>? writeAllText = null); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/TestSupport/FakeSettingsStore.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Core.Models;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>内存版 settings，用于处理器单测（不碰真实 settings.json）。</summary>
internal sealed class FakeSettingsStore : ICliSettingsStore
{
    public FakeSettingsStore(
        HotkeyConfig? hotkey = null,
        ExecutionSettings? execution = null,
        string settingsPath = @"C:\fake\settings.json")
    {
        Hotkey = hotkey ?? new HotkeyConfig(true, 57, 6);
        Execution = execution ?? new ExecutionSettings();
        SettingsPath = settingsPath;
    }

    public HotkeyConfig Hotkey { get; private set; }

    public ExecutionSettings Execution { get; }

    public string SettingsPath { get; }

    public HotkeyConfig GetHotkey() => Hotkey;

    public void SetHotkey(HotkeyConfig hotkey) => Hotkey = hotkey;

    public void SetHotkeyEnabled(bool enabled) => Hotkey = Hotkey with { Enabled = enabled };

    public ExecutionSettings GetExecutionSettings() => Execution;
}
```

`tests/BashCommandManager.Cli.Tests/Application/CliSettingsStoreTests.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Application;

public class CliSettingsStoreTests
{
    private static (CliSettingsStore Store, string Path) Create(string? json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bcm-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        if (json is not null)
        {
            File.WriteAllText(path, json);
        }
        return (new CliSettingsStore(directory), path);
    }

    [Fact]
    public void Missing_file_returns_gui_defaults()
    {
        var (store, _) = Create(null);

        var hotkey = store.GetHotkey();

        Assert.True(hotkey.Enabled);
        Assert.Equal(45, hotkey.Key); // Key.B
        Assert.Equal(3, hotkey.Modifiers); // Ctrl+Alt
        Assert.EndsWith("settings.json", store.SettingsPath);
    }

    [Fact]
    public void Reads_flat_hotkey_format()
    {
        var (store, _) = Create("""{"Enabled":true,"Key":57,"Modifiers":6}""");

        var hotkey = store.GetHotkey();

        Assert.Equal(57, hotkey.Key);
        Assert.Equal(6, hotkey.Modifiers);
        Assert.True(hotkey.Enabled);
    }

    [Fact]
    public void Reads_nested_format_and_execution_section()
    {
        var (store, _) = Create("""
        {
          "Hotkey": { "Enabled": false, "Key": 57, "Modifiers": 6 },
          "Execution": { "Terminal": 3, "CustomExecutablePath": "", "ArgumentsTemplate": "", "KeepWindowOpen": false, "RunAsAdministrator": true }
        }
        """);

        var hotkey = store.GetHotkey();
        var execution = store.GetExecutionSettings();

        Assert.False(hotkey.Enabled);
        Assert.Equal(57, hotkey.Key);
        Assert.Equal(TerminalKind.WindowsTerminal, execution.Terminal);
        Assert.False(execution.KeepWindowOpen);
        Assert.True(execution.RunAsAdministrator);
    }

    [Fact]
    public void Missing_execution_section_falls_back_to_defaults()
    {
        var (store, _) = Create("""{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}""");

        var execution = store.GetExecutionSettings();

        Assert.Equal(TerminalKind.Cmd, execution.Terminal);
        Assert.True(execution.KeepWindowOpen);
    }

    [Fact]
    public void Setting_hotkey_preserves_execution_section()
    {
        var (store, path) = Create("""
        {
          "Hotkey": { "Enabled": true, "Key": 66, "Modifiers": 3 },
          "Execution": { "Terminal": 1, "CustomExecutablePath": "", "ArgumentsTemplate": "", "KeepWindowOpen": true, "RunAsAdministrator": false },
          "Unknown": { "Keep": "me" }
        }
        """);

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
        Assert.Equal(6, root["Hotkey"]!["Modifiers"]!.GetValue<int>());
        Assert.Equal(1, root["Execution"]!["Terminal"]!.GetValue<int>());
        Assert.Equal("me", root["Unknown"]!["Keep"]!.GetValue<string>());
    }

    [Fact]
    public void Setting_hotkey_keeps_flat_format_flat()
    {
        var (store, path) = Create("""{"Enabled":true,"Key":66,"Modifiers":3}""");

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(57, root["Key"]!.GetValue<int>());
        Assert.Equal(6, root["Modifiers"]!.GetValue<int>());
        Assert.Null(root["Hotkey"]);
    }

    [Fact]
    public void Disabling_hotkey_only_touches_enabled()
    {
        var (store, path) = Create("""{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}""");

        store.SetHotkeyEnabled(false);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.False(root["Hotkey"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
    }

    [Fact]
    public void Corrupt_json_raises_config_key_error()
    {
        var (store, path) = Create("{ not json");

        var error = Assert.Throws<CliFailureException>(() => store.GetHotkey()).Error;

        Assert.Equal("E_CONFIG_KEY", error.Code);
        Assert.Equal(1, error.ExitCode);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void Writing_to_missing_file_creates_nested_format()
    {
        var (store, path) = Create(null);

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.NotNull(root["Hotkey"]);
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
    }

    [Fact]
    public void Writes_utf8_without_bom_and_keeps_chinese()
    {
        var (store, path) = Create("""{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}""");

        store.SetHotkeyEnabled(false);

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CliSettingsStoreTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"CliSettingsStore"`。

- [ ] **Step 3: 实现 CliSettingsStore**

`BashCommandManager.Cli/Application/CliSettingsStore.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Application;

/// <summary>settings.json 中的 Hotkey 段（与 GUI 的 HotkeySettings 同字段，纯整数避免 WPF 依赖）。</summary>
public sealed record HotkeyConfig(bool Enabled, int Key, int Modifiers);

/// <summary>CLI 的 settings.json 读写端口。</summary>
public interface ICliSettingsStore
{
    string SettingsPath { get; }

    HotkeyConfig GetHotkey();

    void SetHotkey(HotkeyConfig hotkey);

    void SetHotkeyEnabled(bool enabled);

    ExecutionSettings GetExecutionSettings();
}

/// <summary>用 JsonNode 就地读写：嵌套 {Hotkey,Execution} 与旧扁平格式都支持，且不覆盖其他节点。</summary>
public sealed class CliSettingsStore : ICliSettingsStore
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string> _readAllText;
    private readonly Action<string, string> _writeAllText;

    public CliSettingsStore(
        string settingsDirectory,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null,
        Action<string, string>? writeAllText = null)
    {
        SettingsPath = Path.Combine(settingsDirectory, "settings.json");
        _fileExists = fileExists ?? File.Exists;
        _readAllText = readAllText ?? (path => File.ReadAllText(path, Encoding.UTF8));
        _writeAllText = writeAllText ?? ((path, content) =>
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, content, new UTF8Encoding(false));
        });
    }

    public string SettingsPath { get; }

    public HotkeyConfig GetHotkey()
    {
        var root = LoadRoot();
        if (root is null)
        {
            return new HotkeyConfig(true, 45, 3);
        }

        if (root["Hotkey"] is JsonObject nested)
        {
            return new HotkeyConfig(
                ReadBool(nested["Enabled"], true),
                ReadInt(nested["Key"], 45),
                ReadInt(nested["Modifiers"], 3));
        }

        return new HotkeyConfig(
            ReadBool(root["Enabled"], true),
            ReadInt(root["Key"], 45),
            ReadInt(root["Modifiers"], 3));
    }

    public void SetHotkey(HotkeyConfig hotkey)
    {
        var root = LoadRoot() ?? new JsonObject();
        if (root["Hotkey"] is JsonObject nested)
        {
            nested["Enabled"] = hotkey.Enabled;
            nested["Key"] = hotkey.Key;
            nested["Modifiers"] = hotkey.Modifiers;
        }
        else
        {
            root["Enabled"] = hotkey.Enabled;
            root["Key"] = hotkey.Key;
            root["Modifiers"] = hotkey.Modifiers;
        }
        Save(root);
    }

    public void SetHotkeyEnabled(bool enabled)
    {
        var hotkey = GetHotkey() with { Enabled = enabled };
        SetHotkey(hotkey);
    }

    public ExecutionSettings GetExecutionSettings()
    {
        var root = LoadRoot();
        var section = root?["Execution"] as JsonObject;
        if (section is null)
        {
            return new ExecutionSettings();
        }

        return new ExecutionSettings
        {
            Terminal = ReadTerminal(section["Terminal"]),
            CustomExecutablePath = ReadString(section["CustomExecutablePath"]),
            ArgumentsTemplate = ReadString(section["ArgumentsTemplate"]),
            KeepWindowOpen = ReadBool(section["KeepWindowOpen"], true),
            RunAsAdministrator = ReadBool(section["RunAsAdministrator"], false)
        };
    }

    private JsonObject? LoadRoot()
    {
        if (!_fileExists(SettingsPath))
        {
            return null;
        }

        var text = _readAllText(SettingsPath);
        try
        {
            return JsonNode.Parse(text, NodeOptions, DocumentOptions) as JsonObject
                   ?? throw CliErrors.ConfigKey($"settings.json 根节点不是对象: {SettingsPath}");
        }
        catch (JsonException ex)
        {
            throw CliErrors.ConfigKey($"settings.json 解析失败: {SettingsPath} ({ex.Message})");
        }
    }

    private void Save(JsonObject root)
    {
        try
        {
            _writeAllText(SettingsPath, root.ToJsonString(WriteOptions));
        }
        catch (IOException ex)
        {
            throw CliErrors.Internal($"写入 settings.json 失败: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CliErrors.Internal($"写入 settings.json 失败: {ex.Message}");
        }
    }

    private static int ReadInt(JsonNode? node, int fallback)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    private static bool ReadBool(JsonNode? node, bool fallback)
        => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static string ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text?.Trim() ?? string.Empty : string.Empty;

    private static TerminalKind ReadTerminal(JsonNode? node)
    {
        var number = ReadInt(node, 0);
        return Enum.IsDefined(typeof(TerminalKind), number) ? (TerminalKind)number : TerminalKind.Cmd;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CliSettingsStoreTests"
```

期望：`Passed! - Failed: 0, Passed: 10`。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli\Application\CliSettingsStore.cs tests\BashCommandManager.Cli.Tests\Application tests\BashCommandManager.Cli.Tests\TestSupport
git commit -m "feat(cli): settings.json 就地读写，兼容扁平与嵌套格式"
```

---

### Task 11: GuiLauncher（GUI 路径查找与启动）

**Files:**
- Create: `BashCommandManager.Cli/Infrastructure/GuiLauncher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Infrastructure/GuiLauncherTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `public interface IGuiLauncher { string? ResolveGuiPath(string? explicitPath); int Launch(string guiPath, int? commandId); }`
  - `public sealed class GuiLauncher : IGuiLauncher { public GuiLauncher(Func<string,string?>? getEnvironmentVariable = null, string? cliDirectory = null, Func<string,bool>? fileExists = null); }`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Infrastructure/GuiLauncherTests.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.Infrastructure;

public class GuiLauncherTests
{
    private const string CliDir = @"C:\Program Files\BashCommandManager\cli";

    private static GuiLauncher Create(params string[] existing)
        => new(name => name == "BASHCOMMANDMANAGER_GUI" ? @"D:\portable\BashCommandManager.exe" : null,
            CliDir,
            path => existing.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Explicit_path_wins()
    {
        var launcher = Create(@"D:\custom\BashCommandManager.exe");

        Assert.Equal(@"D:\custom\BashCommandManager.exe", launcher.ResolveGuiPath(@"D:\custom\BashCommandManager.exe"));
    }

    [Fact]
    public void Missing_explicit_path_returns_null()
    {
        var launcher = Create(@"C:\Program Files\BashCommandManager\BashCommandManager.exe");

        Assert.Null(launcher.ResolveGuiPath(@"D:\missing\BashCommandManager.exe"));
    }

    [Fact]
    public void Environment_variable_is_second_choice()
    {
        var launcher = Create(@"D:\portable\BashCommandManager.exe", @"C:\Program Files\BashCommandManager\BashCommandManager.exe");

        Assert.Equal(@"D:\portable\BashCommandManager.exe", launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Parent_directory_of_cli_is_third_choice()
    {
        var launcher = Create(@"C:\Program Files\BashCommandManager\BashCommandManager.exe");

        Assert.Equal(@"C:\Program Files\BashCommandManager\BashCommandManager.exe", launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Cli_directory_is_fourth_choice()
    {
        var launcher = Create(@"C:\Program Files\BashCommandManager\cli\BashCommandManager.exe");

        Assert.Equal(@"C:\Program Files\BashCommandManager\cli\BashCommandManager.exe", launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Returns_null_when_nothing_found()
    {
        var launcher = Create();

        Assert.Null(launcher.ResolveGuiPath(null));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~GuiLauncherTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"GuiLauncher"`。

- [ ] **Step 3: 实现 GuiLauncher**

`BashCommandManager.Cli/Infrastructure/GuiLauncher.cs`：

```csharp
using System.Diagnostics;

namespace BashCommandManager.Cli.Infrastructure;

/// <summary>GUI 可执行文件查找与启动。</summary>
public interface IGuiLauncher
{
    string? ResolveGuiPath(string? explicitPath);

    int Launch(string guiPath, int? commandId);
}

/// <summary>查找顺序：--gui &gt; BASHCOMMANDMANAGER_GUI &gt; {cliDir}\..\BashCommandManager.exe &gt; {cliDir}\BashCommandManager.exe。</summary>
public sealed class GuiLauncher : IGuiLauncher
{
    public const string EnvironmentVariableName = "BASHCOMMANDMANAGER_GUI";
    public const string GuiExecutableName = "BashCommandManager.exe";

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _cliDirectory;
    private readonly Func<string, bool> _fileExists;

    public GuiLauncher(
        Func<string, string?>? getEnvironmentVariable = null,
        string? cliDirectory = null,
        Func<string, bool>? fileExists = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _cliDirectory = cliDirectory ?? AppContext.BaseDirectory;
        _fileExists = fileExists ?? File.Exists;
    }

    public string? ResolveGuiPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return _fileExists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var fromEnvironment = _getEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && _fileExists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var parent = Path.GetFullPath(Path.Combine(_cliDirectory, "..", GuiExecutableName));
        if (_fileExists(parent))
        {
            return parent;
        }

        var sibling = Path.Combine(_cliDirectory, GuiExecutableName);
        return _fileExists(sibling) ? sibling : null;
    }

    public int Launch(string guiPath, int? commandId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = guiPath,
            Arguments = commandId is int id ? $"--command {id}" : string.Empty,
            UseShellExecute = true
        };

        var process = Process.Start(startInfo);
        return process?.Id ?? 0;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~GuiLauncherTests"
```

期望：`Passed! - Failed: 0, Passed: 6`。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli\Infrastructure\GuiLauncher.cs tests\BashCommandManager.Cli.Tests\Infrastructure
git commit -m "feat(cli): 新增 GUI 定位与启动器（--command 透传）"
```

### Task 12: CliServices + CliDispatcher + CliHost 接线；`groups list` / `groups create`

**Files:**
- Create: `BashCommandManager.Cli/CliServices.cs`
- Create: `BashCommandManager.Cli/CliDispatcher.cs`
- Create: `BashCommandManager.Cli/Operations/GroupCommands.cs`
- Modify: `BashCommandManager.Cli/CliHost.cs`（完整接线：DB 会话、dispatcher、meta）
- Create: `tests/BashCommandManager.Cli.Tests/TestSupport/CliServicesFactory.cs`
- Create: `tests/BashCommandManager.Cli.Tests/TestSupport/FakeProcessRunner.cs`
- Create: `tests/BashCommandManager.Cli.Tests/TestSupport/FakeGuiLauncher.cs`
- Create: `tests/BashCommandManager.Cli.Tests/TestSupport/FakeExecutableLocator.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/GroupCommandsTests.cs`
- Test: `tests/BashCommandManager.Cli.Tests/CommandLine/CliHostDatabaseTests.cs`

**Interfaces:**
- Consumes: T7/T8/T9/T10/T11 全部类型；`BashCommandManager.Core.Services.IExecutableLocator`
- Produces:
  - `public sealed class CliServices : IDisposable { public DatabasePathResolution DatabaseResolution; public DatabaseSession? Session; public ICliSettingsStore Settings; public IProcessRunner Runner; public IGuiLauncher Launcher; public IExecutableLocator Locator; public Func<string,bool> FileExists; public Func<string,bool> DirectoryExists; public string CliDirectory; }`
  - `public static class CliDispatcher { public static Task<CliResult> ExecuteAsync(ParsedCommand command, CliServices services, CliCommandStore? store, CommandResolver? resolver); }`
  - `public static class GroupCommands { public static Task<CliResult> ListAsync(ParsedCommand command, CliCommandStore store); public static Task<CliResult> CreateAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver); }`
  - `CliHost.Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null)` 现在对 `groups/commands/run` 自动创建会话

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/TestSupport/FakeProcessRunner.cs`：

```csharp
using BashCommandManager.Cli.Operations;

namespace BashCommandManager.Cli.Tests.TestSupport;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRunRequest> Requests { get; } = new();

    public ProcessRunResult StartResult { get; set; } = new(true, 4321, null, false);

    public ProcessRunResult RunResult { get; set; } = new(true, 4321, 0, false);

    public Exception? StartException { get; set; }

    public Exception? RunException { get; set; }

    public ProcessRunResult Start(ProcessRunRequest request)
    {
        Requests.Add(request);
        if (StartException is not null)
        {
            throw StartException;
        }
        return StartResult;
    }

    public ProcessRunResult RunWithResult(ProcessRunRequest request)
    {
        Requests.Add(request);
        if (RunException is not null)
        {
            throw RunException;
        }
        return RunResult;
    }
}
```

`tests/BashCommandManager.Cli.Tests/TestSupport/FakeGuiLauncher.cs`：

```csharp
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.TestSupport;

internal sealed class FakeGuiLauncher : IGuiLauncher
{
    public string? ResolvedPath { get; set; } = @"C:\fake\BashCommandManager.exe";

    public List<(string GuiPath, int? CommandId)> Launches { get; } = new();

    public string? ResolveGuiPath(string? explicitPath) => ResolvedPath;

    public int Launch(string guiPath, int? commandId)
    {
        Launches.Add((guiPath, commandId));
        return 5555;
    }
}
```

`tests/BashCommandManager.Cli.Tests/TestSupport/FakeExecutableLocator.cs`：

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Cli.Tests.TestSupport;

internal sealed class FakeExecutableLocator : IExecutableLocator
{
    public Func<TerminalKind, string, string?> Resolver { get; set; }
        = (_, _) => @"C:\Windows\System32\cmd.exe";

    public string? Resolve(TerminalKind kind, string customExecutablePath) => Resolver(kind, customExecutablePath);
}
```

`tests/BashCommandManager.Cli.Tests/TestSupport/CliServicesFactory.cs`：

```csharp
using BashCommandManager.Cli;
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli.Tests.TestSupport;

internal static class CliServicesFactory
{
    public static CliServices Create(
        DatabaseSession session,
        ICliSettingsStore? settings = null,
        IProcessRunner? runner = null,
        IGuiLauncher? launcher = null,
        IExecutableLocator? locator = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        string cliDirectory = @"C:\fake\cli")
        => new(
            new DatabasePathResolution(session.DatabasePath, DatabasePathSource.Argument, null, false),
            session,
            settings ?? new FakeSettingsStore(),
            runner ?? new FakeProcessRunner(),
            launcher ?? new FakeGuiLauncher(),
            locator ?? new FakeExecutableLocator(),
            fileExists ?? File.Exists,
            directoryExists ?? Directory.Exists,
            cliDirectory);
}
```

`tests/BashCommandManager.Cli.Tests/Operations/GroupCommandsTests.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class GroupCommandsTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        return (temp, session, store, new CommandResolver(store));
    }

    [Fact]
    public async Task List_returns_root_group_with_command_count()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var result = await GroupCommands.ListAsync(Parse("groups", "list"), arranged.Store);

        var data = result.Data!.AsArray();
        Assert.Single(data);
        var root = data[0]!.AsObject();
        Assert.Equal(1, root["id"]!.GetValue<int>());
        Assert.Equal("根分组", root["name"]!.GetValue<string>());
        Assert.Null(root["parentId"]);
        Assert.Equal(0, root["commandCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task List_counts_direct_commands_per_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });

        var result = await GroupCommands.ListAsync(Parse("groups", "list"), arranged.Store);

        var opsNode = result.Data!.AsArray().Single(node => node!["id"]!.GetValue<int>() == ops)!.AsObject();
        Assert.Equal(1, opsNode["commandCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task List_tree_nests_children()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);

        var result = await GroupCommands.ListAsync(Parse("groups", "list", "--tree"), arranged.Store);

        var root = result.Data!.AsArray()[0]!.AsObject();
        var opsNode = root["children"]!.AsArray().Single(node => node!["id"]!.GetValue<int>() == ops)!.AsObject();
        var prodNode = opsNode["children"]!.AsArray().Single()!.AsObject();
        Assert.Equal(prod, prodNode["id"]!.GetValue<int>());
        Assert.Equal(0, prodNode["children"]!.AsArray().Count);
        Assert.Contains("生产", result.HumanText);
    }

    [Fact]
    public async Task Create_uses_root_parent_by_default()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var result = await GroupCommands.CreateAsync(Parse("groups", "create", "--name", "运维"), arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal("运维", data["name"]!.GetValue<string>());
        Assert.Equal(1, data["parentId"]!.GetValue<int>());
        Assert.Equal(1, data["sortOrder"]!.GetValue<int>());
    }

    [Fact]
    public async Task Create_accepts_parent_path_and_none()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);

        var child = await GroupCommands.CreateAsync(Parse("groups", "create", "--name", "生产", "--parent", "运维"), arranged.Store, arranged.Resolver);
        var top = await GroupCommands.CreateAsync(Parse("groups", "create", "--name", "顶级", "--parent", "none"), arranged.Store, arranged.Resolver);

        Assert.Equal(ops, child.Data!["parentId"]!.GetValue<int>());
        Assert.Null(top.Data!["parentId"]);
    }

    [Fact]
    public async Task Create_rejects_duplicate_sibling_name()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateGroupAsync("运维", 1);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.CreateAsync(Parse("groups", "create", "--name", "运维"), arranged.Store, arranged.Resolver)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Create_rejects_blank_name()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.CreateAsync(Parse("groups", "create", "--name", "   "), arranged.Store, arranged.Resolver));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Create_reports_missing_parent_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.CreateAsync(Parse("groups", "create", "--name", "x", "--parent", "缺失"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }
}
```

`tests/BashCommandManager.Cli.Tests/CommandLine/CliHostDatabaseTests.cs`：

```csharp
using BashCommandManager.Cli;
using BashCommandManager.Cli.Tests.TestSupport;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class CliHostDatabaseTests
{
    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliHost.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Groups_list_creates_database_and_reports_meta()
    {
        using var temp = new TempDatabase();

        var result = Run("groups", "list", "--db", temp.DatabasePath, "--json");

        Assert.Equal(0, result.ExitCode);
        var root = JsonNode.Parse(result.Stdout)!.AsObject();
        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal(Path.GetFullPath(temp.DatabasePath), root["meta"]!["dbPath"]!.GetValue<string>());
        Assert.True(root["meta"]!["created"]!.GetValue<bool>());
        Assert.Equal(1, root["meta"]!["count"]!.GetValue<int>());
        Assert.NotNull(root["meta"]!["durationMs"]);
    }

    [Fact]
    public void Second_call_reports_created_false()
    {
        using var temp = new TempDatabase();
        Run("groups", "list", "--db", temp.DatabasePath, "--json");

        var result = Run("groups", "list", "--db", temp.DatabasePath, "--json");

        var root = JsonNode.Parse(result.Stdout)!.AsObject();
        Assert.False(root["meta"]!["created"]!.GetValue<bool>());
    }

    [Fact]
    public void Groups_create_round_trips_through_cli()
    {
        using var temp = new TempDatabase();

        var create = Run("groups", "create", "--name", "运维", "--db", temp.DatabasePath, "--json");
        var list = Run("groups", "list", "--db", temp.DatabasePath, "--json");

        Assert.Equal(0, create.ExitCode);
        var createRoot = JsonNode.Parse(create.Stdout)!.AsObject();
        Assert.Equal("运维", createRoot["data"]!["name"]!.GetValue<string>());
        Assert.Equal(2, JsonNode.Parse(list.Stdout)!["meta"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void Human_list_renders_table()
    {
        using var temp = new TempDatabase();

        var result = Run("groups", "list", "--db", temp.DatabasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Id", result.Stdout);
        Assert.Contains("根分组", result.Stdout);
    }

    [Fact]
    public void Errors_still_exit_one_with_json_body()
    {
        using var temp = new TempDatabase();

        var result = Run("groups", "create", "--db", temp.DatabasePath, "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        Assert.Equal("E_USAGE", JsonNode.Parse(result.Stderr)!["error"]!["code"]!.GetValue<string>());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~GroupCommandsTests|FullyQualifiedName~CliHostDatabaseTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"CliServices"`。

- [ ] **Step 3: 实现 CliServices**

`BashCommandManager.Cli/CliServices.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli;

/// <summary>处理器依赖容器：一次命令处理所需的全部端口。</summary>
public sealed class CliServices : IDisposable
{
    public CliServices(
        DatabasePathResolution databaseResolution,
        DatabaseSession? session,
        ICliSettingsStore settings,
        IProcessRunner runner,
        IGuiLauncher launcher,
        IExecutableLocator locator,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        string cliDirectory)
    {
        DatabaseResolution = databaseResolution;
        Session = session;
        Settings = settings;
        Runner = runner;
        Launcher = launcher;
        Locator = locator;
        FileExists = fileExists;
        DirectoryExists = directoryExists;
        CliDirectory = cliDirectory;
    }

    public DatabasePathResolution DatabaseResolution { get; }

    public DatabaseSession? Session { get; }

    public ICliSettingsStore Settings { get; }

    public IProcessRunner Runner { get; }

    public IGuiLauncher Launcher { get; }

    public IExecutableLocator Locator { get; }

    public Func<string, bool> FileExists { get; }

    public Func<string, bool> DirectoryExists { get; }

    public string CliDirectory { get; }

    public string MetaDatabasePath => Session?.DatabasePath ?? DatabaseResolution.DatabasePath;

    public bool MetaCreated => Session?.Created ?? false;

    public DatabasePathSource MetaSource => Session?.Source ?? DatabaseResolution.Source;

    public string? MetaWarning => Session?.MigrationWarning;

    public void Dispose() => Session?.Dispose();
}
```

- [ ] **Step 4: 实现 CliDispatcher（本任务只有 groups.list / groups.create）**

`BashCommandManager.Cli/CliDispatcher.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Output;

namespace BashCommandManager.Cli;

/// <summary>CommandPath → 处理器的唯一映射表。</summary>
public static class CliDispatcher
{
    public static Task<CliResult> ExecuteAsync(
        ParsedCommand command,
        CliServices services,
        CliCommandStore? store,
        CommandResolver? resolver)
    {
        if (NeedsStore(command.Verb) && (store is null || resolver is null))
        {
            throw CliErrors.Internal($"命令 {command.CommandPath} 需要数据库，但会话未创建");
        }

        return command.CommandPath switch
        {
            "groups.list" => GroupCommands.ListAsync(command, store!),
            "groups.create" => GroupCommands.CreateAsync(command, store!, resolver!),
            _ => throw CliErrors.Usage($"命令尚未接线: {command.CommandPath}")
        };
    }

    private static bool NeedsStore(string verb) => verb is "groups" or "commands" or "run";
}
```

- [ ] **Step 5: 实现 GroupCommands（list / create）**

`BashCommandManager.Cli/Operations/GroupCommands.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>groups 子命令：list / create（后续任务追加 rename / delete）。</summary>
public static class GroupCommands
{
    public static async Task<CliResult> ListAsync(ParsedCommand command, CliCommandStore store)
    {
        var groups = await store.GetGroupsAsync();
        var counts = await store.GetCommandCountsAsync();
        int CountOf(int id) => counts.TryGetValue(id, out var count) ? count : 0;

        if (command.HasFlag("tree"))
        {
            var roots = new JsonArray();
            foreach (var group in groups.Where(item => item.ParentId is null).OrderBy(item => item.SortOrder))
            {
                roots.Add(BuildTree(group, groups, CountOf));
            }
            return new CliResult(roots, null, RenderTree(groups, CountOf));
        }

        var items = new JsonArray();
        foreach (var group in groups)
        {
            items.Add(JsonShapes.Group(group, CountOf(group.Id)));
        }
        return CliResult.Items(items);
    }

    public static async Task<CliResult> CreateAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var name = (command.GetValue("name") ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw CliErrors.InvalidValue("分组名称不能为空");
        }

        var parentToken = command.GetValue("parent");
        int? parentId;
        if (parentToken is null)
        {
            parentId = 1;
        }
        else if (string.Equals(parentToken.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            parentId = null;
        }
        else
        {
            parentId = (await resolver.ResolveGroupAsync(parentToken)).Id;
        }

        var duplicate = await store.FindChildGroupByNameAsync(parentId, name);
        if (duplicate is not null)
        {
            throw CliErrors.InvalidValue($"同级分组已存在同名分组: {name}", "换一个名称，或用 bcm groups list --json 查看现有分组");
        }

        var id = await store.CreateGroupAsync(name, parentId);
        var created = await store.GetGroupAsync(id)
                      ?? throw CliErrors.DbError($"创建分组后无法读回: {id}");

        var data = new JsonObject
        {
            ["id"] = created.Id,
            ["name"] = created.Name,
            ["parentId"] = created.ParentId.HasValue ? JsonValue.Create(created.ParentId.Value) : null,
            ["sortOrder"] = created.SortOrder
        };
        return CliResult.Object(data, $"id: {created.Id}{Environment.NewLine}name: {created.Name}");
    }

    private static JsonObject BuildTree(Group group, IReadOnlyList<Group> all, Func<int, int> countOf)
    {
        var children = new JsonArray();
        foreach (var child in all.Where(item => item.ParentId == group.Id).OrderBy(item => item.SortOrder))
        {
            children.Add(BuildTree(child, all, countOf));
        }

        return new JsonObject
        {
            ["id"] = group.Id,
            ["name"] = group.Name,
            ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null,
            ["sortOrder"] = group.SortOrder,
            ["commandCount"] = countOf(group.Id),
            ["children"] = children
        };
    }

    private static string RenderTree(IReadOnlyList<Group> groups, Func<int, int> countOf)
    {
        var lines = new List<string>();

        void Visit(int? parentId, int depth)
        {
            foreach (var group in groups.Where(item => item.ParentId == parentId).OrderBy(item => item.SortOrder))
            {
                lines.Add($"{new string(' ', depth * 2)}{group.Name} (id={group.Id}, 命令 {countOf(group.Id)})");
                Visit(group.Id, depth + 1);
            }
        }

        Visit(null, 0);
        return string.Join(Environment.NewLine, lines);
    }
}
```

- [ ] **Step 6: CliHost 完整接线（本任务版本，后续任务只在 `CliDispatcher` 增分支）**

`BashCommandManager.Cli/CliHost.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;
using System.Data.SQLite;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli;

/// <summary>CLI 的唯一入口编排与异常边界。</summary>
public static class CliHost
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error, Console.OpenStandardError());

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null)
    {
        var wantsJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.Ordinal));
        var stopwatch = Stopwatch.StartNew();
        DatabaseSession? session = null;

        try
        {
            var parsed = CommandLineParser.Parse(args);
            var output = new OutputWriter(stdout, stderr, parsed.Json);

            if (parsed.CommandPath == "help")
            {
                stdout.Write(HelpCatalog.Global());
                return 0;
            }

            if (parsed.CommandPath.StartsWith("help.", StringComparison.Ordinal))
            {
                stdout.Write(HelpCatalog.For(parsed.CommandPath["help.".Length..]));
                return 0;
            }

            if (parsed.CommandPath == "version")
            {
                output.WriteResult(new CliResult(new JsonObject { ["version"] = CliVersion.Value }, null, CliVersion.Value), null);
                return 0;
            }

            DatabasePathResolution resolution;
            if (NeedsDatabase(parsed.Verb))
            {
                session = new DatabaseSession(parsed.DatabasePath);
                resolution = session.Resolution;
            }
            else
            {
                resolution = DatabasePathResolver.ResolveForCurrentProcess(parsed.DatabasePath);
            }

            using var services = CreateServices(resolution, session, stderrBytes);
            var store = session is null ? null : new CliCommandStore(session.Connection);
            var resolver = store is null ? null : new CommandResolver(store);

            var result = CliDispatcher.ExecuteAsync(parsed, services, store, resolver).GetAwaiter().GetResult();
            output.WriteResult(result, BuildMeta(result, session, resolution, stopwatch.ElapsedMilliseconds));
            return 0;
        }
        catch (CliFailureException ex)
        {
            new OutputWriter(stdout, stderr, wantsJson).WriteError(ex.Error);
            return ex.Error.ExitCode;
        }
        catch (SQLiteException ex)
        {
            var error = CliErrors.DbError(ex.Message);
            new OutputWriter(stdout, stderr, wantsJson).WriteError(error);
            return error.ExitCode;
        }
        catch (Exception ex)
        {
            var error = CliErrors.Internal(ex.Message);
            new OutputWriter(stdout, stderr, wantsJson).WriteError(error);
            return error.ExitCode;
        }
        finally
        {
            session?.Dispose();
        }
    }

    private static bool NeedsDatabase(string verb) => verb is "groups" or "commands" or "run";

    private static CliServices CreateServices(DatabasePathResolution resolution, DatabaseSession? session, Stream? stderrBytes)
        => new(
            resolution,
            session,
            new CliSettingsStore(CliPaths.SettingsDirectory),
            new ProcessRunner(stderrBytes),
            new GuiLauncher(),
            new ExecutableLocator(),
            File.Exists,
            Directory.Exists,
            AppContext.BaseDirectory);

    private static JsonObject BuildMeta(CliResult result, DatabaseSession? session, DatabasePathResolution resolution, long durationMs)
    {
        var meta = new JsonObject
        {
            ["dbPath"] = session?.DatabasePath ?? resolution.DatabasePath,
            ["created"] = session?.Created ?? false
        };

        if (result.Data is JsonArray array)
        {
            meta["count"] = array.Count;
        }

        var warning = session?.MigrationWarning;
        if (warning is not null)
        {
            meta["warning"] = warning;
        }

        if (result.Meta is not null)
        {
            foreach (var pair in result.Meta)
            {
                meta[pair.Key] = pair.Value?.DeepClone();
            }
        }

        meta["durationMs"] = durationMs;
        return meta;
    }
}
```

> `new ExecutableLocator()` 与 `new GuiLauncher()` 依赖 #9 的公共无参构造。若 #9 的 `ExecutableLocator` 改成带参构造（`App.xaml.cs` 里 `AddSingleton<IExecutableLocator, ExecutableLocator>()` 会因此编译失败），则 CLI 侧改为传 #9 使用的那组参数；`IExecutableLocator` 接口签名（`string? Resolve(TerminalKind, string)`）不变。

- [ ] **Step 7: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed（`Groups_list_creates_database_and_reports_meta` 断言 `meta.count=1`、`created=true`）。

- [ ] **Step 8: 手工冒烟输出形态**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-demo-" + [guid]::NewGuid())
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe groups list --db "$tmp\app.db" --json
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe groups create --name 运维 --db "$tmp\app.db" --json
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe groups list --tree --db "$tmp\app.db"
Remove-Item -Recurse -Force $tmp
```

期望：第一条 `{"ok":true,"data":[{"id":1,"name":"根分组","parentId":null,"sortOrder":0,"commandCount":0}],"meta":{"dbPath":"...","created":true,"count":1,"durationMs":N}}`；第二条 `data` 含 `"id":2,"name":"运维","parentId":1,"sortOrder":1`；第三条树形文本 `根分组 (id=1, 命令 0)` + 缩进的 `运维 (id=2, 命令 0)`。

- [ ] **Step 9: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 接线 DatabaseSession/CliDispatcher 并实现 groups list/create"
```

---

### Task 13: `groups rename` / `groups delete`

**Files:**
- Modify: `BashCommandManager.Cli/Operations/GroupCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/GroupCommandsTests.cs`（追加用例）

**Interfaces:**
- Consumes: `CommandResolver.ResolveGroupAsync`、`CliCommandStore.GetGroupSubtreeIdsAsync`/`GetCommandIdsInGroupsAsync`
- Produces:
  - `public static Task<CliResult> GroupCommands.RenameAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)`
  - `public static Task<CliResult> GroupCommands.DeleteAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)`

- [ ] **Step 1: 追加失败测试**

在 `GroupCommandsTests.cs` 末尾（类内）追加：

```csharp
    [Fact]
    public async Task Rename_updates_name_and_keeps_siblings_unique()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateGroupAsync("生产运维", 1);

        var renamed = await GroupCommands.RenameAsync(Parse("groups", "rename", ops.ToString(), "--name", "生产运维2"), arranged.Store, arranged.Resolver);
        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.RenameAsync(Parse("groups", "rename", ops.ToString(), "--name", "生产运维"), arranged.Store, arranged.Resolver));

        Assert.Equal("生产运维2", renamed.Data!["name"]!.GetValue<string>());
        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Rename_accepts_group_path()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);

        var result = await GroupCommands.RenameAsync(Parse("groups", "rename", "运维/生产", "--name", "生产环境"), arranged.Store, arranged.Resolver);

        Assert.Equal(prod, result.Data!["id"]!.GetValue<int>());
        Assert.Equal("生产环境", result.Data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Delete_reports_subtree_group_and_command_ids()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = prod });

        var result = await GroupCommands.DeleteAsync(Parse("groups", "delete", "运维", "--force"), arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        var groupIds = data["deletedGroupIds"]!.AsArray().Select(node => node!.GetValue<int>()).OrderBy(id => id).ToArray();
        var commandIds = data["deletedCommandIds"]!.AsArray().Select(node => node!.GetValue<int>()).OrderBy(id => id).ToArray();
        Assert.Equal(new[] { ops, prod }.OrderBy(id => id), groupIds);
        Assert.Equal(new[] { first, second }.OrderBy(id => id), commandIds);
        Assert.Null(await arranged.Store.GetGroupAsync(prod));
        Assert.Null(await arranged.Store.GetCommandAsync(second));
    }

    [Fact]
    public async Task Delete_refuses_root_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.DeleteAsync(Parse("groups", "delete", "1", "--force"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_ROOT_GROUP", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Delete_requires_force_at_parser_level()
    {
        var error = Assert.Throws<CliFailureException>(() => Parse("groups", "delete", "运维"));

        Assert.Equal("E_CONFIRM_REQUIRED", error.Error.Code);
    }

    [Fact]
    public async Task Delete_unknown_group_is_not_found()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.DeleteAsync(Parse("groups", "delete", "缺失", "--force"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~GroupCommandsTests"
```

期望：编译失败 `CS0117: "GroupCommands" 未包含"RenameAsync"的定义`。

- [ ] **Step 3: 实现 Rename / Delete**

在 `GroupCommands.cs` 中追加（`CreateAsync` 之后）：

```csharp
    public static async Task<CliResult> RenameAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var group = await resolver.ResolveGroupAsync(command.Arguments[0]);
        var name = (command.GetValue("name") ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw CliErrors.InvalidValue("分组名称不能为空");
        }

        var duplicate = await store.FindChildGroupByNameAsync(group.ParentId, name);
        if (duplicate is not null && duplicate.Id != group.Id)
        {
            throw CliErrors.InvalidValue($"同级分组已存在同名分组: {name}");
        }

        await store.RenameGroupAsync(group.Id, name);
        var data = new JsonObject { ["id"] = group.Id, ["name"] = name };
        return CliResult.Object(data, $"id: {group.Id}{Environment.NewLine}name: {name}");
    }

    public static async Task<CliResult> DeleteAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var group = await resolver.ResolveGroupAsync(command.Arguments[0]);
        if (group.Id == 1)
        {
            throw CliErrors.RootGroup();
        }

        var groupIds = await store.GetGroupSubtreeIdsAsync(group.Id);
        var commandIds = await store.GetCommandIdsInGroupsAsync(groupIds);
        await store.DeleteGroupAsync(group.Id);

        var data = new JsonObject
        {
            ["deletedGroupIds"] = new JsonArray(groupIds.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
            ["deletedCommandIds"] = new JsonArray(commandIds.Select(id => (JsonNode)JsonValue.Create(id)).ToArray())
        };
        return CliResult.Object(data,
            $"删除分组 {groupIds.Count} 个、命令 {commandIds.Count} 个{Environment.NewLine}groupIds: {string.Join(",", groupIds)}");
    }
```

并在 `CliDispatcher` 的 `switch` 中追加两行：

```csharp
            "groups.rename" => GroupCommands.RenameAsync(command, store!, resolver!),
            "groups.delete" => GroupCommands.DeleteAsync(command, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~GroupCommandsTests"
```

期望：全部 Passed（含 `Delete_reports_subtree_group_and_command_ids`）。

- [ ] **Step 5: 手工冒烟级联删除**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-del-" + [guid]::NewGuid())
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
& $exe groups create --name 运维 --db "$tmp\app.db" --json | Out-Null
& $exe groups create --name 生产 --parent 运维 --db "$tmp\app.db" --json | Out-Null
& $exe groups delete 运维 --force --db "$tmp\app.db" --json
& $exe groups list --db "$tmp\app.db" --json
Remove-Item -Recurse -Force $tmp
```

期望：删除输出 `{"ok":true,"data":{"deletedGroupIds":[2,3],"deletedCommandIds":[]},...}`（此时库里还没有命令，`deletedCommandIds` 为空数组）；随后列表 `meta.count` 为 1（仅根分组）。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 groups rename/delete 并回报级联删除计数"
```

---

### Task 14: `commands list` / `commands search`

**Files:**
- Create: `BashCommandManager.Cli/Operations/CommandCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/CommandCommandsTests.cs`

**Interfaces:**
- Consumes: `CliCommandStore`、`CommandResolver`、`JsonShapes.Command`
- Produces:
  - `public static Task<CliResult> CommandCommands.ListAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)`
  - `public static Task<CliResult> CommandCommands.SearchAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)`
  - 私有辅助：`static (SortOption Sort, SortDirection Direction) ParseSort(ParsedCommand)`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/CommandCommandsTests.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class CommandCommandsTests
{
    internal static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    internal static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        return (temp, session, store, new CommandResolver(store));
    }

    [Fact]
    public async Task List_returns_all_commands_with_group_names()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", Description = "每日", FilePath = @"D:\bat\备份.bat", GroupId = ops });

        var result = await CommandCommands.ListAsync(Parse("commands", "list"), arranged.Store, arranged.Resolver);

        var item = result.Data!.AsArray().Single()!.AsObject();
        Assert.Equal("备份", item["name"]!.GetValue<string>());
        Assert.Equal("运维", item["groupName"]!.GetValue<string>());
        Assert.Equal(0, item["executionCount"]!.GetValue<int>());
        Assert.Null(item["lastExecutedAt"]);
    }

    [Fact]
    public async Task List_filters_by_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var test = await arranged.Store.CreateGroupAsync("测试", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "部署", FilePath = @"D:\b.bat", GroupId = test });

        var result = await CommandCommands.ListAsync(Parse("commands", "list", "--group", "运维"), arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!.AsArray());
        Assert.Equal("备份", result.Data![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task List_sorts_by_count_desc_and_applies_limit()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = 1 });
        await arranged.Store.RecordExecutionAsync(second);
        await arranged.Store.RecordExecutionAsync(second);
        await arranged.Store.RecordExecutionAsync(first);

        var result = await CommandCommands.ListAsync(Parse("commands", "list", "--sort", "count", "--order", "desc", "--limit", "1"), arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!.AsArray());
        Assert.Equal("b", result.Data![0]!["name"]!.GetValue<string>());
        Assert.Equal(2, result.Data![0]!["executionCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Search_matches_name_and_description()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", Description = "", FilePath = @"D:\a.bat", GroupId = 1 });
        await arranged.Store.CreateCommandAsync(new Command { Name = "部署", Description = "备份完成后部署", FilePath = @"D:\b.bat", GroupId = 1 });
        await arranged.Store.CreateCommandAsync(new Command { Name = "清理", Description = "", FilePath = @"D:\c.bat", GroupId = 1 });

        var result = await CommandCommands.SearchAsync(Parse("commands", "search", "备份"), arranged.Store, arranged.Resolver);

        Assert.Equal(2, result.Data!.AsArray().Count);
    }

    [Fact]
    public async Task Search_respects_group_scope()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var test = await arranged.Store.CreateGroupAsync("测试", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\b.bat", GroupId = test });

        var result = await CommandCommands.SearchAsync(Parse("commands", "search", "备份", "--group", "运维"), arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!.AsArray());
        Assert.Equal("运维", result.Data![0]!["groupName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_without_match_returns_empty_array()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var result = await CommandCommands.SearchAsync(Parse("commands", "search", "没有"), arranged.Store, arranged.Resolver);

        Assert.Empty(result.Data!.AsArray());
    }

    [Fact]
    public async Task Human_output_uses_default_key_value_when_human_text_is_absent()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\bat\备份.bat", GroupId = 1 });

        var result = await CommandCommands.ListAsync(Parse("commands", "list"), arranged.Store, arranged.Resolver);

        Assert.Null(result.HumanText);
        Assert.IsType<JsonArray>(result.Data);
        Assert.Equal("备份", result.Data![0]!["name"]!.GetValue<string>());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"CommandCommands"`。

- [ ] **Step 3: 实现 List / Search 与排序辅助**

`BashCommandManager.Cli/Operations/CommandCommands.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>commands 子命令。每个子命令一个方法，逐条实现。</summary>
public static class CommandCommands
{
    public static Task<CliResult> ListAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
        => ListCoreAsync(command, store, resolver, keyword: null);

    public static Task<CliResult> SearchAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
        => ListCoreAsync(command, store, resolver, keyword: command.Arguments[0]);

    private static async Task<CliResult> ListCoreAsync(
        ParsedCommand command,
        CliCommandStore store,
        CommandResolver resolver,
        string? keyword)
    {
        int? groupId = null;
        if (command.GetValue("group") is string groupToken)
        {
            groupId = (await resolver.ResolveGroupAsync(groupToken)).Id;
        }

        var (sort, direction) = ParseSort(command);
        var limit = ParseLimit(command);
        var commands = keyword is null
            ? await store.GetCommandsAsync(groupId, sort, direction, limit)
            : await store.SearchCommandsAsync(keyword, groupId, sort, direction, limit);
        await store.FillGroupNamesAsync(commands);

        var items = new JsonArray();
        foreach (var item in commands)
        {
            items.Add(JsonShapes.Command(item, item.GroupName, includeFileExists: false, _ => false));
        }
        return CliResult.Items(items);
    }

    internal static (SortOption Sort, SortDirection Direction) ParseSort(ParsedCommand command)
    {
        var sort = (command.GetValue("sort") ?? "name").ToLowerInvariant() switch
        {
            "last" => SortOption.LastExecutedAt,
            "count" => SortOption.ExecutionCount,
            _ => SortOption.Name
        };
        var direction = string.Equals(command.GetValue("order"), "desc", StringComparison.OrdinalIgnoreCase)
            ? SortDirection.Descending
            : SortDirection.Ascending;
        return (sort, direction);
    }

    internal static int ParseLimit(ParsedCommand command)
        => command.GetValue("limit") is string text ? int.Parse(text, CultureInfo.InvariantCulture) : 0;
}
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "commands.list" => CommandCommands.ListAsync(command, store!, resolver!),
            "commands.search" => CommandCommands.SearchAsync(command, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：全部 Passed。

- [ ] **Step 5: 手工冒烟列表与搜索**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-list-" + [guid]::NewGuid())
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
& $exe groups create --name 运维 --db "$tmp\app.db" --json | Out-Null
& $exe commands list --db "$tmp\app.db" --json
& $exe commands search 备份 --db "$tmp\app.db" --json
Remove-Item -Recurse -Force $tmp
```

期望：两条都退出 0；`data` 为数组（此时为空 `[]`），`meta.count` 为 0。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 commands list/search 与排序、分组过滤"
```

---

### Task 15: `commands show` / `commands add`

**Files:**
- Modify: `BashCommandManager.Cli/Operations/CommandCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/CommandCommandsTests.cs`（追加用例）

**Interfaces:**
- Consumes: `CliServices.FileExists`、`CommandResolver`、`JsonShapes.Command`
- Produces:
  - `public static Task<CliResult> CommandCommands.ShowAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)`
  - `public static Task<CliResult> CommandCommands.AddAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)`

- [ ] **Step 1: 追加失败测试**

在 `CommandCommandsTests.cs` 类内追加（并把 `Parse`/`ArrangeAsync` 保持 `internal` 供后续任务复用）：

```csharp
    [Fact]
    public async Task Show_returns_file_exists_flag()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ShowAsync(Parse("commands", "show", id.ToString()), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["fileExists"]!.GetValue<bool>());
        Assert.Equal("备份", data["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Show_missing_command_is_not_found()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.ShowAsync(Parse("commands", "show", "999"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Add_creates_command_with_sort_order()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.AddAsync(
            Parse("commands", "add", "--name", "备份", "--file", script, "--description", "每日"),
            services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal("备份", data["name"]!.GetValue<string>());
        Assert.Equal(script, data["filePath"]!.GetValue<string>());
        Assert.Equal(1, data["groupId"]!.GetValue<int>());
        var stored = await arranged.Store.GetCommandAsync(data["id"]!.GetValue<int>());
        Assert.Equal("每日", stored!.Description);
    }

    [Fact]
    public async Task Add_requires_existing_file()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.AddAsync(Parse("commands", "add", "--name", "x", "--file", @"Z:\missing\x.cmd"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Add_resolves_group_path_and_returns_group_id()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var script = arranged.Temp.CreateScript("backup.cmd");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.AddAsync(
            Parse("commands", "add", "--name", "备份", "--file", script, "--group", "运维"),
            services, arranged.Store, arranged.Resolver);

        Assert.Equal(ops, result.Data!["groupId"]!.GetValue<int>());
    }
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：编译失败 `CS0117: "CommandCommands" 未包含"ShowAsync"的定义`。

- [ ] **Step 3: 实现 Show / Add**

在 `CommandCommands.cs` 中追加：

```csharp
    public static async Task<CliResult> ShowAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        var item = await resolver.ResolveCommandAsync(command.Arguments[0], command.GetValue("group"));
        var group = await store.GetGroupAsync(item.GroupId);
        var data = JsonShapes.Command(item, group?.Name, includeFileExists: true, services.FileExists);
        return CliResult.Object(data);
    }

    public static async Task<CliResult> AddAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        var name = (command.GetValue("name") ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw CliErrors.InvalidValue("命令名称不能为空");
        }

        var filePath = (command.GetValue("file") ?? string.Empty).Trim();
        if (filePath.Length == 0)
        {
            throw CliErrors.InvalidValue("--file 不能为空");
        }
        if (!services.FileExists(filePath))
        {
            throw CliErrors.FileNotFound($"文件不存在: {filePath}");
        }

        int groupId = 1;
        if (command.GetValue("group") is string groupToken)
        {
            groupId = (await resolver.ResolveGroupAsync(groupToken)).Id;
        }

        var sortOrder = await store.CountCommandsInGroupAsync(groupId);
        var entity = new Command
        {
            Name = name,
            Description = command.GetValue("description") ?? string.Empty,
            FilePath = Path.GetFullPath(filePath),
            GroupId = groupId,
            SortOrder = sortOrder
        };
        var id = await store.CreateCommandAsync(entity);

        var data = new JsonObject
        {
            ["id"] = id,
            ["name"] = entity.Name,
            ["filePath"] = entity.FilePath,
            ["groupId"] = groupId
        };
        return CliResult.Object(data, $"id: {id}{Environment.NewLine}name: {entity.Name}{Environment.NewLine}filePath: {entity.FilePath}");
    }
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "commands.show" => CommandCommands.ShowAsync(command, services, store!, resolver!),
            "commands.add" => CommandCommands.AddAsync(command, services, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed。

- [ ] **Step 5: 手工冒烟 add + show**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-add-" + [guid]::NewGuid())
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
$script = Join-Path $tmp "backup.cmd"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content -Path $script -Value "@echo off`r`nexit /b 0" -Encoding ascii
& $exe groups create --name 运维 --db "$tmp\app.db" --json | Out-Null
& $exe commands add --name 备份 --file $script --group 运维 --db "$tmp\app.db" --json
& $exe commands show 备份 --db "$tmp\app.db" --json
Remove-Item -Recurse -Force $tmp
```

期望：`add` 返回 `{"ok":true,"data":{"id":1,"name":"备份","filePath":"<tmp>\\backup.cmd","groupId":2},...}`；`show` 返回同一条命令并含 `"fileExists":true`。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 commands show/add 并校验文件存在性"
```

### Task 16: `commands edit` / `commands delete`

**Files:**
- Modify: `BashCommandManager.Cli/Operations/CommandCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/CommandCommandsTests.cs`（追加用例）

**Interfaces:**
- Consumes: `CommandResolver.ResolveCommandAsync`/`ResolveCommandsAsync`、`CliCommandStore.UpdateCommandAsync`/`DeleteCommandsAsync`
- Produces:
  - `public static Task<CliResult> CommandCommands.EditAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)`
  - `public static Task<CliResult> CommandCommands.DeleteAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)`

- [ ] **Step 1: 追加失败测试**

在 `CommandCommandsTests.cs` 类内追加：

```csharp
    [Fact]
    public async Task Edit_updates_only_given_fields()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", Description = "旧", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.EditAsync(
            Parse("commands", "edit", id.ToString(), "--description", "每日 2:00"),
            services, arranged.Store, arranged.Resolver);

        var stored = await arranged.Store.GetCommandAsync(id);
        Assert.Equal("每日 2:00", stored!.Description);
        Assert.Equal("备份", stored.Name);
        Assert.Equal(script, stored.FilePath);
        Assert.Equal("每日 2:00", result.Data!["description"]!.GetValue<string>());
        Assert.True(result.Data!["fileExists"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Edit_can_move_to_another_group_and_rename()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.EditAsync(
            Parse("commands", "edit", id.ToString(), "--name", "每日备份", "--group", "运维"),
            services, arranged.Store, arranged.Resolver);

        Assert.Equal("每日备份", result.Data!["name"]!.GetValue<string>());
        Assert.Equal(ops, result.Data!["groupId"]!.GetValue<int>());
        Assert.Equal(ops, (await arranged.Store.GetCommandAsync(id))!.GroupId);
    }

    [Fact]
    public async Task Edit_validates_new_file_exists()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.EditAsync(Parse("commands", "edit", id.ToString(), "--file", @"Z:\missing\x.cmd"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Edit_blank_name_is_invalid()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.EditAsync(Parse("commands", "edit", id.ToString(), "--name", "  "), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Delete_removes_multiple_commands_by_id_list()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = 1 });

        var result = await CommandCommands.DeleteAsync(Parse("commands", "delete", $"{first},{second}", "--force"), arranged.Store, arranged.Resolver);

        var deleted = result.Data!["deleted"]!.AsArray().Select(node => node!.GetValue<int>()).ToArray();
        Assert.Equal(new[] { first, second }, deleted);
        Assert.Null(await arranged.Store.GetCommandAsync(first));
        Assert.Null(await arranged.Store.GetCommandAsync(second));
    }

    [Fact]
    public async Task Delete_by_name_resolves_uniquely()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });

        var result = await CommandCommands.DeleteAsync(Parse("commands", "delete", "备份", "--force"), arranged.Store, arranged.Resolver);

        Assert.Equal(id, result.Data!["deleted"]!.AsArray()[0]!.GetValue<int>());
    }
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：编译失败 `CS0117: "CommandCommands" 未包含"EditAsync"的定义`。

- [ ] **Step 3: 实现 Edit / Delete**

在 `CommandCommands.cs` 中追加：

```csharp
    public static async Task<CliResult> EditAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        // 注意：edit 的 --group 是“移动到”，不是查找范围，因此这里不传 groupToken
        var entity = await resolver.ResolveCommandAsync(command.Arguments[0], groupToken: null);

        if (command.HasValue("name"))
        {
            var name = command.GetValue("name")!.Trim();
            if (name.Length == 0)
            {
                throw CliErrors.InvalidValue("命令名称不能为空");
            }
            entity.Name = name;
        }

        if (command.HasValue("description"))
        {
            entity.Description = command.GetValue("description")!;
        }

        if (command.HasValue("file"))
        {
            var filePath = command.GetValue("file")!.Trim();
            if (!services.FileExists(filePath))
            {
                throw CliErrors.FileNotFound($"文件不存在: {filePath}");
            }
            entity.FilePath = Path.GetFullPath(filePath);
        }

        if (command.GetValue("group") is string groupToken)
        {
            entity.GroupId = (await resolver.ResolveGroupAsync(groupToken)).Id;
        }

        await store.UpdateCommandAsync(entity);

        var group = await store.GetGroupAsync(entity.GroupId);
        var data = JsonShapes.Command(entity, group?.Name, includeFileExists: true, services.FileExists);
        return CliResult.Object(data);
    }

    public static async Task<CliResult> DeleteAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var entities = await resolver.ResolveCommandsAsync(command.Arguments[0], command.GetValue("group"));
        var ids = entities.Select(entity => entity.Id).ToArray();
        await store.DeleteCommandsAsync(ids);

        var data = new JsonObject
        {
            ["deleted"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray())
        };
        return CliResult.Object(data, $"deleted: {string.Join(",", ids)}");
    }
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "commands.edit" => CommandCommands.EditAsync(command, services, store!, resolver!),
            "commands.delete" => CommandCommands.DeleteAsync(command, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：全部 Passed。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 commands edit/delete 并保持 --force 确认"
```

---

### Task 17: `commands move` / `commands frequent`

**Files:**
- Modify: `BashCommandManager.Cli/Operations/CommandCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/CommandCommandsTests.cs`（追加用例）

**Interfaces:**
- Consumes: `CliCommandStore.MoveCommandsAsync`/`GetFrequentlyUsedAsync`/`FillGroupNamesAsync`
- Produces:
  - `public static Task<CliResult> CommandCommands.MoveAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)`
  - `public static Task<CliResult> CommandCommands.FrequentAsync(ParsedCommand command, CliCommandStore store)`

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public async Task Move_reports_moved_ids_and_target_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var target = await arranged.Store.CreateGroupAsync("生产运维", 1);
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = ops });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = ops });

        var result = await CommandCommands.MoveAsync(Parse("commands", "move", $"{first},{second}", "--to", "生产运维"), arranged.Store, arranged.Resolver);

        Assert.Equal(new[] { first, second }, result.Data!["moved"]!.AsArray().Select(node => node!.GetValue<int>()).ToArray());
        Assert.Equal(target, result.Data!["targetGroupId"]!.GetValue<int>());
        Assert.Equal(target, (await arranged.Store.GetCommandAsync(second))!.GroupId);
    }

    [Fact]
    public async Task Move_to_missing_group_is_not_found()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.MoveAsync(Parse("commands", "move", id.ToString(), "--to", "缺失"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Frequent_returns_executed_commands_desc_with_default_limit_10()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "未用过", FilePath = @"D:\c.bat", GroupId = ops });
        await arranged.Store.RecordExecutionAsync(first);
        await arranged.Store.RecordExecutionAsync(second);
        await arranged.Store.RecordExecutionAsync(second);

        var result = await CommandCommands.FrequentAsync(Parse("commands", "frequent"), arranged.Store);

        var names = result.Data!.AsArray().Select(node => node!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "发布", "备份" }, names);
        Assert.Equal("运维", result.Data![0]!["groupName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Frequent_respects_limit()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = 1 });
        await arranged.Store.RecordExecutionAsync(first);
        await arranged.Store.RecordExecutionAsync(second);

        var result = await CommandCommands.FrequentAsync(Parse("commands", "frequent", "--limit", "1"), arranged.Store);

        Assert.Single(result.Data!.AsArray());
    }
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：编译失败 `CS0117: "CommandCommands" 未包含"MoveAsync"的定义`。

- [ ] **Step 3: 实现 Move / Frequent**

在 `CommandCommands.cs` 中追加：

```csharp
    public static async Task<CliResult> MoveAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var entities = await resolver.ResolveCommandsAsync(command.Arguments[0], groupToken: null);
        var target = await resolver.ResolveGroupAsync(command.GetValue("to")!);
        var ids = entities.Select(entity => entity.Id).ToArray();
        await store.MoveCommandsAsync(ids, target.Id);

        var data = new JsonObject
        {
            ["moved"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
            ["targetGroupId"] = target.Id
        };
        return CliResult.Object(data, $"moved: {string.Join(",", ids)}{Environment.NewLine}targetGroup: {target.Name} ({target.Id})");
    }

    public static async Task<CliResult> FrequentAsync(ParsedCommand command, CliCommandStore store)
    {
        var limit = command.GetValue("limit") is string text ? int.Parse(text, CultureInfo.InvariantCulture) : 10;
        var commands = await store.GetFrequentlyUsedAsync(limit);
        await store.FillGroupNamesAsync(commands);

        var items = new JsonArray();
        foreach (var item in commands)
        {
            items.Add(JsonShapes.Command(item, item.GroupName, includeFileExists: false, _ => false));
        }
        return CliResult.Items(items);
    }
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "commands.move" => CommandCommands.MoveAsync(command, store!, resolver!),
            "commands.frequent" => CommandCommands.FrequentAsync(command, store!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CommandCommandsTests"
```

期望：全部 Passed。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 commands move/frequent"
```

---

### Task 18: `commands import`（目录展开 / 递归 / 重复跳过 / `--dry-run`）

**Files:**
- Modify: `BashCommandManager.Cli/Operations/CommandCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/ImportExportTests.cs`

**Interfaces:**
- Consumes: `CliServices.FileExists`/`DirectoryExists`、`CliCommandStore.CreateCommandAsync`/`CountCommandsInGroupAsync`
- Produces: `public static Task<CliResult> CommandCommands.ImportAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/ImportExportTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class ImportExportTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        return (temp, session, store, new CommandResolver(store));
    }

    [Fact]
    public async Task Import_expands_directory_for_bat_and_cmd_only()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "b.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "c.txt"), "not a script");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(
            Parse("commands", "import", "--group", "1", folder), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal(2, data["imported"]!.AsArray().Count);
        Assert.Equal(0, data["skipped"]!.AsArray().Count);
        Assert.Equal(2, data["total"]!.GetValue<int>());
        Assert.False(data["dryRun"]!.GetValue<bool>());
        Assert.Equal(2, (await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0)).Count);
    }

    [Fact]
    public async Task Import_recurses_only_with_recursive_flag()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        var nested = Path.Combine(folder, "sub");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(folder, "top.bat"), "@echo off");
        File.WriteAllText(Path.Combine(nested, "deep.bat"), "@echo off");
        using var services = CliServicesFactory.Create(arranged.Session);

        var shallow = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", folder), services, arranged.Store, arranged.Resolver);
        var deep = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", folder, "--recursive"), services, arranged.Store, arranged.Resolver);

        Assert.Equal(1, shallow.Data!["imported"]!.AsArray().Count);
        Assert.Equal(2, deep.Data!["imported"]!.AsArray().Count + deep.Data!["skipped"]!.AsArray().Count);
    }

    [Fact]
    public async Task Import_skips_duplicate_file_paths()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("a.bat");
        await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", script), services, arranged.Store, arranged.Resolver);

        var skipped = result.Data!["skipped"]!.AsArray();
        Assert.Single(skipped);
        Assert.Equal("duplicate", skipped[0]!["reason"]!.GetValue<string>());
        Assert.Equal(0, result.Data!["imported"]!.AsArray().Count);
        Assert.Equal(1, result.Data!["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Import_allow_duplicates_imports_again()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("a.bat");
        await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(
            Parse("commands", "import", "--group", "1", script, "--allow-duplicates"), services, arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!["imported"]!.AsArray());
        Assert.Equal(2, (await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0)).Count);
    }

    [Fact]
    public async Task Import_dry_run_does_not_write()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(
            Parse("commands", "import", "--group", "1", folder, "--dry-run"), services, arranged.Store, arranged.Resolver);

        Assert.True(result.Data!["dryRun"]!.GetValue<bool>());
        Assert.Null(result.Data!["imported"]![0]!["id"]);
        Assert.Empty(await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0));
    }

    [Fact]
    public async Task Import_skips_explicit_file_with_unsupported_extension()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var text = Path.Combine(arranged.Temp.Root, "notes.txt");
        File.WriteAllText(text, "notes");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", text), services, arranged.Store, arranged.Resolver);

        Assert.Equal("unsupported-extension", result.Data!["skipped"]![0]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Import_missing_path_fails_fast()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", @"Z:\missing"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Import_assigns_incrementing_sort_order()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "b.bat"), "@echo off");
        await arranged.Store.CreateCommandAsync(new Command { Name = "existing", FilePath = @"D:\existing.bat", GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", folder), services, arranged.Store, arranged.Resolver);

        var commands = await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0);
        Assert.Equal(new[] { 0, 1, 2 }, commands.Select(command => command.SortOrder).OrderBy(order => order).ToArray());
    }

    [Fact]
    public async Task Import_reports_group_in_data()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "运维", folder), services, arranged.Store, arranged.Resolver);

        Assert.Equal(ops, result.Data!["group"]!["id"]!.GetValue<int>());
        Assert.Equal("运维", result.Data!["group"]!["name"]!.GetValue<string>());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ImportExportTests"
```

期望：编译失败 `CS0117: "CommandCommands" 未包含"ImportAsync"的定义`。

- [ ] **Step 3: 实现 ImportAsync**

在 `CommandCommands.cs` 中追加：

```csharp
    public static async Task<CliResult> ImportAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        var group = await resolver.ResolveGroupAsync(command.GetValue("group")!);
        var recursive = command.HasFlag("recursive");
        var allowDuplicates = command.HasFlag("allow-duplicates");
        var dryRun = command.HasFlag("dry-run");
        var skipped = new JsonArray();
        var files = new List<string>();

        foreach (var raw in command.Arguments)
        {
            var path = Path.GetFullPath(raw);
            if (services.DirectoryExists(path))
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                files.AddRange(Directory.EnumerateFiles(path, "*.bat", option));
                files.AddRange(Directory.EnumerateFiles(path, "*.cmd", option));
                continue;
            }

            if (services.FileExists(path))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is ".bat" or ".cmd")
                {
                    files.Add(path);
                }
                else
                {
                    skipped.Add(SkipItem(path, "unsupported-extension"));
                }
                continue;
            }

            throw CliErrors.FileNotFound($"路径不存在: {raw}", "传文件或目录的绝对路径");
        }

        var candidates = files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = await store.GetAllCommandsAsync();
        var existingPaths = existing.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = new JsonArray();
        var sortOrder = await store.CountCommandsInGroupAsync(group.Id);

        foreach (var file in candidates)
        {
            if (!allowDuplicates && existingPaths.Contains(file))
            {
                skipped.Add(SkipItem(file, "duplicate"));
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            int? newId = null;
            if (!dryRun)
            {
                var entity = new Command
                {
                    Name = name,
                    Description = string.Empty,
                    FilePath = file,
                    GroupId = group.Id,
                    SortOrder = sortOrder
                };
                newId = await store.CreateCommandAsync(entity);
                existingPaths.Add(file);
            }

            sortOrder++;
            imported.Add(new JsonObject
            {
                ["id"] = newId.HasValue ? JsonValue.Create(newId.Value) : null,
                ["name"] = name,
                ["filePath"] = file
            });
        }

        var data = new JsonObject
        {
            ["group"] = new JsonObject { ["id"] = group.Id, ["name"] = group.Name },
            ["imported"] = imported,
            ["skipped"] = skipped,
            ["total"] = imported.Count + skipped.Count,
            ["dryRun"] = dryRun
        };
        return CliResult.Object(data,
            $"imported: {imported.Count}{Environment.NewLine}skipped: {skipped.Count}{Environment.NewLine}group: {group.Name} ({group.Id})");
    }

    private static JsonObject SkipItem(string filePath, string reason) => new()
    {
        ["filePath"] = filePath,
        ["reason"] = reason
    };
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "commands.import" => CommandCommands.ImportAsync(command, services, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ImportExportTests"
```

期望：全部 Passed。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 commands import 的目录展开、递归、去重与 dry-run"
```

---

### Task 19: `commands export`（JSON/CSV、`--output`、分组过滤）

**Files:**
- Modify: `BashCommandManager.Cli/Operations/CommandCommands.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/ImportExportTests.cs`（追加用例）

**Interfaces:**
- Consumes: `CliCommandStore.GetCommandSubtree`/`GetAllCommandsAsync`/`FillGroupNamesAsync`、`JsonShapes`
- Produces:
  - `public static Task<CliResult> CommandCommands.ExportAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)`
  - 私有辅助：`static string BuildCsv(IReadOnlyList<Command>)`、`static string EscapeCsv(string)`

- [ ] **Step 1: 追加失败测试**

在 `ImportExportTests.cs` 类内追加：

```csharp
    [Fact]
    public async Task Export_without_output_returns_document_in_data()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ExportAsync(Parse("commands", "export"), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal(1, data["schemaVersion"]!.GetValue<int>());
        Assert.NotNull(data["exportedAt"]);
        Assert.Single(data["groups"]!.AsArray());
        Assert.Equal(ops, data["groups"]![0]!["id"]!.GetValue<int>());
        Assert.Single(data["commands"]!.AsArray());
        Assert.Equal("运维", data["commands"]![0]!["groupName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Export_group_filter_is_non_recursive_by_default()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = prod });
        using var services = CliServicesFactory.Create(arranged.Session);

        var shallow = await CommandCommands.ExportAsync(Parse("commands", "export", "--group", "运维"), services, arranged.Store, arranged.Resolver);
        var deep = await CommandCommands.ExportAsync(Parse("commands", "export", "--group", "运维", "--recursive"), services, arranged.Store, arranged.Resolver);

        Assert.Single(shallow.Data!["commands"]!.AsArray());
        Assert.Equal(2, deep.Data!["commands"]!.AsArray().Count);
    }

    [Fact]
    public async Task Export_writes_json_file_and_reports_count()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        var output = Path.Combine(arranged.Temp.Root, "backup.json");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ExportAsync(
            Parse("commands", "export", "--format", "json", "--output", output), services, arranged.Store, arranged.Resolver);

        Assert.Equal(output, result.Data!["path"]!.GetValue<string>());
        Assert.Equal("json", result.Data!["format"]!.GetValue<string>());
        Assert.Equal(1, result.Data!["count"]!.GetValue<int>());
        Assert.True(File.Exists(output));
        var document = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Export_writes_csv_with_fixed_columns_and_escaping()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command
        {
            Name = "备份,每日",
            Description = "含 \"引号\" 的描述",
            FilePath = @"D:\bat\备份.bat",
            GroupId = 1
        });
        var output = Path.Combine(arranged.Temp.Root, "backup.csv");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ExportAsync(
            Parse("commands", "export", "--format", "csv", "--output", output), services, arranged.Store, arranged.Resolver);

        Assert.Equal(1, result.Data!["count"]!.GetValue<int>());
        var lines = File.ReadAllLines(output);
        Assert.Equal("id,name,description,filePath,groupId,groupName,sortOrder,executionCount,lastExecutedAt", lines[0]);
        Assert.Contains("\"备份,每日\"", lines[1]);
        Assert.Contains("\"含 \"\"引号\"\" 的描述\"", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Export_csv_is_utf8_without_bom()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        var output = Path.Combine(arranged.Temp.Root, "backup.csv");
        using var services = CliServicesFactory.Create(arranged.Session);

        await CommandCommands.ExportAsync(Parse("commands", "export", "--format", "csv", "--output", output), services, arranged.Store, arranged.Resolver);

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ImportExportTests"
```

期望：编译失败 `CS0117: "CommandCommands" 未包含"ExportAsync"的定义`。

- [ ] **Step 3: 实现 ExportAsync**

在 `CommandCommands.cs` 中追加：

```csharp
    public static async Task<CliResult> ExportAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        IReadOnlyList<int> scopeGroupIds = Array.Empty<int>();
        if (command.GetValue("group") is string groupToken)
        {
            var group = await resolver.ResolveGroupAsync(groupToken);
            scopeGroupIds = command.HasFlag("recursive")
                ? await store.GetGroupSubtreeIdsAsync(group.Id)
                : new[] { group.Id };
        }

        var all = await store.GetAllCommandsAsync();
        var selected = scopeGroupIds.Count == 0
            ? all
            : all.Where(item => scopeGroupIds.Contains(item.GroupId)).ToList();
        await store.FillGroupNamesAsync(selected);
        selected = selected.OrderBy(item => item.Id).ToList();

        var format = (command.GetValue("format") ?? "json").ToLowerInvariant();
        var outputPath = command.GetValue("output");
        var groups = await store.GetGroupsAsync();
        var referencedGroupIds = selected.Select(item => item.GroupId).Distinct().OrderBy(id => id).ToList();

        if (format == "csv")
        {
            var csv = BuildCsv(selected);
            var target = Path.GetFullPath(outputPath!);
            try
            {
                File.WriteAllText(target, csv, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw CliErrors.Internal($"写入导出文件失败: {ex.Message}");
            }

            var data = new JsonObject { ["path"] = target, ["format"] = "csv", ["count"] = selected.Count };
            return CliResult.Object(data, $"path: {target}{Environment.NewLine}count: {selected.Count}");
        }

        var document = BuildJsonDocument(selected, groups, referencedGroupIds);
        if (outputPath is null)
        {
            return new CliResult(document, null, $"schemaVersion: 1{Environment.NewLine}commands: {selected.Count}");
        }

        var jsonTarget = Path.GetFullPath(outputPath);
        try
        {
            File.WriteAllText(jsonTarget,
                document.ToJsonString(new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }),
                new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CliErrors.Internal($"写入导出文件失败: {ex.Message}");
        }

        var written = new JsonObject { ["path"] = jsonTarget, ["format"] = "json", ["count"] = selected.Count };
        return CliResult.Object(written, $"path: {jsonTarget}{Environment.NewLine}count: {selected.Count}");
    }

    private static JsonObject BuildJsonDocument(
        IReadOnlyList<Command> commands,
        IReadOnlyList<Group> groups,
        IReadOnlyList<int> referencedGroupIds)
    {
        var groupNodes = new JsonArray();
        foreach (var id in referencedGroupIds)
        {
            var group = groups.FirstOrDefault(item => item.Id == id);
            if (group is not null)
            {
                groupNodes.Add(JsonShapes.GroupNode(group));
            }
        }

        var commandNodes = new JsonArray();
        foreach (var item in commands)
        {
            commandNodes.Add(JsonShapes.Command(item, item.GroupName, includeFileExists: false, _ => false));
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["exportedAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            ["groups"] = groupNodes,
            ["commands"] = commandNodes
        };
    }

    private static string BuildCsv(IReadOnlyList<Command> commands)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("id,name,description,filePath,groupId,groupName,sortOrder,executionCount,lastExecutedAt").Append("\r\n");
        foreach (var item in commands)
        {
            builder
                .Append(item.Id).Append(',')
                .Append(EscapeCsv(item.Name)).Append(',')
                .Append(EscapeCsv(item.Description ?? string.Empty)).Append(',')
                .Append(EscapeCsv(item.FilePath)).Append(',')
                .Append(item.GroupId).Append(',')
                .Append(EscapeCsv(item.GroupName)).Append(',')
                .Append(item.SortOrder).Append(',')
                .Append(item.ExecutionCount).Append(',')
                .Append(EscapeCsv(JsonShapes.FormatTimestamp(item.LastExecutedAt) ?? string.Empty))
                .Append("\r\n");
        }
        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "commands.export" => CommandCommands.ExportAsync(command, services, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ImportExportTests"
```

期望：全部 Passed（CSV 两条重点确认转义与无 BOM）。

- [ ] **Step 5: 手工冒烟导出**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-export-" + [guid]::NewGuid())
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content -Path "$tmp\a.cmd" -Value "@echo off`r`nexit /b 0" -Encoding ascii
& $exe commands import --group 1 $tmp --db "$tmp\app.db" --json | Out-Null
& $exe commands export --format csv --output "$tmp\e.csv" --db "$tmp\app.db" --json
Get-Content "$tmp\e.csv" | Select-Object -First 2
Remove-Item -Recurse -Force $tmp
```

期望：`data` 为 `{"path":"<tmp>\\e.csv","format":"csv","count":1}`；CSV 首行是固定列头，第二行含 `a`。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 commands export 的 JSON/CSV 导出与分组过滤"
```

### Task 20: `run`（默认 / `--wait` / `--admin` / `--timeout` / `--dry-run`）

**Files:**
- Create: `BashCommandManager.Cli/Operations/RunCommand.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/RunCommandTests.cs`

**Interfaces:**
- Consumes: `TerminalCommandBuilder.Build/GetDefaultTemplate`、`ExecutionSettings.Clone/WithTerminal`、`TerminalLaunchPlan`（均来自 #9 的 `TerminalCommandBuilder.cs`/`ExecutionSettings.cs`/`ExecutionResult.cs`）、`IExecutableLocator`（#9）、`IProcessRunner`（T9）、`CliSettingsStore.GetExecutionSettings`（T10）
- Produces: `public static Task<CliResult> RunCommand.ExecuteAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/RunCommandTests.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class RunCommandTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private sealed record Arranged(
        TempDatabase Temp,
        DatabaseSession Session,
        CliCommandStore Store,
        CommandResolver Resolver,
        FakeProcessRunner Runner,
        FakeExecutableLocator Locator,
        FakeSettingsStore Settings,
        string ScriptPath,
        int CommandId);

    private static async Task<Arranged> ArrangeAsync(ExecutionSettings? execution = null, string content = "@echo off\r\nexit /b 0\r\n")
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var script = temp.CreateScript("backup.cmd", content);
        var id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        return new Arranged(
            temp,
            session,
            store,
            new CommandResolver(store),
            new FakeProcessRunner(),
            new FakeExecutableLocator(),
            new FakeSettingsStore(execution: execution),
            script,
            id);
    }

    [Fact]
    public async Task Dry_run_returns_cmd_k_line_without_starting_process()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--dry-run"), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["dryRun"]!.GetValue<bool>());
        Assert.Equal("cmd.exe", data["fileName"]!.GetValue<string>());
        Assert.Equal($"/k \"{arranged.ScriptPath}\"", data["arguments"]!.GetValue<string>());
        Assert.Equal(Path.GetDirectoryName(arranged.ScriptPath), data["workingDirectory"]!.GetValue<string>());
        Assert.Empty(arranged.Runner.Requests);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Dry_run_with_wait_uses_c_switch()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", "备份", "--wait", "--dry-run"), services, arranged.Store, arranged.Resolver);

        Assert.Equal($"/c \"{arranged.ScriptPath}\"", result.Data!["arguments"]!.GetValue<string>());
    }

    [Fact]
    public async Task Default_run_starts_process_records_execution_and_reports_pid()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["started"]!.GetValue<bool>());
        Assert.False(data["waited"]!.GetValue<bool>());
        Assert.Equal(4321, data["pid"]!.GetValue<int>());
        var request = Assert.Single(arranged.Runner.Requests);
        Assert.Equal("cmd.exe", request.FileName);
        Assert.True(request.UseShellExecute);
        Assert.Null(request.Verb);
        Assert.Equal(Path.GetDirectoryName(arranged.ScriptPath), request.WorkingDirectory);
        Assert.Equal(1, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Admin_run_uses_runas_verb()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        await RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--admin"), services, arranged.Store, arranged.Resolver);

        Assert.Equal("runas", Assert.Single(arranged.Runner.Requests).Verb);
    }

    [Fact]
    public async Task Wait_success_reports_exit_code_and_duration()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, 0, false);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--wait"), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["waited"]!.GetValue<bool>());
        Assert.Equal(0, data["exitCode"]!.GetValue<int>());
        Assert.NotNull(data["durationMs"]);
        var request = Assert.Single(arranged.Runner.Requests);
        Assert.False(request.UseShellExecute);
        Assert.True(request.RedirectOutput);
        Assert.EndsWith("/c \"" + arranged.ScriptPath + "\"", request.Arguments);
    }

    [Fact]
    public async Task Wait_nonzero_exit_raises_command_failed_with_details()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, 7, false);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--wait"), services, arranged.Store, arranged.Resolver)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("E_COMMAND_FAILED", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Equal("命令退出码 7", error.Error.Message);
        Assert.Equal(7, error.Error.Details!["exitCode"]!.GetValue<int>());
    }

    [Fact]
    public async Task Wait_timeout_raises_timeout_and_does_not_record_execution()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, null, true);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--wait", "--timeout", "3"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_TIMEOUT", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Missing_script_file_is_exit_three()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        File.Delete(arranged.ScriptPath);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
    }

    [Fact]
    public async Task Elevation_cancelled_is_reported()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.StartException = new Win32Exception(1223, "操作已被用户取消");
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--admin"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_ELEVATION_CANCELLED", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Missing_custom_terminal_is_exit_three()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { Terminal = TerminalKind.Custom, CustomExecutablePath = @"Z:\missing\app.exe" });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Locator.Resolver = (_, _) => null;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Contains("自定义程序不存在", error.Error.Message);
    }

    [Fact]
    public async Task Missing_wt_falls_back_to_cmd_with_warning()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Locator.Resolver = (kind, _) => kind == TerminalKind.Cmd ? @"C:\Windows\System32\cmd.exe" : null;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver);

        Assert.Equal("cmd.exe", Assert.Single(arranged.Runner.Requests).FileName);
        Assert.NotNull(result.Meta);
        Assert.Contains("已改用 cmd", result.Meta!["warning"]!.GetValue<string>());
    }

    [Fact]
    public async Task Resolved_executable_from_locator_is_used()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Locator.Resolver = (_, _) => @"C:\Fake\cmd.exe";
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        await RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver);

        Assert.Equal(@"C:\Fake\cmd.exe", Assert.Single(arranged.Runner.Requests).FileName);
    }

    [Fact]
    public async Task Name_lookup_with_group_scope_works()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.UpdateCommandAsync(new Command
        {
            Id = arranged.CommandId,
            Name = "备份",
            FilePath = arranged.ScriptPath,
            GroupId = ops,
            SortOrder = 0
        });
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(Parse("run", "备份", "--group", "运维"), services, arranged.Store, arranged.Resolver);

        Assert.True(result.Data!["started"]!.GetValue<bool>());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~RunCommandTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"RunCommand"`。

- [ ] **Step 3: 实现 RunCommand**

`BashCommandManager.Cli/Operations/RunCommand.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>run 子命令：自实现启动逻辑（D2.2），命令构造复用 TerminalCommandBuilder。</summary>
public static class RunCommand
{
    private const int ErrorCancelled = 1223;

    public static async Task<CliResult> ExecuteAsync(
        ParsedCommand command,
        CliServices services,
        CliCommandStore store,
        CommandResolver resolver)
    {
        var entity = await resolver.ResolveCommandAsync(command.Arguments[0], command.GetValue("group"));
        var settings = services.Settings.GetExecutionSettings().Clone();
        var wait = command.HasFlag("wait");
        var admin = command.HasFlag("admin");

        if (admin)
        {
            settings = settings.Clone();
            settings.RunAsAdministrator = true;
        }

        if (wait)
        {
            settings = WithoutKeepWindowOpen(settings);
        }

        if (command.HasFlag("dry-run"))
        {
            var preview = TerminalCommandBuilder.Build(settings, entity);
            var previewData = new JsonObject
            {
                ["dryRun"] = true,
                ["fileName"] = preview.FileName,
                ["arguments"] = preview.Arguments,
                ["workingDirectory"] = preview.WorkingDirectory
            };
            return CliResult.Object(previewData,
                $"fileName: {preview.FileName}{Environment.NewLine}arguments: {preview.Arguments}{Environment.NewLine}workingDirectory: {preview.WorkingDirectory}");
        }

        if (!services.FileExists(entity.FilePath))
        {
            throw CliErrors.FileNotFound($"脚本文件不存在: {entity.FilePath}", "先用 bcm commands show <id> --json 核对 filePath", exitCode: 3);
        }

        var extraMeta = new JsonObject();
        var resolved = services.Locator.Resolve(settings.Terminal, settings.CustomExecutablePath);
        if (resolved is null && settings.Terminal is TerminalKind.WindowsTerminal or TerminalKind.PowerShell7)
        {
            var requested = settings.Terminal;
            settings = settings.WithTerminal(TerminalKind.Cmd);
            resolved = services.Locator.Resolve(TerminalKind.Cmd, string.Empty);
            extraMeta["warning"] = $"未检测到 {requested}，已改用 cmd 启动";
        }

        if (resolved is null)
        {
            throw CliErrors.FileNotFound($"自定义程序不存在: {settings.CustomExecutablePath}",
                "在设置窗口修正「执行」页的自定义程序路径", exitCode: 3);
        }

        var plan = TerminalCommandBuilder.Build(settings, entity, resolved);
        var stopwatch = Stopwatch.StartNew();

        if (wait)
        {
            var timeout = command.GetValue("timeout") is string timeoutText
                ? int.Parse(timeoutText, CultureInfo.InvariantCulture)
                : (int?)null;
            var request = new ProcessRunRequest(
                plan.FileName,
                plan.Arguments,
                plan.WorkingDirectory,
                UseShellExecute: false,
                Verb: plan.Verb,
                RedirectOutput: true,
                TimeoutSeconds: timeout);

            ProcessRunResult runResult;
            try
            {
                runResult = services.Runner.RunWithResult(request);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                throw CliErrors.ElevationCancelled();
            }

            if (runResult.TimedOut)
            {
                throw CliErrors.Timeout(timeout ?? 0);
            }

            await store.RecordExecutionAsync(entity.Id);
            if (runResult.ExitCode is not 0)
            {
                throw CliErrors.CommandFailed(runResult.ExitCode ?? -1);
            }

            var waitData = new JsonObject
            {
                ["id"] = entity.Id,
                ["name"] = entity.Name,
                ["started"] = true,
                ["waited"] = true,
                ["exitCode"] = runResult.ExitCode ?? 0,
                ["durationMs"] = (int)stopwatch.ElapsedMilliseconds
            };
            return new CliResult(waitData, extraMeta.Count > 0 ? extraMeta : null,
                $"exitCode: 0{Environment.NewLine}durationMs: {stopwatch.ElapsedMilliseconds}");
        }

        try
        {
            var startResult = services.Runner.Start(new ProcessRunRequest(
                plan.FileName,
                plan.Arguments,
                plan.WorkingDirectory,
                UseShellExecute: true,
                Verb: plan.Verb,
                RedirectOutput: false,
                TimeoutSeconds: null));
            await store.RecordExecutionAsync(entity.Id);

            var data = new JsonObject
            {
                ["id"] = entity.Id,
                ["name"] = entity.Name,
                ["started"] = true,
                ["pid"] = startResult.ProcessId.HasValue ? JsonValue.Create(startResult.ProcessId.Value) : null,
                ["waited"] = false
            };
            return new CliResult(data, extraMeta.Count > 0 ? extraMeta : null,
                $"started: true{Environment.NewLine}pid: {(startResult.ProcessId.HasValue ? startResult.ProcessId.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw CliErrors.ElevationCancelled();
        }
    }

    // --wait 语义：把「保持窗口打开」用的模板换成关闭模板（仅当模板仍是默认模板）
    private static ExecutionSettings WithoutKeepWindowOpen(ExecutionSettings settings)
    {
        var clone = settings.Clone();
        var defaultTemplate = TerminalCommandBuilder.GetDefaultTemplate(settings.Terminal, settings.KeepWindowOpen);
        if (string.IsNullOrWhiteSpace(settings.ArgumentsTemplate)
            || string.Equals(settings.ArgumentsTemplate, defaultTemplate, StringComparison.Ordinal))
        {
            clone.KeepWindowOpen = false;
            clone.ArgumentsTemplate = string.Empty;
        }
        return clone;
    }
}
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "run" => RunCommand.ExecuteAsync(command, services, store!, resolver!),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~RunCommandTests"
```

期望：全部 Passed（`Elevation_cancelled_is_reported`、`Wait_timeout_...` 重点确认）。

- [ ] **Step 5: 手工冒烟 dry-run 与真实执行**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-run-" + [guid]::NewGuid())
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content -Path "$tmp\ok.cmd" -Value "@echo off`r`nexit /b 0" -Encoding ascii
$env:BASHCOMMANDMANAGER_SETTINGS_DIR = $tmp
& $exe commands add --name ok --file "$tmp\ok.cmd" --db "$tmp\app.db" --json | Out-Null
& $exe run ok --dry-run --db "$tmp\app.db" --json
& $exe run ok --wait --db "$tmp\app.db" --json
& $exe run ok --wait --json
$LASTEXITCODE
Remove-Item -Recurse -Force $tmp
Remove-Item Env:\BASHCOMMANDMANAGER_SETTINGS_DIR
```

期望：dry-run 输出 `{"ok":true,"data":{"dryRun":true,"fileName":"cmd.exe","arguments":"/k \"<tmp>\\ok.cmd\"","workingDirectory":"<tmp>"},...}`；第二条 `data.exitCode` 为 0；第三条命令不存在 → 退出 2。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 run 的默认/wait/admin/timeout/dry-run 五种路径"
```

---

### Task 21: `config get`

**Files:**
- Create: `BashCommandManager.Cli/Operations/ConfigCommand.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/ConfigCommandTests.cs`

**Interfaces:**
- Consumes: `ICliSettingsStore`、`IGuiLauncher.ResolveGuiPath`、`CliServices.DatabaseResolution/MetaSource`、`CliVersion.Value`
- Produces:
  - `public static Task<CliResult> ConfigCommand.GetAsync(ParsedCommand command, CliServices services)`
  - `public static string ConfigCommand.SourceName(DatabasePathSource source)`
  - 本任务**不**实现 `config set`（T22 才加入 `SetAsync` 与 dispatch 分支，避免留下未实现的占位方法）

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/ConfigCommandTests.cs`：

```csharp
using BashCommandManager.Cli;
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Infrastructure;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class ConfigCommandTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static CliServices Services(FakeSettingsStore settings, string? guiPath = @"C:\Program Files\BashCommandManager\BashCommandManager.exe")
        => new(
            new DatabasePathResolution(@"C:\Users\tester\AppData\Local\BashCommandManager\data\app.db", DatabasePathSource.LocalAppData, null, false),
            session: null,
            settings,
            new FakeProcessRunner(),
            new FakeGuiLauncher { ResolvedPath = guiPath },
            new FakeExecutableLocator(),
            File.Exists,
            Directory.Exists,
            @"C:\Program Files\BashCommandManager\cli");

    [Fact]
    public async Task Get_without_key_returns_all_sections()
    {
        using var services = Services(new FakeSettingsStore());

        var result = await ConfigCommand.GetAsync(Parse("config", "get"), services);

        var data = result.Data!.AsObject();
        Assert.Equal("C:\\Users\\tester\\AppData\\Local\\BashCommandManager\\data\\app.db", data["db"]!["path"]!.GetValue<string>());
        Assert.Equal("localAppData", data["db"]!["source"]!.GetValue<string>());
        Assert.Equal(@"C:\fake\settings.json", data["settings"]!["path"]!.GetValue<string>());
        Assert.True(data["hotkey"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(57, data["hotkey"]!["key"]!.GetValue<int>());
        Assert.Equal("N", data["hotkey"]!["keyName"]!.GetValue<string>());
        Assert.Equal(6, data["hotkey"]!["modifiers"]!.GetValue<int>());
        Assert.Equal("Ctrl+Shift+N", data["hotkey"]!["display"]!.GetValue<string>());
        Assert.Equal(@"C:\Program Files\BashCommandManager\BashCommandManager.exe", data["gui"]!["path"]!.GetValue<string>());
        Assert.Equal("1.3.0", data["version"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("db.path", "db")]
    [InlineData("db.source", "db")]
    [InlineData("hotkey", "hotkey")]
    [InlineData("hotkey.enabled", "hotkey")]
    [InlineData("settings.path", "settings")]
    [InlineData("gui.path", "gui")]
    public async Task Get_with_key_returns_containing_section(string key, string expectedSection)
    {
        using var services = Services(new FakeSettingsStore());

        var result = await ConfigCommand.GetAsync(Parse("config", "get", key), services);

        var data = result.Data!.AsObject();
        Assert.Single(data);
        Assert.Equal(expectedSection, data.Single().Key);
    }

    [Fact]
    public async Task Get_version_key_returns_flat_version()
    {
        using var services = Services(new FakeSettingsStore());

        var result = await ConfigCommand.GetAsync(Parse("config", "get", "version"), services);

        Assert.Equal("1.3.0", result.Data!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task Get_reports_null_gui_path_when_missing()
    {
        using var services = Services(new FakeSettingsStore(), guiPath: null);

        var result = await ConfigCommand.GetAsync(Parse("config", "get", "gui.path"), services);

        Assert.Null(result.Data!["gui"]!["path"]);
    }

    [Theory]
    [InlineData(DatabasePathSource.Argument, "argument")]
    [InlineData(DatabasePathSource.Environment, "environment")]
    [InlineData(DatabasePathSource.LocalAppData, "localAppData")]
    [InlineData(DatabasePathSource.LegacyApplicationDirectory, "legacy")]
    public void Source_names_match_spec(DatabasePathSource source, string expected)
    {
        Assert.Equal(expected, ConfigCommand.SourceName(source));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigCommandTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"ConfigCommand"`。

- [ ] **Step 3: 实现 Get**

`BashCommandManager.Cli/Operations/ConfigCommand.cs`：

```csharp
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Infrastructure;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>config get/set：只读写 settings.json 与只读路径信息。</summary>
public static class ConfigCommand
{
    public static Task<CliResult> GetAsync(ParsedCommand command, CliServices services)
    {
        var key = command.Arguments.Count == 1 ? command.Arguments[0] : null;
        var hotkey = services.Settings.GetHotkey();
        var spec = HotkeySpec.Create(hotkey.Key, hotkey.Modifiers);
        var guiPath = services.Launcher.ResolveGuiPath(null);

        var db = new JsonObject
        {
            ["path"] = services.DatabaseResolution.DatabasePath,
            ["source"] = SourceName(services.MetaSource)
        };
        var settings = new JsonObject { ["path"] = services.Settings.SettingsPath };
        var hotkeyNode = new JsonObject
        {
            ["enabled"] = hotkey.Enabled,
            ["key"] = hotkey.Key,
            ["keyName"] = spec.KeyName,
            ["modifiers"] = hotkey.Modifiers,
            ["display"] = spec.Display
        };
        var gui = new JsonObject { ["path"] = guiPath is null ? null : guiPath };

        var full = new JsonObject
        {
            ["db"] = db,
            ["settings"] = settings,
            ["hotkey"] = hotkeyNode,
            ["gui"] = gui,
            ["version"] = CliVersion.Value
        };

        JsonNode data = key switch
        {
            null => full,
            "hotkey" or "hotkey.enabled" => new JsonObject { ["hotkey"] = hotkeyNode.DeepClone() },
            "db.path" or "db.source" => new JsonObject { ["db"] = db.DeepClone() },
            "settings.path" => new JsonObject { ["settings"] = settings.DeepClone() },
            "gui.path" => new JsonObject { ["gui"] = gui.DeepClone() },
            "version" => new JsonObject { ["version"] = CliVersion.Value },
            _ => throw CliErrors.ConfigKey($"未知配置键: {key}")
        };

        return Task.FromResult(new CliResult(data, null, RenderHuman(data)));
    }

    public static string SourceName(DatabasePathSource source) => source switch
    {
        DatabasePathSource.Argument => "argument",
        DatabasePathSource.Environment => "environment",
        DatabasePathSource.LegacyApplicationDirectory => "legacy",
        _ => "localAppData"
    };

    private static string RenderHuman(JsonNode data)
    {
        var lines = new List<string>();

        void Flatten(JsonObject node, string prefix)
        {
            foreach (var pair in node)
            {
                var label = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
                if (pair.Value is JsonObject nested)
                {
                    Flatten(nested, label);
                    continue;
                }

                var text = pair.Value is JsonValue value && value.TryGetValue<string>(out var stringValue)
                    ? stringValue
                    : pair.Value?.ToJsonString() ?? string.Empty;
                lines.Add($"{label}: {text}");
            }
        }

        if (data is JsonObject root)
        {
            Flatten(root, string.Empty);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
```

并在 `CliDispatcher` 的 `switch` 中追加（本任务只加 `config.get`）：

```csharp
            "config.get" => ConfigCommand.GetAsync(command, services),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigCommandTests"
```

期望：全部 Passed（1 条全量 + 6 条 `Get_with_key_...` + `version` + `gui.path` 为 null + 4 条 `Source_names_...`）。

- [ ] **Step 5: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 config get 的完整/按键两种输出"
```

---

### Task 22: `config set`

**Files:**
- Modify: `BashCommandManager.Cli/Operations/ConfigCommand.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/ConfigCommandTests.cs`（追加用例）

**Interfaces:**
- Consumes: `HotkeySpec.Parse`、`ICliSettingsStore.SetHotkey/SetHotkeyEnabled/GetHotkey`
- Produces:
  - `public static Task<CliResult> ConfigCommand.SetAsync(ParsedCommand command, CliServices services)`（新增）
  - 私有辅助：`static CliResult HotkeyResult(HotkeyConfig hotkey)`
  - `CliDispatcher` 增加 `"config.set"` 分支

- [ ] **Step 1: 追加失败测试**

```csharp
    [Fact]
    public async Task Set_hotkey_writes_key_and_modifiers()
    {
        var settings = new FakeSettingsStore(new HotkeyConfig(true, 45, 3));
        using var services = Services(settings);

        var result = await ConfigCommand.SetAsync(Parse("config", "set", "hotkey", "Ctrl+Shift+N"), services);

        Assert.Equal(57, settings.Hotkey.Key);
        Assert.Equal(6, settings.Hotkey.Modifiers);
        Assert.True(settings.Hotkey.Enabled);
        Assert.Equal("Ctrl+Shift+N", result.Data!["hotkey"]!["display"]!.GetValue<string>());
        Assert.Equal(57, result.Data!["hotkey"]!["key"]!.GetValue<int>());
    }

    [Fact]
    public async Task Set_hotkey_preserves_disabled_state()
    {
        var settings = new FakeSettingsStore(new HotkeyConfig(false, 45, 3));
        using var services = Services(settings);

        await ConfigCommand.SetAsync(Parse("config", "set", "hotkey", "Ctrl+Alt+B"), services);

        Assert.False(settings.Hotkey.Enabled);
        Assert.Equal(45, settings.Hotkey.Key);
        Assert.Equal(3, settings.Hotkey.Modifiers);
    }

    [Fact]
    public async Task Set_hotkey_enabled_toggles_only_flag()
    {
        var settings = new FakeSettingsStore(new HotkeyConfig(true, 57, 6));
        using var services = Services(settings);

        var result = await ConfigCommand.SetAsync(Parse("config", "set", "hotkey.enabled", "false"), services);

        Assert.False(settings.Hotkey.Enabled);
        Assert.Equal(57, settings.Hotkey.Key);
        Assert.False(result.Data!["hotkey"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Set_hotkey_rejects_missing_modifier()
    {
        var settings = new FakeSettingsStore();
        using var services = Services(settings);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            ConfigCommand.SetAsync(Parse("config", "set", "hotkey", "N"), services));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Set_unknown_key_is_config_key_error()
    {
        using var services = Services(new FakeSettingsStore());

        var error = Assert.Throws<CliFailureException>(() => Parse("config", "set", "execution.terminal", "1"));

        Assert.Equal("E_CONFIG_KEY", error.Error.Code);
    }
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ConfigCommandTests"
```

期望：编译失败 `CS0117: "ConfigCommand" 未包含"SetAsync"的定义`（T21 只实现了 `GetAsync`）。

- [ ] **Step 3: 新增 SetAsync 并接线**

在 `ConfigCommand` 类中新增（`SourceName` 之前）：

```csharp
    public static Task<CliResult> SetAsync(ParsedCommand command, CliServices services)
    {
        var key = command.Arguments[0];
        var value = command.Arguments[1];

        switch (key)
        {
            case "hotkey":
            {
                var spec = HotkeySpec.Parse(value);
                var current = services.Settings.GetHotkey();
                var updated = new HotkeyConfig(current.Enabled, spec.Key, spec.Modifiers);
                services.Settings.SetHotkey(updated);
                return Task.FromResult(HotkeyResult(updated));
            }
            case "hotkey.enabled":
            {
                var enabled = bool.Parse(value);
                services.Settings.SetHotkeyEnabled(enabled);
                return Task.FromResult(HotkeyResult(services.Settings.GetHotkey()));
            }
            default:
                throw CliErrors.ConfigKey($"未知配置键: {key}", "可用键: hotkey,hotkey.enabled");
        }
    }

    private static CliResult HotkeyResult(HotkeyConfig hotkey)
    {
        var spec = HotkeySpec.Create(hotkey.Key, hotkey.Modifiers);
        var node = new JsonObject
        {
            ["enabled"] = hotkey.Enabled,
            ["key"] = hotkey.Key,
            ["keyName"] = spec.KeyName,
            ["modifiers"] = hotkey.Modifiers,
            ["display"] = spec.Display
        };
        return new CliResult(new JsonObject { ["hotkey"] = node }, null,
            $"hotkey: {spec.Display}{Environment.NewLine}enabled: {hotkey.Enabled}");
    }
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "config.set" => ConfigCommand.SetAsync(command, services),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed（含 T21 的 `Get` 用例）。

- [ ] **Step 5: 手工冒烟（用临时 settings 目录，绝不碰真实 settings.json）**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-config-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$env:BASHCOMMANDMANAGER_SETTINGS_DIR = $tmp
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
& $exe config set hotkey "Ctrl+Shift+N" --json
& $exe config get hotkey --json
Get-Content "$tmp\settings.json"
Remove-Item -Recurse -Force $tmp
Remove-Item Env:\BASHCOMMANDMANAGER_SETTINGS_DIR
```

期望：`set` 返回 `data.hotkey = {enabled:true,key:57,keyName:"N",modifiers:6,display:"Ctrl+Shift+N"}`；`get` 返回相同；`settings.json` 内容为 `{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}`（缩进两空格、UTF-8 无 BOM）。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 config set hotkey/hotkey.enabled"
```

---

### Task 23: `open`

**Files:**
- Create: `BashCommandManager.Cli/Operations/OpenCommand.cs`
- Modify: `BashCommandManager.Cli/CliDispatcher.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Operations/OpenCommandTests.cs`

**Interfaces:**
- Consumes: `IGuiLauncher`
- Produces: `public static Task<CliResult> OpenCommand.ExecuteAsync(ParsedCommand command, CliServices services)`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Cli.Tests/Operations/OpenCommandTests.cs`：

```csharp
using BashCommandManager.Cli;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli.Tests.Operations;

public class OpenCommandTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static CliServices Services(FakeGuiLauncher launcher)
        => new(
            new DatabasePathResolution(@"C:\db\app.db", DatabasePathSource.LocalAppData, null, false),
            session: null,
            new FakeSettingsStore(),
            new FakeProcessRunner(),
            launcher,
            new FakeExecutableLocator(),
            File.Exists,
            Directory.Exists,
            @"C:\Program Files\BashCommandManager\cli");

    [Fact]
    public async Task Open_launches_gui_without_command()
    {
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher);

        var result = await OpenCommand.ExecuteAsync(Parse("open"), services);

        Assert.True(result.Data!["opened"]!.GetValue<bool>());
        Assert.Equal(@"C:\fake\BashCommandManager.exe", result.Data!["guiPath"]!.GetValue<string>());
        Assert.Null(result.Data!["commandId"]);
        var launch = Assert.Single(launcher.Launches);
        Assert.Null(launch.CommandId);
    }

    [Fact]
    public async Task Open_passes_command_id()
    {
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher);

        var result = await OpenCommand.ExecuteAsync(Parse("open", "--command", "7"), services);

        Assert.Equal(7, result.Data!["commandId"]!.GetValue<int>());
        Assert.Equal(7, Assert.Single(launcher.Launches).CommandId);
    }

    [Fact]
    public async Task Open_uses_explicit_gui_path_when_launcher_accepts_it()
    {
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher);

        await OpenCommand.ExecuteAsync(Parse("open", "--gui", @"D:\custom\BashCommandManager.exe"), services);

        Assert.Equal(@"D:\custom\BashCommandManager.exe", Assert.Single(launcher.Launches).GuiPath);
    }

    [Fact]
    public async Task Open_without_gui_is_not_found_exit_two()
    {
        var launcher = new FakeGuiLauncher { ResolvedPath = null };
        using var services = Services(launcher);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => OpenCommand.ExecuteAsync(Parse("open"), services));

        Assert.Equal("E_GUI_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Open_with_missing_explicit_gui_reports_specific_message()
    {
        var launcher = new FakeGuiLauncher { ResolvedPath = null };
        using var services = Services(launcher);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            OpenCommand.ExecuteAsync(Parse("open", "--gui", @"D:\missing\BashCommandManager.exe"), services));

        Assert.Equal("E_GUI_NOT_FOUND", error.Error.Code);
        Assert.Contains(@"D:\missing\BashCommandManager.exe", error.Error.Message);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~OpenCommandTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"OpenCommand"`。

- [ ] **Step 3: 实现 OpenCommand**

`BashCommandManager.Cli/Operations/OpenCommand.cs`：

```csharp
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>open 子命令：定位并启动 GUI（可选定位到某条命令）。</summary>
public static class OpenCommand
{
    public static Task<CliResult> ExecuteAsync(ParsedCommand command, CliServices services)
    {
        var explicitGui = command.GetValue("gui");
        var guiPath = services.Launcher.ResolveGuiPath(explicitGui);
        if (guiPath is null)
        {
            throw explicitGui is null
                ? CliErrors.GuiNotFound("未找到 BashCommandManager.exe",
                    $"用 --gui <路径> 指定，或设置环境变量 {GuiLauncher.EnvironmentVariableName}")
                : CliErrors.GuiNotFound($"指定的 GUI 不存在: {explicitGui}", "核对 --gui 路径");
        }

        int? commandId = command.GetValue("command") is string text
            ? int.Parse(text, CultureInfo.InvariantCulture)
            : null;

        var processId = services.Launcher.Launch(guiPath, commandId);

        var data = new JsonObject
        {
            ["opened"] = true,
            ["guiPath"] = guiPath,
            ["commandId"] = commandId.HasValue ? JsonValue.Create(commandId.Value) : null
        };
        var human = $"guiPath: {guiPath}{Environment.NewLine}commandId: {(commandId?.ToString(CultureInfo.InvariantCulture) ?? "null")}";
        return Task.FromResult(new CliResult(data,
            processId > 0 ? new JsonObject { ["pid"] = processId } : null,
            human));
    }
}
```

并在 `CliDispatcher` 的 `switch` 中追加：

```csharp
            "open" => OpenCommand.ExecuteAsync(command, services),
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：全部 Passed。

- [ ] **Step 5: 手工冒烟（真实 GUI 未安装时按失败路径验证）**

```powershell
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
& $exe open --json ; $LASTEXITCODE
& $exe open --gui "Z:\missing\BashCommandManager.exe" --json ; $LASTEXITCODE
```

期望：第一条若同目录无 GUI → `{"ok":false,"error":{"code":"E_GUI_NOT_FOUND",...}}` 且退出 2；第二条 `message` 含 `Z:\missing\BashCommandManager.exe`，退出 2。

- [ ] **Step 6: 提交**

```powershell
git add BashCommandManager.Cli tests\BashCommandManager.Cli.Tests
git commit -m "feat(cli): 实现 open 的 GUI 定位与 --command 透传"
```

### Task 24: GUI 侧支持 `--command` 定位（解析器 / 管道消息 / 启动参数 / 定位方法）

**Files:**
- Create: `Core/Services/StartupArgumentParser.cs`
- Create: `Core/Services/SingleInstanceMessage.cs`
- Modify: `Core/Services/SingleInstanceService.cs`
- Modify: `Core/Services/CommandService.cs`（接口 + 实现追加 `GetByIdAsync`）
- Modify: `App.xaml.cs`
- Modify: `MainWindow.xaml.cs`
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `ViewModels/GroupTreeViewModel.cs`
- Test: `tests/BashCommandManager.Tests/Core/StartupArgumentParserTests.cs`
- Test: `tests/BashCommandManager.Tests/Core/SingleInstanceMessageTests.cs`

**Interfaces:**
- Consumes: 现有 `ICommandService`、`IGroupService`、`MainViewModel`
- Produces:
  - `public static class StartupArgumentParser { public const string CommandOption = "--command"; public static bool TryGetCommandId(string[] args, out int commandId); }`
  - `public static class SingleInstanceMessage { public const string ActivateVerb = "ACTIVATE"; public static string Format(string[]? args); public static bool TryParse(string? message, out string[] args); }`
  - `public bool SingleInstanceService.Initialize(Action<string[]> onActivateCallback, string[] startupArgs)`
  - `public void MainWindow.ApplyStartupArguments(string[] args)`
  - `public Task MainViewModel.SelectCommandAsync(int commandId)`
  - `public Group? GroupTreeViewModel.FindGroupById(int id)`
  - `Task<Command?> ICommandService.GetByIdAsync(int id)`

- [ ] **Step 1: 写失败测试**

`tests/BashCommandManager.Tests/Core/StartupArgumentParserTests.cs`：

```csharp
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Core;

public class StartupArgumentParserTests
{
    [Fact]
    public void Parses_command_id()
    {
        Assert.True(StartupArgumentParser.TryGetCommandId(new[] { "--command", "7" }, out var id));
        Assert.Equal(7, id);
    }

    [Fact]
    public void Ignores_case_and_position()
    {
        Assert.True(StartupArgumentParser.TryGetCommandId(new[] { "--other", "--COMMAND", "12" }, out var id));
        Assert.Equal(12, id);
    }

    [Theory]
    [InlineData(new string[0])]
    [InlineData(new[] { "--command" })]
    [InlineData(new[] { "--command", "abc" })]
    [InlineData(new[] { "--command", "0" })]
    [InlineData(new[] { "--command", "-3" })]
    [InlineData(new[] { "--noargs" })]
    public void Returns_false_for_invalid_input(string[] args)
    {
        Assert.False(StartupArgumentParser.TryGetCommandId(args, out var id));
        Assert.Equal(0, id);
    }
}
```

`tests/BashCommandManager.Tests/Core/SingleInstanceMessageTests.cs`：

```csharp
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Core;

public class SingleInstanceMessageTests
{
    [Fact]
    public void Empty_args_format_is_plain_activate()
    {
        Assert.Equal("ACTIVATE", SingleInstanceMessage.Format(Array.Empty<string>()));
        Assert.Equal("ACTIVATE", SingleInstanceMessage.Format(null));
    }

    [Fact]
    public void Args_format_is_json_after_tab()
    {
        Assert.Equal("ACTIVATE\t[\"--command\",\"7\"]", SingleInstanceMessage.Format(new[] { "--command", "7" }));
    }

    [Fact]
    public void Parses_legacy_activate_without_args()
    {
        Assert.True(SingleInstanceMessage.TryParse("ACTIVATE", out var args));
        Assert.Empty(args);
    }

    [Fact]
    public void Parses_activate_with_args()
    {
        Assert.True(SingleInstanceMessage.TryParse("ACTIVATE\t[\"--command\",\"7\"]", out var args));
        Assert.Equal(new[] { "--command", "7" }, args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SOMETHING")]
    [InlineData("ACTIVATE\tnot-json")]
    public void Rejects_unknown_messages(string? message)
    {
        Assert.False(SingleInstanceMessage.TryParse(message, out var args));
        Assert.Empty(args);
    }

    [Fact]
    public void Round_trips()
    {
        var original = new[] { "--command", "42" };

        Assert.True(SingleInstanceMessage.TryParse(SingleInstanceMessage.Format(original), out var parsed));
        Assert.Equal(original, parsed);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~StartupArgumentParserTests|FullyQualifiedName~SingleInstanceMessageTests"
```

期望：编译失败 `CS0246: 未能找到类型或命名空间名"StartupArgumentParser"`。

- [ ] **Step 3: 实现两个纯函数类**

`Core/Services/StartupArgumentParser.cs`：

```csharp
namespace BashCommandManager.Core.Services;

/// <summary>解析 GUI 启动参数中的 --command &lt;id&gt;（纯函数，便于单测）。</summary>
public static class StartupArgumentParser
{
    public const string CommandOption = "--command";

    public static bool TryGetCommandId(string[] args, out int commandId)
    {
        commandId = 0;
        if (args is null)
        {
            return false;
        }

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], CommandOption, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(args[index + 1], out var parsed) && parsed > 0)
            {
                commandId = parsed;
                return true;
            }

            return false;
        }

        return false;
    }
}
```

`Core/Services/SingleInstanceMessage.cs`：

```csharp
using System.Text.Json;

namespace BashCommandManager.Core.Services;

/// <summary>单实例管道消息编解码：兼容旧 "ACTIVATE" 与带参数 "ACTIVATE\t[args]"。</summary>
public static class SingleInstanceMessage
{
    public const string ActivateVerb = "ACTIVATE";
    private const char Separator = '\t';

    public static string Format(string[]? args)
        => args is { Length: > 0 } ? ActivateVerb + Separator + JsonSerializer.Serialize(args) : ActivateVerb;

    public static bool TryParse(string? message, out string[] args)
    {
        args = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var text = message.Trim();
        if (string.Equals(text, ActivateVerb, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = ActivateVerb + Separator;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            args = JsonSerializer.Deserialize<string[]>(text[prefix.Length..]) ?? Array.Empty<string>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```powershell
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj --filter "FullyQualifiedName~StartupArgumentParserTests|FullyQualifiedName~SingleInstanceMessageTests"
```

期望：`Passed! - Failed: 0`。

- [ ] **Step 5: CommandService 增加 GetByIdAsync**

`Core/Services/CommandService.cs`：接口里追加（`Task<IEnumerable<Command>> GetAllAsync();` 之后）：

```csharp
    Task<Command?> GetByIdAsync(int id);
```

实现里追加（`GetAllAsync()` 方法之后）：

```csharp
    public async Task<Command?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }
```

- [ ] **Step 6: SingleInstanceService 传参**

`Core/Services/SingleInstanceService.cs` 改动三处（其余保持原样）：

```csharp
        private Action<string[]>? _onActivateCallback;
        private string[] _startupArgs = Array.Empty<string>();

        /// <summary>
        /// 初始化单例服务；startupArgs 为本次进程启动参数，会随激活消息转发给第一个实例。
        /// </summary>
        public bool Initialize(Action<string[]> onActivateCallback, string[] startupArgs)
        {
            _onActivateCallback = onActivateCallback;
            _startupArgs = startupArgs ?? Array.Empty<string>();
            _cancellationTokenSource = new CancellationTokenSource();
            // ……以下原有逻辑逐字保留（Mutex 判定、StartPipeServer、NotifyFirstInstance）……
        }
```

管道服务里把

```csharp
                        if (message == ActivateMessage)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    _onActivateCallback?.Invoke();
                                }
```

替换为

```csharp
                        if (SingleInstanceMessage.TryParse(message, out var activateArgs))
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    _onActivateCallback?.Invoke(activateArgs);
                                }
```

`NotifyFirstInstance()` 里把 `writer.WriteLine(ActivateMessage);` 改为 `writer.WriteLine(SingleInstanceMessage.Format(_startupArgs));`，并删除不再使用的 `private const string ActivateMessage = "ACTIVATE";`。

- [ ] **Step 7: App.xaml.cs 接线**

把现有 `_singleInstanceService.Initialize(() => { ... })` 改为（保留 #8 在该 lambda 内已做的窗口激活逻辑，只在末尾追加一行）：

```csharp
        bool isFirstInstance = _singleInstanceService.Initialize(
            args =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.ApplyStartupArguments(args);
                }
            },
            e.Args);
```

并在 `mainWindow.Show();` 之后追加：

```csharp
        mainWindow.ApplyStartupArguments(e.Args);
```

- [ ] **Step 8: MainWindow 支持挂起与执行启动参数**

`MainWindow.xaml.cs` 追加字段与方法（放在 `_isExiting` 附近与类尾）：

```csharp
    private bool _viewLoaded;
    private string[]? _pendingStartupArguments;

    /// <summary>应用启动参数（--command &lt;id&gt;）；窗口未 Loaded 时先挂起。</summary>
    public void ApplyStartupArguments(string[] args)
    {
        if (!_viewLoaded)
        {
            _pendingStartupArguments = args;
            return;
        }

        _ = ApplyStartupArgumentsCoreAsync(args);
    }

    private async Task ApplyStartupArgumentsCoreAsync(string[] args)
    {
        if (DataContext is MainViewModel viewModel && StartupArgumentParser.TryGetCommandId(args, out var commandId))
        {
            await viewModel.SelectCommandAsync(commandId);
        }
    }
```

并在 `Loaded` lambda 的**最后**（`await viewModel.GroupTreeViewModel.LoadGroupsAsync();` 等原有语句之后）追加：

```csharp
            _viewLoaded = true;
            if (_pendingStartupArguments is not null)
            {
                var pending = _pendingStartupArguments;
                _pendingStartupArguments = null;
                await ApplyStartupArgumentsCoreAsync(pending);
            }
```

- [ ] **Step 9: MainViewModel.SelectCommandAsync 与 GroupTreeViewModel.FindGroupById**

`ViewModels/MainViewModel.cs`：

构造函数改为（新增 `_commandService` 字段与参数）：

```csharp
    private readonly ICommandService _commandService;

    public MainViewModel(
        GroupTreeViewModel groupTreeViewModel,
        CommandListViewModel commandListViewModel,
        ICommandService commandService)
    {
        _groupTreeViewModel = groupTreeViewModel;
        _commandListViewModel = commandListViewModel;
        _commandService = commandService;

        // ……原有 PropertyChanged 订阅逐字保留……
    }
```

并追加方法：

```csharp
    /// <summary>按 id 定位命令：选中其分组、填入搜索框并搜索（不自动执行）。</summary>
    public async Task SelectCommandAsync(int commandId)
    {
        var command = await _commandService.GetByIdAsync(commandId);
        if (command is null)
        {
            return;
        }

        var group = GroupTreeViewModel.FindGroupById(command.GroupId);
        if (group is not null)
        {
            GroupTreeViewModel.SelectedGroup = group;
        }

        SearchKeyword = command.Name;
        await SearchAsync();
        StatusText = $"已定位命令: {command.Name}";
    }
```

文件头需要 `using BashCommandManager.Core.Services;`。

`ViewModels/GroupTreeViewModel.cs` 追加：

```csharp
    /// <summary>在已加载的树中按 id 查找分组（含子分组，跳过虚拟节点）。</summary>
    public Group? FindGroupById(int id)
    {
        return Find(Groups, id);

        static Group? Find(IEnumerable<Group> nodes, int targetId)
        {
            foreach (var node in nodes)
            {
                if (!node.IsVirtual && node.Id == targetId)
                {
                    return node;
                }

                var found = Find(node.Children, targetId);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
```

- [ ] **Step 10: 构建并跑 GUI 侧测试**

```powershell
dotnet build -c Release
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
```

期望：构建 `0 个警告 0 个错误`；测试全部 Passed（`MainViewModel` 构造函数多了一个参数，DI 会自动解析 `ICommandService`——它已注册为 Scoped）。

- [ ] **Step 11: 手工验证 GUI 定位（需桌面会话）**

```powershell
Start-Process .\bin\Release\net8.0-windows10.0.19041.0\win-x64\BashCommandManager.exe
# 记下一条真实命令的 id（例如 bcm commands list --json 里的第一个 id），关闭 GUI 到托盘后再执行：
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe open --command <实际命令 id>
```

期望：GUI 从托盘恢复、切到命令 1 所在分组、搜索框填入该命令名、列表只剩该命令；任务管理器里 `BashCommandManager.exe` 只有 1 个进程（验收 9）。

- [ ] **Step 12: 提交**

```powershell
git add Core\Services\StartupArgumentParser.cs Core\Services\SingleInstanceMessage.cs Core\Services\SingleInstanceService.cs Core\Services\CommandService.cs App.xaml.cs MainWindow.xaml.cs ViewModels\MainViewModel.cs ViewModels\GroupTreeViewModel.cs tests\BashCommandManager.Tests\Core
git commit -m "feat(cli): GUI 支持 --command 定位并经单实例管道转发参数"
```

---

### Task 25: 端到端冒烟测试（真实进程 + 临时数据库 + 立即关闭 stdin）

**Files:**
- Create: `tests/BashCommandManager.Cli.Tests/TestSupport/CliProcess.cs`
- Test: `tests/BashCommandManager.Cli.Tests/Smoke/CliSmokeTests.cs`

**Interfaces:**
- Consumes: 已构建的 `bcm.exe`（路径由 `RepoRoot` + 配置推导）
- Produces:
  - `internal sealed record CliProcessResult(int ExitCode, string Stdout, string Stderr)`
  - `internal static class CliProcess { public static string RepoRoot; public static string CliExecutablePath; public static CliProcessResult Run(string[] args, string? settingsDirectory = null, int timeoutMs = 30000); }`

- [ ] **Step 1: 写测试支持与失败测试**

`tests/BashCommandManager.Cli.Tests/TestSupport/CliProcess.cs`：

```csharp
using System.Diagnostics;
using System.Text;

namespace BashCommandManager.Cli.Tests.TestSupport;

internal sealed record CliProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>起真实 bcm.exe；始终关闭 stdin，默认 30 秒超时。</summary>
internal static class CliProcess
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string CliExecutablePath
    {
        get
        {
            var baseDirectory = Path.Combine(RepoRoot, "BashCommandManager.Cli", "bin", Configuration);
            foreach (var candidate in new[]
                     {
                         Path.Combine(baseDirectory, "net8.0-windows", "bcm.exe"),
                         Path.Combine(baseDirectory, "net8.0-windows", "win-x64", "bcm.exe")
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(
                $"未找到 bcm.exe（{baseDirectory}）。先执行 dotnet build BashCommandManager.Cli\\BashCommandManager.Cli.csproj -c {Configuration}");
        }
    }

    public static CliProcessResult Run(string[] args, string? settingsDirectory = null, int timeoutMs = 30000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CliExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = RepoRoot
        };

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Remove("BASHCOMMANDMANAGER_DB");
        startInfo.Environment.Remove("BASHCOMMANDMANAGER_GUI");
        if (settingsDirectory is not null)
        {
            startInfo.Environment["BASHCOMMANDMANAGER_SETTINGS_DIR"] = settingsDirectory;
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动 bcm.exe");

        // 验收 7：stdin 立即关闭
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            throw new TimeoutException($"bcm 未在 {timeoutMs}ms 内退出: {string.Join(' ', args)}");
        }

        return new CliProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BashCommandManager.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录（BashCommandManager.csproj）");
    }
}
```

`tests/BashCommandManager.Cli.Tests/Smoke/CliSmokeTests.cs`：

```csharp
using BashCommandManager.Cli.Tests.TestSupport;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Smoke;

public class CliSmokeTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    [Fact]
    public void Smoke1_groups_list_creates_temp_database()
    {
        using var temp = new TempDatabase();

        var result = CliProcess.Run(new[] { "--db", temp.DatabasePath, "groups", "list", "--json" });

        Assert.Equal(0, result.ExitCode);
        var root = Json(result.Stdout);
        Assert.True(root["meta"]!["created"]!.GetValue<bool>());
        Assert.Equal("根分组", root["data"]![0]!["name"]!.GetValue<string>());
        Assert.True(File.Exists(temp.DatabasePath));
    }

    [Fact]
    public void Smoke2_full_group_and_command_lifecycle()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("t.cmd");

        Assert.Equal(0, CliProcess.Run(new[] { "groups", "create", "--name", "运维", "--db", temp.DatabasePath, "--json" }).ExitCode);
        var added = CliProcess.Run(new[] { "commands", "add", "--name", "t", "--file", script, "--group", "运维", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(0, added.ExitCode);
        var commandId = Json(added.Stdout)["data"]!["id"]!.GetValue<int>();

        var list = CliProcess.Run(new[] { "commands", "list", "--json", "--db", temp.DatabasePath });
        Assert.Contains("t", list.Stdout);

        Assert.Equal(0, CliProcess.Run(new[] { "groups", "create", "--name", "测试", "--db", temp.DatabasePath, "--json" }).ExitCode);
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "move", commandId.ToString(), "--to", "测试", "--db", temp.DatabasePath, "--json" }).ExitCode);
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "delete", commandId.ToString(), "--force", "--db", temp.DatabasePath, "--json" }).ExitCode);

        var after = CliProcess.Run(new[] { "commands", "list", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(0, after.ExitCode);
        Assert.Empty(Json(after.Stdout)["data"]!.AsArray());
    }

    [Fact]
    public void Smoke3_dry_run_wait_exit_code_and_not_found()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("fail.cmd", "@echo off\r\nexit /b 7\r\n");
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "add", "--name", "fail", "--file", script, "--db", temp.DatabasePath, "--json" }).ExitCode);
        var id = Json(CliProcess.Run(new[] { "commands", "list", "--db", temp.DatabasePath, "--json" }).Stdout)["data"]![0]!["id"]!.GetValue<int>();

        var dryRun = CliProcess.Run(new[] { "run", id.ToString(), "--dry-run", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(0, dryRun.ExitCode);
        Assert.Equal("cmd.exe", Json(dryRun.Stdout)["data"]!["fileName"]!.GetValue<string>());
        Assert.Contains(script, Json(dryRun.Stdout)["data"]!["arguments"]!.GetValue<string>());

        var wait = CliProcess.Run(new[] { "run", id.ToString(), "--wait", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(3, wait.ExitCode);
        Assert.Equal("E_COMMAND_FAILED", Json(wait.Stderr)["error"]!["code"]!.GetValue<string>());
        Assert.Equal(7, Json(wait.Stderr)["error"]!["details"]!["exitCode"]!.GetValue<int>());

        var missing = CliProcess.Run(new[] { "run", "9999", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(2, missing.ExitCode);
        Assert.Equal("E_NOT_FOUND", Json(missing.Stderr)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Smoke4_wait_forwards_child_output_to_stderr_and_keeps_json_clean()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("echo.cmd", "@echo off\r\necho marker\r\nexit /b 0\r\n");
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "add", "--name", "echo", "--file", script, "--db", temp.DatabasePath, "--json" }).ExitCode);
        var id = Json(CliProcess.Run(new[] { "commands", "list", "--db", temp.DatabasePath, "--json" }).Stdout)["data"]![0]!["id"]!.GetValue<int>();

        var result = CliProcess.Run(new[] { "run", id.ToString(), "--wait", "--db", temp.DatabasePath, "--json" });

        Assert.Equal(0, result.ExitCode);
        var root = Json(result.Stdout); // stdout 必须仍是干净 JSON
        Assert.Equal(0, root["data"]!["exitCode"]!.GetValue<int>());
        Assert.Contains("marker", result.Stderr);
        Assert.DoesNotContain("marker", result.Stdout);
    }

    [Fact]
    public void Smoke5_csv_export_writes_fixed_header()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("e.cmd");
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "add", "--name", "e", "--file", script, "--db", temp.DatabasePath, "--json" }).ExitCode);
        var output = Path.Combine(temp.Root, "export.csv");

        var result = CliProcess.Run(new[] { "commands", "export", "--format", "csv", "--output", output, "--db", temp.DatabasePath, "--json" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, Json(result.Stdout)["data"]!["count"]!.GetValue<int>());
        var lines = File.ReadAllLines(output);
        Assert.Equal("id,name,description,filePath,groupId,groupName,sortOrder,executionCount,lastExecutedAt", lines[0]);
        Assert.Contains("e", lines[1]);
    }

    [Fact]
    public void Smoke6_delete_without_force_exits_one()
    {
        using var temp = new TempDatabase();

        var result = CliProcess.Run(new[] { "groups", "delete", "根分组", "--db", temp.DatabasePath, "--json" });

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("E_CONFIRM_REQUIRED", Json(result.Stderr)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Smoke7_config_set_hotkey_writes_settings_in_temp_directory()
    {
        using var temp = new TempDatabase();
        var settingsDirectory = temp.CreateDirectory("settings");

        var set = CliProcess.Run(new[] { "config", "set", "hotkey", "Ctrl+Shift+N", "--json" }, settingsDirectory);
        Assert.Equal(0, set.ExitCode);
        Assert.Equal(57, Json(set.Stdout)["data"]!["hotkey"]!["key"]!.GetValue<int>());

        var get = CliProcess.Run(new[] { "config", "get", "hotkey", "--json" }, settingsDirectory);
        Assert.Equal("Ctrl+Shift+N", Json(get.Stdout)["data"]!["hotkey"]!["display"]!.GetValue<string>());

        var settingsJson = JsonObject.Parse(File.ReadAllText(Path.Combine(settingsDirectory, "settings.json")))!.AsObject();
        Assert.Equal(57, settingsJson["Hotkey"]!["Key"]!.GetValue<int>());
        Assert.Equal(6, settingsJson["Hotkey"]!["Modifiers"]!.GetValue<int>());
    }

    [Fact]
    public void Smoke8_help_is_fast_and_lists_all_commands()
    {
        var result = CliProcess.Run(new[] { "--help" });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bcm commands export", result.Stdout);
        Assert.Contains("bcm run", result.Stdout);
    }
}
```

- [ ] **Step 2: 跑冒烟测试（先构建 CLI Release）**

```powershell
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~CliSmokeTests"
```

期望：8 条全 Passed；若报 `未找到 bcm.exe`，说明 CLI 未按 `Release` 构建（测试用 `#if DEBUG` 推断配置）。

- [ ] **Step 3: 断言不依赖真实安装库**

```powershell
# 确认本机真实旧库未被修改（时间戳不变）
Get-Item 'D:\Program Files\BashCommandManager\data\app.db' | Select-Object FullName, Length, LastWriteTime
```

期望：`LastWriteTime` 仍为迁移前的时间（本步骤只是人工核对，不写入任何文件）。

- [ ] **Step 4: 提交**

```powershell
git add tests\BashCommandManager.Cli.Tests
git commit -m "test(cli): 8 条端到端冒烟覆盖退出码、stdin、CSV 与配置"
```

---

### Task 26: `.claude/skills/bash-command-manager/SKILL.md`

**Files:**
- Create: `.claude/skills/bash-command-manager/SKILL.md`

**Interfaces:**
- Consumes: `bcm --help` 的 19 条命令与真实 JSON 输出（T12–T23 已实现）
- Produces: 供 AI 代理加载的操作说明（frontmatter 仅 `name` + `description`）

- [ ] **Step 1: 生成真实输出，作为文档示例素材**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-skill-" + [guid]::NewGuid())
$env:BASHCOMMANDMANAGER_SETTINGS_DIR = "$tmp\settings"
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content -Path "$tmp\备份.cmd" -Value "@echo off`r`nexit /b 0" -Encoding ascii
& $exe groups list --db "$tmp\app.db" --json
& $exe groups create --name 运维 --db "$tmp\app.db" --json
& $exe commands add --name 备份 --file "$tmp\备份.cmd" --description 备份库 --group 运维 --db "$tmp\app.db" --json
& $exe commands list --db "$tmp\app.db" --json
& $exe run 备份 --wait --db "$tmp\app.db" --json
& $exe run 9999 --db "$tmp\app.db" --json
Remove-Item -Recurse -Force $tmp
Remove-Item Env:\BASHCOMMANDMANAGER_SETTINGS_DIR
```

期望：把 6 条输出原样抄进 SKILL.md 的「JSON 示例」段（含中文与 `dbPath`，不得手写字段名）。

- [ ] **Step 2: 写 SKILL.md（完整内容）**

`.claude/skills/bash-command-manager/SKILL.md`：

```markdown
---
name: bash-command-manager
description: Use when an agent needs to list, search, create, edit, move, delete or run BashCommandManager batch commands, manage groups, change the global hotkey, or open the BashCommandManager GUI on Windows via the bcm CLI
---

# BashCommandManager CLI（bcm）

用 `bcm` 读写本机的 BashCommandManager 数据：分组、批处理命令、执行统计、全局快捷键，以及打开 GUI。

## When to Use

- 用户要求「列出/搜索/新增/修改/移动/删除」批处理命令或分组。
- 用户要求运行某个已注册的 `.bat/.cmd`（例如“跑一下备份”）。
- 用户要求查看或修改全局快捷键。
- 用户要求「打开 BashCommandManager 并定位到某条命令」。

## When Not to Use

- 执行**未注册**的任意命令（本 CLI 只能运行库里已有的命令）。
- 直接编辑 SQLite 文件、`settings.json` 之外的 GUI 配置（`app.ini`、注册表等）。
- 修改数据库 schema、做数据修复（没有这类子命令）。

## Locating the CLI

按顺序取第一个存在的路径：

1. `Get-Command bcm`（PATH 命中的就是它）。
2. 卸载项 `InstallLocation` + `\cli\bcm.exe`：

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' |
  Where-Object DisplayName -eq 'BashCommandManager' | Select-Object -ExpandProperty InstallLocation
```

3. `%ProgramFiles%\BashCommandManager\cli\bcm.exe`
4. 仓库产物：`BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe`

都找不到时：提示用户安装 BashCommandManager，或用 `--gui <BashCommandManager.exe>` 指定 GUI 路径、设 `BASHCOMMANDMANAGER_GUI`。

## Global Conventions

- **始终加 `--json`**：成功 JSON 在 stdout，失败 JSON 在 stderr。
- 检查退出码：`0` 成功、`1` 参数错误、`2` 未找到、`3` 运行时失败。
- 只在用户明确指定库文件时用 `--db <path>`；否则用默认库。
- 用 `meta.dbPath` 确认「同一个库」；`meta.created=true` 表示这次运行刚建了空库。
- 删除类命令必须显式 `--force`，否则退出 1（`E_CONFIRM_REQUIRED`）。
- 名称不唯一会返回 `E_AMBIGUOUS` 与候选 id，改用 id 重试，不要猜。

## Cheat Sheet（19 条）

| 目的 | 命令 |
|---|---|
| 列出分组 | `bcm groups list --json` |
| 树形分组 | `bcm groups list --tree --json` |
| 新建分组 | `bcm groups create --name 运维 --parent 1 --json` |
| 重命名分组 | `bcm groups rename 2 --name 生产运维 --json` |
| 删除分组（级联） | `bcm groups delete 运维 --force --json` |
| 列出命令 | `bcm commands list --json` |
| 按分组+排序 | `bcm commands list --group 运维 --sort count --order desc --json` |
| 搜索 | `bcm commands search 备份 --json` |
| 查看单条 | `bcm commands show 7 --json` |
| 新增 | `bcm commands add --name 备份 --file "D:\bat\备份.bat" --group 运维 --json` |
| 编辑 | `bcm commands edit 7 --description "每日 2:00" --json` |
| 删除 | `bcm commands delete 7,8 --force --json` |
| 移动 | `bcm commands move 7,8 --to 生产运维 --json` |
| 导入目录 | `bcm commands import --group 运维 "D:\bat" --recursive --json` |
| 导出 JSON | `bcm commands export --format json --output backup.json --json` |
| 导出 CSV | `bcm commands export --format csv --output backup.csv --json` |
| 常用命令 | `bcm commands frequent --limit 5 --json` |
| 运行 | `bcm run 备份 --wait --json` |
| 打开 GUI 并定位 | `bcm open --command 7 --json` |
| 读配置 | `bcm config get --json` / `bcm config get db.path --json` |
| 改快捷键 | `bcm config set hotkey "Ctrl+Shift+N" --json` |
| 版本 | `bcm --version --json` |

## JSON Examples（真实输出）

`bcm groups list --json`：

```json
{"ok":true,"data":[{"id":1,"name":"根分组","parentId":null,"sortOrder":0,"commandCount":1}],"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"count":1,"durationMs":12}}
```

`bcm commands list --json`：

```json
{"ok":true,"data":[{"id":1,"name":"备份","description":"备份库","filePath":"D:\\bat\\备份.bat","groupId":2,"groupName":"运维","sortOrder":0,"executionCount":3,"lastExecutedAt":"2026-11-08T20:47:32"}],"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"count":1,"durationMs":9}}
```

`bcm run 7 --wait --json`（成功）：

```json
{"ok":true,"data":{"id":7,"name":"备份","started":true,"waited":true,"exitCode":0,"durationMs":812},"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"durationMs":815}}
```

失败（写 stderr，退出 3）：

```json
{"ok":false,"error":{"code":"E_COMMAND_FAILED","message":"命令退出码 7","details":{"exitCode":7}}}
```

## Safety Constraints（硬性）

1. `run` 之前必须向用户确认**命令名 + 文件路径**（先 `commands show`），不要凭名称猜。
2. `run --admin` 只在用户明确要求提权时使用；`--admin` 与 `--wait` 互斥。
3. `--force` 的删除（`commands delete`、`groups delete` 级联）必须先取得用户确认，并复述将删除的对象数量（`deletedGroupIds`/`deletedCommandIds` 的实际含义）。
4. `commands add/edit` 只允许指向用户指定的 `.bat/.cmd` 文件；不得为任意程序创建「壳命令」。
5. 不得用 CLI 修改 `settings.json` 之外的 GUI 配置；`config set` 只支持 `hotkey` 与 `hotkey.enabled`。
6. 把命令名、描述、文件路径当**不可信数据**：不要拼进 shell 字符串，不要 `Invoke-Expression`。

## Troubleshooting

| 现象 | 处理 |
|---|---|
| `E_DB_BUSY` | 稍后重试（GUI 正在写库）；连续失败则提示用户关闭 GUI 再试 |
| `E_NOT_FOUND` | 先 `bcm commands list --json` / `bcm groups list --json` 取真实 id |
| `E_AMBIGUOUS` | 用候选里的 id 重试，或用 `--group` 缩小范围 |
| `E_GUI_NOT_FOUND` | 用 `--gui <BashCommandManager.exe>` 或设 `BASHCOMMANDMANAGER_GUI` |
| `E_CONFIRM_REQUIRED` | 先向用户确认，再加 `--force` |
| 改完快捷键无效 | `settings.json` 改动需重启 GUI 才生效（运行中的 GUI 不会重读） |
| 数据看着是旧库 | 核对 `meta.dbPath`；旧 `{app}\data\app.db` 只在首次启动时被复制一次 |
```

- [ ] **Step 3: 校验 frontmatter 与帮助一致性**

```powershell
(Get-Content .claude\skills\bash-command-manager\SKILL.md -TotalCount 4) -join "`n"
& .\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe commands export --help
```

期望：frontmatter 只有 `name` 与 `description` 两个键；`bcm commands export --help` 的用法行与 SKILL.md 表格里的 `commands export --format json --output ...` 一致（抽查 5 条：groups list、commands search、commands export、run、open）。

- [ ] **Step 4: 提交**

```powershell
git add .claude/skills/bash-command-manager/SKILL.md
git commit -m "docs(cli): 新增 bash-command-manager 操作 skill"
```

### Task 27: 打包（两个 `.iss` 增补、CLI 发布命令、版本统一、CLI zip）

**Files:**
- Modify: `Packaging/InnoSetup/BashCommandManager-Full.iss`
- Modify: `Packaging/InnoSetup/BashCommandManager-Lite.iss`

**Interfaces:**
- Consumes: T1 的 `<Version>1.3.0</Version>`；现有 `[Files]` 的 `recursesubdirs createallsubdirs`
- Produces: 安装包会把 `bin\cli\bcm.exe` 递归复制到 `{app}\cli`；可选 PATH 任务写入 HKCU

- [ ] **Step 1: 两个 `.iss` 各改三处（内容完全相同）**

`#define MyAppVersion "1.2.1"` → `#define MyAppVersion "1.3.0"`。

`[Setup]` 段（放在 `ArchitecturesInstallIn64BitMode=x64` 之后）加一行：

```pascal
ChangesEnvironment=yes
```

`[Tasks]` 段（`desktopicon` 之后）加：

```pascal
Name: "addtocli"; Description: "Add bcm CLI to PATH"; GroupDescription: "Command line:"; Flags: unchecked
```

`[Files]` 之后新增 `[Registry]` 段（若文件已有 `[Registry]` 则复用该段）：

```pascal
[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\cli"; Tasks: addtocli; Flags: preservestringtype
```

> `[Files]` 段**不要改**：`Source: "Full\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs`（Lite 同理，源为 `Lite\bin\*`）会把 `bin\cli\*` 递归复制到 `{app}\cli`。

- [ ] **Step 2: 发布两个 CLI 版本**

```powershell
dotnet publish BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o Packaging\InnoSetup\Full\bin\cli
dotnet publish BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o Packaging\InnoSetup\Lite\bin\cli
```

期望：两个目录都有 `bcm.exe`；Full 的 `bcm.exe` 约 60-80MB（自包含），Lite 的约 1MB 以内（框架依赖）。

- [ ] **Step 3: 验证单文件自包含产物可运行**

```powershell
& .\Packaging\InnoSetup\Full\bin\cli\bcm.exe --version
& .\Packaging\InnoSetup\Lite\bin\cli\bcm.exe --version
Test-Path .\Packaging\InnoSetup\Full\bin\cli\bcm.exe
Test-Path .\Packaging\InnoSetup\Lite\bin\cli\bcm.exe
```

期望：两次都输出 `1.3.0`；`Test-Path` 均为 `True`。若运行时报 `SQLite.Interop.dll` 加载失败，确认 csproj 含 `IncludeNativeLibrariesForSelfExtract=true`（T1 已加）。

- [ ] **Step 4: 打包 CLI zip**

```powershell
New-Item -ItemType Directory -Force -Path bin\Publish\Installer | Out-Null
Compress-Archive -Path Packaging\InnoSetup\Full\bin\cli\* -DestinationPath bin\Publish\Installer\BashCommandManager-Cli-v1.3.0-win-x64.zip -Force
Get-Item bin\Publish\Installer\BashCommandManager-Cli-v1.3.0-win-x64.zip | Select-Object Name, Length
```

期望：zip 存在，`Length` 约 20-30MB。

- [ ] **Step 5: 提交**

```powershell
git add Packaging\InnoSetup\BashCommandManager-Full.iss Packaging\InnoSetup\BashCommandManager-Lite.iss
git commit -m "feat(cli): 安装包加入可选 PATH 任务并把版本统一到 1.3.0"
```

---

### Task 28: 文档同步（PACKAGING / CLAUDE / README）

**Files:**
- Modify: `docs/PACKAGING.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: T27 的发布命令与产物命名
- Produces: 文档中不再出现「数据存储在安装目录 data/app.db」的过时说法

- [ ] **Step 1: `docs/PACKAGING.md` 三处增补**

（a）「打包概述」表格下加一行说明：

```markdown
两个安装包都会把 `bcm.exe`（CLI）随附到 `{app}\cli`；安装时可选勾选「Add bcm CLI to PATH」（写 HKCU 的 Path，卸载不自动移除）。
```

（b）「打包步骤」在 Lite 之后新增一节：

```markdown
### 4. 发布 CLI（bcm.exe）

```bash
# Full（自包含单文件）
dotnet publish BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o Packaging/InnoSetup/Full/bin/cli

# Lite（框架依赖单文件）
dotnet publish BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o Packaging/InnoSetup/Lite/bin/cli
```

两个 `[Files]` 段已有 `recursesubdirs createallsubdirs`，`cli` 目录会随 `bin\*` 递归复制到 `{app}\cli`，无需改 `.iss`。

验证：

```bash
./Packaging/InnoSetup/Full/bin/cli/bcm.exe --version   # 1.3.0
```

可另发一个 zip 给无 GUI 的机器：

```powershell
Compress-Archive -Path Packaging\InnoSetup\Full\bin\cli\* -DestinationPath bin\Publish\Installer\BashCommandManager-Cli-v1.3.0-win-x64.zip -Force
```
```

（c）把「注意事项」里的过时说法替换掉：

原文 `- 数据存储在应用安装目录的 data/app.db 中` 改为：

```markdown
- 数据库位于 `%LOCALAPPDATA%\BashCommandManager\data\app.db`；首次启动会把旧版安装目录下的 `data/app.db` **复制**过去（旧文件保留，可回滚）
- 可用环境变量 `BASHCOMMANDMANAGER_DB` 指向其他库（便携/回滚），或用 `bcm --db <path>` 单次覆盖
```

- [ ] **Step 2: `CLAUDE.md` 两处增补**

（a）「数据持久化」段把「SQLite 数据库存储在应用目录 `data/app.db`」改为：

```markdown
SQLite 数据库存储在 `%LOCALAPPDATA%\BashCommandManager\data\app.db`（`Infrastructure/DatabasePathResolver.cs` 统一解析）：
- 旧位置 `{应用目录}\data\app.db` 仅在首次解析时作为迁移源，**只复制不删除**
- 迁移失败会回退旧库并只写 `Debug.WriteLine`；`BASHCOMMANDMANAGER_DB` 可覆盖路径
- 连接串含 `Foreign Keys=True`，删除分组会级联删除命令
```

（b）「常用命令」段追加：

```bash
# CLI 构建与测试
dotnet build BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release
dotnet test tests/BashCommandManager.Cli.Tests/BashCommandManager.Cli.Tests.csproj

# 运行 CLI
./BashCommandManager.Cli/bin/Release/net8.0-windows/bcm.exe --help
```

并在文件末尾「开发计划」下追加一行：

```markdown
CLI（`bcm`）覆盖 `groups/commands/run/config/open` 共 19 条子命令，操作说明见 `.claude/skills/bash-command-manager/SKILL.md`。
```

- [ ] **Step 3: `README.md` 三处增补**

（a）「安装说明」之后新增：

```markdown
### 命令行（bcm）

安装包会把 CLI 放在 `{安装目录}\cli\bcm.exe`，首次安装可勾选「Add bcm CLI to PATH」。

```bash
bcm --help
bcm groups list --json
bcm commands search 备份 --json
bcm run 备份 --wait
bcm open --command 7
```

数据位置：`%LOCALAPPDATA%\BashCommandManager\data\app.db`（旧版安装目录下的 `data\app.db` 会在首次启动时自动复制过来）。
```

（b）「项目结构」树里追加：

```markdown
├── BashCommandManager.Cli/     # bcm CLI（源码链接 Core 的 WPF-free 子集）
```

（c）版本相关：确认 README 中的版本号/发布说明为 `1.3.0`（若出现 `1.2.1` 一并改）。

- [ ] **Step 4: 校验无过时路径残留**

```powershell
Select-String -Path README.md,CLAUDE.md,docs\PACKAGING.md -Pattern 'data/app.db','data\\app.db' | Select-Object Path, LineNumber, Line
Select-String -Path README.md,CLAUDE.md,docs\PACKAGING.md,Packaging\InnoSetup\*.iss,BashCommandManager.Cli\BashCommandManager.Cli.csproj -Pattern '1\.2\.1'
```

期望：第一条只匹配到「旧位置/迁移源」的说明性文字（不再有「数据存储在安装目录」的表述）；第二条无输出（版本已全部为 1.3.0）。

- [ ] **Step 5: 提交**

```powershell
git add README.md CLAUDE.md docs\PACKAGING.md
git commit -m "docs(cli): 同步 CLI 用法、数据位置与打包说明"
```

---

### Task 29: 全量验证（构建 / 测试 / 冒烟 / 发布 / 一致性对照）

**Files:** 无新增，只跑命令并记录结果

**Interfaces:**
- Consumes: 前 28 个任务的全部交付物
- Produces: 验收证据（可贴进 PR 说明）

- [ ] **Step 1: GUI 全量构建与测试**

```powershell
dotnet build -c Release
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
```

期望：构建 `0 个警告 0 个错误`；测试全部 Passed。

- [ ] **Step 2: CLI 构建与全量测试**

```powershell
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj -c Release
```

期望：`0 个警告 0 个错误`；单元测试 + 8 条冒烟全部 Passed。

- [ ] **Step 3: 验收 1——GUI 发布产物不含 CLI 与测试源码**

```powershell
$out = Join-Path $env:TEMP ("bcm-gui-publish-" + [guid]::NewGuid())
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o $out
Test-Path "$out\BashCommandManager.exe"
Test-Path "$out\bcm.exe"
Get-ChildItem $out -Filter "xunit*" -Recurse | Measure-Object | Select-Object Count
Get-ChildItem $out -Filter "bcm.dll" -Recurse | Measure-Object | Select-Object Count
Remove-Item -Recurse -Force $out
```

期望：`BashCommandManager.exe` True；`bcm.exe` False；`xunit*` 计数 0；`bcm.dll` 计数 0。

- [ ] **Step 4: 验收 3——GUI 与 CLI 的数据库路径一致**

```powershell
Remove-Item Env:\BASHCOMMANDMANAGER_DB -ErrorAction SilentlyContinue
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
$expected = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BashCommandManager\data\app.db'
$actual = (& $exe config get db.path --json | ConvertFrom-Json).data.db.path
"expected: $expected"
"actual:   $actual"
if ($actual -ne $expected) { throw "路径不一致" }
```

期望：两行路径完全相同，且脚本不抛异常；随后确认真实旧库仍在：

```powershell
Get-Item 'D:\Program Files\BashCommandManager\data\app.db' | Select-Object FullName, Length, LastWriteTime
```

- [ ] **Step 5: `--dry-run` 与 GUI 命令行一致性对照**

```powershell
$tmp = Join-Path $env:TEMP ("bcm-dry-" + [guid]::NewGuid())
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Set-Content -Path "$tmp\a.cmd" -Value "@echo off`r`nexit /b 0" -Encoding ascii
$exe = ".\BashCommandManager.Cli\bin\Release\net8.0-windows\bcm.exe"
& $exe commands add --name a --file "$tmp\a.cmd" --db "$tmp\app.db" --json | Out-Null
$plan = (& $exe run a --dry-run --db "$tmp\app.db" --json | ConvertFrom-Json).data
$plan | Format-List
if ($plan.fileName -ne 'cmd.exe') { throw "fileName 不一致" }
if ($plan.arguments -ne ('/k "' + (Join-Path $tmp 'a.cmd') + '"')) { throw "arguments 不一致" }
if ($plan.workingDirectory -ne $tmp) { throw "workingDirectory 不一致" }
"GUI 等价断言: TerminalCommandBuilder.GetDefaultTemplate(Cmd, keepWindowOpen:true) == '/k {file}'"
Remove-Item -Recurse -Force $tmp
```

期望：`fileName: cmd.exe`、`arguments: /k "<tmp>\a.cmd"`、`workingDirectory: <tmp>`；三条断言都不抛异常。GUI 侧同源证明：`tests/BashCommandManager.Tests/TerminalCommandBuilderTests`（#9 的 B1 用例）断言 `cmd.exe` + `/k "C:\work\demo.bat"`，与本 CLI 输出同一模板。

- [ ] **Step 6: 验收 2——CLI 发布产物与安装包编译**

```powershell
dotnet publish BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o Packaging\InnoSetup\Full\bin\cli
& .\Packaging\InnoSetup\Full\bin\cli\bcm.exe --version
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Packaging\InnoSetup\BashCommandManager-Full.iss
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Packaging\InnoSetup\BashCommandManager-Lite.iss
Get-ChildItem bin\Publish\Installer\*Setup.exe | Select-Object Name, Length
```

期望：`--version` 输出 `1.3.0`；两次 ISCC 均 `Successful compile`；两个 `*-Setup.exe` 存在且时间是刚生成的（Lite 编译前需已完成 Lite 的 `dotnet publish`，见 docs/PACKAGING.md）。若本机 Inno Setup 不在 `C:\Program Files (x86)\Inno Setup 6\`，先用 `Get-Command ISCC.exe -ErrorAction SilentlyContinue` 或 `Get-ChildItem 'C:\Program Files*\Inno Setup*\ISCC.exe'` 找到实际路径再替换命令。

- [ ] **Step 7: 版本一致性核对（验收 10 的一部分）**

```powershell
Select-String -Path BashCommandManager.Cli\BashCommandManager.Cli.csproj -Pattern '<Version>'
Select-String -Path Packaging\InnoSetup\*.iss -Pattern 'MyAppVersion'
Select-String -Path README.md -Pattern '1\.3\.0','1\.2\.1'
```

期望：csproj 为 `1.3.0`；两个 `.iss` 都是 `1.3.0`；README 无 `1.2.1`。

- [ ] **Step 8: 手工验收（需桌面会话，逐条记录结果）**

| 验收 spec 条目 | 操作 | 期望 |
|---|---|---|
| 4 / 退出码 | 依次跑 `bcm bogus`、`bcm commands show 9999`、`bcm run 不存在` | 1 / 2 / 2 |
| 5 | `bcm groups delete 根分组`（无 `--force`）与 `bcm groups delete 1 --force` | 分别是 `E_CONFIRM_REQUIRED`（1）与 `E_ROOT_GROUP`（1） |
| 6 | `bcm groups create --name 中文分组 --json`，再 `groups list --json` | 中文原样往返，无 `\uXXXX` |
| 7 | `echo. | bcm groups list --json`（stdin 有内容） | 不读 stdin，正常退出 0 |
| 8 | `bcm groups delete 某分组 --force --json` | `deletedCommandIds` 数量与该分组+子分组实际命令数一致 |
| 9 | GUI 未运行 / 已运行各执行一次 `bcm open --command <id>` | 两种情况都能定位，任务管理器只有 1 个 GUI 进程 |
| 10 | 抽查 `bcm --help` 与 SKILL.md 的 5 条示例 | 用法与参数完全一致 |

- [ ] **Step 9: 提交（仅当 Step 1-8 修了问题时；无改动就跳过）**

```powershell
git status --short
# 若上面有输出（例如版本号/路径修正），把改动文件逐个加入后提交：
git add <改动的文件>
git commit -m "chore(cli): 修正 v1.3.0 全量验证中发现的问题"
```

无论是否提交，都要把 Step 1-8 的「命令 / 期望 / 实际」三列贴进 PR 描述（`docs/superpowers/README.md` 要求 verification-before-completion 证据附在 PR/Release 说明里）。**不要** `git add -A`。

## Self-Review

### 1. Spec 目标覆盖（`2026-11-08-cli-and-skill-design.md` 第「目标」节，实际 5 条）

| 目标 | 落地任务 | 证据 |
|---|---|---|
| G1 独立 CLI 项目产出 `bcm.exe`，覆盖 groups/commands/run/config/open | T1（项目）、T2/T3（解析）、T12–T23（19 条命令逐条实现） | `CommandCatalog.Commands.Count == 19`（T1 测试）+ 各命令处理器与单测 |
| G2 GUI 与 CLI 共用 `DatabasePathResolver`/`DatabaseMigrator` | T5、T6、T7、T12 | `DatabasePathResolverTests`/`DatabaseMigratorTests`/`DatabaseInitializerTests`；`CliHost` 走同一解析器 |
| G3 `--json` 契约与 0/1/2/3 退出码、UTF-8、无交互 | T1（错误码）、T4（信封/人类可读）、T25（退出码冒烟） | `ExitCodesTests`、`JsonEnvelopeTests`、`CliSmokeTests` 第 3/6 条；测试辅助统一关闭 stdin |
| G4 `SKILL.md`（frontmatter 仅 name+description） | T26 | 文件内容 + 校验步骤 |
| G5 随主安装包发布、`.iss` 最小增补、CLI 可单测 | T27、T29 | `[Tasks]/[Registry]` 增补、`recursively` 复制验证、CLI 测试项目 |

### 2. 验收标准覆盖（spec 第「验收标准」节，实际 10 条）

| 验收 | 落地任务 | 验证方式 |
|---|---|---|
| 1 GUI publish 无 `bcm.exe`、不编译 CLI/测试源码 | T1（排除项）、T29 步骤 3 | `dotnet publish` 后断言 `bcm.exe` 不存在、`xunit*`/`bcm.dll` 计数 0 |
| 2 CLI 发布产出 `bcm.exe`、Full 自包含可运行 | T27、T29 步骤 6 | publish 两步 + `bcm --version` |
| 3 GUI/CLI 的 `db.path` 完全相同；旧库仍在 | T5、T6、T29 步骤 4 | `config get db.path` 与 `%LOCALAPPDATA%` 比对；旧库时间戳核对 |
| 4 19 条命令可执行且字段一致 | T12–T23、T25 | 每条命令的 JSON 断言测试 + 冒烟 |
| 5 退出码 0/1/2/3 | T1、T3、T25 步骤 3 | `ExitCodesTests`、`ParserValidationTests`、`CliSmokeTests` |
| 6 中文往返无损、UTF-8 无 BOM | T4、T10、T19、T25 | `JsonEnvelopeTests.Chinese_is_not_escaped`、`Writes_utf8_without_bom`、CSV 无 BOM 断言 |
| 7 stdin 立即关闭、30s 内结束、无弹窗 | T25 `CliProcess` | 每次启动都 `StandardInput.Close()` + 30s 超时 |
| 8 删除需 `--force`；级联数量真实 | T3、T13、T16、T25 | `E_CONFIRM_REQUIRED` 测试 + `deletedCommandIds` 断言 |
| 9 `open --command` 两种情况都定位、不开第二窗口 | T23、T24 | `OpenCommandTests` + T24 步骤 11 手工验收 |
| 10 SKILL.md 存在且与帮助一致；测试全绿；文档更新 | T26、T28、T29 | frontmatter 检查、抽查 5 条、`dotnet test`、文档 grep |

### 3. 命令表逐行核对（spec 第 3 节 19 行 → 任务/处理器/测试）

| # | 命令 | 任务 | 处理器方法 | 测试文件 |
|---|---|---|---|---|
| 1 | `groups list [--tree]` | T12 | `GroupCommands.ListAsync` | `GroupCommandsTests` |
| 2 | `groups create --name [--parent]` | T12 | `GroupCommands.CreateAsync` | `GroupCommandsTests` |
| 3 | `groups rename <id\|path> --name` | T13 | `GroupCommands.RenameAsync` | `GroupCommandsTests` |
| 4 | `groups delete <id\|path> --force` | T13 | `GroupCommands.DeleteAsync` | `GroupCommandsTests` |
| 5 | `commands list [--group --sort --order --limit]` | T14 | `CommandCommands.ListAsync` | `CommandCommandsTests` |
| 6 | `commands search <kw> [...]` | T14 | `CommandCommands.SearchAsync` | `CommandCommandsTests` |
| 7 | `commands show <id\|name> [--group]` | T15 | `CommandCommands.ShowAsync` | `CommandCommandsTests` |
| 8 | `commands add --name --file [--description --group]` | T15 | `CommandCommands.AddAsync` | `CommandCommandsTests` |
| 9 | `commands edit <id\|name> [--name --description --file --group]` | T16 | `CommandCommands.EditAsync` | `CommandCommandsTests` |
| 10 | `commands delete <id\|name[,..]> --force` | T16 | `CommandCommands.DeleteAsync` | `CommandCommandsTests` |
| 11 | `commands move <ids> --to` | T17 | `CommandCommands.MoveAsync` | `CommandCommandsTests` |
| 12 | `commands import --group <path...> [--recursive --allow-duplicates --dry-run]` | T18 | `CommandCommands.ImportAsync` | `ImportExportTests` |
| 13 | `commands export [--group --recursive --format --output]` | T19 | `CommandCommands.ExportAsync` | `ImportExportTests` |
| 14 | `commands frequent [--limit]` | T17 | `CommandCommands.FrequentAsync` | `CommandCommandsTests` |
| 15 | `run <id\|name> [--group --admin --wait --timeout --dry-run]` | T20 | `RunCommand.ExecuteAsync` | `RunCommandTests` |
| 16 | `config get [key]` | T21 | `ConfigCommand.GetAsync` | `ConfigCommandTests` |
| 17 | `config set <hotkey\|hotkey.enabled> <值>` | T22 | `ConfigCommand.SetAsync` | `ConfigCommandTests` |
| 18 | `open [--command] [--gui]` | T23 | `OpenCommand.ExecuteAsync` | `OpenCommandTests` |
| 19 | `--version` | T1 | `CliHost` 的 version 分支 | `CliHostHelpVersionTests` |

### 4. DECISIONS 落实检查

| 决策 | 落实位置 | 说明 |
|---|---|---|
| D2 主 csproj 用 `DefaultItemExcludes` 排除 `tests\**;BashCommandManager.Cli\**` | T1 步骤 2 | 只验证/补齐，不重复添加 `<Compile Remove>` |
| D2.1 两个测试项目、CLI 专属测试独立 | T1 步骤 3 | `tests/BashCommandManager.Cli.Tests` 引用 CLI；GUI 测试只在既有项目子目录追加 |
| **D2.2 不链接 `CommandExecutor.cs`** | T1 的 `SharedSources` 清单、T20 `RunCommand` | 清单里没有 `CommandExecutor.cs`/`CommandService.cs`/`SettingsService.cs`/`HotkeySettings.cs`/`SingleInstanceService.cs`；`ExecutionResult.cs`（`TerminalLaunchPlan` 等）按 D2.2 第 3 条加入；`run` 自实现启动，`--dry-run` 复用 `TerminalCommandBuilder` |
| **D4.1 迁移只复制不删除、失败回退、可覆盖、有单测** | T5（实现+测试）、T6（GUI 接入）、T12（`meta.warning`）、T29 步骤 4 | `DatabaseMigratorTests` 覆盖成功/目标已存在/复制失败/建目录失败；解析器测试覆盖 `BASHCOMMANDMANAGER_DB` 与 `--db` |
| D5.1 根目录 `dotnet build -c Release` 仍只构建主项目 | T1 步骤 2、T29 步骤 1/3 | publish 产物断言 |
| D5.2 GUI「运行」行为不变 | T20（CLI 独立实现）、T24 不改 `CommandExecutor` | GUI 侧只新增 `GetByIdAsync` 与启动参数 |
| D5.3 不删改已有公开成员 | T6（`Initialize()` 签名不变）、T24（只新增） | `DatabaseInitializer.Initialize()` 仍为 `void` |
| D5.4 先失败测试再实现 | 每个任务 Step 1/2 vs Step 3/4 | 每条实现都有对应的“跑测试确认失败”步骤 |
| D5.5 不新增第三方依赖 | T1 步骤 3 | CLI 只 3 个包（与主工程同版本），测试项目只 xunit 三件套 |
| D5.6 中文无乱码 | T4/T10/T19 的 UTF-8 断言、T28 grep | 所有输出与文件写入均 `UTF8Encoding(false)` |
| D6 版本 1.3.0 三处统一 | T1（csproj）、T27（两个 .iss）、T28（README）、T29 步骤 7 | grep 核对 |

### 5. 占位符扫描

已逐项检查并排除：本计划不含 `TBD`、`TODO`、`适当处理`、`实现其余命令`、`同上`、`类似 Task N` 这类描述；每条命令都有自己的处理器方法、完整代码与测试落点；每个任务的代码块都是可直接粘贴的完整实现（辅助方法就在同一代码块内）。

### 6. 类型/签名一致性抽查

| 定义于 | 名称 | 后续任务使用处 | 一致 |
|---|---|---|---|
| T1 | `CliErrors.FileNotFound(string message, string? hint = null, int exitCode = 1)` | T6、T15、T16、T18、T20 用命名参数 `exitCode:` | ✅ |
| T4 | `JsonShapes.Command(Command, string?, bool, Func<string,bool>)` | T14、T15、T16、T18、T19 全部 4 参 | ✅ |
| T7 | `CliCommandStore.GetCommandIdsInGroupsAsync(IReadOnlyList<int>)` | T13 传入 `GetGroupSubtreeIdsAsync` 的返回值（`IReadOnlyList<int>`） | ✅ |
| T7 | `CliCommandStore.FillGroupNamesAsync(IReadOnlyList<Command>)` | T14 传入 `GetCommandsAsync` 返回值；T17 传入 `GetFrequentlyUsedAsync`；T19 传入 `List<Command>`（`IReadOnlyList` 兼容） | ✅ |
| T9 | `IProcessRunner.Start/RunWithResult(ProcessRunRequest)` | T20 使用 `ProcessRunRequest(FileName, Arguments, WorkingDirectory, UseShellExecute, Verb, RedirectOutput, TimeoutSeconds)` 7 个位置参数 | ✅ |
| T10 | `ICliSettingsStore.GetExecutionSettings()` 返回 #9 的 `ExecutionSettings` | T20 调 `.Clone()`/`.WithTerminal()`（#9 定义） | ✅ |
| T12 | `CliServices` 构造 9 个参数顺序 | T20/T21/T22/T23 单测用 `CliServicesFactory`（同一顺序） | ✅ |
| T21 | `ConfigCommand.GetAsync` / `SourceName` | T22 在同一类中追加 `SetAsync`，不改 `GetAsync` 签名 | ✅ |
| T21 | `config set` 在 T21 不存在 | T21 只接 `config.get`；T22 同时新增 `SetAsync` 与 dispatch 分支，因此任何时刻都不存在「未实现方法」 | ✅ |


