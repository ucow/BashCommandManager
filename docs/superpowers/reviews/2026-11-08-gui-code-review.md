# v1.3.0 GUI 主工程代码评审（`master...HEAD` 范围内）

- 仓库：`F:\WorkSpace\Personal\BashCommandManage_v8`，分支 `feat/v1.3.0`，基线 `master`
- 范围：`git diff master...HEAD -- . ":(exclude)BashCommandManager.Cli" ":(exclude)tests/BashCommandManager.Cli.Tests"`
- 方法：逐文件阅读 + 与 `master` 逐段 diff 对照 + 现有测试通读 + 两项本机探针（引号解析、FK 孤儿数据）；未修改任何产品代码，未 commit
- 验证基线：`dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj -c Release --no-restore` → **167 passed / 0 failed**

# 结论

**需修复后发布（不建议按当前 HEAD 直接发布 v1.3.0）。**

- 共 **2 条阻断**：升级后数据库迁移可能被 CLI 抢先创建的空库永久跳过（旧数据在 GUI 中“消失”）；wt/pwsh 缺失时的 cmd 降级会沿用原终端模板，降级功能实际不可用。
- 共 **9 条重要**：集中在设置保存的一致性/单例状态、迁移失败静默、`--db` 未传给 GUI、安装包 PATH 的幂等与卸载、`{dir}` 根目录引号。
- 共 **16 条次要**：多为边界、可观测性、跨版本兼容与测试覆盖问题。

未发现直接删除/覆盖用户数据库的代码路径：`DatabaseMigrator` 只复制旧库、临时文件 + `File.Move`（目标已存在时不覆盖）、失败回退旧库且不删除旧库；`settings.json` 先写 `.tmp` 再 `File.Move(overwrite:true)`，单进程内是原子替换。**主要数据风险不是“丢文件”，而是“迁移未触发/被跳过导致旧数据不可见”**，以及设置保存失败后的内存/磁盘不一致（见 B-1、I-1、I-4）。

已核对、未发现缺陷的项（避免误报）：

- 热键注册/注销成对：注册在 `App.xaml.cs:68-85`，注销/Dispose 集中在 `App.xaml.cs:129-143`；`MainWindow.OnClosing` 只隐藏、不注销（`MainWindow.xaml.cs:132-146`），托盘隐藏路径不会误注销。
- `HotkeyMessageWindow` 的 HWND 创建前有 `_dispatcher.VerifyAccess()`（`GlobalHotkeyService.cs:31-47`），创建/销毁都实际发生在 UI 线程；托盘/关闭路径不再触碰热键。
- `SingleInstanceService` 的激活回调统一用 `Application.Current.Dispatcher.Invoke` 封送到 UI 线程（`SingleInstanceService.cs:103-113`），`ApplyStartupArguments` 的跨线程调用是安全的。
- `--wait` 与全局 admin 的语义在 CLI 侧被显式拒绝（`BashCommandManager.Cli/CommandLine/CommandLineParser.cs:307-309`、`RunCommand.cs:36-42`）；GUI 没有 wait 语义，未见回归。
- `Foreign Keys=True` 探针：对既有孤儿 `Commands` 行执行非外键列 `UPDATE`、删除已不存在的父组都不会抛错；FK 也不会回溯校验旧数据（但见 m-5）。

## 阻断级（必须修）

### B-1 升级后若先运行 `bcm`，GUI 的旧库永远不会迁移，用户看到“数据消失”

- **位置**：`Infrastructure/DatabasePathResolver.cs:37`（`LegacyDatabasePath = AppContext.BaseDirectory\data\app.db`）、`:60-70`（canonical 存在即不迁移）、`:74-75`（CLI 与 GUI 共用 `ResolveForCurrentProcess`，只认当前进程基目录）；`Infrastructure/DatabaseInitializer.cs:11-12`；CLI 侧证据：`BashCommandManager.Cli/Operations/DatabaseSession.cs:17`、`BashCommandManager.Cli/CliHost.cs:49-51`
- **理由**：安装包的 CLI 位于 `{app}\cli`，所以 CLI 进程的 `LegacyDatabasePath` 是 `{app}\cli\data\app.db`（不存在）；GUI 的旧库在 `{app}\data\app.db`。升级到 v1.3.0 后，如果用户先执行 `bcm groups list` / `bcm commands list` / `bcm run`，`DatabaseSession` 会调用 `initializer.Initialize()` 在 `%LOCALAPPDATA%\BashCommandManager\data\app.db` **立即创建一个只有 schema 的空库**。之后 GUI 启动时 `Resolve` 看到 canonical 已存在 → `RequiresMigration=false` → 永远不再尝试从 `{app}\data\app.db` 迁移。旧数据实际仍在磁盘上，但 GUI/CLI 都只看到空库，用户会认为升级导致数据丢失；`bcm open --command <id>` 在该状态下也会因为 canonical 无该命令而返回 `E_NOT_FOUND`（`OpenCommand.cs:21-25`）。
- **影响**：升级路径的数据可用性（不是文件删除，是永久跳过迁移）；这是发布说明承诺的“首次启动自动复制迁移”失效。
- **修法**：把“旧库候选”与进程基目录解耦，至少同时探测 `{base}\data\app.db` 和 `{base}\..\data\app.db`（CLI 的父目录即 GUI 安装目录）；`Resolve`/`ResolveForCurrentProcess` 改为接收候选列表并取第一个存在者。更稳妥的是由安装包写入一次“安装目录”标记（注册表/版本文件），GUI 与 CLI 都从该标记取旧库路径。修复后新增回归测试：canonical 不存在、`{base}\..\data\app.db` 存在 → `RequiresMigration=true` 且 `Prepare` 复制成功。

