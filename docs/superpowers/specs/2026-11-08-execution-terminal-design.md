# 执行终端可配置化设计（Issue #9）
## 背景与现状

Issue #9：Windows 上有多种终端，用户希望选择自己置顶的终端来运行批处理。

`Core/Services/CommandExecutor.cs` 现状：

- 硬编码 `cmd.exe` + `$"/k \"{command.FilePath}\""`，`UseShellExecute = true`，`WorkingDirectory` 为脚本所在目录；`runAsAdmin` 时设置 `psi.Verb = "runas"` 触发 UAC。
- 用 `Dictionary<int, Process>` 跟踪进程，`Exited` 回调按 `ExitCode` 写 `Completed`/`Failed`，启动后置 `Running`。
- 脚本不存在时置 `Failed` 并抛 `FileNotFoundException`。

`Core/Services/SettingsService.cs` 现状：文件 `%LOCALAPPDATA%\BashCommandManager\settings.json`，只把 `HotkeySettings` 序列化到根对象（`{"Enabled":true,"Key":66,"Modifiers":3}`，枚举写数字），读取失败静默回退默认值，保存时整文件覆盖。

执行相关调用点：

| 位置 | 内容 |
|---|---|
| `ViewModels/CommandListViewModel.cs:184`、`:202` | `_executor.Execute(command, runAsAdmin: false/true)` |
| `App.xaml.cs:71` | `AddSingleton<ICommandExecutor, CommandExecutor>()` |
| `MainWindow.xaml:224-231` | 「运行」「管理员运行」按钮 |
| `ViewModels/SettingsViewModel.cs`、`Views/SettingsWindow.xaml` | 仅快捷键设置 |

仓库无 `.sln` 与测试项目；`BashCommandManager.csproj` 在根目录，SDK 会把根目录 `**/*.cs` 全编进主项目，测试目录必须 `Compile Remove`。
## 目标

1. 新增全局「执行/终端」配置：cmd、Windows PowerShell、PowerShell 7（`pwsh`）、Windows Terminal（`wt`）、自定义可执行文件；支持参数模板（`{file}` `{dir}` 等占位符）、保持窗口打开、管理员运行。
2. 配置持久化在 `%LOCALAPPDATA%\BashCommandManager\settings.json`，兼容现有扁平 `HotkeySettings` JSON（旧文件可读、可无损迁移，不报错、不丢数据）。
3. 命令行构造是**纯函数** `TerminalCommandBuilder.Build(ExecutionSettings, Command, string?)`，返回 `FileName`/`Arguments`/`WorkingDirectory`/`UseShellExecute`/`Verb`，可单元测试。
4. 设置窗口新增「执行」分组；保存后下一次执行立即生效；输入校验有内联提示。
5. `ICommandExecutor` 既有成员语义不变，仅**新增**一个带结果的方法；wt/pwsh 缺失时降级 cmd 并 Growl 提示，不崩溃。
## 非目标

- 不做「每条命令单独指定终端」（`Command` 实体与数据库零改动）。
- 不做内置终端/输出捕获（ConPTY、`RedirectStandardOutput`），现有执行模型不变。
- 不做命令预览、设置导入导出、终端版本探测；不保证新版本配置能被旧版本完整读取（只保证旧 → 新）。
## 假设与待确认

