# CLI 调用层与操作 Skill 设计（Issue #10）

## 背景与现状

Issue #10：为当前所有功能提供对应的 CLI 调用方式，并生成可被 AI 代理使用的操作 skill。本仓库是单项目 WPF 桌面应用（`BashCommandManager.csproj`：`OutputType=WinExe`、`net8.0-windows10.0.19041.0`、`UseWPF=true`），无 `.sln`、无测试项目，`dotnet build/publish` 在仓库根目录只作用于唯一 csproj。

### 已核实的数据库路径结论（关键）

| 事实 | 证据 |
|---|---|
| GUI 数据库真实位置 = **应用目录** `data/app.db`，即 `AppContext.BaseDirectory\data\app.db` | `Infrastructure/DatabaseInitializer.cs:14-18`；`App.xaml.cs:38-44` 用 `initializer.ConnectionString` 注册 `IDatabaseService` |
| `CLAUDE.md` 的「data/app.db」说法对**数据库**成立 | 同上；本机已安装实例 `D:\Program Files\BashCommandManager\data\app.db`（24576 B，2026-03-05）存在 |
| `%LOCALAPPDATA%\BashCommandManager` 只存 `settings.json`，不存数据库 | `Core/Services/SettingsService.cs:23-32`；本机该目录只有 `settings.json`、`logs` |
| 该目录下的 `BashCommandManager.db` 是**异种 schema 历史文件**，不能当本应用数据库 | 实测表为 `Groups/Settings/Scripts/ExecutionLogs`、`Groups` 0 行；本仓库 schema 是 `Groups/Commands`（`DatabaseInitializer.cs:29-47`） |
| 连接串 `Data Source={path};Version=3;` **未启用外键**，GUI 删除分组时命令不会级联删除（`GroupService.cs:68-72` 注释与实现不符） | `DatabaseInitializer.cs:21`；`GroupService.DeleteGroupAsync` 只调 `_repository.DeleteAsync` |
| 命令列表是 `ItemsControl`，没有“选中项”概念 | `MainWindow.xaml:152`、`MainWindow.xaml.cs:122-128` |
| 单实例用 Mutex + NamedPipe 激活，管道只传 `ACTIVATE`，不传参数 | `Core/Services/SingleInstanceService.cs:12-14,26-62,96-109` |

矛盾来源解释：`SettingsService` 的 `%LOCALAPPDATA%` 是**快捷键设置**，与数据库无关；两者从来不在同一目录。CLI 若沿用 `AppContext.BaseDirectory`，会因自身安装目录不同而访问另一个库，因此必须引入统一解析器。

### GUI 功能盘点（CLI 需覆盖）

分组树（新增/重命名/删除/多级父子）；命令列表（增删改、搜索、排序、移动分组、批量移动/删除）；批处理导入（多文件，`CommandService.cs:47-79` 走 `OpenFileDialog`）；运行/管理员运行并记录执行统计（`CommandListViewModel.cs:179-213`）；常用命令 Top10（`CommandListViewModel.cs:551-561`）；全局快捷键（`settings.json`）；托盘、单实例；开机自启动与主题切换未实现。

## 目标

1. 新增独立控制台项目 `BashCommandManager.Cli/BashCommandManager.Cli.csproj`，产出 `bcm.exe`，覆盖 `groups`/`commands`/`run`/`config`/`open` 全部子命令。
2. CLI 与 GUI 访问**同一个** SQLite 文件：新增 `Infrastructure/DatabasePathResolver.cs`，GUI 与 CLI 共用同一解析与迁移逻辑。
3. 统一 `--json` 输出契约与退出码（0/1/2/3）、UTF-8、全程无交互（不弹窗、不读 stdin）。
4. 生成 `.claude/skills/bash-command-manager/SKILL.md`，frontmatter 仅 `name` + `description`，与现有 `.claude/skills/*/SKILL.md` 一致。
5. CLI 随主安装包发布，`Packaging/InnoSetup/*.iss` 只做最小增补，现有 `dotnet publish` 与打包脚本继续可用；CLI 逻辑层可单测，端到端用临时数据库冒烟。

## 非目标

- 不把 GUI 重构为多项目/多程序集，不抽出独立 Core 类库（见「备选方案」）。
- 不实现 GUI 未实现的功能（开机自启动、主题切换），不做 CLI 版执行输出面板。
- CLI 不做交互式编辑（`$EDITOR`）、不做 `--watch`、不做 HTTP/守护进程模式。
- 不改变 GUI 发布命令、产物名（`BashCommandManager.exe`）与 Inno `OutputBaseFilename`。
- 不做 `commands export` 的 JSON 反向恢复（导出仅作备份/审计快照）。
- 不保证新版本数据库可被旧版本 GUI 使用（schema 幂等、旧库不删，可回滚）。

## 假设与待确认

本会话无人可回答问题，以下为默认选择、理由与风险，实现按默认执行。