### B-2 终端缺失降级 cmd 时沿用原终端模板，降级后的命令行是错的

- **位置**：`Core/Services/CommandExecutor.cs:70-77`（`settings.WithTerminal(TerminalKind.Cmd)` 只改 `Terminal`）、`Core/Services/CommandExecutor.cs:92`（`Build` 使用 `settings.ArgumentsTemplate`）；模板持久化链：`ViewModels/SettingsViewModel.cs:86-94`（切换终端即写入对应默认模板）、`Core/Services/SettingsService.cs:147-152`（非空模板原样保留）、`Core/Services/TerminalCommandBuilder.cs:50-52`（非空模板优先）
- **理由**：用户在设置里选过 Windows Terminal / PowerShell 7 后，`ArgumentsTemplate` 会被保存为该终端的默认模板（如 `-d {dir} cmd.exe /k {file}` 或 `-NoExit -Command "& '{filePs}'"`）。运行时探测不到 `wt.exe`/`pwsh.exe` 时，`WithTerminal(Cmd)` 只替换终端类型，模板仍是 wt/pwsh 的；`Build` 于是生成 `cmd.exe` + `-d "…" cmd.exe /k "…"` / `-NoExit …`，cmd 不认识这些参数，脚本不会按预期执行（通常直接报错/空窗口）。
- **影响**：v1.3.0 发布说明主打的“wt/pwsh 缺失时自动降级 cmd 并提示”在最常见配置下实际不可用；现有测试 `CommandExecutorTests.cs:44-71` 用 `new ExecutionSettings { Terminal = missing }`（模板为空）覆盖，恰好绕过了这条真实路径。
- **修法**：降级分支显式把模板重置为 cmd 默认模板：
  ```csharp
  settings = settings.WithTerminal(TerminalKind.Cmd);
  settings.ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(
      TerminalKind.Cmd, settings.KeepWindowOpen);
  ```
  如果希望保留用户自定义模板，至少只在“模板等于缺失终端默认值”时重置；并在 `notice` 中说明模板已切换。补测试：设 `ArgumentsTemplate = "-d {dir} cmd.exe /k {file}"`、`Resolve(WindowsTerminal)=null`，断言 `StartInfo.FileName == "cmd.exe"` 且 `StartInfo.Arguments` 是 `/k "<脚本>"`。

## 重要（建议修）

### I-1 设置写盘失败时内存已更新；两段设置分两次写导致半保存

- **位置**：`Core/Services/SettingsService.cs:62-74`（先改 `_appSettings` 再 `SaveAll`）、`:175-194`（`SaveAll` 内备份/写临时文件/`File.Move`，任何一步失败都会抛）、`ViewModels/SettingsViewModel.cs:277-293`（先 `SaveHotkeySettings` 再 `SaveExecutionSettings`，第二个失败只弹一行错误）
- **理由**：`SaveHotkeySettings`/`SaveExecutionSettings` 在调用 `SaveAll()` 之前就把新对象挂到内存 `_appSettings`；`SaveAll` 抛异常（磁盘满、文件被占用、备份失败）时调用方只显示“设置保存失败”，但服务内存已经是新值。下一次 `GetExecutionSettings()` 会让 `CommandExecutor` 使用**从未成功落盘**的终端配置；再次保存又可能把该值写下去。两段分别写还有半保存窗口：hotkey 已落盘、execution 未落盘（或反之）。
- **影响**：设置内存/磁盘不一致；失败后本进程行为与下次启动行为不同；对“执行”配置尤其容易造成“我刚保存失败，怎么运行方式变了”。
- **修法**：`SaveAll` 先在克隆/快照上序列化并成功写盘，再提交 `_appSettings`；失败时回滚。为 GUI 提供一次写入两段的 `SaveSettings(HotkeySettings, ExecutionSettings)`（或让 VM 在一个事务里提交），避免半保存。现有 `SettingsServiceTests.cs:186-213`（S8）把“失败后内存已更新”当成期望行为，需同步改为断言回滚/失败结果。

### I-2 `SettingsViewModel` 实际是根容器单例，取消后重开仍显示未保存的修改

- **位置**：`App.xaml.cs:122`（`AddScoped<SettingsViewModel>()`）、`Views/SettingsWindow.xaml.cs:12`（从根 `App.ServiceProvider` 解析）、`ViewModels/MainViewModel.cs:164-171`（`new SettingsWindow()`，手工构造故不会创建新 scope）、`ViewModels/SettingsViewModel.cs:308-312`（Cancel 只关窗，不重载/回滚）
- **理由**：MS.DI 从根 provider 解析 Scoped 服务会得到根 scope 的单例；设置窗口每次打开都拿到同一个 VM，而 VM 只在构造函数里读一次 `ISettingsService`。用户改了终端/快捷键后点“取消”，VM 里的编辑副本不会回滚；再次打开设置页会看到上次未保存的值，再点保存就会把“已取消”的修改写盘。`SettingsViewModel.cs:295` 的注释已经承认“本 VM 由根容器解析、实际是单例”，但没有处理取消/重开。
- **影响**：用户可意外持久化已取消的修改；设置页显示值与磁盘值不一致。
- **修法**：每打开一次设置窗口创建并持有 `IServiceScope`（窗口 `Closed` 时 Dispose），或把 `SettingsViewModel` 注册为 Transient 并由 `SettingsWindow` 显式注入；至少在窗口 `Loaded` 时从服务重新加载一份快照，Cancel 时丢弃。