| 编号 | 假设 / 问题 | 默认选择与理由 | 风险 |
|---|---|---|---|
| A1 | 全局「管理员运行」与按钮的关系 | 取或：全局 `RunAsAdministrator` \|\| 本次 `runAsAdmin`；全局表示默认提权，按钮仍可单次提权 | 勾选后每次运行都弹 UAC |
| A2 | 默认终端与「保持窗口打开」 | `Cmd` + `/k {file}` + `KeepWindowOpen=true`，与现状逐字节等价 | 用户需手动切到 wt |
| A3 | 是否自动探测并默认 wt | 不探测，仅在用户选择后探测；避免无谓 IO | 新用户可能发现不了该特性 |
| A4 | 参数模板存储与重写 | 单字段 `ArgumentsTemplate`，空白时用默认模板；仅当模板仍是「上一组合默认模板」时随终端/开关重写，不吞掉用户编辑 | 手改模板后「保持打开」勾选框失效，UI 需提示 |
| A5 | 占位符集合 | 固定 `{file}` `{dir}` `{filePs}` `{dirPs}` `{fileRaw}` `{dirRaw}`，覆盖 cmd/PowerShell/自定义程序三种引用风格 | 用户需学新语法 |
| A6 | 终端程序缺失时 | wt/pwsh 未安装 → 降级 cmd + `Growl.Warning`；自定义程序缺失 → 报错且 `Started=false`，不悄悄换程序 | 用户可能忽略警告 |
| A7 | 状态回写语义 | 沿用宿主进程 `ExitCode`；`/k` 下宿主常驻，关窗才结束 | 窗口开着时状态一直 `Running`（与现状相同） |
| A8 | 设置文件格式 | 根对象改为 `{ "Hotkey": {...}, "Execution": {...} }`，枚举仍写数字，旧版可读 | 旧版本降级会被覆盖（见「风险与回滚」） |
| A9 | wt 窗口/profile 与内层 shell | 不指定 `-w`/`-p`，内层固定 `cmd.exe /k`（或 `/c`），与 Issue 示例一致 | 无法固定 profile；想用 pwsh 需自定义程序 |
| Q1 | 是否做命令预览与每命令覆盖 | 都不做；预览需多字段联动、覆盖需 DB 迁移，收益低 | 高级场景需切全局配置 |
| Q2 | wt 提权与设置写入失败 | 允许 `Verb=runas` 作用于 `wt.exe`，失败时用户可切 cmd；写失败不回滚内存值并 Growl.Error，保证当前会话可用 | 提权受版本/策略影响；重启后旧值生效 |
## 方案设计
### 1. 数据模型（`BashCommandManager.Core.Models`）

```csharp
public enum TerminalKind { Cmd = 0, WindowsPowerShell = 1, PowerShell7 = 2, WindowsTerminal = 3, Custom = 4 }

public class ExecutionSettings
{
    public TerminalKind Terminal { get; set; } = TerminalKind.Cmd;
    public string CustomExecutablePath { get; set; } = string.Empty;
    public string ArgumentsTemplate { get; set; } = string.Empty; // 空白 = 默认模板
    public bool KeepWindowOpen { get; set; } = true;
    public bool RunAsAdministrator { get; set; } = false;
    public ExecutionSettings Clone() => (ExecutionSettings)MemberwiseClone();
    public ExecutionSettings WithTerminal(TerminalKind k) { var c = Clone(); c.Terminal = k; return c; }
}

public class AppSettings { public HotkeySettings Hotkey { get; set; } = new(); public ExecutionSettings Execution { get; set; } = new(); }

public sealed record ExecutionOptions(bool RunAsAdmin = false);
public sealed record ExecutionResult(bool Started, TerminalKind EffectiveTerminal, bool FallbackApplied, string? Notice, Exception? Error);
public sealed record TerminalLaunchPlan(string FileName, string Arguments, string? WorkingDirectory, bool UseShellExecute, string? Verb) { public const string RunAsVerb = "runas"; }
```

`HotkeySettings` 不改（仍只有 `Enabled`/`Key`/`Modifiers` 与 `IsValid`）。
### 2. 设置持久化与兼容（`SettingsService`）

新格式（`Execution` 段字段顺序固定，便于人工编辑）：

```json
{
  "Hotkey": { "Enabled": true, "Key": 66, "Modifiers": 3 },
  "Execution": { "Terminal": 0, "CustomExecutablePath": "", "ArgumentsTemplate": "/k {file}", "KeepWindowOpen": true, "RunAsAdministrator": false }
}
```

`ISettingsService` 新增（其余成员不变）：

```csharp
ExecutionSettings GetExecutionSettings();
void SaveExecutionSettings(ExecutionSettings settings);
event EventHandler? ExecutionSettingsChanged;
```

构造函数改为 `public SettingsService(string? settingsDirectory = null)`（默认 `%LOCALAPPDATA%\BashCommandManager`）以便测试注入临时目录；`App.xaml.cs:74` 改为 `services.AddSingleton<ISettingsService>(_ => new SettingsService())`。

加载算法（`LoadAll`）：