| 编号 | 问题 | 默认选择 | 理由 | 风险 |
|---|---|---|---|---|
| A1 | CLI 代码复用方式 | 源码链接 WPF-free 子集，不 `ProjectReference` 主工程 | 主工程 WinExe/WPF，引用会把 WindowsDesktop 运行时拖进 CLI（TFM 必须精确匹配、发布体积大），详见「备选方案」 | 链接清单需维护 |
| A2 | 数据库规范位置 | `%LOCALAPPDATA%\BashCommandManager\data\app.db` | 与设置同应用目录、免管理员写权限、目录结构与旧 `data/app.db` 同构 | 位置变更需一次性迁移（已设计） |
| A3 | 是否用 `%LOCALAPPDATA%\BashCommandManager\app.db` | 否，改用 `data\app.db` | 该目录已有异种 schema 的 `BashCommandManager.db`，`data\` 与旧目录对齐、避免再混淆 | 与 Issue 示例路径略有差异（本文说明） |
| A4 | 旧 `{app}\data\app.db` 处理 | 首次解析到旧库时**复制**到新位置，旧文件保留 | 可回滚、非破坏性；Program Files 只读场景也可用 | 旧版 GUI 继续写旧库会造成数据分叉 |
| A5 | 删除类命令确认 | 必须显式 `--force`，否则退出 1（`E_CONFIRM_REQUIRED`） | 非交互进程不能弹窗，用参数表达确认 | 代理必须向人类确认后再加 `--force` |
| A6 | 名称不唯一 | 退出 1 `E_AMBIGUOUS` 并列出候选 id，不自动取第一个 | 静默选错对象比失败更危险 | 用户需改用 id 或 `--group` |
| A7 | `run` 默认行为 | 与 GUI 一致：`cmd.exe /k "<file>"`、可见窗口、立即返回 | 保持“双击执行”体验；`--wait` 供自动化 | 窗口常驻，CLI 已返回 |
| A8 | `run --admin` | 与 `--wait` 互斥；UAC 取消返回 3 `E_ELEVATION_CANCELLED` | 提权与等待/重定向语义冲突 | 无桌面会话必然失败（已文档化） |
| A9 | `open --command` 语义 | 定位：激活窗口、切到命令所在分组、把名称填入搜索框；不自动执行 | 自动执行风险高；不新增列表选中态 | 同名命令会显示多条 |
| A10 | `settings.json` 读写 | `JsonNode` 就地改写，兼容当前扁平格式与 Issue #9 的 `{Hotkey:{...}}` 嵌套格式 | 不覆盖 `Execution` 等段、不绑定 WPF 类型 | 与 #9 合并需回归两者设置测试 |
| A11 | 外键级联 | 连接串加 `Foreign Keys=True`，删除分组时真正级联删命令 | 修复 `DatabaseInitializer.cs:21` 潜在缺陷，使 CLI 语义可预测 | 旧库已有孤儿命令不会被自动清理 |
| A12 | 参数解析库 | 手写解析器，不引入 `System.CommandLine`（2.0 仍预发布） | 命令/选项数量可控且可单测 | 需自行实现帮助与错误提示 |
| A13 | 发布形态 | Full 自包含单文件 + `IncludeNativeLibrariesForSelfExtract=true`；Lite 框架依赖单文件 | Full 用户无 .NET；Lite 用户按定义已装 .NET 8 | Full 安装包增大（压缩后约 20-25MB） |
| A14 | 版本号一致性 | `BashCommandManager.Cli.csproj` 的 `<Version>` 与两个 `.iss` 的 `MyAppVersion` 同步 | 仓库无统一版本文件，沿用现有手工同步 | 发版漏改会导致 `bcm --version` 偏差 |
| Q1 | 是否支持 `commands import --from-json` 恢复 | 不做 | 需 id 重映射与冲突策略，Issue 未要求 | 备份只能人工处理 |
| Q2 | 是否单独发布 CLI zip | 只把同一二进制打成 zip 作为 Release 附加产物 | 便于无 GUI 的 CI/代理环境，零额外构建 | Release 说明需同时列安装包与 zip |
| Q3 | 是否提供 `--portable` | 不做，用 `BASHCOMMANDMANAGER_DB` 指向便携库 | 少一个开关，解析顺序已覆盖 | 便携用户需自行设环境变量 |

## 方案设计

### 1. 项目结构与复用方式

**结论：新增独立控制台项目，通过显式 `<Compile Include>` 链接 Core 的 WPF-free 子集，不引用主工程。**

- `ProjectReference` 主工程：SDK 允许引用 `WinExe`（同样产出 dll），但主工程 TFM 是 `net8.0-windows10.0.19041.0`，CLI 必须精确匹配（`net8.0-windows` 默认平台版本 7.0，会触发 NU1201）；`HotkeySettings`/`CommandService` 依赖 `System.Windows.Input`/`Application`/`OpenFileDialog`，CLI 必须 `UseWPF=true`。后果：框架依赖发布要求目标机装 .NET **Desktop** Runtime（Full 用户没有），自包含发布重复携带约 150MB WindowsDesktop 运行时，且输出夹带 `Assets/app.ico`、HandyControl。结论：可行但不适合分发。
- 源码链接：CLI 目标 `net8.0-windows`、无 WPF，自包含单文件约 70MB（不裁剪，避免 Dapper/SQLite 反射损坏），框架依赖仅几百 KB；GUI 产物零变化；被链接的都是最稳定的数据层文件。

**CLI 项目定义（关键属性）**：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <AssemblyName>bcm</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>   <!-- 链接文件依赖隐式 using -->
    <Version>1.2.1</Version>                   <!-- 与两个 .iss 的 MyAppVersion 同步 -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
</Project>
```

`PackageReference` 与主工程同版本：`Dapper 2.1.35`、`System.Data.SQLite.Core 1.0.115.5`、`CommunityToolkit.Mvvm 8.2.2`（链接的模型/仓库依赖它们）。

**链接清单（精确）**，均设 `Link="Shared\<相对路径>"`：`Core/Models/Command.cs`、`Core/Models/Group.cs`、`Core/Models/SortOptions.cs`、`Core/Repositories/CommandRepository.cs`、`Core/Repositories/GroupRepository.cs`、`Core/Services/DatabaseService.cs`、`Core/Services/GroupService.cs`、`Core/Services/CommandExecutor.cs`、`Infrastructure/DatabaseInitializer.cs`、`Infrastructure/DatabasePathResolver.cs`（新增）。**不链接**：`HotkeySettings.cs`/`SettingsService.cs`（WPF `Key`/`ModifierKeys`）、`CommandService.cs`（`Application.Current.Dispatcher` + `OpenFileDialog`，`CommandService.cs:49-78`）、`SingleInstanceService.cs`、`GlobalHotkeyService.cs`。CLI 需要的写操作与 `settings.json` 读写分别由 `Application/CliCommandStore.cs`、`CliSettingsStore.cs` 基于复用仓库/JSON 实现。