### I-3 快捷键注册失败会阻断“执行”设置保存；关闭快捷键时先注销后落盘

- **位置**：`ViewModels/SettingsViewModel.cs:232-260`（`UpdateHotkey` 失败直接 `return`，此时 execution 设置还没写）、`:262-266`（`Enabled=false` 先 `Unregister` 再保存）
- **理由**：`HotkeySettings.Enabled` 为 true 时，哪怕用户本次只想改终端配置，只要当前快捷键被其他程序占用（启动时注册失败很常见），Save 就在 hotkey 分支返回，execution 永远写不进去；提示文案“已保留原快捷键”在启动注册就失败、`_currentKey` 为 None 时也不成立。另一分支 `Unregister()` 在 `SaveHotkeySettings`/`SaveExecutionSettings` 之前执行，若随后写盘失败，本次进程已经失去热键，而磁盘仍是 Enabled=true。
- **影响**：用户无法保存与快捷键无关的设置；保存失败后热键状态与磁盘不一致。
- **修法**：先校验并保存 execution（独立失败域），再尝试 hotkey 注册；注册失败时只回退 hotkey 段并提示“执行设置已保存，快捷键未更改”。关闭 Enabled 时先成功落盘再 Unregister，或失败时恢复注册。

### I-4 `DatabaseMigrator` 把“目标已存在”一律当作并发成功，丢弃刚复制出来的旧库快照

- **位置**：`Infrastructure/DatabasePathResolver.cs:107-121`（`File.Exists(targetPath)` → 删 temp、返回 `Migrated=true`；`File.Move` 的 `IOException` 同样处理）
- **理由**：`Resolution.RequiresMigration=true` 的前提是“解析时 canonical 不存在”。复制期间 canonical 出现，只可能是另一个进程创建/迁移了它；当前实现不做任何内容/来源校验，一律丢弃自己的副本并报告 `Migrated=true`。如果对方是 `DatabaseInitializer.Initialize()` 刚创建的空 schema 库（B-1 的 CLI 路径、或另一个 CLI/初始化的并发），旧库数据就被永久忽略；如果对方是并发迁移器，则结果正确，但两种情况无法区分。
- **影响**：与 B-1 叠加会扩大“旧数据不可见”的窗口；`Migrated` 语义不可信，调用方无法据此告警。`DatabaseMigratorTests.cs:57-77` 反而把 `Migrated=true` 固化成期望。
- **修法**：迁移前用锁文件/具名互斥量串行化；或复制完后对比目标是否由迁移产生（写入迁移标记表/`PRAGMA user_version`），目标存在但无标记时保留 temp 并返回带 Warning 的回退结果，至少让调用方知道“旧库未采用”。同时修正测试期望与 `Migrated` 命名。

### I-5 `bcm open --db <path>` 校验的是自定义库，GUI 打开的却是默认库

- **位置**：`App.xaml.cs:47`（GUI 启动只用无参 `new DatabaseInitializer()`，不解析 `--db`）、`Core/Services/StartupArgumentParser.cs:6`（只解析 `--command`）；CLI 侧证据：`BashCommandManager.Cli/Infrastructure/GuiLauncher.cs:71-82`（只传 `--command`，不传 `--db`，也不设置 `BASHCOMMANDMANAGER_DB`）
- **理由**：`bcm open [--db <path>] --command <id>` 先用 `--db` 指向的库校验命令存在（`OpenCommand.cs:21-25`），随后启动 GUI 时没有把 db 路径传下去；GUI 走 `%LOCALAPPDATA%` 默认库。结果可能是：校验通过，但 GUI 里找不到该 id（或定位到别的库里的同 id 命令）。这是 v1.3.0 新增 CLI 与 GUI 协作面的真实断点。
- **影响**：`bcm open --db ... --command ...` 语义不成立；用户会误以为定位成功。
- **修法**：`GuiLauncher.Launch` 接收 db 路径并在 `ProcessStartInfo.Environment` 设置 `BASHCOMMANDMANAGER_DB`（或给 GUI 增加 `--db` 参数并在 `OnStartup` 的 `DatabaseInitializer` 之前解析）。若 GUI 已运行、库不同，单实例无法切换，CLI 应返回明确错误而不是静默成功。

### I-6 安装包 PATH：重复安装会重复追加，卸载不清理；提权安装写入的是管理员 HKCU

- **位置**：`Packaging/InnoSetup/BashCommandManager-Full.iss:44-45`、`Packaging/InnoSetup/BashCommandManager-Lite.iss:44-45`（`{olddata};{app}\cli`，`Check` 只判断 Path 是否为空/非空）、`:37`（任务可重复勾选）；`docs/PACKAGING.md:26` 已承认“卸载不自动移除”
- **理由**：`Check` 没有判断 `{app}\cli` 是否已在 Path 中，重复运行安装包（或先 Full 后 Lite 同目录）会再次追加，Path 越来越长；卸载不删除该段，留下指向已删除目录的条目。Inno 的 `HKCU` 是执行安装的（通常是提权后的）账户，若标准用户用管理员凭据做 per-machine 安装，PATH 写进管理员配置而不是当前用户，CLI 对当前用户不可用。`ChangesEnvironment=yes` 只负责广播变更，不负责回滚。
- **影响**：用户环境变量被永久污染/重复；CLI 可能装了但当前用户找不到。
- **修法**：新增 `Check: ShouldAddCliToPath`，要求 `FileExists(ExpandConstant('{app}\cli\bcm.exe'))` 且 Path 按 `;` 拆分后不存在 `{app}\cli`（大小写不敏感、去空白）。卸载清理必须用 `[Code] CurUninstallStepChanged(usPostUninstall)` 自己拆分/移除该段并写回；**不要**简单加 `uninsdeletevalue`，那会删除整个 Path 值。PATH 应写到与安装模式匹配的 hive（per-user 用 HKCU，per-machine 用 HKLM 环境变量），或明确只支持 per-user。