1. 文件不存在 → `new AppSettings()`，跳到第 4 步。
2. 用 `JsonDocument` 探测根属性：含 `Hotkey` 或 `Execution` → 按 `AppSettings` 反序列化（新格式）。
3. 仅含 `Enabled`/`Key`/`Modifiers` → 反序列化为 `HotkeySettings` 赋给 `AppSettings.Hotkey`，置 `_needsMigration = true`（旧格式迁移）；都不匹配 → `new AppSettings()`。
4. 归一化：`Hotkey` 为 null 或 `!IsValid` → 新建；`Execution` 为 null → 新建；`Terminal` 非法枚举 → `Cmd`；`CustomExecutablePath` 去空白；`ArgumentsTemplate` 空白 → `TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen)`。
5. `_needsMigration` 且 `settings.json.v1.bak` 不存在 → 保存前先复制原文件为 `settings.json.v1.bak`；解析/IO 异常 → 复制为 `settings.json.corrupt.bak`（不覆盖），用默认值且不抛异常。

序列化与写入：

- 选项：`WriteIndented = true`、`PropertyNameCaseInsensitive = true`、`AllowTrailingCommas = true`、`ReadCommentHandling = Skip`；**不注册** `JsonStringEnumConverter`，保持枚举为数字。
- 两个 Save 方法都先更新内存 `_appSettings`，再统一 `SaveAll()`：序列化完整 `AppSettings` → 写 `settings.json.tmp` → `File.Move(tmp, path, overwrite: true)`；不得只写 `HotkeySettings`，否则会抹掉 `Execution` 段。
- 立即生效：`CommandExecutor` 每次执行都调 `GetExecutionSettings()` 现取；写入失败则异常上抛给 `SettingsViewModel.Save`，Growl.Error、窗口不关闭、内存值生效（Q2）。
### 3. 命令构造规则（`TerminalCommandBuilder`，纯函数）

```csharp
public static class TerminalCommandBuilder
{
    public static string GetDefaultTemplate(TerminalKind kind, bool keepWindowOpen);
    public static TerminalLaunchPlan Build(ExecutionSettings settings, Command command, string? resolvedExecutable = null);
    public static IReadOnlyList<string> ValidateTemplate(string template);
}
```

纯度契约：不访问文件系统、环境变量、注册表、时钟；不缓存；相同输入必得相同输出；除 `TerminalConfigurationException` 外不抛异常。可执行文件由调用方通过 `resolvedExecutable` 传入，null 时使用逻辑名。

| 占位符 | 展开值 | 典型用途 |
|---|---|---|
| `{file}` | 始终双引号包裹：`"C:\工作 目录\a.bat"` | cmd `/k`、wt 内层 cmd |
| `{dir}` | 始终双引号包裹：`"C:\工作 目录"` | wt `-d`、工作目录 |
| `{filePs}`/`{dirPs}` | PowerShell 单引号字面量内容，`'` → `''`，不包裹 | `-Command "& '{filePs}'"` |
| `{fileRaw}`/`{dirRaw}` | 原样输出 | 自定义程序自行加引号 |
| 其他 `{x}` | 抛 `TerminalConfigurationException`，消息含 `{x}` 与合法占位符列表 | — |

转义与替换规则：

- 单次扫描模板替换，替换结果不二次扫描（防止路径中的 `{file}` 被递归替换）。
- `{file}`/`{dir}` 的值含 `"` 时抛 `TerminalConfigurationException`（Windows 文件名不允许 `"`，正常已被 `File.Exists` 拦截）；wt 参数中的路径始终双引号包裹，避免 `;`（wt 标签页分隔符）被解释。
- 模板空白 → 用默认模板；模板缺少任一 file 类占位符 → `Build` 抛异常，`ValidateTemplate` 在保存时先行拦截。

| TerminalKind | FileName（未传 resolved） | 默认模板（keepOpen=true） | 默认模板（false） | WorkingDirectory |
|---|---|---|---|---|
| `Cmd` | `cmd.exe` | `/k {file}` | `/c {file}` | `Path.GetDirectoryName(command.FilePath)` |
| `WindowsPowerShell` | `powershell.exe` | `-NoExit -Command "& '{filePs}'"` | `-Command "& '{filePs}'"` | 同上 |
| `PowerShell7` | `pwsh.exe` | 同上 | 同上 | 同上 |
| `WindowsTerminal` | `wt.exe` | `-d {dir} cmd.exe /k {file}` | `-d {dir} cmd.exe /c {file}` | 同上 |
| `Custom` | `settings.CustomExecutablePath` | `{file}` | `{file}` | 同上 |