**CLI 目录结构**：

```
BashCommandManager.Cli/
├── BashCommandManager.Cli.csproj
├── Program.cs                     # 解析 → DatabaseSession → 分发 → 输出/退出码
├── CommandLine/                   # CommandLineParser.cs、ParsedCommand.cs、CliUsageException.cs
├── Application/                   # CliCommandStore.cs、CliSettingsStore.cs、HotkeySpec.cs、HotkeyKeyMap.cs
├── Operations/                    # DatabaseSession.cs、CommandResolver.cs、GroupCommands.cs、CommandCommands.cs、RunCommand.cs、ConfigCommand.cs、OpenCommand.cs
├── Output/                        # OutputWriter.cs、JsonEnvelope.cs、TableWriter.cs
└── Infrastructure/                # GuiLauncher.cs、CliFailureException.cs（携带 ErrorCode + ExitCode）
```

**主工程必须的排除项**（否则 GUI 编译直接失败）：SDK 默认把根目录 `**/*.cs` 编进主项目，且只排除根级 `bin/**`、`obj/**`（已核对 SDK `Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.DefaultOutputPaths.targets:133` 与同目录 `Microsoft.NET.Sdk.DefaultItems.props:36`），所以 `BashCommandManager.Cli/**`、`tests/**` 会被吞进主程序集（`obj/*.AssemblyInfo.cs` 还会重复 `TargetFrameworkAttribute`）。在 `BashCommandManager.csproj` 追加：

```xml
<ItemGroup>
  <Compile Remove="BashCommandManager.Cli/**" />
  <None Remove="BashCommandManager.Cli/**" />
  <EmbeddedResource Remove="BashCommandManager.Cli/**" />
  <Page Remove="BashCommandManager.Cli/**" />
  <Resource Remove="BashCommandManager.Cli/**" />
  <Compile Remove="tests/**" />
  <None Remove="tests/**" />
</ItemGroup>
```

`dotnet publish -c Release -r win-x64 ...`（仓库根目录）仍只发布主工程；`Packaging/InnoSetup/*.iss` 的 `[Files]` 不动。

### 2. 数据库路径统一解析

新增 `Infrastructure/DatabasePathResolver.cs`（GUI 与 CLI 共用，CLI 源码链接）。接口：

```csharp
public enum DatabasePathSource { Argument, Environment, LocalAppData, LegacyApplicationDirectory }

public sealed record DatabasePathResolution(
    string DatabasePath, DatabasePathSource Source, string? LegacyPath, bool RequiresMigration);

public static class DatabasePathResolver
{
    public const string EnvironmentVariableName = "BASHCOMMANDMANAGER_DB";
    public static string SettingsDirectory { get; }     // %LOCALAPPDATA%\BashCommandManager
    public static string CanonicalDatabasePath { get; } // ...\data\app.db
    public static string LegacyDatabasePath { get; }    // AppContext.BaseDirectory\data\app.db
    public static DatabasePathResolution Resolve(string? explicitPath, string? environmentValue, Func<string, bool> fileExists);
    public static DatabasePathResolution ResolveForCurrentProcess(string? explicitPath = null);
}

public sealed record DatabasePreparationResult(string DatabasePath, bool Migrated, string? Warning);

public static class DatabaseMigrator
{
    // copyDatabase 默认实现：System.Data.SQLite 的 SQLiteConnection.BackupDatabase（一致性快照）
    public static DatabasePreparationResult Prepare(DatabasePathResolution resolution,
        Action<string, string>? copyDatabase = null, Action<string>? createDirectory = null);
}
```

解析顺序（首个命中即用；相对路径按 `Environment.CurrentDirectory` 转绝对）：

| 优先级 | 来源 | 说明 |
|---|---|---|
| 1 | `--db <path>` | 仅 CLI；GUI 无此参数；命中后**不做**迁移 |
| 2 | 环境变量 `BASHCOMMANDMANAGER_DB` | GUI 与 CLI 都读取；命中后不做迁移 |
| 3 | `%LOCALAPPDATA%\BashCommandManager\data\app.db` | 规范位置；不存在但第 4 项存在时先迁移再使用 |
| 4 | `{AppContext.BaseDirectory}\data\app.db` | 旧位置（当前 GUI 唯一位置），仅作迁移源 |
| 5 | 以上都不存在 | 创建规范位置并初始化 schema |

迁移规则（`DatabaseMigrator.Prepare`）：

1. `RequiresMigration == false` → 直接返回目标路径；`RequiresMigration` 定义为 `explicitPath` 为空、环境变量为空、规范位置文件不存在、旧位置文件存在四者同时成立。
2. 用 `SQLiteConnection.BackupDatabase` 把旧库备份到 `app.db.migrating-{guid}`（避免热复制撕裂），成功后 `File.Move(tmp, target)`；目标已存在视为并发迁移成功，删除临时文件。
3. 创建目录/备份/移动失败（权限、磁盘）→ 返回 `Warning` 并**回退使用旧路径**（保持可启动）；GUI 写 `Debug.WriteLine`，CLI 写入 `meta.warning` 与 stderr。
4. 不删除、不改名旧库；回滚只需删除新文件或设 `BASHCOMMANDMANAGER_DB` 指回旧路径。

