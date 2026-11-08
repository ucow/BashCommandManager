# 结论

**阻断（合并前必须修复 2 个阻断级缺陷；修复并回归后可发布）。**

评审对象为 `feat/v1.3.0` 相对 `master` 的 CLI 项目与测试（`BashCommandManager.Cli/**`、`tests/BashCommandManager.Cli.Tests/**`），并交叉核对了 CLI 直接链接的共享源码、打包布局与权威契约 `docs/superpowers/specs/2026-11-08-cli-and-skill-design.md`（下称 spec）。结论是：19 条命令基本接线完整，SQL 全部参数化，命令名/描述没有进入 shell，JSON/CSV 的常规转义正确；但存在两类会直接破坏“升级数据安全”和“`run --wait` 自动化语义”的缺陷，另有 7 个重要问题与一批测试盲区。

## 评审方法与证据

- 通读 `BashCommandManager.Cli/**` 全部 32 个源文件与 `tests/BashCommandManager.Cli.Tests/**` 全部 42 个测试文件，逐条对照 spec 第 3 节命令表（L181–L199）、第 4 节输出/退出码（L213–L220）与 `SKILL.md`。
- 使用本分支构建的 `bcm 1.3.0`（`BashCommandManager.Cli/bin/Debug/net8.0-windows/bcm.exe`，以及 `Packaging/InnoSetup/Full/bin/cli/bcm.exe` 单文件产物）在临时目录、临时 `BASHCOMMANDMANAGER_SETTINGS_DIR`、临时 SQLite 库上实测；未读写真实 `%LOCALAPPDATA%\BashCommandManager` 与真实安装库。
- 关键实测：升级布局模拟（`{app}\cli\bcm.exe` + `{app}\data\app.db`）、`run --wait` 在 Windows PowerShell/自定义模板下的行为、`--timeout` 溢出、CSV 公式注入、`--json` 失败路径的 stderr 字节、`open --command` 首跑。

## 已核对无问题（不作为缺陷）

- **SQL 注入**：`CommandRepository`/`GroupRepository` 的查询全部参数化；`ORDER BY` 列名来自 `SortOption` 枚举白名单（`CommandRepository.cs:129-140`），CLI 未拼接任何用户字符串；`OpenCommand.CommandExists` 也用参数（`OpenCommand.cs:67-69`）。
- **命令名/描述未进入 shell**：`run` 只把 `Command.FilePath` 交给 `TerminalCommandBuilder`（`RunCommand.cs:85`）；`TerminalCommandBuilder` 对 `{file}`/`{dir}` 加双引号、对 PowerShell 单引号字面量做 `''` 转义，并显式拒绝路径中的 `"`（`Core/Services/TerminalCommandBuilder.cs:60-67,197-211`）。通过 `<name>`/`<description>` 无法执行代码。
- **JSON 导出转义**：`JsonNode.ToJsonString` + `UnsafeRelaxedJsonEscaping` 对引号、反斜杠、控制字符仍会正确转义；中文不转 `\uXXXX`（`JsonEnvelope.cs:11-15`）。
- **外键级联**：连接串统一 `Foreign Keys=True`（`Infrastructure/DatabaseInitializer.cs:32`），`groups delete` 的级联删除在测试中真实生效（`CliCommandStoreTests.cs:112-131`）。
- **数据库忙重试**：`CliCommandStore.Retry` 对 `Busy/Locked` 按 200/500/1000ms 重试 3 次后映射 `E_DB_BUSY`（`CliCommandStore.cs:12,169-202`），与 spec L219 一致。
- **stdout 纯净性（数据命令）**：除 `help`/`version` 特例外，所有成功数据都走 stdout、失败 JSON 走 stderr；`run --wait` 的子进程输出被转到 stderr（`ProcessRunner.cs:73-74,102-122`），`--json` 成功时 stdout 仍是可解析 JSON（`Smoke4` 验证）。注意“成功”之外的 stderr 污染见 I-1。

---

## 阻断级（必须修）

### B-1 CLI 的旧库迁移路径指向 `{cli}\data\app.db`，升级后先运行 CLI 会新建空库并让 GUI 跳过迁移（数据分叉/看似丢失）

**位置**

- `Infrastructure/DatabasePathResolver.cs:37`：`LegacyDatabasePath => Path.Combine(AppContext.BaseDirectory, "data", app.db)`；`:46-48` 虽支持传入 `legacyDatabasePath`，但 `:74-75` 的 `ResolveForCurrentProcess` 没有暴露该参数。
- `Infrastructure/DatabasePathResolver.cs:60-68`：先判断 canonical 是否存在，存在就 `RequiresMigration=false`，不再看 legacy。
- `BashCommandManager.Cli/Operations/DatabaseSession.cs:17`：`new DatabaseInitializer(explicitDatabasePath, CliPaths.SettingsDirectory)`，未传 legacy 路径。
- `Packaging/InnoSetup/BashCommandManager-Full.iss:41`（Lite 同）：`cli\bcm.exe` 被复制到 `{app}\cli`，所以 CLI 的 `AppContext.BaseDirectory` 是 `{app}\cli`，而 GUI 旧库在 `{app}\data\app.db`。
- `BashCommandManager.Cli/Operations/OpenCommand.cs:51-56`：只检查 canonical 文件，canonical 不存在直接返回 `false`。

**事实与复现（已用发布产物实测）**

用 `Packaging/InnoSetup/Full/bin/cli/bcm.exe` 构造真实安装布局：

```
{sim}\cli\bcm.exe                 # 发布产物
{sim}\data\app.db                 # 旧版 GUI 库，含 1 条命令 old-command
{sim}\settings\                   # 临时 SETTINGS_DIR，隔离 canonical
```

`BASHCOMMANDMANAGER_SETTINGS_DIR={sim}\settings` 下执行：

```powershell
bcm groups list --json
```

实际结果（节选）：

```json
{"ok":true,"data":[{"id":1,"name":"根分组","parentId":null,"sortOrder":0,"commandCount":0}],
 "meta":{"dbPath":"...\\settings\\data\\app.db","created":true,"count":1,...}}
```

- 旧库 `{sim}\data\app.db` 里的 `old-command` 没有被迁移；CLI 在 `{sim}\settings\data\app.db` **新建了一个只有根分组的空库**。
- 随后 `bcm commands list --json` 返回空数组；旧库仍存在且仍有 1 条命令（Python 查询确认 `Commands` 计数 1）。
- 模拟 GUI 首次启动：`DatabasePathResolver.Resolve` 看到 canonical 已存在（`DatabasePathResolver.cs:60-62`），直接返回 canonical 且 `RequiresMigration=false`，因此 **GUI 也不会再复制旧库**。用户看到的界面是空库；如果用户在新库继续创建命令，就会形成两套数据。
- 同布局下执行 `bcm open --command 1 --json`（旧库确有 id=1）实际返回：