统一约定：`UseShellExecute = true`（runas 必需，与现状一致）；`Verb = settings.RunAsAdministrator ? "runas" : null`；「保持打开」即 `/k`、`-NoExit`，关闭即 `/c`、去掉 `-NoExit`。
### 4. 可执行文件探测（新组件）

```csharp
public interface IExecutableLocator { string? Resolve(TerminalKind kind, string customExecutablePath); }
```

- `Cmd`：`%COMSPEC%` → `%SystemRoot%\System32\cmd.exe` → `cmd.exe`；`WindowsPowerShell`：`%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` → `powershell.exe`（两者永不返回 null）。
- `PowerShell7`：`%ProgramFiles%\PowerShell\7\pwsh.exe` → `%ProgramFiles(x86)%\PowerShell\7\pwsh.exe` → `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` → `PATH` → null；`WindowsTerminal`：`%LOCALAPPDATA%\Microsoft\WindowsApps\wt.exe` → `PATH` → null。
- `Custom`：`File.Exists(customExecutablePath)` → 全路径，否则 null。

辅助纯函数（可单测）：`static string? FindOnPath(string fileName, string? pathVariable, Func<string, bool> fileExists)`，忽略空条目与含引号的目录。
### 5. `CommandExecutor` 最小改动

```csharp
public interface ICommandExecutor
{
    void Execute(Command command, bool runAsAdmin = false);                        // 语义不变
    ExecutionResult ExecuteWithResult(Command command, ExecutionOptions options);  // 新增
    bool IsRunning(int commandId);
    void RegisterProcess(int commandId, Process process);
    void UnregisterProcess(int commandId);
}
```

接口只**追加**：主项目为 `WinExe` 且未作为库分发，仓库内唯一实现是 `CommandExecutor`，不存在外部实现者，因此源码兼容。`Execute` 委托给 `ExecuteWithResult`，失败时抛 `result.Error`，`Error` 为 null（用户取消 UAC）时抛 `OperationCanceledException`，保留 VM 现有 `catch` 行为。

`ExecuteWithResult` 流程：

1. `File.Exists` 失败 → `Status = Failed` 并抛 `FileNotFoundException`（现状不变）；否则 `settings = GetExecutionSettings()`，`options.RunAsAdmin` 为 true 时 `settings = settings.Clone(); settings.RunAsAdministrator = true;`。
2. `resolved = _locator.Resolve(...)`；为 null 且终端是 wt/pwsh → `settings.WithTerminal(Cmd)`、重新解析、`notice = $"未检测到 {旧终端显示名}，已改用 cmd 启动"`、`fallback = true`；为 null 且终端是 Custom → 返回 `Started=false` + `Error = new FileNotFoundException("自定义程序不存在: …")`。
3. `plan = TerminalCommandBuilder.Build(settings, command, resolved)`；按 plan 组装 `ProcessStartInfo`，沿用现有 `Exited` 回调与 `RegisterProcess`，`Start()` 后 `Status = Running`。
4. 异常：`Win32Exception` 且 `NativeErrorCode == 1223`（取消 UAC）→ `Status = Idle`，返回 `Started=false`、`Notice=「已取消管理员授权」`、`Error=null`；其他异常 → `Status = Failed`，返回 `Started=false` + 异常。

`CommandListViewModel` 两处调用合并为私有方法（命令名与按钮不变）：

```csharp
var result = _executor.ExecuteWithResult(command, new ExecutionOptions(RunAsAdmin: runAsAdmin));
if (result.Notice is not null) Growl.Warning(new GrowlInfo { Message = result.Notice, WaitTime = 5 });
if (!result.Started) { if (result.Error is not null) Growl.Error(new GrowlInfo { Message = $"执行失败: {result.Error.Message}", WaitTime = 5 }); return; }
await _commandService.RecordExecutionAsync(command.Id);
```

依赖注入：新增 `services.AddSingleton<IExecutableLocator, ExecutableLocator>();`，`ICommandExecutor` 注册不变。