`Infrastructure/DatabaseInitializer.cs` 改动：构造函数 `public DatabaseInitializer(string? explicitDatabasePath = null)` → `ResolveForCurrentProcess` → `DatabaseMigrator.Prepare` → `Directory.CreateDirectory(Path.GetDirectoryName(path)!)`；新增只读属性 `DatabasePath`、`MigrationWarning`；`ConnectionString` 改为 `Data Source={_dbPath};Version=3;Foreign Keys=True;`（A11）；`Initialize()` 的建表/根分组语句不变（幂等）。`App.xaml.cs:39` 的 `new DatabaseInitializer()` 调用不变。

不改 `Core/Services/SettingsService.cs`（避免 Core→Infrastructure 反向依赖）：CLI 的 `CliSettingsStore` 用 `DatabasePathResolver.SettingsDirectory` 拼 `settings.json`，测试断言该值等于 `%LOCALAPPDATA%\BashCommandManager`。CLI 的 `Operations/DatabaseSession.cs`：`new DatabaseInitializer(options.DbPath)` → 读取 `DatabasePath`/`MigrationWarning` → 记录 `Created = !File.Exists(path)` → `Initialize()`；`--db` 指向不存在文件时按 SQLite 语义创建空库并建 schema（与 GUI 首次启动一致），`meta.dbPath`/`meta.created` 始终回带，代理应先核对路径再写入。

### 3. 命令集与参数表

全局选项（可出现在子命令前后）：`--json`、`--db <path>`、`--help`、`--version`。`bcm` 无参数时打印帮助并退出 0；未知动词退出 1。

| 命令 | 参数 | 示例 | `--json` 输出形态 |
|---|---|---|---|
| `groups list` | `[--tree]`（`--tree` 输出 `children` 嵌套） | `bcm groups list --json` | `{"ok":true,"data":[{"id":1,"name":"根分组","parentId":null,"sortOrder":0,"commandCount":2}],"meta":{"count":1,"dbPath":"...","created":false}}` |
| `groups create` | `--name <n>` `[--parent <id\|path>]`（默认 `1`，`--parent none` 建顶级） | `bcm groups create --name 运维 --parent 1` | `{"ok":true,"data":{"id":2,"name":"运维","parentId":1,"sortOrder":0}}` |
| `groups rename` | `<id\|path>` `--name <new>` | `bcm groups rename 2 --name 生产运维` | `{"ok":true,"data":{"id":2,"name":"生产运维"}}` |
| `groups delete` | `<id\|path>` `--force` | `bcm groups delete 运维 --force` | `{"ok":true,"data":{"deletedGroupIds":[2,4],"deletedCommandIds":[7,8]}}`；删根分组/缺 `--force` → 1 |
| `commands list` | `[--group <id\|path>]` `[--sort name\|last\|count]`（默认 name）`[--order asc\|desc]` `[--limit <n>]`（0=全部） | `bcm commands list --group 运维 --sort count --order desc` | `{"ok":true,"data":[{"id":7,"name":"备份","description":"备份库","filePath":"D:\\bat\\备份.bat","groupId":2,"groupName":"运维","sortOrder":0,"executionCount":3,"lastExecutedAt":"2026-11-08T20:47:32"}],"meta":{"count":1}}` |
| `commands search` | `<keyword>` `[--group]` `[--sort]` `[--order]` `[--limit]` | `bcm commands search 备份 --json` | 同 `commands list`（`data` 为数组） |
| `commands show` | `<id\|name>` `[--group <id\|path>]` | `bcm commands show 7 --json` | 单条命令对象 + `"fileExists":true` |
| `commands add` | `--name <n>` `--file <path>` `[--description <d>]` `[--group <id\|path>]` | `bcm commands add --name 备份 --file D:\bat\备份.bat --group 运维` | `{"ok":true,"data":{"id":7,"name":"备份","filePath":"D:\\bat\\备份.bat","groupId":2}}`；文件不存在 → 1 `E_FILE_NOT_FOUND` |
| `commands edit` | `<id\|name>` `[--name]` `[--description]` `[--file]` `[--group]`（至少一项） | `bcm commands edit 7 --description "每日 2:00"` | 更新后的命令对象 |
| `commands delete` | `<id\|name[,..]>` `--force` | `bcm commands delete 7,8 --force` | `{"ok":true,"data":{"deleted":[7,8]}}`；缺 `--force` → 1 |
| `commands move` | `<id\|name[,..]>` `--to <id\|path>` | `bcm commands move 7,8 --to 生产运维` | `{"ok":true,"data":{"moved":[7,8],"targetGroupId":4}}` |
| `commands import` | `--group <id\|path>` `<path...>` `[--recursive]` `[--allow-duplicates]` `[--dry-run]` | `bcm commands import --group 运维 D:\bat --recursive` | `{"ok":true,"data":{"group":{"id":2,"name":"运维"},"imported":[{"id":10,"name":"a","filePath":"D:\\bat\\a.bat"}],"skipped":[{"filePath":"D:\\bat\\b.bat","reason":"duplicate"}],"total":2}}` |
| `commands export` | `[--group <id\|path>]` `[--recursive]` `[--format json\|csv]` `[--output <file>]` | `bcm commands export --format json --output backup.json` | 写文件：`{"ok":true,"data":{"path":"...","format":"json","count":2}}`；未给 `--output`：`data` 为 `{"schemaVersion":1,"exportedAt":"...","groups":[...],"commands":[...]}` |
| `commands frequent` | `[--limit <n>]`（默认 10） | `bcm commands frequent --limit 5 --json` | 同 `commands list` 数组，按 `executionCount` 降序 |
| `run` | `<id\|name>` `[--group]` `[--admin]` `[--wait]` `[--timeout <秒>]` `[--dry-run]` | `bcm run 备份 --wait` | 默认：`{"ok":true,"data":{"id":7,"name":"备份","started":true,"pid":1234,"waited":false}}`；`--wait` 成功：`{"ok":true,"data":{"id":7,"started":true,"waited":true,"exitCode":0,"durationMs":812}}`；非 0：`{"ok":false,"error":{"code":"E_COMMAND_FAILED","message":"命令退出码 7","details":{"exitCode":7}}}`；`--dry-run`：`{"ok":true,"data":{"dryRun":true,"fileName":"cmd.exe","arguments":"/k \"D:\\bat\\备份.bat\"","workingDirectory":"D:\\bat"}}` |
| `config get` | `[key]`，key ∈ `hotkey`/`hotkey.enabled`/`db.path`/`db.source`/`settings.path`/`gui.path`/`version` | `bcm config get --json` | `{"ok":true,"data":{"db":{"path":"...app.db","source":"localAppData"},"settings":{"path":"...settings.json"},"hotkey":{"enabled":true,"key":57,"keyName":"N","modifiers":6,"display":"Ctrl+Shift+N"},"gui":{"path":"...BashCommandManager.exe"},"version":"1.2.1"}}` |
| `config set` | `hotkey <Ctrl+Shift+N>` 或 `hotkey.enabled <true\|false>` | `bcm config set hotkey "Ctrl+Shift+N"` | `{"ok":true,"data":{"hotkey":{"enabled":true,"key":57,"keyName":"N","modifiers":6,"display":"Ctrl+Shift+N"}}}` |
| `open` | `[--command <id>]` `[--gui <path>]`（查找顺序：`--gui` > `BASHCOMMANDMANAGER_GUI` > `{cliDir}\..\BashCommandManager.exe` > `{cliDir}\BashCommandManager.exe`） | `bcm open --command 7` | `{"ok":true,"data":{"opened":true,"guiPath":"...BashCommandManager.exe","commandId":7}}` |
| `--version` | — | `bcm --version` | 非 JSON 输出 `1.2.1`；`--json` 下 `{"ok":true,"data":{"version":"1.2.1"}}` |