### I-7 数据库迁移失败的告警只进 Debug，用户会静默继续用旧库（可能只读）

- **位置**：`App.xaml.cs:48-51`（`MigrationWarning` 只 `Debug.WriteLine`）；回退行为 `Infrastructure/DatabasePathResolver.cs:125-130`；文档 `CLAUDE.md` 也写“只写 Debug.WriteLine”
- **理由**：迁移失败（磁盘满、权限、旧库被占用）后 `DatabaseInitializer.DatabasePath` 回退到 `{app}\data\app.db`。如果程序装在 Program Files/`{autopf}`，普通用户对旧库通常只有读权限，随后保存命令/记录执行次数会写失败；用户只看到功能异常，不知道当前用的是旧库、更不知道如何恢复。
- **影响**：数据可用性/可诊断性；迁移失败不可见，用户无法自助恢复。
- **修法**：在 `mainWindow.Show()` 之后用 `Growl.Warning` 显示迁移失败（窗口尚未创建时先缓存 Warning，Show 后弹出），文案包含当前库路径和 `BASHCOMMANDMANAGER_DB` 回滚方法；同时把 Warning 暴露到主窗口状态栏/日志文件。

### I-8 `TerminalCommandBuilder.Quote` 未处理结尾反斜杠，根目录 `{dir}` 会拼坏参数

- **位置**：`Core/Services/TerminalCommandBuilder.cs:209`（`Quote` 只包双引号）、`:36-38`（wt 默认模板使用 `{dir}`）；相关测试只断言了 WorkingDirectory：`tests/BashCommandManager.Tests/TerminalCommandBuilderTests.cs:228-235`
- **理由**：`Path.GetDirectoryName(@"C:\demo.bat")` 返回 `C:\`，`Quote` 生成 `"C:\"`。按 `CommandLineToArgvW` 规则，结尾反斜杠会转义闭合引号；本机探针 `-d "C:\" cmd.exe /k "C:\demo.bat"` 解析为 `-d` + `C:" cmd.exe /k C:\demo.bat`，wt/custom 的 `-d` 目录参数直接错误。cmd 默认模板只用 `{file}`（文件路径不会以反斜杠结尾），所以 B18 没覆盖到。
- **影响**：脚本位于盘符根目录且使用 wt/自定义终端（模板含 `{dir}`）时无法启动；属于参数拼装正确性缺陷。
- **修法**：`Quote` 对结尾 `\` 做转义（追加一个 `\`，即 `"C:\\"` 交给 CreateProcess 风格解析；cmd 路径也能接受重复分隔符），或对 cmd 与 CreateProcess 风格分别实现两套引号函数；补测试：`Build(new ExecutionSettings { Terminal = WindowsTerminal }, @"C:\demo.bat")` 断言 `Arguments` 中目录部分正确、且 `WorkingDirectory == @"C:\"`。

### I-9 读取设置失败会静默重置为默认并在下次保存覆盖原文件

- **位置**：`Core/Services/SettingsService.cs:84-93`（读失败 → `BackupCorrupt` + 默认值）、`:119-124`（解析失败同样）、`:207-223`（corrupt 备份名固定、已有则不覆盖）
- **理由**：`File.ReadAllText` 的 `IOException`（杀软占用/短暂锁）与真正的 JSON 损坏被同样处理成“默认设置”，且只写 Debug；用户随后改任意设置保存时，会把默认值写回，原文件只剩一份 `settings.json.corrupt.bak`（若已存在则不再更新）。这不是数据库数据丢失，但会丢失用户的热键/终端配置且无提示。
- **影响**：设置丢失不可见、不可恢复；与“写入失败”路径叠加时更难诊断。
- **修法**：区分 `IOException`（提示并保留原文件、不要以默认值继续保存；或重试）与 `JsonException`（备份后默认值，并在 UI 告警）；corrupt 备份加时间戳；不要在读取失败后自动覆盖原文件，除非用户明确选择。

## 次要

### m-1 启动后立即退出的进程可能被登记为 Running（启动/退出的竞态）

- **位置**：`Core/Services/CommandExecutor.cs:113-118`（订阅 `Exited`）、`:135-136`（Start 之后才 `RegisterProcess` + `Running`）
- **理由**：`Process.Start()` 返回后、`RegisterProcess` 前，短命进程可能已经触发 `Exited`：回调先 `UnregisterProcess`（此时尚未登记）并把 `Status` 设为 Completed/Failed，随后主线程又把已退出的进程登记进去并把 `Status` 覆盖回 Running → 状态永久停在 Running、`IsRunning` 为 true（进程句柄也未 Dispose）。`CommandExecutorTests.cs:69-70` 用 no-op 的 `StartProcess` 断言 Running，不可能发现该竞态。
- **修法**：启动后先 `if (process.HasExited)` 走终止状态分支；登记与状态更新放在同一把锁内，或先登记再 Start、失败回滚；`Exited` 回调用 `TryRemove` 并 Dispose 进程。