| 类型 | 文件 |
|---|---|
| 新增 | `Core/Models/TerminalKind.cs`（枚举 + `GetDisplayName()`）、`Core/Models/ExecutionSettings.cs`、`Core/Models/AppSettings.cs`；`Core/Services/TerminalCommandBuilder.cs`（含 `TerminalConfigurationException`）、`ExecutableLocator.cs`、`ExecutionResult.cs` |
| 新增 | `tests/BashCommandManager.Tests/`（csproj、`TerminalCommandBuilderTests.cs`、`SettingsServiceTests.cs`、`ExecutableLocatorTests.cs`、`FakeSettingsService.cs`） |
| 修改 | `SettingsService.cs`、`CommandExecutor.cs`、`SettingsViewModel.cs`、`CommandListViewModel.cs`、`SettingsWindow.xaml`、`App.xaml.cs`、`BashCommandManager.csproj` |
### 6. UI（`Views/SettingsWindow.xaml` + `SettingsViewModel`）

窗口由 `Height=400 Width=500` 改为 `Height=560 Width=560`，行定义：标题 / TabControl / 错误提示 / 按钮；新增第二个 TabItem：

| 控件 | 绑定 | 说明 |
|---|---|---|
| ComboBox | `ItemsSource={Binding TerminalOptions}`、`DisplayMemberPath=DisplayName`、`SelectedValuePath=Kind`、`SelectedValue={Binding Terminal}` | 5 个中文选项（`GetDisplayName()`） |
| TextBox + Button | `CustomExecutablePath`、`BrowseCustomExecutableCommand` | 仅 `IsCustomTerminal` 可见；`OpenFileDialog` 过滤 `*.exe;*.bat;*.cmd` |
| TextBox | `ArgumentsTemplate`（`UpdateSourceTrigger=PropertyChanged`） | 单行模板 |
| TextBlock | 静态提示 | `占位符：{file} {dir} {filePs} {dirPs} {fileRaw} {dirRaw}；展开值自带引号，请勿再加` |
| Button | `ResetTemplateCommand` | 按当前 Terminal/KeepWindowOpen 恢复默认模板 |
| CheckBox | `KeepWindowOpen`；`RunAsAdministrator` | 模板仍为默认值时保持打开同步重写模板；管理员为全局默认提权（A1） |
| TextBlock | `ErrorMessage` + `HasError` | 移到 TabControl 下方，两个 Tab 共用 |

ViewModel 用编辑缓冲字段（`Terminal`、`CustomExecutablePath`、`ArgumentsTemplate`、`KeepWindowOpen`、`RunAsAdministrator`、`IsCustomTerminal`、`TerminalOptions`），构造时从 `GetExecutionSettings()` 载入，`Cancel` 丢弃。`OnTerminalChanged`/`OnKeepWindowOpenChanged` 在 `_templateIsDefault == true` 时重写模板。

保存校验（任一失败：`HasError = true`、窗口不关闭）：

1. 快捷键 `IsValid`（现有逻辑）；自定义终端路径非空且 `File.Exists`，否则「自定义程序不存在：{path}」。
2. `ValidateTemplate`：未知占位符、缺少 file 类占位符、花括号不配对，分别给出内联提示。
3. 通过后 `SaveHotkeySettings` + `SaveExecutionSettings`（各写整份文件）+ Growl.Success；IO 异常 → Growl.Error「设置保存失败：{msg}」并保留窗口。
### 7. 错误处理

| 场景 | 检测点 | 行为 |
|---|---|---|
| 脚本文件不存在 | `ExecuteWithResult` | `Status=Failed` + `FileNotFoundException`（不变），VM Growl.Error |
| 自定义路径为空/不存在 | 设置保存 | 内联 `ErrorMessage`，不关闭窗口 |
| 未安装 wt/pwsh | 执行时探测 | 降级 cmd + `FallbackApplied=true` + `Notice`，Growl.Warning，照常记录执行 |
| 自定义程序被删除 | 执行时探测 | `Status=Failed` + `Started=false`，Growl.Error，不崩溃 |
| 终端启动失败 / 取消 UAC（1223） | `Start()` / `catch Win32Exception` | 启动失败：`Status=Failed` + Growl.Error「执行失败：…」；取消：`Status=Idle` + Growl.Warning「已取消管理员授权」，不记录执行次数 |
| 模板非法（外部手改 JSON） | `Build` | Growl.Error「执行配置无效：…」，不崩溃 |
| `settings.json` 损坏/写入失败 | 加载/保存 | 损坏：备份 `corrupt.bak` 并用默认值；写入失败：Growl.Error，内存生效 |
## 备选方案与取舍