参数语义（确定性规则）：

- `<id|name>`：纯数字按 Id；否则名称精确匹配（去首尾空白、`OrdinalIgnoreCase`）；0 条 → 2 `E_NOT_FOUND`，多条 → 1 `E_AMBIGUOUS` 并输出候选 `[{id,name,groupId}]`；`--group` 先限定候选。批量列表按逗号分隔，名称含逗号时只能用 id。
- `<id|path>`：数字按 Id；否则按 `父/子/孙` 逐段匹配分组名；0 条 → 2，多条 → 1；分组 Id 1 名称为“根分组”。
- `commands import`：目录展开 `*.bat;*.cmd`，`--recursive` 才递归；`Name=文件名去扩展名`、`Description=""`、`SortOrder=同级现有命令数+序号`；默认按 `FilePath`（忽略大小写）跳过已存在项并在 `skipped` 回报，`--allow-duplicates` 关闭检查；`--dry-run` 不写库。
- `commands export`：`groups` 仅含被导出命令引用的分组（按 Id 升序去重）；CSV 固定列 `id,name,description,filePath,groupId,groupName,sortOrder,executionCount,lastExecutedAt`，RFC4180 转义，UTF-8 无 BOM。
- `run`：默认 `cmd.exe /k "<file>"`、`UseShellExecute=true`、`WorkingDirectory=脚本目录`（与 `CommandExecutor.Execute` 完全一致）；`--wait` 用 `cmd.exe /c`，成功启动后按 `RecordExecutionAsync` 同一算法写 `ExecutionCount+1`、`LastExecutedAt=DateTime.Now`（`--dry-run` 不写、启动失败不写）；`--wait` 子进程 0→0、非 0→3 `E_COMMAND_FAILED`；`--timeout` 只在 `--wait` 下有效（否则 1），超时杀进程树→3 `E_TIMEOUT`；`--admin` 加 `Verb=runas`，与 `--wait`/`--timeout` 同时出现→1。
- `--json` + `run --wait`：子进程 stdout/stderr 重定向后**逐字节转发到本进程 stderr**，stdout 只留 JSON，避免中文控制台编码问题与 JSON 污染。
- `config set hotkey`：`Ctrl/Control`、`Alt`、`Shift`、`Win/Windows` 任意顺序 + 单键（A-Z、0-9、F1-F24、Space、Tab、Enter、Escape、Insert、Delete、Home、End、PageUp、PageDown、方向键）；至少一个修饰键；整数即 `System.Windows.Input.Key`/`ModifierKeys` 枚举值（实测 GUI 写入 `Key:57, Modifiers:6` = Ctrl+Shift+N）；写入时保留 `settings.json` 其他节点。

### 4. 输出契约与退出码