### m-2 单实例消息只做到“旧客户端 → 新服务端”兼容，反向不兼容

- **位置**：`Core/Services/SingleInstanceMessage.cs:11-12`（新格式 `ACTIVATE\t<json>`）、`Core/Services/SingleInstanceService.cs:145`（新客户端写新格式）；master 的旧服务端是精确比较 `message == "ACTIVATE"`
- **理由**：v1.3.0 客户端向仍在运行的 v1.2 实例发送 `ACTIVATE\t[...]` 时，旧服务端不识别，窗口不会激活（v1.2 客户端向 v1.3 服务端则因 `TryParse` 接受裸 `ACTIVATE` 而正常）。升级瞬间可能“新进程退出、旧窗口没出现”。
- **修法**：接受混合版本，或把 Mutex/Pipe 名称升级到 v3 让两代并存（至少新进程能显示自己的窗口）；若必须兼容旧服务端，只能发送裸 `ACTIVATE`，再另开一条带参数通道。

### m-3 管道服务端每个循环重建，连接空档会丢激活；退出时 UI 线程最多等 1 秒

- **位置**：`Core/Services/SingleInstanceService.cs:83-127`（循环内 new `NamedPipeServerStream`）、`:157-165`（`Cancel` 后 `_pipeServerTask.Wait(1s)` 在 UI 线程）
- **理由**：服务端处理完一条消息后销毁管道再新建，两个实例恰好在空档连接会 `Connect` 超时（异常被吞），表现为“第二次启动没反应”。`Dispose` 在 OnExit 的 UI 线程同步等待 1 秒，且此时管道线程可能正阻塞在 `Dispatcher.Invoke`，退出会明显卡顿。
- **修法**：用单个长生命周期 server 循环 `WaitForConnectionAsync`，或至少保留一个实例；`Dispose` 改为异步/有限非阻塞，并避免在 UI 线程 `Wait`。

### m-4 `HotkeySettings.IsValid` 不校验 Key.None；启动失败提示一律说“被占用”

- **位置**：`Core/Models/HotkeySettings.cs:44-48`、`Core/Services/SettingsService.cs:129`、`App.xaml.cs:77-85`
- **理由**：`{ "Hotkey": { "Key": 0, "Modifiers": 3 } }` 会通过 `IsValid`（只看修饰键），随后 `TryRegister` 返回 `InvalidCombination`，但启动提示统一写成“可能已被其他程序占用”；用户在设置页保存 execution 也会被 `UpdateHotkey` 以 `InvalidCombination` 阻断（见 I-3）。
- **修法**：`IsValid` 同时要求 `settings.Key != Key.None`；启动告警按 `HotkeyRegistrationStatus` 分档（Conflict / InvalidCombination / HostUnavailable）。

### m-5 开启 `Foreign Keys=True` 不会清理旧版本遗留的孤儿命令

- **位置**：`Infrastructure/DatabaseInitializer.cs:32`（连接串）、`:39-66`（建表/根组，无 `foreign_key_check`）
- **理由**：v1.2 的连接串没有 `Foreign Keys=True`，但建表 SQL 里已有 `ON DELETE CASCADE`；在升级前删除分组时级联并未生效，旧库中可能留下 `GroupId` 指向已删除分组的 `Commands`。新版本开启 FK 只影响后续 DML，不会回溯校验或修复这些行；它们不会出现在任何分组，但可能出现在“所有命令”/搜索中（取决于查询）。本机探针确认：对孤儿行做非外键列 `UPDATE`、删除已不存在的父组都不会抛错，所以不会崩溃，但数据视图不一致。
- **修法**：启动时执行一次 `PRAGMA foreign_key_check`，把结果（数量/示例 id）写日志并在 UI 告警；提供显式修复选项（挂到根分组或显示“未知分组”），不要静默删除。

### m-6 `IsAppModalOpen` 只识别 owned Window，HandyControl 的窗口内弹层不会被识别

- **位置**：`Infrastructure/MainWindowVisibilityController.cs:25`（`_window.OwnedWindows.OfType<Window>().Any(...)`）；使用方如 `ViewModels/CommandListViewModel.cs:248`、`ViewModels/GroupTreeViewModel.cs:289`
- **理由**：HandyControl `Dialog.Show` 是 `DialogContainer`（`AdornerDecorator`）内的窗口内遮罩，不是 owned `Window`；删除确认/输入分组名等“应用内模态”打开时，`IsAppModalOpen` 仍为 false，主窗口仍 active → 热键会执行 `HideToTray`，把弹层一起隐藏。`SettingsWindow`（ShowDialog + Owner）检测是正常的。现有 `HotkeyToggleCoordinatorTests` 只喂快照，覆盖不到该真实实现。
- **修法**：在 `Capture` 中同时查询 `DialogContainer`/`Dialog.IsClosed` 或维护一个应用级 modal 计数（所有 `Dialog.Show` 前后增减）；或在 `MainWindow` 的 `IsEnabled == false` 时也视为模态。补一个手工验收项（删除确认框打开时按热键）。

### m-7 `RegisterHotKey` 未加 `MOD_NOREPEAT`，长按会连续触发切换