| 方案 | 做法 | 取舍 | 结论 |
|---|---|---|---|
| A | 全局 `ExecutionSettings` + 纯函数 builder + 探测降级 | 可单测、改动集中 Core、新增终端只改一张表；仅全局配置、不捕获脚本退出码 | 采用 |
| B | ConPTY 内置终端并重写输出面板 | 可拿真实退出码；工程量远超 Issue | 否决 |
| C | `Command` 增加 `LaunchProfile` + DB 迁移 | 每命令独立终端；需 SQLite 迁移与列表 UI 扩展 | 列为后续 |
| D | `cmd /c start "" wt …` 间接触发 | 改动小；丢失进程跟踪与提权控制 | 否决 |
| E | 新建 `execution.json` 与旧文件并存 | 不碰旧 JSON；违背「持久化在 settings.json」要求 | 否决 |
| F | 新文件同时写扁平 `Enabled/Key/Modifiers` 镜像 | 旧版降级可读；双份数据易不一致 | 否决，改用备份 |
## 测试策略

新增 xUnit 项目 `tests/BashCommandManager.Tests`（`net8.0-windows10.0.19041.0`、`UseWPF=true`，引用主项目）；主项目加 `<Compile Remove="tests/**/*.cs" />`；运行 `dotnet test tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`（本设计任务不执行）。

`TerminalCommandBuilder` 用例表：