- 成功：`{"ok":true,"data":<对象|数组>,"meta":{...}}` 写 **stdout**；`meta` 必含 `dbPath`、`created`，数组结果至少再加 `count`，`durationMs`/`warning` 可选；第 3 节表格中单对象示例只列 `data`，数组示例的 `meta` 为节选，实际按本节输出。
- 失败：`{"ok":false,"error":{"code":"E_NOT_FOUND","message":"未找到命令: 备份","hint":"用 bcm commands list --json 查看可用 id","details":{...}}}` 写 **stderr**；`details` 可选（如 `run --wait` 的 `exitCode`）。stdout 始终只含成功数据，便于管道。
- 人类可读：列表用等宽表格（`Id  Name  Group  FilePath`），单对象 `key: value`，错误 `错误 [E_CODE] 消息` 写 stderr；`Console.IsOutputRedirected` 为 true 时不输出 ANSI 颜色。
- UTF-8：`Console.OutputEncoding = new UTF8Encoding(false)`；`JsonSerializerOptions` 用 `WriteIndented=false` + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`（中文不转 `\uXXXX`）；`lastExecutedAt` 输出本地时间 `yyyy-MM-ddTHH:mm:ss`，空值 `null`。
- 退出码：`0` 成功；`1` 参数/用法错误（未知动词或选项、缺值、非法枚举、名称歧义、缺 `--force`、`--admin` 与 `--wait` 冲突、`--timeout` 单独用、`--db` 指向目录）；`2` 未找到（命令/分组/GUI 可执行文件）；`3` 运行时失败（数据库打开/写入/被占用、脚本文件缺失、子进程非 0、UAC 取消、超时）。
- 错误码固定集合：`E_USAGE`、`E_INVALID_VALUE`、`E_AMBIGUOUS`、`E_CONFIRM_REQUIRED`、`E_ROOT_GROUP`、`E_NOT_FOUND`、`E_FILE_NOT_FOUND`、`E_GUI_NOT_FOUND`、`E_CONFIG_KEY`、`E_DB_ERROR`、`E_DB_BUSY`、`E_COMMAND_FAILED`、`E_TIMEOUT`、`E_ELEVATION_CANCELLED`。
- 并发：每个数据库操作捕获 `SQLiteException` 的 `Busy`/`Locked`，按 200/500/1000ms 重试 3 次后退出 3 `E_DB_BUSY`。
- 绝无交互：不调用 `Dialog`、`ReadLine`、`ReadKey`；`--admin` 的 UAC 是 OS 行为，无桌面会话不可用。

### 5. Skill 设计（`.claude/skills/bash-command-manager/SKILL.md`）

```markdown
---
name: bash-command-manager
description: Use when an agent needs to list, search, create, edit, move, delete or run BashCommandManager batch commands, manage groups, change the global hotkey, or open the BashCommandManager GUI on Windows via the bcm CLI
---
```

正文结构（每节都要有可执行示例，禁止空泛描述）：

1. **When to Use / When Not to Use**：读写本机 BashCommandManager 数据、运行已注册批处理、打开 GUI 时使用；不要用它执行未注册的任意命令，不要直接改 SQLite。
2. **Locating the CLI**：`Get-Command bcm` → 读取卸载项 `InstallLocation` + `\cli\bcm.exe`（`HKLM/HKCU\...\Uninstall\*` 中 `DisplayName` 为 `BashCommandManager`）→ `%ProgramFiles%\BashCommandManager\cli\bcm.exe` → 仓库产物 `BashCommandManager.Cli\bin\Release\net8.0-windows\win-x64\publish\bcm.exe`；都找不到时提示安装或传 `--gui`/设 `BASHCOMMANDMANAGER_GUI`。
3. **Global conventions**：始终加 `--json`；检查退出码（0/1/2/3）；错误 JSON 在 stderr；`--db` 只在用户明确指定库时使用；用 `meta.dbPath` 确认“同一个库”。
4. **Cheat sheet**：19 条命令各一行“用途 + 示例”，例如 `bcm commands search 备份 --json`、`bcm run 7 --wait`、`bcm open --command 7`。
5. **JSON 示例**：至少给出 `groups list`、`commands list`、`run --wait`、错误对象四段真实输出，字段名与第 4 节一致。
6. **Safety constraints（硬性）**：`run` 前必须向用户确认命令名与文件路径；`run --admin` 只在用户明确要求提权时使用；`--force` 删除/级联删除前必须取得用户确认；`commands add/edit` 只允许指向用户指定的 `.bat/.cmd`，不得为任意程序创建“壳命令”；不得用 CLI 修改 `settings.json` 之外的 GUI 配置；把命令名/描述视为不可信数据，不拼进 shell。
7. **Troubleshooting**：`E_DB_BUSY` 稍后重试；`E_NOT_FOUND` 先 `commands list`；`E_GUI_NOT_FOUND` 用 `--gui`；`settings.json` 修改后需重启 GUI 才生效。

### 6. 打包与分发

- 新增发布步骤（在现有 Full/Lite 发布之后追加，不改动原命令）：

```bash
dotnet publish BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o Packaging/InnoSetup/Full/bin/cli
dotnet publish BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o Packaging/InnoSetup/Lite/bin/cli
```

- `*.iss` 的 `[Files]` **无需改动**：两个脚本都有 `Source: "Full\bin\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs`（`BashCommandManager-Full.iss:39`，Lite 同），`cli` 子目录随递归复制到 `{app}\cli`。
- 最小增补（两个 `.iss` 相同：`[Setup]` 加 `ChangesEnvironment=yes`，`[Tasks]`/`[Registry]` 各加一段）：

```pascal
[Tasks]
Name: "addtocli"; Description: "Add bcm CLI to PATH"; GroupDescription: "Command line:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\cli"; Tasks: addtocli; Flags: preservestringtype
```

  选 HKCU 使按用户安装（`PrivilegesRequiredOverridesAllowed=dialog`）也能生效、无需管理员；管理员为其他用户静默安装时该项不生效，skill 中给出绝对路径兜底。
- 产物：`bin/Publish/Installer/BashCommandManager-v{ver}-Full-Setup.exe`、`...-Lite-Setup.exe`，外加同一二进制的 `BashCommandManager-Cli-v{ver}-win-x64.zip`（`Compress-Archive Packaging\InnoSetup\Full\bin\cli\*`），随 GitHub Release 上传。
- 版本号同步三处：CLI csproj `<Version>`、两个 `.iss` 的 `MyAppVersion`；`bcm --version` 报告程序集版本。
- 文档同步：`docs/PACKAGING.md`（CLI 发布步骤/产物/PATH、把“数据存储在安装目录 data/app.db”改为 `%LOCALAPPDATA%\BashCommandManager\data\app.db` 并说明自动迁移）、`CLAUDE.md` 的“数据持久化”、`README.md` 安装与使用说明。