- **位置**：`Core/Services/HotkeyNativeMethods.cs:18-19`（`RegisterHotKey` 声明与 modifiers 常量）
- **理由**：没有 `MOD_NOREPEAT (0x4000)` 时按住热键会产生重复 `WM_HOTKEY`，`HotkeyToggleCoordinator` 会连续执行 Hide/Show，窗口闪烁、行为不可预期。
- **修法**：注册时 OR `MOD_NOREPEAT`；更新 `HotkeyNativeMethodsTests` 的映射期望（或在包装层单独加，不改变公开枚举语义）。

### m-8 热键 id 分配器回绕后可能与当前 id 相同，先注册后注销会把自己注销掉

- **位置**：`Core/Services/HotkeyIdAllocator.cs:14-18`（0x9000–0x90FF 回绕）、`Core/Services/GlobalHotkeyService.cs:46-52`（新 id 注册成功后才注销旧 id）
- **理由**：连续改键 256 次后 `Next()` 回到 0x9000；若 `_currentId` 恰好也是 0x9000，`RegisterHotKey` 使用同 hwnd+同 id、`UnregisterNative(_host.Handle, _currentId)` 会把刚注册的新组合注销，内部状态却认为已注册。概率极低，但逻辑上真实。
- **修法**：分配器跳过 `_currentId`（以及正在使用的 id），或改用单调不回绕的随机/查找空闲 id 并处理耗尽。

### m-9 `ExecutableLocator` 忽略带引号的 PATH 目录且不校验 COMSPEC

- **位置**：`Core/Services/ExecutableLocator.cs:38-67`（`FindOnPath` 跳过含 `"` 的目录）、`:69-88`（COMSPEC 直接返回、不 `File.Exists`）；测试 `tests/BashCommandManager.Tests/ExecutableLocatorTests.cs:43-59` 把“忽略”固化成期望
- **理由**：部分安装器会把 PATH 写成 `"C:\Program Files\Tool";...`；跳过该条目会让 pwsh/wt 探测失败并错误降级到 cmd。COMSPEC 也可能被设置成失效路径，随后 `Process.Start` 才报错。
- **修法**：对每个 PATH 条目先 `Trim().Trim('"')` 再 `Path.Combine`（保留空条目/引号配对的保护），并检查 COMSPEC 是否存在；单测改为覆盖带引号目录能命中。

### m-10 GUI 与 CLI 共用一个固定 `settings.json.tmp` 名，跨进程并发保存会互相覆盖/丢更新

- **位置**：`Core/Services/SettingsService.cs:186-188`（固定 `settings.json.tmp` + `File.Move`）；CLI 侧 `BashCommandManager.Cli/Application/CliSettingsStore.cs:56-64`（直接 `File.WriteAllText`，非原子）
- **理由**：GUI 和 `bcm config set` 同时运行可能写同一个 tmp 或 last-writer-wins；GUI 内存里是启动时的快照，保存时会覆盖 CLI 刚改的另一段。单进程内原子写解决不了跨进程。
- **修法**：tmp 名加 `ProcessId`/随机后缀；用跨进程锁（`Mutex`/锁文件）串行化读写，或保存前重新读取磁盘并做 section 级合并；CLI 也改为同目录临时文件 + 原子替换。

### m-11 打包脚本不清理发布目录，可能把上一次的残留文件打进安装包

- **位置**：`Packaging/InnoSetup/Build-Installers.bat:23-37`（直接 `dotnet publish -o Full\bin` / `Lite\bin`，无删除）
- **理由**：`[Files]` 使用 `Full\bin\*` 递归；旧版本残留的 `cli`、旧程序集、甚至有人从发布目录运行后留下的 `data\app.db` 都可能被带入安装包。`docs/PACKAGING.md:58` 手工步骤要求先清理，但批处理没有。
- **修法**：publish 前 `if exist Full\bin rmdir /s /q Full\bin`（Lite 同理），或 publish 到全新临时目录再用 `robocopy /MIR` 同步；发布后在 `[Files]` 侧仍保持递归，但要校验 `Full\bin\cli\bcm.exe` 存在。

### m-12 文档与实现/约束的偏差

- **位置**：`docs/superpowers/DECISIONS.md:23-28`（把 `DefaultItemExcludes` 写进 `<ItemGroup>`，这是 MSBuild 属性，应为 `<PropertyGroup>`；实际 `BashCommandManager.csproj:8` 是对的）、`docs/PACKAGING.md:26`（只写“卸载不自动移除”，未提重复安装会重复追加）、`docs/PACKAGING.md:90`（递归复制正确，但没有校验 `bcm.exe` 是否真的被发布）
- **理由**：计划/决策文档会被后续实现代理当作规范；`<ItemGroup>` 里的属性不会生效，复制粘贴会制造无效配置。PATH 的重复追加风险未被文档化。
- **修法**：把 D2 片段改为 `<PropertyGroup>`；在 PACKAGING 中补充重复安装/卸载清理的限制和 `bcm.exe` 存在性检查。

### m-13 测试：真实进程/退出状态路径默认不执行（smoke 静默通过）

- **位置**：`tests/BashCommandManager.Tests/CommandExecutorSmokeTests.cs:39-42`（`BCM_EXECUTOR_SMOKE != "1"` 时直接 `return`）
- **理由**：未设置环境变量时该测试直接返回，xUnit 记为通过；因此“167 passed”并不包含真实的 `Process.Start`/`Exited`/状态回写路径。m-1 的竞态、fallback 的真实命令行都没有被覆盖。其余执行测试用 `protected override StartProcess`（`CommandExecutorTests.cs:286-327`）完全绕过进程启动。
- **修法**：改为显式可发现的状态（xUnit `Skip` 带原因，或单独的 `Smoke` 运行脚本并在发布门禁中强制 `BCM_EXECUTOR_SMOKE=1`），并在 CI 中把该组测试单独执行。