| # | 输入 | 期望 |
|---|---|---|
| B1 | Cmd / keepOpen / `C:\work\demo.bat` | `cmd.exe`；`/k "C:\work\demo.bat"`；`C:\work`；`Verb=null` |
| B2 | Cmd / keepOpen / `C:\工作 目录\我的 脚本.bat` | `/k "C:\工作 目录\我的 脚本.bat"`；目录 `C:\工作 目录` |
| B3 | Cmd / 不保持 | `/c "C:\work\demo.bat"` |
| B4 | Cmd / `RunAsAdministrator=true` | `Verb="runas"`、`UseShellExecute=true` |
| B5 | WindowsPowerShell / keepOpen | `powershell.exe`；`-NoExit -Command "& 'C:\work\demo.bat'"` |
| B6 | PowerShell7 / keepOpen | `pwsh.exe`；参数形式同 B5 |
| B7 | PowerShell / `C:\work\it's.bat` | `-NoExit -Command "& 'C:\work\it''s.bat'"`（单引号翻倍） |
| B8 | WindowsTerminal / `C:\work 目录\demo.bat` | `wt.exe`；`-d "C:\work 目录" cmd.exe /k "C:\work 目录\demo.bat"` |
| B9 | WindowsTerminal / 不保持 | `-d "C:\work 目录" cmd.exe /c "…\demo.bat"` |
| B10 | WindowsTerminal / 管理员 | `Verb="runas"` |
| B11 | WindowsTerminal / `C:\a;b\demo.bat` | 路径双引号包裹，`;` 不裸露 |
| B12 | Custom（`D:\Apps\ConEmu\ConEmu64.exe`，模板 `-run {dir} {file}`） | FileName 为自定义路径；`-run "C:\work" "C:\work\demo.bat"` |
| B13 | Custom（模板 `-run {fileRaw}`） | 原样输出，无引号 |
| B14 | 传入 `resolvedExecutable="C:\Fake\wt.exe"` | `FileName="C:\Fake\wt.exe"` |
| B15 | 模板含 `{foo}` / 模板 `-Command echo hi` | 均抛 `TerminalConfigurationException` |
| B16 | `FilePath` 含 `"` | 抛 `TerminalConfigurationException` |
| B17 | 磁盘不存在的 `Z:\nope\x.bat` | 仍返回 plan（纯函数不碰文件系统） |
| B18 | 根目录文件 `C:\demo.bat` | `WorkingDirectory` 为 `C:\` |

`SettingsService` 用例表（临时目录注入）：

| # | 输入 | 期望 |
|---|---|---|
| S1 | 旧格式 `{"Enabled":true,"Key":66,"Modifiers":3}` | 不抛异常；Key=B、Modifiers=Ctrl+Alt；Execution 为默认（Cmd、keepOpen、`/k {file}`） |
| S2 | 旧格式 `Enabled=false` + 自定义 Key/Modifiers | 原样保留 |
| S3 | 新格式 round-trip / 缺 `Execution` 段 / 含未知字段 | 字段一致；缺失段用默认；未知字段忽略 |
| S4 | 损坏 JSON | 不抛异常；生成 `settings.json.corrupt.bak`；默认值 |
| S5 | 先写 Execution 再 `SaveHotkeySettings`；反向再存一次 | 两次文件中另一段均仍存在 |
| S6 | 旧格式首次保存 | `settings.json.v1.bak` 等于旧 JSON；新文件为嵌套格式 |
| S7 | `Terminal: 99` / `ArgumentsTemplate: "  "` | 归一化为 `Cmd` / 默认模板 |

`ExecutableLocator`：Custom 存在 → 全路径、不存在 → null；`FindOnPath` 命中/未命中/空 PATH/带引号目录；Cmd 与 WindowsPowerShell 永不返回 null。

人工冒烟：① 保留旧 `settings.json` 启动无异常、快捷键生效；② 选 wt 保存后运行，wt 在脚本目录打开且脚本执行（含中文+空格路径），关窗后状态 `Completed`；③ 临时改名 `wt.exe` 后运行，Growl 警告 + cmd 打开；④ 自定义路径填错，保存被拦截并显示内联错误；⑤ 管理员运行后取消 UAC，提示且状态回 `Idle`；⑥ 保存快捷键后检查 `settings.json`，`Execution` 段未被抹掉。
## 验收标准

1. 设置窗口有「执行」Tab（终端下拉、自定义路径+浏览、参数模板、保持打开、管理员运行）；保存后下次执行即用新配置。
2. 旧 `settings.json` 启动无异常、快捷键不丢；首次保存生成 `.v1.bak` 且新文件为嵌套格式。
3. `TerminalCommandBuilder` 通过 B1–B18，覆盖空格/中文路径、引号转义、wt 形式、自定义程序、runas 组合，且同输入两次调用结果相等。
4. wt/pwsh 未安装时降级 cmd 并 Growl.Warning，应用不崩溃；自定义程序不存在时保存被拦截、运行时报错不崩溃。
5. `ICommandExecutor` 原四个成员签名与语义不变；「运行」「管理员运行」按钮行为不变。
6. 主项目正常编译（`tests/**` 已排除），`dotnet test` 全绿。
## 风险与回滚

| 风险 | 影响 | 缓解 |
|---|---|---|
| 默认值不等价导致回归 | 现有运行方式变化 | 默认 `Cmd` + `/k {file}`，与现状逐字节一致 |
| 新→旧版本降级 | 旧版读嵌套 JSON 用默认快捷键，保存后覆盖文件 | 首次迁移生成 `.v1.bak`；文档说明降级步骤 |
| 手改模板后「保持打开」失效 | 勾选框无效果 | UI 提示 +「恢复默认模板」按钮 |
| wt 对 `;`、引号特殊解析 | 路径被拆成多标签页 | 路径强制双引号；B11 覆盖 |
| wt 提权行为随版本/策略变化 | 管理员运行失败 | 捕获异常 Growl.Error，可切 cmd |
| `/k` 下宿主常驻 | 状态长期 `Running`（与现状相同） | 非目标已声明；后续可改 `/c` 或捕获脚本退出码 |
| 测试目录被主项目编译 | 主项目编译失败 | `Compile Remove="tests/**/*.cs"` 写入清单 |
| 接口追加成员 | 理论实现者编译失败 | 主项目为 WinExe、无外部实现者；`Execute` 保留为兼容入口 |

回滚：本特性集中在单个 PR，`git revert` 即可；回滚后旧版本仍可启动（必要时把 `settings.json.v1.bak` 复制回 `settings.json`）；删除 `settings.json` 可恢复全默认。