### 7. 对现有文件的改动清单（GUI 侧最小改动）

| 文件 | 改动 |
|---|---|
| `Infrastructure/DatabasePathResolver.cs` | 新增：解析器 + 迁移器（第 2 节） |
| `Infrastructure/DatabaseInitializer.cs` | 构造函数支持 `explicitDatabasePath`；用解析器/迁移器并建目录；新增 `DatabasePath`、`MigrationWarning`；连接串加 `Foreign Keys=True` |
| `Core/Services/CommandService.cs` | 新增 `Task<Command?> GetByIdAsync(int id)`（WPF-free，供 GUI 定位与测试） |
| `Core/Services/SingleInstanceService.cs` | `Initialize(Action<string[]> onActivate, string[] startupArgs)`；管道消息 `ACTIVATE\t<json args>`，兼容旧 `ACTIVATE` |
| `App.xaml.cs`、`MainWindow.xaml.cs` | 传入 `e.Args`；新增 `MainWindow.ApplyStartupArguments(string[])`：`--command <id>` → `MainViewModel.SelectCommandAsync(id)` |
| `ViewModels/MainViewModel.cs`、`GroupTreeViewModel.cs` | 新增 `SelectCommandAsync(int)`（取命令→选分组→`SearchKeyword=命令名`→`SearchCommand`，复用 `MainViewModel.cs:57-79`）与 `FindGroupById(int)` |
| `BashCommandManager.csproj` | 追加第 1 节的 `Compile/None/Page/Resource Remove` |
| `.claude/skills/bash-command-manager/SKILL.md`、`BashCommandManager.Cli/**`、`tests/BashCommandManager.Cli.Tests/**` | 新增（第 5 节 / 第 1 节 / 测试策略） |
| `Packaging/InnoSetup/*.iss` | 可选 PATH 任务与注册表（第 6 节） |
| `docs/PACKAGING.md`、`CLAUDE.md`、`README.md` | 路径与 CLI 说明 |

## 备选方案与取舍

| 方案 | 做法 | 优点 | 缺点/代价 | 结论 |
|---|---|---|---|---|
| A（采纳） | 独立 CLI 项目 + 源码链接 WPF-free 子集 | 无 WPF 依赖、发布小、GUI 产物零变化、可单测 | 链接清单与少量命令层代码重复 | **采纳** |
| B | `ProjectReference` 主工程（WinExe/WPF） | 零重复、行为天然一致 | TFM 必须精确匹配且 `UseWPF=true`；框架依赖需 Desktop Runtime，自包含重复 150MB WPF；输出夹带 GUI 资源 | 否决 |
| C | 抽出 `BashCommandManager.Core` 类库（移走 WPF 相关类） | 真正分层，CLI/GUI 共享全部服务 | 需移动文件、改命名空间/DI、把对话框逻辑上移，改动面大且本轮无法构建验证 | 记为后续演进 |
| D | CLI 只做 IPC/HTTP 客户端，由 GUI 执行 | 不碰数据库、天然单写者 | GUI 必须运行；批量/离线不可用；需新增服务端与鉴权 | 否决 |

## 测试策略