### m-14 测试：断言与实现耦合/把缺陷固化

- **位置**：`tests/BashCommandManager.Tests/SettingsServiceTests.cs:186-213`（S8 断言写盘失败后内存已是新值）、`tests/BashCommandManager.Tests/Fakes/FakeGlobalHotkeyService.cs:28-33`（`Resume` 永远返回 `UpdateResult`，注释声称与真实实现一致；真实 `Resume()` 在 `_currentKey == None` 时返回 `NotRegistered`）、`tests/BashCommandManager.Tests/CommandExecutorTests.cs:44-71`（fallback 用空模板，掩盖 B-2）
- **理由**：S8 会让 I-1 的修复变成“测试变红”；Fake 的 Resume 语义不真实，`SettingsViewModel.SetHotkey` 的恢复失败分支无法被覆盖；fallback 测试没有使用持久化后的模板，断言只到 `Started/EffectiveTerminal`，不校验 `Arguments`。
- **修法**：S8 改为断言失败不改变内存或明确返回失败结果；Fake 的 `Resume` 按 `IsRegistered`/当前键返回 `NotRegistered`；fallback 用缺失终端的默认模板构造 `ExecutionSettings` 并断言 `StartInfo.Arguments`。

### m-15 冒烟脚本会强杀用户正在运行的实例，启动失败时清理代码还会二次报错

- **位置**：`scripts/verify-hotkey-tray.ps1:121-123`（`Get-Process -Name 'BashCommandManager' | Stop-Process -Force`）、`:130-137`（仅临时设置环境变量）、`:172-175`（`finally` 直接访问 `$proc.HasExited`）
- **理由**：脚本用于验证热键，但会无提示地杀掉所有同名进程——包括用户正在使用、尚未保存的实例；如果 `Start-Process` 抛错，`$proc` 未赋值，`finally` 里 `$proc.HasExited` 会再抛一次，掩盖原始错误。脚本注释声称“只操作临时数据库，不修改用户配置”，但没有说明会终止用户进程。
- **修法**：启动前若检测到已有同名进程，默认报错退出（提供 `-Force` 显式允许终止）；`finally` 先判断 `$null -ne $proc -and -not $proc.HasExited`，并把 `$proc` 初始化为 `$null`。

### m-16 测试：缺少迁移/保存成功路径/根目录/Key.None 等关键覆盖；测试工程耦合 CLI 源文件

- **位置**：`tests/BashCommandManager.Tests/Infrastructure/DatabaseInitializerTests.cs:88-109`（只测新目录，不测 legacy→canonical 全链路）、`tests/BashCommandManager.Tests/DatabaseMigratorTests.cs:57-77`（目标已存在却断言 `Migrated=true`）、`tests/BashCommandManager.Tests/SettingsViewModelTests.cs:107-176`（只有失败路径，无保存成功/SetHotkey/禁用/写盘失败）、`tests/BashCommandManager.Tests/TerminalCommandBuilderTests.cs:228-235`（只断言 WorkingDirectory，未断言 `Arguments`）、`tests/BashCommandManager.Tests/HotkeySettingsTests.cs:47-53`（只测无修饰键，不测 Key.None）、`tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj:26-28`（把 CLI 的 `HotkeyKeyMap.cs` 链接进 GUI 测试工程）
- **理由**：B-1/B-2/I-1/I-2 的共同问题是“没有测试覆盖真实配置/真实路径”；`Migrated=true` 的断言会阻碍 I-4 修复；测试工程引用 CLI 源文件会让 GUI 测试依赖 CLI 目录，CLI 重构会误伤 GUI 测试。
- **修法**：补 4 条最小用例：① legacy 在父目录时的迁移；② 切换终端后保存再降级 cmd 的完整 `StartInfo`；③ 设置保存成功/失败回滚与两段一致性；④ `{dir}` 根目录 + `Key.None`。`HotkeyKeyMap` 的交叉断言移到 CLI 测试工程，或把共享映射抽到不依赖 WPF 的公共文件并让两侧引用同一文件。

## 复核建议（文件:行号 + 修法）