```json
{"ok":false,"error":{"code":"E_NOT_FOUND","message":"未找到命令: 1",...}}
```

因为 `OpenCommand.CommandExists` 只查 canonical（`OpenCommand.cs:51-56`），且 `open` 不属于 `CliHost.NeedsDatabase`（`CliHost.cs:90`），不会触发迁移。

**为什么现有验收没发现**

- 安装包的 `[Run]` 默认是“安装完启动 GUI”（`Packaging/InnoSetup/BashCommandManager-Full.iss:67-68`，`postinstall skipifsilent`），交互安装时 GUI 先启动、完成迁移，掩盖了 CLI 首跑路径；`/SILENT` 安装、用户取消“Launch”、只发 CLI zip、或先执行 `bcm` 再打开 GUI 的场景都会命中。
- 现有 `DatabaseSessionTests` 全部使用显式 `--db`/`BASHCOMMANDMANAGER_DB` 或空临时目录（`DatabaseSessionTests.cs:12-103`）；迁移测试在 GUI 测试工程里直接传 `legacyDatabasePath`，没有覆盖 `{app}\cli` 布局。

**修法（建议按顺序）**

1. `DatabasePathResolver.ResolveForCurrentProcess` 增加可选 `legacyDatabasePath` 参数并透传给 `Resolve`；`DatabaseInitializer` 构造函数同样增加可选参数。
2. CLI 计算候选旧库：`{cliDir}\data\app.db`、`{cliDir}\..\data\app.db`（打包布局下即 `{app}\data\app.db`），取第一个存在者传给 `DatabaseInitializer`。不要在 `DatabaseSession` 里自行硬编码 `".."`；把“从 CLI 目录推导旧库候选”做成可单测的纯函数。
3. `open --command` 不要只查 canonical：用同一套 `Resolve + DatabaseMigrator.Prepare`（只复制已有旧库、不创建空库）得到 effective path，或旧库存在时以只读连接查询旧库。
4. 增加回归测试：
   - `DatabaseSessionTests`：模拟 `{cli}\..\data\app.db`，`groups list` 后旧命令可见、`meta.created=false`、canonical 已含旧数据；再断言 `DatabasePathResolver.Resolve` 返回的 `RequiresMigration=false` 且路径为 canonical。
   - `OpenCommandTests`：只有旧库、canonical 不存在时，`open --command <旧 id>` 必须成功定位（或至少不再误报 `E_NOT_FOUND`），且不创建空库。
   - 用发布产物跑一次真实 `{app}\cli\bcm.exe` 的进程级测试（可放到 Smoke，使用临时布局）。
5. 文档/发布说明明确：**CLI 首次运行也会迁移旧库**；若用户先运行 CLI，数据不会丢。

**风险等级理由**：这是升级路径上的数据可见性/分叉问题，且会绕过 GUI 的迁移保护；只靠文档提醒“先开 GUI”不够，必须在代码中修复。

### B-2 `run --wait` 没有按 spec 强制 `cmd.exe /c`：Windows PowerShell 退出码被改写、Windows Terminal 立即假成功、自定义 `/k` 模板可能永不返回

**位置**

- 契约：spec L207 明确“`--wait` 用 `cmd.exe /c`”；`CommandCatalog.cs:155` 的选项说明也写“（用 /c）”。
- `RunCommand.cs:70-100`：`--wait` 分支先按用户 `ExecutionSettings.Terminal` 解析终端，再构造 `ProcessRunRequest`；只有 `WithoutKeepWindowOpen`（`:180-191`）在“模板恰等于默认模板”时把 `/k` 换成 `/c`，**没有强制 Cmd**。
- `RunCommand.cs:46` 调用 `WithoutKeepWindowOpen`；`:184-189` 仅在 `ArgumentsTemplate` 为空或等于默认模板时生效。
- `ProcessRunner.cs:77-99`：`WaitForExit`/`ExitCode` 等待的是被启动进程本身。

**事实与复现**

1. **Windows PowerShell 退出码被改写（已实测）**：临时 settings 写 `Execution.Terminal=1`（`TerminalKind.WindowsPowerShell`），脚本 `fail.cmd` 内容 `@echo off` + `exit /b 7`，执行：

   ```powershell
   bcm run fail --wait --json
   ```

   实际返回：

   ```json
   {"ok":false,"error":{"code":"E_COMMAND_FAILED","message":"命令退出码 1","details":{"exitCode":1}}}
   ```

   子进程真实退出码是 7，CLI 报告 1；spec 的 `details.exitCode` 契约被破坏。原因是 `powershell.exe -Command "& '<file>'"` 对原生命令非 0 返回只映射为进程退出码 1（本机 Windows PowerShell 5.1 实测）。