**单元测试**（`tests/BashCommandManager.Cli.Tests/`，xUnit + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`，`net8.0-windows`，`ProjectReference` CLI；主工程已 `Compile Remove="tests/**"`）：

| 测试类 | 覆盖点 |
|---|---|
| `CommandLineParserTests` | 19 个命令形态（含 `--version`）、全局选项任意位置、`--name "值 带空格"`、缺值/未知选项→`E_USAGE`、`--admin --wait` 冲突、`--timeout` 单独用、`--db` 为目录 |
| `DatabasePathResolverTests` | 优先级（参数>环境>LocalAppData>旧目录>创建）、相对路径、`RequiresMigration` 判定、结果不指向 `BashCommandManager.db` |
| `DatabaseMigratorTests` | 真实临时 SQLite 文件：迁移成功且旧库仍在、目标已存在、备份失败回退旧库并给 warning |
| `JsonEnvelopeTests` | 信封字段、错误写 stderr 的决策（`OutputWriter` 返回值）、中文不转义、数组 `meta.count` |
| `HotkeySpecTests` | `Ctrl+Shift+N`→`Key=57,Modifiers=6`、`Ctrl+Alt+B`、无修饰键→`E_INVALID_VALUE`、未知键、大小写与顺序；抽样断言 A=44、Z=69、D0=34、F1=90、Space=18、Enter=6 |
| `CliSettingsStoreTests` | 扁平 `{Enabled,Key,Modifiers}` 读取/原地写回；嵌套 `{Hotkey:{...},Execution:{...}}` 只改 Hotkey 保留 Execution；文件缺失时写扁平格式；损坏 JSON 返回 `E_CONFIG_KEY` |
| `CommandResolverTests` | id、名称、分组路径、0 条→2、多条→1、`--group` 收窄、名称含逗号 |
| `CommandsImportExportTests` | 临时目录 `.bat/.cmd`：目录展开、递归、重复跳过、`--dry-run` 不写库、JSON/CSV 字段与转义 |
| `CliCommandStoreTests` | 临时 SQLite：分组/命令 CRUD、移动、`RecordExecution` 计数、删分组级联（`Foreign Keys=True`）、拒绝删根分组 |

**集成/端到端冒烟**（`CliSmokeTests`，起真实进程，`--db` 指向 `Path.GetTempPath()` 下新文件）：

1. `bcm --db <tmp>\app.db groups list --json` → 退出 0、`data` 含根分组、`meta.created=true`。
2. `groups create --name 运维 --json` → `commands add --name t --file <tmp>\t.cmd --group 运维 --json` → `commands list --json` 命中；`commands move/delete --force` 后 `list` 为空。
3. `run <id> --dry-run --json` → `fileName=cmd.exe`、参数含引号路径；`run <id> --wait`（`.cmd` 内容 `@exit /b 7`）→ 退出 3 且 `details.exitCode=7`；`run 9999 --json` → 2。
4. `run --wait --json`（`.cmd` 内容 `@echo marker`）：stdout 可被 `ConvertFrom-Json` 解析，`marker` 出现在 stderr（验证不污染 JSON）。
5. `commands export --format csv --output <tmp>\e.csv` → 文件存在且表头为固定列；`--json` 的 `count` 匹配。
6. 所有冒烟用例关闭 stdin（`RedirectStandardInput=true` 后立即 `Close()`），30s 超时判失败。
7. `config set hotkey "Ctrl+Shift+N"` 后 `settings.json` 为 `Key:57,Modifiers:6`，`bcm config get hotkey --json` 显示 `Ctrl+Shift+N`。
8. 迁移：在临时目录构造旧库（`data\app.db` + 一条命令），用 `DatabaseMigrator.Prepare(Resolve(explicitPath: null, environmentValue: null, fileExists: ...))` 断言复制成功、旧库未变、新库可读；进程级迁移在手工验收执行。

**GUI 回归（手工，本设计不执行）**：`dotnet publish` 原命令仍成功且产物无 `bcm`；首次启动把旧 `{app}\data\app.db` 复制到新位置并正常显示原有数据；重启不重复迁移；`bcm open --command <id>` 在 GUI 未运行/已运行时都能定位；GUI 内删除分组后命令被级联删除。

## 验收标准

1. 仓库根目录 `dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o <dir>` 仍成功，产物为 `BashCommandManager.exe`，目录中无 `bcm.exe`，主工程未编译 CLI/测试源码。
2. `dotnet publish BashCommandManager.Cli/... -o Packaging/InnoSetup/Full/bin/cli` 产出 `bcm.exe`；在无 .NET 的机器上 `bcm --version` 可运行（Full 自包含）。
3. 未加 `--db` 时 GUI 与 CLI 的 `config get db.path --json` 完全相同，均为 `%LOCALAPPDATA%\BashCommandManager\data\app.db`；首次运行后旧 `{app}\data\app.db` 仍存在且内容未变。
4. 第 3 节全部命令可执行，`--json` 输出经 `ConvertFrom-Json` 解析后字段名/类型与表格一致。
5. 退出码符合 0/1/2/3 约定（参数错 1、未找到 2、运行失败 3），冒烟用例逐条断言。
6. 中文分组名/命令名/描述在 `--json` 与人类表格中往返无损（UTF-8 无 BOM）。
7. 所有命令在 stdin 立即关闭时 30s 内结束、无弹窗（`--admin` 的 UAC 除外）。
8. 删除类命令无 `--force` 时退出 1；删分组时 `deletedCommandIds` 与实际级联删除数一致。
9. `bcm open --command <id>`：GUI 未运行时启动并定位；已运行时经 NamedPipe 激活定位，不产生第二个窗口。
10. `.claude/skills/bash-command-manager/SKILL.md` 存在、frontmatter 为 `name`+`description`，命令示例与真实 `bcm --help` 一致（抽查 5 条）；单测与冒烟全绿；`docs/PACKAGING.md`、`CLAUDE.md` 已更新。

## 风险与回滚

| 风险 | 触发场景 | 缓解 | 回滚 |
|---|---|---|---|
| 数据分叉 | 旧版 GUI 仍写 `{app}\data\app.db`，新版写 LocalAppData | 发布说明“升级后只用新版”；迁移不删旧库；`BASHCOMMANDMANAGER_DB` 可指回旧库 | 删除新库或设环境变量指回旧库 |
| 迁移失败/半写 | 权限不足、磁盘满、并发启动 | `BackupDatabase` 快照 + 临时文件 + `Move`；失败回退旧库给 warning | 删除目标目录 `app.db*`，旧库仍在 |
| 主工程编译被污染 | 忘记 `Compile Remove` | 第 1 节固化 csproj 片段；验收标准 1 验证 | 移除 CLI 目录或恢复 csproj |
| 外键级联误删 | 启用 `Foreign Keys=True` 后首次删分组 | 与 GUI 删除对话框文案一致；CLI 强制 `--force` 并回报计数 | 连接串去掉 `Foreign Keys=True` |
| `settings.json` schema 漂移（Issue #9） | #9 改为 `{Hotkey,Execution}` 根对象 | `JsonNode` 就地改 Hotkey、保留其余节点；两种格式都有测试 | CLI 停止写 settings，提示在 GUI 修改 |
| 单文件原生库加载失败 | `SQLite.Interop.dll` 未随单文件解包 | `IncludeNativeLibrariesForSelfExtract=true`；冒烟覆盖 | 改为非单文件目录发布 |
| 安装包体积膨胀 | Full 自带自包含 CLI | 单文件 + lzma；预计压缩后增 20-25MB | 不随 Full 分发，只发 zip |
| PATH 修改影响系统 | 勾选 `addtocli` 写 HKCU Path | 默认不勾选、只追加不覆盖、`preservestringtype`；卸载不移除（文档说明） | 从环境变量删除 `{app}\cli` |
| `open --command` 与旧 GUI 不兼容 | 只升级 CLI、GUI 仍旧版 | 旧 GUI 忽略参数仍启动；CLI 返回 0 并 `meta.warning` 提示升级 | 不传 `--command`，只用 `open` |
| 名称歧义被代理误判 | 同名命令/分组 | 强制 `E_AMBIGUOUS` + 候选列表；skill 要求先 `list` 再按 id 操作 | 用 id 重试 |