- `Infrastructure/DatabasePathResolver.cs:37,74-75`：把 legacy 改为候选列表（`{base}\data\app.db`、`{base}\..\data\app.db`，CLI 的父目录即 GUI 安装目录），`Resolve` 取第一个存在者；补父目录迁移测试。这是 B-1 的核心修法。
- `Core/Services/CommandExecutor.cs:70-77`：降级分支在 `WithTerminal(Cmd)` 后重置 `ArgumentsTemplate = GetDefaultTemplate(Cmd, KeepWindowOpen)`；补 `StartInfo.Arguments` 断言（B-2）。
- `Core/Services/SettingsService.cs:62-74,175-194` + `ViewModels/SettingsViewModel.cs:280-281`：先把新设置序列化到临时文件并成功替换，再提交 `_appSettings`；提供一个一次写入两段的 API，失败回滚（I-1）。
- `App.xaml.cs:122` + `Views/SettingsWindow.xaml.cs:12` + `ViewModels/MainViewModel.cs:164-171`：设置窗口创建独立 `IServiceScope`（窗口关闭时 Dispose）或把 VM 注册为 Transient；`Loaded` 时重新加载快照，Cancel 丢弃（I-2）。
- `ViewModels/SettingsViewModel.cs:232-266`：execution 校验/保存与 hotkey 注册解耦；hotkey 失败只回退 hotkey 段并给明确文案；Enabled=false 先落盘再 `Unregister`（I-3）。
- `Infrastructure/DatabasePathResolver.cs:107-121`：目标存在时不要无条件丢弃副本；用迁移标记/锁区分“并发迁移成功”和“别人创建的空库”，后者返回 Warning（I-4）。
- `App.xaml.cs:47,65` + `Core/Services/StartupArgumentParser.cs:6`：支持 `--db`（在 `DatabaseInitializer` 构造前解析），或让 CLI 的 `GuiLauncher` 设置 `BASHCOMMANDMANAGER_DB`；GUI 已运行时库不同则让 CLI 报错（I-5）。
- `Packaging/InnoSetup/BashCommandManager-Full.iss:44-45`、`...-Lite.iss:44-45`：新增 `ShouldAddCliToPath`（`bcm.exe` 存在且 Path 分段不含 `{app}\cli`）；卸载用 `CurUninstallStepChanged` 移除该段；PATH 写入与安装模式匹配的 hive；不要用 `uninsdeletevalue`（I-6）。
- `App.xaml.cs:48-51`：缓存 `MigrationWarning`，`mainWindow.Show()` 后 `Growl.Warning` 并显示当前库路径/回滚方法（I-7）。
- `Core/Services/TerminalCommandBuilder.cs:209`：`Quote` 转义结尾反斜杠（或按 cmd / CreateProcess 两套引号函数），补 `C:\` 根目录 `{dir}` 测试（I-8）。
- `Core/Services/SettingsService.cs:84-93,119-124`：区分 IO 失败与 JSON 损坏；IO 失败不要用默认值继续并可覆盖原文件；corrupt 备份加时间戳（I-9）。
- `Core/Services/CommandExecutor.cs:113-118,135-136`：启动后检查 `HasExited`，或先登记后 Start；登记/移除加锁并在移除时 Dispose 进程（m-1）。
- `Core/Services/SingleInstanceMessage.cs:11-12` / `SingleInstanceService.cs:145`：升级 Mutex/Pipe 版本或增加协议版本协商，避免新→旧激活静默失败（m-2）。
- `Core/Services/SingleInstanceService.cs:83-127,157-165`：管道 server 常驻/复用；退出等待移出 UI 线程（m-3）。
- `Core/Models/HotkeySettings.cs:44-48` + `App.xaml.cs:77-85`：`IsValid` 校验 `Key != Key.None`；启动告警按 `HotkeyRegistrationStatus` 分档（m-4）。
- `Infrastructure/DatabaseInitializer.cs:32,39-66`：启动时 `PRAGMA foreign_key_check`，记录/提示孤儿数据，提供显式修复（挂根组或“未知分组”），不自动删除（m-5）。
- `Infrastructure/MainWindowVisibilityController.cs:25`：把 HandyControl `DialogContainer`/`Dialog` 打开状态纳入 `IsAppModalOpen`，或维护应用级 modal 计数（m-6）。
- `Core/Services/HotkeyNativeMethods.cs:18-19`：注册时 OR `MOD_NOREPEAT`（0x4000）（m-7）。
- `Core/Services/HotkeyIdAllocator.cs:14-18` + `GlobalHotkeyService.cs:46-52`：分配时跳过 `_currentId` / 正在使用的 id（m-8）。
- `Core/Services/ExecutableLocator.cs:38-67,69-88`：PATH 条目先 `Trim('"')`，COMSPEC 校验存在；单测改为覆盖带引号目录（m-9）。
- `Core/Services/SettingsService.cs:186-188` + `BashCommandManager.Cli/Application/CliSettingsStore.cs:56-64`：tmp 名加唯一后缀；跨进程锁/重新读取合并（m-10）。
- `Packaging/InnoSetup/Build-Installers.bat:23-37`：publish 前删除 `Full\bin`/`Lite\bin`，并校验 `Full\bin\cli\bcm.exe`（m-11）。
- `docs/superpowers/DECISIONS.md:23-28`：`<ItemGroup>` 改 `<PropertyGroup>`；`docs/PACKAGING.md:26,90` 补充重复安装/卸载限制与 CLI 文件存在性检查（m-12）。
- 测试项：`CommandExecutorSmokeTests.cs:39-42` 改为显式 Skip/强制 smoke；`SettingsServiceTests.cs:186-213`、`FakeGlobalHotkeyService.cs:28-33`、`CommandExecutorTests.cs:44-71` 按 m-14 修；`DatabaseInitializerTests.cs:88-109`、`SettingsViewModelTests.cs:107-176`、`TerminalCommandBuilderTests.cs:228-235`、`HotkeySettingsTests.cs:47-53` 补 m-16 的四类用例；`BashCommandManager.Tests.csproj:26-28` 的 CLI 源链接移到 CLI 测试工程（m-13/14/16）。
- `scripts/verify-hotkey-tray.ps1:121-123,172-175`：已有实例默认报错退出（`-Force` 才强杀）；`finally` 判空 `$proc`，避免启动失败时二次异常（m-15）。

---

**复核通过的最低条件**：B-1、B-2 修复并有回归测试；I-1～I-3、I-5、I-6 修复或明确记录为已知限制；I-4、I-7、I-8 给出处理结论；测试门禁显式区分 smoke 与真实进程用例。