2. **Windows Terminal 假成功（按 wt 启动器语义可复现）**：临时 settings 写 `Execution.Terminal=3`，`run fail --wait --dry-run --json` 显示：

   ```json
   {"fileName":"powershell.exe",...}   // Terminal=1 时
   {"fileName":"wt.exe",...}           // Terminal=3 时
   ```

   `wt.exe` 是“启动新窗口后立即退出”的启动器，Windows Terminal 至今没有 `--wait` 参数（官方开放 issue：[microsoft/terminal#8856](https://github.com/microsoft/terminal/issues/8856)，状态 open/Backlog）。因此 `ProcessRunner.WaitForExit` 等到的只是 `wt.exe` 的退出，CLI 会立即返回 `exitCode:0`、`waited:true` 并 `RecordExecutionAsync`，而脚本仍在终端窗口里运行；失败脚本也会被报告为成功。验证步骤（避免在本机弹窗未执行）：`Terminal=3` + 一个 `timeout /t 5` 脚本，`bcm run <id> --wait --json` 应在 1 秒内返回 0，而窗口里的脚本还在跑。

3. **自定义模板可能挂起**：若用户把 `ArgumentsTemplate` 设成非默认的 `/k {file} & echo done`（`WithoutKeepWindowOpen` 不会替换），`--wait` 启动的是 `cmd /k`；当 stdin 不是 EOF（交互控制台/继承控制台）时，`WaitForExit()` 没有默认超时，CLI 会一直不返回。是否命中取决于调用方是否给 CLI 关闭/重定向了 stdin；自动化环境并不总是 EOF，风险真实存在。spec 没有为 `--wait` 定义默认超时。

**影响**：`run --wait` 是 SKILL 推荐给代理的自动化路径（`.claude/skills/bash-command-manager/SKILL.md` Cheat Sheet 的“运行”行），也是最容易出现“假成功/假退出码”的地方；当前测试全部用默认 Cmd（`RunCommandTests` 用 `FakeProcessRunner`，`Smoke3/Smoke4` 用默认 settings），没有覆盖配置终端。

**修法**

- 在 `RunCommand.ExecuteAsync` 的 `wait` 分支显式构造 Cmd 计划，不信任 `settings.Terminal`/`ArgumentsTemplate`/`CustomExecutablePath`：

  ```csharp
  if (wait)
  {
      settings = settings.Clone();
      settings.Terminal = TerminalKind.Cmd;
      settings.CustomExecutablePath = string.Empty;
      settings.ArgumentsTemplate = string.Empty; // 让 Build 使用 /c {file}
      settings.KeepWindowOpen = false;
  }
  ```

  这样 `--wait` 与 spec L207 一致，也顺带消除 `/k` 挂起和 wt 假成功。若产品坚持保留用户终端，则至少：wt 直接 `E_USAGE`（不支持等待），PowerShell 在命令后追加 `; exit $LASTEXITCODE`，并对非默认模板强制 `/c` 或加默认超时。
- 增加测试：
  - `RunCommandTests`：`Terminal=WindowsPowerShell/PowerShell7/WindowsTerminal` 且 `--wait` 时断言 `ProcessRunRequest.FileName` 为 `cmd.exe`、参数含 `/c`；若采用拒绝方案，断言 `E_USAGE`。
  - `RunCommandTests`：自定义模板 `/k {file}` 且 `--wait` 时断言使用 `/c`。
  - Smoke：在单文件产物上跑一次真实 `run --wait`（默认 Cmd）确保未回归。

---

## 重要（建议修）

### I-1 `run --wait --json` 的 stderr 不是纯 JSON：子进程输出与错误信封混在一起，且两种编码混写

**位置**：`ProcessRunner.cs:73-74,102-122`（stdout/stderr 都 `CopyTo` 同一个 `_stderrBytes`）；`RunCommand.cs:93-100`；`OutputWriter.cs:68-77`（错误 JSON 写 stderr）；`CliHost.cs:17`（真实入口传 `Console.OpenStandardError()`）。

**证据**：脚本 `cn.cmd` 用 GBK 写 `echo 中文执行输出` 并 `exit /b 9`，`bcm run cn --wait --json` 的 stderr 原始字节为：

```
D6 D0 CE C4 ... 0D 0A 7B 22 6F 6B 22 3A 66 61 6C 73 65 ...   # GBK 中文 + UTF-8 JSON
```

- 用 GBK 解码：中文正常，JSON 里的“命令退出码”乱码；用 UTF-8 解码：JSON 正常，子进程输出乱码；**不存在一种编码能同时正确解码整个 stderr**。
- 即使忽略编码，`stderr` 也不是 `{"ok":false,...}` 纯 JSON；若子进程最后一段输出没有换行，错误 JSON 会直接粘在子进程输出后面，连“取最后一行解析”都失败。
- 契约冲突：spec L208 有意把子进程输出转发到 stderr 以保持 stdout 纯 JSON；spec L214 又承诺“失败 JSON 写 stderr”，`SKILL.md:42` 让代理解析 stderr。stdout 本身确实是干净的（这点通过），但错误通道不可机器解析。
- 现有 `Smoke4` 只测成功用例且只断言 `Contains("marker")`（`CliSmokeTests.cs:71-85`）；`RunCommandTests` 的失败用例全部用 `FakeProcessRunner`，没有真实输出。

**修法（二选一，建议 1）**

1. JSON 模式下不把子进程字节直接写 stderr：用有界缓冲捕获子进程 stdout/stderr，失败时放进 `error.details.output`（截断，例如 64KB，并标注 `truncated`），成功时放进 `meta.childOutput` 或丢弃；stderr 只保留一个 JSON 信封。人类模式继续逐字节转发。
2. 若坚持转发，至少给子进程输出加行前缀（如 `[child] `）并写进文档/测试契约；同时在 spec 中明确“stderr 可能先包含子进程输出”，并调整 SKILL 的解析方式。

**测试**：`CliHost`/Smoke 增加“脚本向 stdout 和 stderr 各写中文、然后 exit 7”的 `run --wait --json` 用例，断言 stderr 可被 `JsonNode.Parse` 解析（方案 1），且 `details.output` 含 child 文本（截断标志正确）。

### I-2 CSV 导出存在公式注入（CSV injection）

**位置**：`CommandCommands.cs:399-406`（`EscapeCsv` 只处理逗号/引号/换行）；`:378-397` 的 `BuildCsv` 把 `name/description/filePath/groupName` 原样输出。

**证据（已实测）**：

```powershell
bcm commands add --name "=1+1" --description "..." --file ok.cmd --db app.db --json
bcm commands export --format csv --output export.csv --db app.db --json
```

CSV 数据行（节选）：

```csv
4,=1+1,"=HYPERLINK(http://evil,click)",C:\...\ok.cmd,1,根分组,3,0,
```

`=1+1` 未被加任何前缀；在 Excel 中打开会被当作公式执行。`commands import` 的 `Name` 来自文件名（`ImportAsync:239`），攻击者可诱导用户导入一个下载来的 `.bat`（文件名为 `=cmd|' /C calc'!A0.bat` 之类）再导出 CSV 打开，形成“文件名 → CSV → Excel 公式”的链路。JSON 导出没有这个问题。

**修法**：在 `EscapeCsv` 开头对文本字段做公式前缀中和：

```csharp
if (value.Length > 0 && (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
{
    value = "'" + value; // Excel 视为文本；同时仍走下面的 RFC4180 引号规则
}
```

不要只加引号（Excel 会去掉引号后继续当公式）。在 CSV 导出文档里说明该行为；如果产品要求导出值与库中完全一致，则改为输出 `sep=,`/制表符前缀并在文档中提示“不要直接双击打开”。增加 `ImportExportTests` 用例：name/description 以 `=`、`+`、`-`、`@` 开头，断言文件里不以裸前缀出现在单元格开头。

### I-3 `open --db` 的校验库与 GUI 实际使用的库不一致，可能返回“定位成功”但 GUI 看不到命令

**位置**：`OpenCommand.cs:21-25` 用 `services.DatabaseResolution.DatabasePath` 校验；`:37` `services.Launcher.Launch(guiPath, commandId)`；`GuiLauncher.cs:71-83` 只传 `--command <id>`，没有 DB 环境/参数。

**事实**：`--db` 是全局选项（`HelpCatalog.cs:28`、`CommandCatalog` 的 Usage 都列出），用户自然会写 `bcm open --command 7 --db D:\portable\app.db`。CLI 在 `D:\portable\app.db` 校验 id=7，但 GUI 进程没有收到任何 DB 参数，会按自己的解析（默认 LocalAppData 或继承的 `BASHCOMMANDMANAGER_DB`）打开另一个库；id=7 可能对应另一条命令或不存在。CLI 仍返回 `{"opened":true,"commandId":7}`，属于假成功。`BASHCOMMANDMANAGER_SETTINGS_DIR` 也会造成同类偏差（GUI 不读该变量）。

**修法（二选一）**

1. 把 DB 传下去：`GuiLauncher.Launch(guiPath, commandId, databasePath)`，在 `ProcessStartInfo.Environment[DatabasePathResolver.EnvironmentVariableName] = databasePath`（仅当 `--db` 显式给出或 effective path 与默认不同）；GUI 已支持 `BASHCOMMANDMANAGER_DB`。
2. 明确拒绝：`open` 在 `ParsedCommand.DatabasePath != null` 时返回 `E_USAGE`（“open 不支持 --db，请用 BASHCOMMANDMANAGER_DB 或让 GUI 使用默认库”），并更新帮助/SKILL。

**测试**：`FakeGuiProcessStarter` 断言子进程 `Environment["BASHCOMMANDMANAGER_DB"]`；`OpenCommandTests` 增加“`--db` 指向另一库且命令只在该库”的组合。

### I-4 `BASHCOMMANDMANAGER_SETTINGS_DIR` 会重定向数据库，但 CLI 帮助、README、CLAUDE、PACKAGING、SKILL 都没有说明

**位置**：`CliPaths.cs:8-18`；`DatabaseSession.cs:16-17`（把 `CliPaths.SettingsDirectory` 传给 `DatabaseInitializer`，canonical 变成 `<dir>\data\app.db`）；`CliHost.cs:56`（非 DB 命令也用它解析）；`HelpCatalog.cs:26-30` 只列 `--db`，没有环境变量段。

**事实**：全仓库 grep `BASHCOMMANDMANAGER_SETTINGS_DIR` 只命中 CLI 源码、内部计划/评审和测试（`DatabaseSessionTests.cs:81-103`、`RunCommandHostTests.cs:27-28`、`CliProcess.cs:70-71`）；`README.md`、`CLAUDE.md`、`docs/PACKAGING.md`、`SKILL.md`、`bcm --help` 均无该变量。用户在/代理环境里设置该变量（例如为测试隔离 settings）后，所有不带 `--db` 的 `groups/commands/run` 会静默操作 `<dir>\data\app.db`，而 `bcm config get db.source` 仍报告 `localAppData`（`ConfigCommand.cs:85-91`）；GUI 不读该变量，CLI 与 GUI 会指向不同库。

**修法**

- `HelpCatalog.Global` 增加“环境变量”段：`BASHCOMMANDMANAGER_DB`、`BASHCOMMANDMANAGER_SETTINGS_DIR`（注明“未指定 --db 时数据库也随之改为 `<dir>\data\app.db`，仅供测试/便携，正常使用请用 BASHCOMMANDMANAGER_DB”）、`BASHCOMMANDMANAGER_GUI`。
- README/CLAUDE/PACKAGING/SKILL 同步；`HelpCatalogTests` 增加断言。
- 若要更严谨：`config get db.source` 对 settings-dir 覆盖返回 `settingsDirectory`（新增枚举值/来源字符串），并在 `meta` 中提示 override；或把该变量限制为“仅测试”并拒绝在正式发布中生效。

### I-5 `config set` 非原子写 settings.json；GUI 运行时的内存缓存会在下一次保存时覆盖 CLI 的改动

**位置**：`CliSettingsStore.cs:161-175`（`_writeAllText` → 默认 `File.WriteAllText`，`:63`）；对照 GUI `Core/Services/SettingsService.cs:55`（启动时 `LoadAll` 一次）、`:62-73`（保存整体 `_appSettings`）、`:175-194`（temp + `File.Move` 原子写）。

**事实/影响**

- CLI 写 settings.json 没有临时文件 + rename；进程被 Ctrl+C/超时/磁盘满打断时会留下截断的 JSON。`CliSettingsStore.LoadRoot` 对损坏 JSON 抛 `E_CONFIG_KEY`（`:142-158`），之后 `config get/set` 和所有 `run` 都失败，而 GUI 的 `SettingsService` 能备份损坏文件并回退默认值（`SettingsService.cs:83-124`）。
- GUI 通常常驻托盘；它只在启动时读 settings。CLI `config set hotkey` 成功后，GUI 内存里仍是旧值；用户随后在设置窗口保存（哪怕只改 Execution），GUI `SaveAll` 会把缓存的旧 Hotkey 整段写回，**静默回滚 CLI 的修改**。SKILL 只写“改完快捷键需重启 GUI 生效”（`.claude/skills/bash-command-manager/SKILL.md` Troubleshooting），没有提示 GUI 保存会覆盖 CLI 改动。
- 这是 CLI 选择直接写共享 settings.json 的后果；GUI 侧文件监听/合并可由 GUI 评审处理，但 CLI 至少应原子写并文档化“先退出 GUI 再 config set”。

**修法**

- `CliSettingsStore.Save` 改为与 GUI 一致的临时文件 + `File.Move(temp, SettingsPath, overwrite: true)`，异常映射 `E_CONFIG_KEY`/`E_INTERNAL` 并保留原文件。
- SKILL/README 写明：`config set` 前先退出 GUI（或改完重启 GUI），不要把 CLI 与 GUI 的设置窗口同时使用。
- 若允许并发：GUI 增加 `FileSystemWatcher` 重载（GUI 评审项）；CLI 写前可选做一次“读取版本/时间戳”检查，但不必在本轮实现。

### I-6 执行统计写入失败会把已经成功执行的脚本变成 exit 3；`RecordExecution` 的读-改-写还会丢计数

**位置**：`RunCommand.cs:117`（wait 路径在子进程成功/失败后、返回前调用 `RecordExecutionAsync`）、`:146`（非 wait 路径在 `Start` 后调用）；`CliCommandStore.cs:130-141`（先 `GetByIdAsync`，`ExecutionCount += 1`，再 `UpdateAsync`）。

**事实/影响**

- 脚本已经真实执行后，如果 `RecordExecutionAsync` 因锁/只读/磁盘错误最终失败，`CliHost` 会把它映射为 `E_DB_ERROR` 退出 3；自动化会认为“命令失败”并可能重跑，造成重复副作用。spec L207 只定义了子进程 0/非 0 的退出码，没有定义“脚本成功但统计写失败”的策略。
- `RecordExecutionAsync` 是两次独立语句的读-改-写：GUI 与 CLI 并发执行同一命令时，两边都读到 5，各自写 6，丢失一次计数；`LastExecutedAt` 也会被后写者覆盖。
- 另一个语义不一致：`--wait` 超时路径在 `RecordExecutionAsync` 之前就 `throw`（`RunCommand.cs:112-115`），超时的命令实际已启动并运行过，但统计不增加；现有测试 `Wait_timeout_raises_timeout_and_does_not_record_execution`（`RunCommandTests.cs:157-171`）固化了该行为，与 spec“成功启动后写统计”存在解释冲突。

**修法**

- `CliCommandStore.RecordExecutionAsync` 改为单条原子语句（参数化，非拼接）：

  ```sql
  UPDATE Commands
  SET ExecutionCount = ExecutionCount + 1, LastExecutedAt = @Now
  WHERE Id = @Id
  ```

  这样不存在丢失更新，也少一次读。
- `RunCommand` 把统计写入失败降级为 `meta.warning`（或在成功 `CliResult` 中带 warning），**不要**在脚本已成功时返回 exit 3；如果产品要求“统计失败也算失败”，则新增独立错误码/退出码并在 spec 写明，且提示“不要重试脚本”。
- 明确超时是否计数：建议超时也记（命令已运行），或至少在 spec 中写明“超时不计数”并保留测试。

### I-7 `commands add/edit` 接受任意可执行文件，`run` 会直接执行它；与 `commands import` 的扩展名过滤和 SKILL 的硬性约束不一致

**位置**：`CommandCommands.cs:63-71`（Add 只 `File.Exists`）、`:121-129`（Edit 只 `File.Exists`）；对照 `:206-215`（Import 显式文件要求 `.bat/.cmd`）；`SKILL.md:127`“`commands add/edit` 只允许指向 `.bat/.cmd` … 不得为任意程序创建壳命令”。

**证据（已实测）**：

```powershell
bcm commands add --name notabatch --file C:\Windows\System32\cmd.exe --db app.db --json
# => {"ok":true,"data":{"id":3,...}}

bcm run notabatch --dry-run --db app.db --json
# => {"fileName":"cmd.exe","arguments":"/k \"C:\\Windows\\System32\\cmd.exe\"",...}
```

即 CLI 可以注册一个 `.exe`，`run` 会通过 `cmd /k` 执行它；`.ps1`、`.vbs` 等也同理。GUI 的 `OpenFileDialog` 同时有“所有文件|*.*”（`Core/Services/CommandService.cs:52-55`），所以产品可能本来就允许任意文件；但 `import` 已经按 `.bat/.cmd` 过滤，SKILL 又把它写成硬性安全约束，CLI 行为自相矛盾，且代理可能因此绕过“只管理批处理”的产品边界。

**修法（二选一，推荐 1）**

1. Add/Edit 与 Import 统一：`Path.GetExtension(path)` 忽略大小写只允许 `.bat`/`.cmd`，否则 `E_INVALID_VALUE`（hint 指向 `.bat/.cmd`）；补测试拒绝 `.exe`/`.txt`/`.ps1`。
2. 若产品确实允许任意文件：同步修改 `CommandCatalog` 的 `commands.add/edit --file` 说明、SKILL 的安全约束和 README，明确“CLI 会执行任意可执行文件”，并考虑把 `--file` 限制在用户指定路径（这已是现状）。

---

## 次要

### S-1 `--timeout` 只校验正数，`seconds * 1000` 会 int 溢出并返回 `E_INTERNAL`

- 位置：`CommandLineParser.cs:311-321`；`ProcessRunner.cs:79`。
- 实测：`bcm run ok --wait --timeout 2147484 --json`（脚本立即成功）→ 退出 3：

  ```json
  {"ok":false,"error":{"code":"E_INTERNAL","message":"内部错误: millisecondsTimeout ('-2147483296') must be greater than or equal to '-1'..."}}
  ```

- 修法：在 parser 加上限（例如 `seconds <= int.MaxValue / 1000`，或直接限制 1–86400），或 `ProcessRunner` 用 `TimeSpan`/`checked` 并映射 `E_INVALID_VALUE`；补边界测试。

### S-2 `run --dry-run` 不应用终端缺失的降级，预览与实际执行不一致

- 位置：`RunCommand.cs:49-62`（dry-run 在 `:70-77` 的 locator/fallback 之前）。
- 事实：`Terminal=WindowsTerminal` 但本机无 wt（或 `PowerShell7` 无 pwsh）时，实际 `run` 会降级 `cmd.exe` 并写 `meta.warning`；`run --dry-run` 仍输出 `fileName: wt.exe`/`pwsh.exe`（前缀 `BuildPlan(settings, entity)` 没有 `resolvedExecutable`），用户/代理会误判。无 GUI 的自定义终端同理：dry-run 显示自定义路径，实际执行报 `E_FILE_NOT_FOUND`。
- 修法：先做 locator/fallback，再进入 dry-run；把 `extraMeta.warning` 一并输出在 dry-run 的 meta 中。补 `Dry_run_with_missing_wt_reports_cmd` 测试。

### S-3 `--version --json` 没有 `meta`，与 spec 第 4 节“meta 必含 dbPath/created”冲突；`--json --help` 则输出纯文本

- 位置：`CliHost.cs:42-46`（version 传 `meta: null`）、`:30-40`（help 直接 `stdout.Write`，忽略 `parsed.Json`）；`CliHostHelpVersionTests.cs:45-55` 断言 `root["meta"]` 为 null，把该行为固化了。
- 冲突：spec L213 说成功信封 `meta` 必含 `dbPath`、`created`；spec L199 的 version 示例又没有 meta。SKILL 让代理用 `meta.dbPath` 确认库（`SKILL.md:45`），对 `--version` 会得到 null。
- 修法：二选一并同步 spec/测试/文档：version 也带 `{dbPath,created,durationMs}`；或明确“`version` 是唯一无 meta 的命令”并从 SKILL 的通用规则中豁免。`--json --help` 至少要在 SKILL/帮助里声明“help 永远是人类可读文本，即使带 --json”。

### S-4 `bcm groups --help` / `bcm commands --help` 显示的是全局帮助，而不是动词级帮助

- 位置：`CommandLineParser.cs:110-119`（子动词缺失且带 `--help` 时返回 `CommandPath="help"`）；`CliHost.cs:30-34`（`"help"` → `HelpCatalog.Global()`）；`HelpCatalog.For` 只接受完整 path（`HelpCatalog.cs:43-46`）。
- 实测：`bcm groups --help` 输出全局帮助并退出 0；`bcm groups list --help` 正常。`bcm help`（不带 `--`）则返回 `E_USAGE`（未知命令）。
- 修法：解析器返回 `help.groups` 之类的动词级路径，`HelpCatalog` 增加 `ForVerb` 展示子命令列表；顺带支持 `bcm help [verb]`；补 `bcm groups --help` 测试。

### S-5 `E_AMBIGUOUS` 的 hint 对分组不适用（分组没有 `--group`）

- 位置：`CliFailureException.cs:68-72`（统一 hint 写“用 bcm commands list --json 或 bcm groups list --json 查看 id”）；实际 message 是“{what}不唯一，请改用 id 或 --group”。
- 事实：`CommandResolver.ResolveGroupAsync` 对同名分组调用 `CliErrors.Ambiguous("分组", ...)`（`CommandResolver.cs:25`），但所有分组命令都没有 `--group` 选项，用户按提示操作会得到“不支持选项 --group”。`SKILL.md:138` 也复制了同样的错误提示。
- 修法：`Ambiguous` 增加 `hint`/`message` 参数，分组场景改为“用 id 或 父/子 路径”；同步 SKILL。

### S-6 `run` 的 `E_CONFIG_KEY` hint 让用户运行 `bcm config get`，但 `config get` 不返回 Execution 配置

- 位置：`RunCommand.cs:174-176`（hint：“检查 Execution 配置或运行 bcm config get”）；`ConfigCommand.cs:36-54`（data 只有 db/settings/hotkey/gui/version，没有 Execution）。
- 事实：模板非法/自定义程序为空时，用户按 hint 执行 `bcm config get --json` 看不到 `Execution`，无法定位问题；`SKILL.md:141` 也写了“用 bcm config get --json 检查配置”。
- 修法：要么给 `config get` 增加 `execution` 键（spec 的 key 集合是闭集，需要同步）、要么把 hint/SKILL 改成“在 GUI 设置窗口的「执行」页检查”，并把实际配置值写进 `error.details`。

### S-7 批量删除/移动不是事务，可能部分生效后报错

- 位置：`CliCommandStore.cs:111-117`（逐条 `DeleteAsync`）、`:119-125`（逐条 `MoveToGroupAsync`）。
- 事实：`commands delete 7,8,9 --force` 若第 3 条在重试后仍失败（磁盘/锁/被删除），前两条已删除，CLI 返回 `E_DB_ERROR`，JSON 没有 `deleted` 部分结果；代理只能猜。`commands move` 同理。
- 修法：用 `IDbTransaction` 包住整个批次（`Connection.BeginTransaction()` + Dapper 的 `transaction:`），或用单条 `DELETE FROM Commands WHERE Id IN @ids` / `UPDATE Commands SET GroupId=@g WHERE Id IN @ids`；失败回滚并返回完整错误。补“中间失败不产生部分删除”的测试。

### S-8 `ProcessRunner` 的并发转发/异常处理细节

- 位置：`ProcessRunner.cs:73-74`（两个 `Task.Run` 同时 `CopyTo` 同一个 `_stderrBytes`）、`:82-88`（只捕 `InvalidOperationException`）、`:98`（`Task.WaitAll` 超时后仍继续，任务可能未观察）、`:33-46`/`:48-70`（`Start` 固定 `UseShellExecute=true`、`RunWithResult` 固定 `false`，忽略 `request.UseShellExecute/RedirectOutput`）。
- 事实：子进程 stdout/stderr 同时大量输出时，两个线程无锁写同一个流，输出顺序不保证、可能交错；`Kill` 若抛 `Win32Exception`（权限/竞态）会落到 `E_INTERNAL` 而不是 `E_TIMEOUT`；`Task.WaitAll` 中若 `Forward` 抛出未捕获异常（如 `NotSupportedException`），会抛 `AggregateException` 变成 `E_INTERNAL`。当前调用方传入的 `UseShellExecute/RedirectOutput` 恰好与硬编码一致，但接口契约已失真。
- 修法：给 `Forward` 写入加锁（或一个 pump 线程用两个独立缓冲后再合并）；`Kill` 捕获 `Win32Exception` 并记录；`WaitAll` 用 try/catch 并短暂等待；要么使用请求字段，要么删除字段避免误用。

### S-9 解析器没有 `--` 终止符；空值路径会落到 `E_INTERNAL`

- 位置：`CommandLineParser.cs:26-44`（任何 `--` 开头都当选项，`--` 本身报“选项名不能为空”）；`CommandCommands.cs:308`/`:328`（`Path.GetFullPath(outputPath!)` 前没有空值校验）。
- 实测：`bcm commands export --format json --output= --db app.db --json` → 退出 3：

  ```json
  {"ok":false,"error":{"code":"E_INTERNAL","message":"内部错误: The path is empty. (Parameter 'path')"}}
  ```

  用户笔误应得到 `E_INVALID_VALUE`/`E_USAGE`，而不是内部错误。
- 修法：支持 `--` 终止符（之后全部按位置参数）；对 `--output`、`import` 的路径参数做 `string.IsNullOrWhiteSpace` 校验并映射 `E_INVALID_VALUE`；补测试。

### S-10 `meta.warning` 单键会被后写者覆盖，可能丢失迁移告警

- 位置：`CliHost.cs:117-129`（先写 `session.MigrationWarning`，再用 `result.Meta` 逐键覆盖）；`RunCommand.cs:76`（终端降级写 `extraMeta["warning"]`）。
- 事实：迁移失败（回退旧库）+ 终端降级同时发生时，最终 `meta.warning` 只有“已改用 cmd”，迁移告警丢失；`meta.dbPath` 仍指向旧库，但用户/代理可能忽略。这是诊断信息的丢失，不是数据损坏。
- 修法：改用 `meta.warnings` 数组（或 `meta.migrationWarning` + `meta.executionWarning` 两个键），保留 `meta.warning` 兼容；补组合测试。

### S-11 资源/工程细节

- `CliHost.cs:59` 的 `using var services` 与 `:86` 的 `finally session?.Dispose()`，加上 `CliServices.cs:60` 的 `Session?.Dispose()`，同一次会话可能 `Dispose` 2–3 次；当前 `SQLiteConnection.Dispose` 幂等、实测未抛，但所有权不清晰，建议只保留一处（由 `CliServices.Dispose` 负责，`CliHost` 不再 `finally`）。
- `BashCommandManager.Cli.csproj:31-32` 链接了 `DatabaseService.cs`、`GroupService.cs`，但 CLI 源码无任何引用（`grep` 0 命中）；建议移除未使用的链接，减少维护面/编译面。
- `RunCommand.cs:146-155` 非 wait 路径的 `started` 硬编码为 `true`，不使用 `startResult.Started`；`ProcessRunner.Start` 总是返回 `Started=true`（即使 `Process.Start` 返回 null）。建议使用真实结果，避免“启动成功”假象。
- `CommandCommands.cs:311`/`:331` 导出直接覆盖已存在的 `--output` 文件；建议存在时 `--force` 或明确提示，避免覆盖上一次备份（与删除类命令的 `--force` 风格一致）。

### S-12 测试质量（重点：断言过弱 / mock 掩盖真实行为）

#### 断言过弱

- `CliSmokeTests.cs:36`：`Assert.Contains("t", list.Stdout)`。`commands list --json` 的 `meta` 里含 `"created"`/`"count"`，`"t"` 必然出现；即使命令没有被列出，测试也通过。应解析 JSON 断言 `data` 中存在 id/name。
- `CliSmokeTests.cs:101`：`Assert.Contains("e", lines[1])`。CSV 表头 `filePath` 含 `e`，数据行无需正确也通过。应断言具体列值（如 `"e.cmd"`、`name=e`）。
- `ConfigCommandTests.cs:101-112`：初始 `HotkeyConfig(false, 45, 3)`，设置 `Ctrl+Alt+B` 后恰好还是 `(45,3)`，因此“设置成功”无法从 Key/Modifiers 断言看出；应换一个不同的键（如 `Ctrl+Shift+K`）或断言写入文件/FakeSettingsStore 的调用。
- `ImportExportTests.cs:59-63`：`Assert.Equal(2, imported + skipped)` 只验证总数，无法区分“deep.bat 被导入”还是“被错误跳过/漏扫”；应断言 `imported` 的 `filePath` 包含 `deep.bat`。
- `CliSettingsStoreTests.cs:169-177`：测试名 `Writes_utf8_without_bom_and_keeps_chinese` 但用例里没有中文；只断言无 BOM。应写入含中文的 `Description`/未知节点并回读断言原文（此前 A 评审 S-6 已指出，仍未补）。

#### Mock/覆盖盲区

- `RunCommandTests.cs` 全部用 `FakeProcessRunner`，只断言请求字段；真实 PS/wt 退出码、子进程输出、超时杀树都没有覆盖。`Smoke3/Smoke4` 只覆盖默认 Cmd。
- `RunCommandTests.cs:138-171` 固化了“超时不计执行次数”，但没有与 spec“成功启动后写统计”对齐的说明。
- 没有“失败 + 子进程输出 + `--json`”的 stderr 契约测试（现有 `Smoke4` 是成功路径）。
- 没有 CSV 公式注入/`=` 前缀测试；没有 Add/Edit 拒绝 `.exe` 的测试（若采纳 I-7）；没有 `--timeout` 上限测试；没有 `open --db` 传播测试；没有升级布局迁移测试（B-1）；没有 `BASHCOMMANDMANAGER_SETTINGS_DIR` 帮助文本测试。
- `ProcessRunnerTests.cs:25-35` 只让子进程写 stderr，没有 stdout/stderr 同时大量输出、混合编码、stdin EOF 的用例；超时用例只断言 `TimedOut`，没有确认子孙进程真的被杀。
- `CliCommandStoreTests` 没有 `Busy/Locked` 重试测试；可用第二个连接持写事务验证“重试后成功/`E_DB_BUSY`”。
- `CliSettingsStoreTests.cs:10-20` 每次 `Create` 建临时目录但从不删除，长期跑会泄漏大量 `%TEMP%\bcm-settings-*`；`TempDatabase.Dispose` 只捕 `IOException`（`TempDatabase.cs:35-44`），未捕 `UnauthorizedAccessException`，失败时同样泄漏。建议统一 `IDisposable` 清理。
- `ProcessRunnerTests.cs:49-58` 的 `Start_with_shell_execute` 会真的用 `UseShellExecute=true` 起 `cmd.exe`，在桌面会话可能闪窗；测试应改用无窗口的 helper 或标记为集成测试。

---

## 复核建议（具体文件:行号 + 修法）

以下按“修复 → 回归”给出最小闭环；行号均为当前工作区。

| ID | 文件:行号 | 修法 | 复核/新增测试 |
|---|---|---|---|
| B-1 | `Infrastructure/DatabasePathResolver.cs:37,74-75`；`BashCommandManager.Cli/Operations/DatabaseSession.cs:17`；`OpenCommand.cs:51-56` | 给 `ResolveForCurrentProcess`/`DatabaseInitializer` 增加 `legacyDatabasePath` 参数；CLI 从 `{cliDir}` 与 `{cliDir}\..\` 找 `data\app.db`；`open` 使用 `Prepare` 后的 effective path 或直接只读查旧库 | `DatabaseSessionTests` 新增 `{cli}\..\data\app.db` 布局迁移用例；`OpenCommandTests` 新增旧库首跑用例；用 `Packaging/InnoSetup/Full/bin/cli/bcm.exe` 做一次进程级模拟 |
| B-2 | `RunCommand.cs:36-47,70-100,180-191`；spec `docs/superpowers/specs/2026-11-08-cli-and-skill-design.md:207` | `wait` 分支强制 `Terminal=Cmd`、清空 `CustomExecutablePath`/`ArgumentsTemplate`、`KeepWindowOpen=false`；或对 wt/PS 返回 `E_USAGE` 并给 PS 追加 `; exit $LASTEXITCODE` | `RunCommandTests` 用 `Terminal=WindowsPowerShell/PowerShell7/WindowsTerminal` + `--wait` 断言 `cmd.exe /c`；自定义 `/k` 模板用例；真实 Cmd smoke 回归 |
| I-1 | `ProcessRunner.cs:73-74,102-122`；`OutputWriter.cs:68-77` | JSON 模式缓冲子进程输出（截断）进 `error.details.output`/`meta.childOutput`，stderr 只留 JSON；人类模式保持转发 | `CliHost`/Smoke：子进程同时写 stdout/stderr 中文并 exit 7，断言 `JsonNode.Parse(stderr)` 成功 |
| I-2 | `CommandCommands.cs:399-406` | 文本字段以 `= + - @ \t \r` 开头时前置 `'`；仍执行 RFC4180 引号转义 | `ImportExportTests`：name/description 以四个前缀开头，断言单元格不以裸前缀开头；JSON 导出回归 |
| I-3 | `OpenCommand.cs:37`；`GuiLauncher.cs:71-83` | `Launch` 增加 `databasePath` 参数，设置子进程 `BASHCOMMANDMANAGER_DB`；或 `open` 对 `--db` 返回 `E_USAGE` | `GuiLauncherTests` 断言 `ProcessStartInfo.Environment`；`OpenCommandTests` 双库用例 |
| I-4 | `CliPaths.cs:8-18`；`HelpCatalog.cs:26-30`；`DatabaseSession.cs:16-17` | help 增加环境变量段；README/CLAUDE/PACKAGING/SKILL 写明 `SETTINGS_DIR` 同时改 DB 且仅 CLI；`config get db.source` 标明 override | `HelpCatalogTests` 断言变量名；文档 grep；`DatabaseSessionTests` 已覆盖行为 |
| I-5 | `CliSettingsStore.cs:161-175`；对照 `Core/Services/SettingsService.cs:175-194` | `Save` 改 temp + `File.Move(overwrite:true)`；SKILL 写明先退出 GUI；可选文件监听（GUI 侧） | `CliSettingsStoreTests` 注入写委托验证 temp/rename；损坏文件恢复测试；手工：GUI 运行中 CLI 改键后 GUI 保存，观察是否覆盖 |
| I-6 | `CliCommandStore.cs:130-141`；`RunCommand.cs:117,146` | 单条 `UPDATE ... ExecutionCount = ExecutionCount + 1`；统计失败降级为 `meta.warning`，不掩码子进程结果；明确超时是否计数 | `CliCommandStoreTests` 两连接并发 +1 断言；`RunCommandTests` 注入 record 失败仍返回成功；超时用例与 spec 对齐 |
| I-7 | `CommandCommands.cs:63-71,121-129`；对照 `:206-215` | Add/Edit 校验 `.bat/.cmd`（忽略大小写）→ `E_INVALID_VALUE`；或产品明确允许任意文件并同步 SKILL/选项说明 | `CommandCommandsTests` 拒绝 `.exe/.txt/.ps1`；或文档/测试明确允许 |
| S-1 | `CommandLineParser.cs:311-321`；`ProcessRunner.cs:79` | 加 `seconds <= int.MaxValue/1000` 上限并映射 `E_INVALID_VALUE` | `ParserValidationTests` 加 `2147484`、`int.MaxValue` 用例 |
| S-2 | `RunCommand.cs:49-62` | 先 locator/fallback，再 dry-run；dry-run `meta` 带 warning | `Dry_run_with_missing_wt_reports_cmd` |
| S-3 | `CliHost.cs:30-40,42-46`；`CliHostHelpVersionTests.cs:54` | 统一 version 是否带 meta；help 在 `--json` 下的行为写进 SKILL/spec；更新测试 | `CliHostHelpVersionTests` 改断言；`HelpCatalogTests` |
| S-4 | `CommandLineParser.cs:110-119`；`HelpCatalog.cs:43-46` | 返回 `help.<verb>`，新增 `ForVerb`；支持 `bcm help <verb>` | `ParserCommandShapeTests` + `HelpCatalogTests` |
| S-5 | `CliFailureException.cs:68-72`；`CommandResolver.cs:25` | `Ambiguous` 增加 hint 参数；分组用“id 或 父/子 路径” | `CommandResolverTests` 断言 group hint |
| S-6 | `RunCommand.cs:174-176`；`ConfigCommand.cs:36-54` | hint 改为 GUI「执行」页，或 `config get` 增加 `execution`；SKILL 同步 | `RunCommandHostTests` 断言新 hint；`ConfigCommandTests` |
| S-7 | `CliCommandStore.cs:111-125` | 批量操作包事务或单条 `IN @ids` | 中间失败回滚测试 |
| S-8 | `ProcessRunner.cs:73-74,82-88,98,33-70` | 写锁/单 pump；捕 `Win32Exception`；`WaitAll` try/catch；使用或删除请求字段 | 同时大量 stdout/stderr、kill 失败、超时后无未观察异常 |
| S-9 | `CommandLineParser.cs:26-44`；`CommandCommands.cs:308,328` | 支持 `--`；路径空值校验 → `E_INVALID_VALUE` | `--output=`、`--` 后位置参数用例 |
| S-10 | `CliHost.cs:117-129`；`RunCommand.cs:76` | `meta.warnings` 数组或分键 | 迁移 warning + 降级 warning 组合用例 |
| S-11 | `CliHost.cs:59,86`；`CliServices.cs:60`；`BashCommandManager.Cli.csproj:31-32` | 单点 Dispose；移除未用链接；`started` 用真实值；导出覆盖确认 | 资源/构建审查 |
| S-12 | `tests/...`（见上） | 修 vacuous/tautological 断言；补真实进程与契约测试；清理临时目录 | `dotnet test` 全绿 + 新增用例逐条验证 |

### 建议的回归命令

```powershell
# 构建与全量测试（当前基线 302/302，修复后应不降）
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release --no-incremental
dotnet test tests\BashCommandManager.Cli.Tests\BashCommandManager.Cli.Tests.csproj

# B-1：真实安装布局（临时目录，勿用真实库）
#   {sim}\cli\bcm.exe + {sim}\data\app.db（含 1 条命令）
#   BASHCOMMANDMANAGER_SETTINGS_DIR={sim}\settings
#   期望 groups list --json 的 data 含旧命令、meta.created=false
#   期望 open --command <旧 id> 不再 E_NOT_FOUND

# B-2：终端矩阵（每个都用临时 settings + 临时库）
#   Terminal=1（WindowsPowerShell）/2（PowerShell7）/3（WindowsTerminal）
#   期望 --wait 仍走 cmd.exe /c，exit 7 时 details.exitCode=7
#   自定义 ArgumentsTemplate="/k {file} & echo done" + --wait 期望仍 /c（或 E_USAGE）

# I-1：失败输出契约
#   带中文 stdout/stderr、exit 7 的脚本 + run --wait --json
#   期望 stdout 为空、stderr 可被 JsonNode.Parse

# I-2：CSV 注入
#   name/description 以 = + - @ 开头，export csv 后单元格应以 ' 开头

# I-4：帮助可发现性
bcm --help | Select-String BASHCOMMANDMANAGER_SETTINGS_DIR
```

---

## 附：19 条命令契约核对摘要（仅列关键偏差）

| 命令 | 结论 |
|---|---|
| `groups list/create/rename/delete` | 通过（`groups --help` 见 S-4；`groups delete` 级联测试通过） |
| `commands list/search/show/frequent` | 通过（弱断言见 S-12） |
| `commands add/edit` | 字段/退出码通过；扩展名策略见 I-7 |
| `commands delete/move` | 功能通过；批量无事务见 S-7；`--group` 提示见 S-5 |
| `commands import` | 通过（重复检测、排序、dry-run 测试通过；空路径见 S-9） |
| `commands export` | 字段/转义通过；CSV 公式注入见 I-2；覆盖行为见 S-11 |
| `run` | 默认/dry-run 基本通过；`--wait` 见 B-2；`--timeout` 溢出见 S-1；stderr 见 I-1 |
| `config get` | 通过；不含 Execution 见 S-6；SETTINGS_DIR 来源见 I-4 |
| `config set` | 通过；原子性/GUI 覆盖见 I-5 |
| `open` | 校验存在性通过；`--db` 传播见 I-3；升级首跑见 B-1 |
| `version` | 数据通过；meta 契约见 S-3 |

---

**评审边界**：未修改任何源码、未 commit；仅新增本报告。测试与模拟均使用临时目录/临时库，未触碰真实用户数据库与 `%LOCALAPPDATA%\BashCommandManager\settings.json`。
