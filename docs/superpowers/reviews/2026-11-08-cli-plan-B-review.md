# CLI + Skill（#10）实施计划 B 部分（Task 12–29）评审报告

- 评审对象：`docs/superpowers/plans/2026-11-08-cli-and-skill.md` 第 5669 行至文件末尾（Task 12–Task 29 + Self-Review，共 4956 行）
- 权威契约：`docs/superpowers/specs/2026-11-08-cli-and-skill-design.md`（第 3 节命令表 L181–L199、第 4 节输出/退出码 L213–L220、测试策略 L290–L317、验收 L319–L330）
- 决策：`docs/superpowers/DECISIONS.md`（工作区当前版本，D2.2 第 3 条已被更新为显式包含 `TerminalLaunchPlan.cs`，见该文件 L50）
- 交叉计划：`docs/superpowers/plans/2026-11-08-execution-terminal.md`（#9）——核对 `TerminalCommandBuilder` / `ExecutionSettings` / `TerminalLaunchPlan` 的最终签名
- 真实源码（本报告行号均为写入报告时的工作区状态）：`Core/Repositories/CommandRepository.cs`、`GroupRepository.cs`、`Core/Services/GroupService.cs`、`CommandExecutor.cs`、`CommandService.cs`、`SingleInstanceService.cs`、`TerminalCommandBuilder.cs`、`ExecutableLocator.cs`、`Core/Models/TerminalLaunchPlan.cs`、`ExecutionSettings.cs`、`ViewModels/MainViewModel.cs`、`GroupTreeViewModel.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`App.xaml.cs`、`BashCommandManager.csproj`、`Packaging/InnoSetup/*.iss`、`docs/PACKAGING.md`、`README.md`、`.claude/skills/*/SKILL.md`

> **环境说明（会影响结论的前提）**：任务说明称「#8、#9 尚未实现、仓库是原始代码」，但实际工作区 **#8 已合入**（`feat(hotkey)` 5 个提交 `4d5c1bc`…`3319d2e`，`Core/Services/GlobalHotkeyService.cs` 等文件已存在，`MainWindow.xaml.cs`/`App.xaml.cs` 已是 #8 之后的版本）；**#9 在评审期间正在陆续落盘**（`f86a429`/`4fd3788`/`7b7b93a` 等提交后，`Core/Models/TerminalLaunchPlan.cs`、`Core/Services/TerminalCommandBuilder.cs`、`ExecutableLocator.cs`、`ExecutionResult.cs` 已存在，签名与 #9 计划一致；`App.xaml.cs`/`CommandListViewModel.cs` 仍有未提交改动）。因此：
> 1. 按 T1 Step1 的门禁，B 部分**必须等 #9 全部任务（含 GUI 接线与测试）完成后再执行**；本报告只做静态评审。
> 2. **B-1 已用真实文件复核**：`Core/Models/TerminalLaunchPlan.cs` 确实是**独立文件**（`public sealed record TerminalLaunchPlan(...)`），而 `Core/Services/TerminalCommandBuilder.cs:43` 的 `Build(...)` 返回该类型——CLI 的 `SharedSources`（计划 262-278）却没有链接它。
> 3. T24 的 GUI 改法我按「#8 之后」的真实代码核对，并因此发现了 B-6（#8 已从 `MainWindow.xaml.cs` 删掉 `using BashCommandManager.Core.Services;`）。
> 4. `BashCommandManager.csproj:8` 已有 `DefaultItemExcludes`（T1 Step2 会跳过补建，符合预期）。

### 评审方法与证据

1. 逐行通读 B 部分全部 4956 行，并把 A 部分（T1–T11）中 B 依赖的契约（`CommandCatalog`、`CliErrors`、`JsonShapes`、`CommandResolver`、`CliCommandStore`、`ProcessRunner`、`CliSettingsStore`、`GuiLauncher`）一并核对，避免「B 的测试通过但 A 的接口不对」。
2. 把 19 条命令逐条对比 spec 表（L181–L199）的参数名、默认值、JSON 字段与退出码，并回查每个处理器的实现代码与测试断言。
3. 把 `CliCommandStore` 对 `ICommandRepository`/`IGroupRepository` 的调用逐方法对照真实接口（`CommandRepository.cs:7-30`、`GroupRepository.cs:7-15`），并对照 `GroupService.cs:45-72`、`CommandService.cs:134-143` 的既有算法。
4. 两个本机探针（临时目录，未触碰仓库）：
   - **同名属性/类型**：`class App { public Window? MainWindow {get;set;} void M(){ if (MainWindow is MainWindow w) … } }` 在 `net8.0-windows` 下**编译通过**。计划 T24 Step7 的 `if (MainWindow is MainWindow window)` 不会编译失败。
   - **`UseShellExecute=false` + `Verb="runas"`**：`ProcessStartInfo{FileName="cmd.exe",UseShellExecute=false,Verb="runas"}` 实测**不抛异常**、进程正常以 exit 0 结束（Verb 被 `CreateProcess` 路径静默忽略）。因此「`--wait` + 全局 `RunAsAdministrator=true` 会抛 `InvalidOperationException`」**不成立**；真实问题是**静默不提权**（见建议 2）。
5. 编码核对：计划文件为合法 UTF-8、LF、10624 行；两个 `.iss`、README、CLAUDE、PACKAGING 均为合法 UTF-8 无 BOM（T27 新增内容为纯 ASCII，不会给 `.iss` 引入乱码风险）。

---

# 结论

**不通过（需修复 6 个阻断项；均为 1–5 行级改动，修完可直接执行）。**

计划质量整体较高：19 条命令与 spec 命令表基本一一对应；`CliCommandStore` 调用的仓库方法全部真实存在且签名一致；`run` 的四种路径语义（默认 / `--wait` / `--admin` / `--dry-run`）与 #9 的 `TerminalCommandBuilder.Build(settings, command, resolvedExecutable)` 最终签名吻合；GUI 侧 `SelectCommandAsync`/`FindGroupById`/`ApplyStartupArguments` 与真实代码结构（方法、字段、线程模型）基本对得上；打包改动确实最小。但存在：

- **2 项会直接编译失败**：CLI 缺 `Core/Models/TerminalLaunchPlan.cs` 链接；T24 给 `MainWindow` 加的方法缺 `using`（#8 后该 using 已移除）。
- **2 项测试必红**：`groups create` 的 `sortOrder` 断言与实现/ spec 相反；`CliSettingsStore.SetHotkey` 对「不存在 settings.json」写扁平格式，与 T10/T22/T25 的嵌套断言矛盾（并违反 DECISIONS D3）。
- **2 项违反「测试/冒烟不碰真实用户数据」硬约束**：T25 的 `run` 冒烟读取真实 `settings.json`；T20 手工冒烟第 3 条不带 `--db`，会创建/写入默认库、甚至可能执行真实命令。

### 逐项校验结论（任务清单 1–9）

| # | 校验项 | 结论 | 关键位置 |
|---|---|---|---|
| 1 | 19 条命令对照 spec | **16 条通过、3 条有条件**：`groups create`（B-2 断言反了）、`config set`（B-3 落盘形状）、`run`（`pid` 可为 null / `--dry-run` 无 verb / wait 丢全局提权）；另 `commands edit` 的 `--group` 语义与 §3「先限定候选」规则冲突；**无缺参数、无字段名不一致** | 见「命令契约对照表」 |
| 2 | 数据操作正确性 | **通过**。`CliCommandStore` 调用的 `GetAllAsync/GetByIdAsync/CreateAsync/UpdateAsync/DeleteAsync/GetByGroupIdWithSortAsync/GetAllWithSortAsync/SearchWithSortAsync/SearchInGroupAsync/MoveToGroupAsync/GetFrequentlyUsedAsync/GetByGroupIdAsync` 在真实仓库中全部存在且参数/返回类型一致；`SortOrder=同级数量`、`ExecutionCount+1/LastExecutedAt=DateTime.Now`、级联删除（依赖 T6 的 `Foreign Keys=True`）、根分组 Id=1 保护均符合 spec | `CommandRepository.cs:7-30,47-60,142-163`；`GroupRepository.cs:38-60`；`GroupService.cs:45-72`；`CommandService.cs:134-143`；计划 4258-4375、6602-6605 |
| 3 | `run` 语义 | **通过**（默认 `/k`+ShellExecute、`--wait` `/c`+0/3、`--timeout` 杀进程树→`E_TIMEOUT`、`--admin` runas、`--admin`/`--wait` 冲突由 parser 拦 1、`--dry-run` 复用 `TerminalCommandBuilder`）；stdout 在 `--json` 下保持纯 JSON（子进程 stdout/stderr 都转发到 stderr）；剩余偏差见建议 2/3/4 | 计划 8466-8615；`ProcessRunner` 4964-5038；parser 2370-2387；真实 `TerminalCommandBuilder.cs:43,90`（签名 `Build(ExecutionSettings, Command, string?)`，Verb 由 `RunAsAdministrator` 决定）与 `ExecutableLocator.cs:18`（有无参构造，计划的 `new ExecutableLocator()` 成立） |
| 4 | GUI 侧改动 | **结构吻合、1 处编译失败**：`MainViewModel.cs:23-37/57-79`、`GroupTreeViewModel.cs:314-329`、`SingleInstanceService.cs:14/21/26-71/96-109/140`、`App.xaml.cs:25-33/54-57`、`MainWindow.xaml.cs:21-24` 均与计划假设一致，旧 `ACTIVATE` 兼容成立；但 `MainWindow` 缺 `using`（B-6），且 TreeView 未绑定 `SelectedGroup`（建议 11） | 见 B-6、建议 11 |
| 5 | 冒烟 T25 | **可自动化（实际 8 条，非 7 条）**：临时库、stdin 立即关闭、30s 超时、进程树 kill、CSV、hotkey 往返都能自动化；**但 `run` 冒烟读真实 settings（B-4）**，且 Step3 硬编码 `D:\Program Files\...`（建议 15） | 9868-10010、9799-9823 |
| 6 | SKILL.md T26 | **通过**：frontmatter 与 `.claude/skills/github-releasing/SKILL.md:1-4` 同风格（`name`+`description`），JSON 示例字段与 T4 的 `JsonShapes` 一致；仅「19 条」标题与 22 行表格不符（建议 8） | 10073-10195 |
| 7 | 打包 T27 | **通过**：两份 `.iss` 只改 `MyAppVersion`/`[Setup] ChangesEnvironment`/`[Tasks]`/`[Registry]` 四处，`[Files]` 不动；`Full.iss:39`/`Lite.iss:39` 的 `recursesubdirs createallsubdirs` 确实会把 `bin\cli\*` 复制到 `{app}\cli`；PATH 为 HKCU、只追加、`Flags: unchecked`；发布命令与现有 `docs/PACKAGING.md:52-85` 兼容 | `Full.iss:3,29,34-39`；`Lite.iss:3,29,34-39`；计划 10223-10283 |
| 8 | 文档 T28 / 全量验证 T29 | **部分通过**：1.3.0 在 CLI csproj（计划 251）与两个 `.iss`（L3）一致；但 **README 当前没有任何版本号**（`README.md` 全文无 `1.3.0`/`1.2.1`），T28(c) 只写「若出现 1.2.1 一并改」，D6 的 README 项实际落空；GUI `BashCommandManager.csproj` 也没有 `<Version>`；T29 缺少验收 4（19 条命令字段一致性）的步骤，且 Step8 的验收编号错位 | 见建议 5/6/7 |
| 9 | 与 DECISIONS 一致性 | **基本一致、1 处违约**：D2.2（不链接 `CommandExecutor`/`CommandService`/`SettingsService`/`HotkeySettings`/`SingleInstanceService`/`GlobalHotkeyService`）✓（计划 262-278 确实没有）；D4.1（只复制不删除/失败回退/可覆盖/有单测）✓（T5 计划 3133-3582、T6 3583-3803、VD-7 计划 64）；D5.1 ✓（`BashCommandManager.csproj:8` 已有 `DefaultItemExcludes`）；D5.5 ✓（无新依赖）；D5.6 ✓（计划与新增内容均 UTF-8）；**D5.3 ✗**（`SingleInstanceService.Initialize` 改签名未记录）；**D2.2 第 3 条 ✗**（漏链 `TerminalLaunchPlan.cs`，见 B-1）；D6 **部分**（README、GUI csproj 版本项） | `DECISIONS.md:48-52,54-68,87-96,97-105` |

---

## 阻断性问题

> 每条给出：位置（计划行号）→ 事实/证据 → 影响 → 可直接采用的修法。修法均不改设计意图、不改设计文档。

### B-1 CLI 未链接 `Core/Models/TerminalLaunchPlan.cs`（CLI 编译必失败）

- **位置**：计划 262-278（T1 Step3 `SharedSources` 只有 14 个 `Compile Include`，264-276 行里没有 `TerminalLaunchPlan.cs`；`DatabasePathResolver.cs` 在 3562 补入，但补的也不是它）；描述错误在 19、50、8152、10596。
- **事实**：
  1. #9 计划明确把 `TerminalLaunchPlan` 放**独立文件** `Core/Models/TerminalLaunchPlan.cs`（#9 计划 50、75、352-367），并专门写了交接说明：「#10 链接 `TerminalCommandBuilder.cs` 时必须**同时链接** `TerminalLaunchPlan.cs` 与 `ExecutionResult.cs`，否则编译失败」。**工作区真实文件已复核**：`Core/Models/TerminalLaunchPlan.cs:4` 就是 `public sealed record TerminalLaunchPlan(…)`，`Core/Services/ExecutionResult.cs` 只有 `ExecutionOptions`（:6）与 `ExecutionResult`（:9），不含 `TerminalLaunchPlan`。
  2. 工作区 DECISIONS 已在 D2.2 第 3 条显式列出 `TerminalLaunchPlan.cs`（`DECISIONS.md:50`）。
  3. 计划 19/50/8152/10596 把它写成「`ExecutionResult.cs`（含 `TerminalLaunchPlan`…）」，是照抄了**旧版** DECISIONS 的错误描述；实际 `ExecutionResult.cs` 只有 `ExecutionOptions`/`ExecutionResult`（#9 计划 369-386）。
  4. 被链接的 `TerminalCommandBuilder.cs` 返回 `TerminalLaunchPlan`（真实文件 `Core/Services/TerminalCommandBuilder.cs:43`，返回类型即 `TerminalLaunchPlan`），`RunCommand` 也直接用 `plan.FileName/Arguments/WorkingDirectory/Verb`（计划 8490-8498、8523-8538），因此缺失时 CLI 编译报 `CS0246: TerminalLaunchPlan`。
- **修法**：
  1. 在计划 268 之后（`ExecutionSettings.cs` 之后）加一行：
     ```xml
     <Compile Include="..\Core\Models\TerminalLaunchPlan.cs" Link="Shared\Core\Models\TerminalLaunchPlan.cs" />
     ```
  2. 把计划 19、50、8152、10596 的括号说明改为「`Core/Models/TerminalLaunchPlan.cs`（单独链接）、`Core/Services/ExecutionResult.cs`（含 `ExecutionOptions`/`ExecutionResult` 记录）」。

### B-2 `groups create` 的 `sortOrder` 断言与实现、spec 三方矛盾（T7/T12 测试必红）

- **位置**：计划 3987-3988（T7 `Creates_groups_with_monotonic_sort_order` 期望第一个同级分组 `SortOrder=1`、第二个 `=2`）、5899（T12 `Create_uses_root_parent_by_default` 期望 `data["sortOrder"]==1`）。
- **事实**：
  - T7 的实现（计划 4262-4272）是 `sortOrder = all.Count(group => group.ParentId == parentId)`；根分组 `ParentId=null`（`DatabaseInitializer.cs:53-54` 插入 `(1,'根分组',NULL,0)`），所以**第一个**挂在根下的分组 `SortOrder=0`、第二个 `=1`。
  - spec 命令表 `groups create` 的示例就是 `{"id":2,"name":"运维","parentId":1,"sortOrder":0}`（design L182）。
  - GUI 既有算法同样是 `siblings.Count()`（`GroupService.cs:47-53`），CLI 必须与之同源。
- **影响**：Step1 写的失败测试在 Step4 后仍然红；执行者若「让实现迁就测试」把公式改成 `Count+1`，就直接违背 spec 示例与 GUI 一致性。
- **修法**：把 3987 改为 `Assert.Equal(0, …)`、3988 改为 `Assert.Equal(1, …)`；把 5899 改为 `Assert.Equal(0, …)`（并在测试名/注释里点明「同级从 0 开始，与 spec L182、GUI `GroupService` 一致」）。

### B-3 `CliSettingsStore.SetHotkey` 对不存在的 `settings.json` 写扁平格式（T10/T22/T25 三处必红，且违反 D3）

- **位置**：实现 5379-5395（`var root = LoadRoot() ?? new JsonObject(); if (root["Hotkey"] is JsonObject nested) {…} else { root["Enabled"]=… }`）；受影响的断言：5252-5261（T10 `Writing_to_missing_file_creates_nested_format` 断言 `root["Hotkey"]["Key"]==57`）、9072（T22 手工冒烟期望 `{"Hotkey":{"Enabled":true,...}}`）、9997-9998（T25 Smoke7 读 `settingsJson["Hotkey"]["Key"]`）。
- **事实**：文件不存在或内容为 `{}` 时走 `else` 分支，写出 `{"Enabled":true,"Key":57,"Modifiers":6}`，**没有 `Hotkey` 节点**；Smoke7 在空临时目录里执行 `config set hotkey`，随后 `settingsJson["Hotkey"]` 为 null，测试以 `NullReferenceException` 失败。DECISIONS D3（`DECISIONS.md:54-68`）规定 `settings.json` 唯一形状是 `{ "Hotkey": {...}, "Execution": {...} }`，并写明「#10 不得再定义第二套格式」。
- **影响**：T10 Step4、T22 Step5、T25 Smoke7 三条验收链全断；且 `config set` 会在 GUI 从未启动过的机器上写出旧扁平格式（#9 虽能迁移，但计划自己承诺的是嵌套）。
- **修法**：`SetHotkey` 改为「有 `Hotkey` 对象 → 改它；否则若根上已有旧扁平 `Enabled`/`Key`/`Modifiers`（仅 `root["Enabled"] is not null` 时）→ 保持扁平（`Setting_hotkey_keeps_flat_format_flat` 仍通过）；否则新建 `root["Hotkey"] = new JsonObject { ["Enabled"]=…, ["Key"]=…, ["Modifiers"]=… }`」。建议同时让 `GetHotkey()` 在扁平分支读根、嵌套分支读节点（现状已如此）。

### B-4 T25 的 `run` 冒烟读取真实 `%LOCALAPPDATA%\BashCommandManager\settings.json`（测试隔离硬约束被破坏）

- **位置**：计划 9923、9928（Smoke3 的 `run --dry-run`、`run --wait`）、9946（Smoke4 的 `run --wait`）都调用 `CliProcess.Run(new[]{ "run", … })`，**没有传 `settingsDirectory`**；`CliProcess.Run` 的默认值是 `settingsDirectory = null`（9752、9799），只清 `BASHCOMMANDMANAGER_DB`/`BASHCOMMANDMANAGER_GUI`（9818-9819），不清/不设 `BASHCOMMANDMANAGER_SETTINGS_DIR`。
- **事实**：`RunCommand` 第一步就是 `services.Settings.GetExecutionSettings()`（计划 8473），而 `CliHost.CreateServices` 用 `new CliSettingsStore(CliPaths.SettingsDirectory)`（6385），默认目录是 `%LOCALAPPDATA%\BashCommandManager`（`CliPaths` 4152-4165 + `DatabasePathResolver.SettingsDirectory`）。所以 Smoke3/Smoke4 会**读开发机真实 settings.json**；结果直接受开发机配置影响：
  - `Terminal=Custom/WindowsTerminal/PowerShell7` 且程序缺失 → 走降级或直接 `E_FILE_NOT_FOUND`（Smoke3 的 `exitCode=7` 断言失效）；
  - `ArgumentsTemplate` 被手工改坏（不含 `{file}`）→ `TerminalCommandBuilder.Build` 抛 `TerminalConfigurationException`（计划未捕获，落到 `E_INTERNAL`，见建议 4）→ Smoke3/Smoke4 失败；
  - `KeepWindowOpen=false` 等也会改变 `/k`、`/c` 的 dry-run 断言。
- **影响**：冒烟不再是「本机可复现」，且违反 Global Constraints 27「任何测试/冒烟不得读写真实 `settings.json`；一律用 … `BASHCOMMANDMANAGER_SETTINGS_DIR` 指向临时目录」。
- **修法**（任选其一，推荐第 1 条）：
  1. `CliProcess.Run` 的 `settingsDirectory` 默认值改为「本次调用创建/复用一个临时空目录」，即 `settingsDirectory ??= Path.Combine(Path.GetTempPath(), "bcm-smoke-settings-" + Guid.NewGuid().ToString("N"))`，并无条件 `startInfo.Environment["BASHCOMMANDMANAGER_SETTINGS_DIR"] = settingsDirectory`；或在 `CliSmokeTests` 里给 Smoke3/Smoke4 显式传 `temp.CreateDirectory("settings")`（Smoke7 已这么做）。
  2. 同时在 9818 之后补一行 `startInfo.Environment.Remove("BASHCOMMANDMANAGER_SETTINGS_DIR")` 防止 ambient 变量泄漏（先删除再按需设置）。

### B-5 T20 手工冒烟第 3 条不带 `--db`，会写默认库、可能执行真实命令（违反隔离约束）

- **位置**：计划 8644 `& $exe run ok --wait --json`（同一脚本前两条 8642/8643 都带 `--db "$tmp\app.db"`）。
- **事实**：没有 `--db` 时走 `DatabasePathResolver.ResolveForCurrentProcess(null)` → 规范位置 `%LOCALAPPDATA%\BashCommandManager\data\app.db`；`DatabaseSession` 的构造会 `Initialize()` 建表（文件不存在时**创建真实库**）。更危险的是：如果开发机真实库里恰好存在名为 `ok` 的命令，这一步会**真的执行它并写执行统计**。该步骤的期望「第三条命令不存在 → 退出 2」本身就依赖「真实库里没有 `ok`」这一未验证前提。
- **影响**：违反 Global Constraints 27（所有冒烟必须 `--db` 临时库）与「绝不碰真实用户数据」的验收前提；一旦命中真实命令，属于越权执行。
- **修法**：把 8644 改为 `& $exe run definitely-not-exist --wait --db "$tmp\app.db" --json`，期望仍为退出 2；或整行删除（`run 9999` 的 E_NOT_FOUND 已由 T25 Smoke3 覆盖）。

### B-6 T24 给 `MainWindow` 新增的方法缺 `using BashCommandManager.Core.Services;`（GUI 编译必失败）

- **位置**：计划 9604-9622（`ApplyStartupArguments` / `ApplyStartupArgumentsCoreAsync`，其中 9618 调用 `StartupArgumentParser.TryGetCommandId`）；计划只在 9683 提醒「`MainViewModel` 文件头需要 `using BashCommandManager.Core.Services;`」，**没有提醒 `MainWindow.xaml.cs`**。
- **事实**：`StartupArgumentParser` 的命名空间是 `BashCommandManager.Core.Services`（计划 9413）。#8 落地后 `MainWindow.xaml.cs` 的 using 只剩 `BashCommandManager.ViewModels`、`System.ComponentModel`、`System.Windows`、`System.Windows.Controls`、`System.Windows.Input`（真实文件 1-5 行，已无 `Core.Services`，旧版那句 using 是为 `IGlobalHotkeyService`/`ISettingsService` 服务的，被 #8 删掉了）。因此 9618 行会报 `CS0246: 未能找到类型或命名空间名"StartupArgumentParser"`。
- **修法**：在 T24 Step8 的说明里补一句「`MainWindow.xaml.cs` 文件头追加 `using BashCommandManager.Core.Services;`」，或在代码里写全限定 `BashCommandManager.Core.Services.StartupArgumentParser.TryGetCommandId(...)`。

---

## 命令契约对照表（19 条）

> 「spec 契约」列取 design L181–L199 的命令行形态 + JSON data 字段 + 退出码；「计划位置」列指向 B 部分实现与测试；结论中 ⚠ 表示有可接受但需记录的偏差。

| # | 命令 | spec 契约（参数/默认、JSON 字段、退出码） | 计划位置 | 结论 |
|---|---|---|---|---|
| 1 | `groups list` | `[--tree]`；data `id,name,parentId,sortOrder,commandCount`，tree 时 `children` 嵌套；meta `count` | T12：6180-6201（实现）、5835-5885（测试）；字段 `JsonShapes.Group` 2783-2823 | 通过 |
| 2 | `groups create` | `--name`、`[--parent <id\|path>]` 默认 `1`、`--parent none` 建顶级；data `id,name,parentId,sortOrder`；重名→1 `E_INVALID_VALUE` | T12：6204-6245；测试 5887-5957 | **有条件**：实现/spec 一致（`sortOrder` 从 0 起，spec L182），但计划自己的两处断言写反 → B-2 |
| 3 | `groups rename` | `<id\|path> --name <new>`；data `id,name`；0 条→2、多条→1 | T13：6579-6597；测试 6474-6505 | 通过 |
| 4 | `groups delete` | `<id\|path> --force`；data `deletedGroupIds,deletedCommandIds`；缺 `--force`→1、根分组→1（`E_ROOT_GROUP`） | T13：6599-6618；parser 2287-2293；测试 6507-6563 | 通过 |
| 5 | `commands list` | `[--group]`、`[--sort name\|last\|count]` 默认 name、`[--order asc\|desc]` 默认 asc、`[--limit n]` 0=全部；data 9 字段（含 `groupName`、`executionCount`、`lastExecutedAt`） | T14：6841-6889；`CommandCatalog` 785-796；测试 6700-6752 | 通过 |
| 6 | `commands search` | `<keyword>` + `[--group --sort --order --limit]`；data 同 list | T14：6844-6845；测试 6754-6796 | 通过 |
| 7 | `commands show` | `<id\|name> [--group]`；单条 + `fileExists`；0 条→2、多条→1 | T15：7047-7053；测试 6948-6978 | 通过 |
| 8 | `commands add` | `--name --file [--description] [--group]`；data `id,name,filePath,groupId`；文件不存在→1 `E_FILE_NOT_FOUND` | T15：7055-7098；`JsonShapes` 2800-2823；测试 6980-7031 | 通过 |
| 9 | `commands edit` | `<id\|name> [--name --description --file --group]` 至少一项；返回更新后命令对象 | T16：7276-7316；parser 2351-2358；测试 7157-7230 | ⚠ 通过：`--group` 被实现为「移动到目标」，与 §3「`<id\|name>` 的 `--group` 先限定候选」的通用规则冲突（计划 7278 有注释，但 spec 未豁免）；建议在 spec/skill 里明确「edit 的 `--group` 是目标分组」 |
| 10 | `commands delete` | `<id\|name[,..]> --force`；data `deleted:[id]`；缺 `--force`→1 | T16：7318-7329；测试 7232-7260 | 通过 |
| 11 | `commands move` | `<id\|name[,..]> --to <id\|path>`；data `moved,targetGroupId` | T17：7456-7469；测试 7372-7403 | 通过 |
| 12 | `commands import` | `--group <id\|path>` `<path...>` `[--recursive --allow-duplicates --dry-run]`；data `group,imported,skipped,total`；`Name`=去扩展名、`Description=""`、`SortOrder`=同级数+序号；按 FilePath 忽略大小写去重；dry-run 不写库 | T18：7724-7813；测试 7547-7708 | 通过（额外输出 `dryRun` 布尔，属附加字段） |
| 13 | `commands export` | `[--group] [--recursive] [--format json\|csv] [--output]`；写文件时 `path,format,count`；无 `--output` 时 data 为 `{schemaVersion,exportedAt,groups,commands}`；CSV 固定 9 列、RFC4180、UTF-8 无 BOM | T19：7981-8105；parser 2360-2368（csv 必须带 `--output`）；测试 7863-7965 | 通过 |
| 14 | `commands frequent` | `[--limit n]` 默认 10，1-1000；data 同 list、按 `executionCount` 降序 | T17：7471-7483；parser 2343-2349；测试 7406-7440 | 通过 |
| 15 | `run` | `<id\|name> [--group] [--admin] [--wait] [--timeout] [--dry-run]`；默认 `/k` 不等待（`started,pid,waited:false`）；`--wait` `/c`、0/3、`durationMs`；`--admin` runas 且与 wait 冲突→1；`--timeout` 仅 wait、杀树→3 `E_TIMEOUT`；`--dry-run` 输出 `fileName/arguments/workingDirectory` | T20：8466-8615；parser 2370-2387；测试 8204-8432；真实 `TerminalCommandBuilder.cs:43-90` | **有条件**：① `pid` 类型由 `int` 变 `int?`（VD-3，计划 60，真实 shell-execute 可能拿到 id、也可能 null）；② `--dry-run` 不含 `verb`，`--admin --dry-run` 看不出提权；③ 全局 `RunAsAdministrator=true` 时 `--wait` 静默不提权（建议 2）；④ 超时/E_INTERNAL 记录策略与 spec 未明确处已按计划测试固化 |
| 16 | `config get` | `[key]`，key ∈ `hotkey`/`hotkey.enabled`/`db.path`/`db.source`/`settings.path`/`gui.path`/`version`；全量 data 含 `db{path,source}/settings{path}/hotkey{enabled,key,keyName,modifiers,display}/gui{path}/version` | T21：8801-8883；parser 2389-2394；测试 8706-8773 | 通过（单 key 返回「包含该 key 的整节」，spec 未细化；`version` 返回扁平 `{version}`） |
| 17 | `config set` | `hotkey <组合>` / `hotkey.enabled <bool>`；data `hotkey{enabled,key,keyName,modifiers,display}`；未知键→1 `E_CONFIG_KEY` | T22：9002-9048；parser 2396-2410；测试 8925-8986 | **有条件**：接口契约通过，但落盘形状缺陷 → B-3 |
| 18 | `open` | `[--command <id>] [--gui <path>]`；查找顺序 `--gui` > `BASHCOMMANDMANAGER_GUI` > `{cliDir}\..\BashCommandManager.exe` > `{cliDir}\BashCommandManager.exe`；data `opened,guiPath,commandId`；找不到→2 `E_GUI_NOT_FOUND` | T23：9213-9241；`GuiLauncher` 5596-5650；测试 9124-9186 | 通过（不校验 `--command` 指向的命令是否存在，见建议 9） |
| 19 | `--version` | 非 JSON 输出裸版本；`--json` 下 `{"ok":true,"data":{"version":"…"}}`（无 meta） | T1：930-935；`CliHost` 6331-6335；测试 496-515 | 通过（版本由程序集 `<Version>1.3.0</Version>` 推导，D6 口径） |

**表外说明**：19 行中无「缺参数」、无「字段名不一致」；唯一字段级偏差是 `run` 的 `pid` 可空（VD-3 已记录）与 `run`/`import` 的若干**附加**字段（`name`、`dryRun`），对 `ConvertFrom-Json` 消费方是向后兼容的。

---

## 建议改进

1. **D5.3 违约需记录或加兼容重载**：`SingleInstanceService.Initialize(Action)` → `Initialize(Action<string[]>, string[])`（计划 9295、9534-9540、9576-9588）是**替换已有 public 方法签名**，与 `DECISIONS.md:91`「不删除、不改名任何已有公开类/方法（只允许新增）」冲突；仓库内唯一调用点是 `App.xaml.cs:25`（已确认无其它调用者）。建议：新增重载 `public bool Initialize(Action onActivateCallback) => Initialize(_ => onActivateCallback(), Array.Empty<string>());`，并在计划 0.3 的偏差表加一条 VD（引用 D5.3）；否则至少在 T24 提交说明里按 DECISIONS 前言「记录偏差」。
2. **`--wait` 会静默丢弃全局提权设置**：`RunCommand` 读 `ExecutionSettings` 后只处理 `--admin`（计划 8473-8486），`--wait` 分支把 `plan.Verb` 传给 `ProcessRunner.RunWithResult`（8531-8538），而该实现用 `UseShellExecute=false`（4966-4980）；真实 `TerminalCommandBuilder.cs:90` 会按 `settings.RunAsAdministrator` 写出 `Verb=runas`，但本机探针确认该 Verb 在 `UseShellExecute=false` 时被静默忽略、不报错。结果是：用户在 GUI「执行」页勾了「默认管理员」后，`bcm run X --wait` 会**不知不觉以普通权限运行**。建议在 `WithoutKeepWindowOpen`（8604-8615）里同时 `RunAsAdministrator=false`，并在 `meta.warning` 或文档里说明；或对「全局 admin + `--wait`」直接报 `E_USAGE`。
3. **`--dry-run` 应包含 `verb`**：8491-8499 的 preview data 只有 `fileName/arguments/workingDirectory`；`--admin --dry-run` 时无法看出将走 `runas`。建议加 `["verb"] = preview.Verb`（null 时输出 null），并在 T20 测试里断言。
4. **`TerminalCommandBuilder` 的配置异常未映射**：8490/8523 的 `Build` 会抛 `TerminalConfigurationException`（#9 计划 766-771、814-821），`CliHost` 的兜底把它变成 `E_INTERNAL`（计划 3099-3101），退出码 3；这属于用户配置错误，应该给 `E_CONFIG_KEY`（1）+ 指向设置页的 hint。建议在 `RunCommand` 里 `catch (TerminalConfigurationException ex) → CliErrors.ConfigKey(...)`。
5. **T29 缺验收 4 的步骤 + Step8 编号错位**：T29 通过 Step3/4/6/7/8 覆盖了验收 1/2/3/5/6/7/8/9/10，但 **验收 4「19 条命令可执行且 `ConvertFrom-Json` 字段一致」没有对应步骤**（Self-Review 10558 只写「T12–T23、T25」）；Step8 表（10518-10526）把「退出码」标成验收 4（实际是 spec 验收 5）、把「删分组」标成 5（实际是 8），且验收 8 出现两行。建议：补一条 PowerShell 脚本遍历 19 条命令（`--json`）用 `ConvertFrom-Json` 断言字段集合；并把 Step8 的行号改成 `5/6/7/8/9/10` 并补 `4` 的自动步骤。
6. **D6 的 README 版本项落空**：`README.md` 目前没有版本号（全文无 `1.3.0`/`1.2.1`），T28(c)（计划 10397）只说「若出现 1.2.1 一并改」，T29 Step7（10511-10514）也只断言「README 无 1.2.1」；同时 `BashCommandManager.csproj` 没有 `<Version>`（真实文件 1-13 行）。D6（`DECISIONS.md:104`）要求「csproj `Version`、Inno `MyAppVersion`、README、Release 标题」。建议：在 README「关于/版本」处显式写 `v1.3.0`，或把 D6 的「csproj `Version`」明确为「CLI csproj」并删掉 README 项，避免发版时口径不一致。
7. **T28 的 PACKAGING 增补位置有冲突**：计划 10309 要新增 `### 4. 发布 CLI（bcm.exe）`，但 `docs/PACKAGING.md:78` 已有 `### 4. 检查生成的安装包`，会得到两个「4.」；且 T28(c)（10334-10341）替换的那句在 `docs/PACKAGING.md:124` 的 `gh release create --notes` 模板里，不是在「注意事项」正文。建议：把 PACKAGING 原有「### 4. 检查生成的安装包」改为「### 5.」并同步顶部目录（计划 9-13 行），或把新增节编为 `### 3.1`；路径说明改到「打包概述」或新加「数据位置」小节，release notes 模板单独改。
8. **SKILL 的「19 条」与实际 22 行不符**：10122-10147 的 Cheat Sheet 标题写「19 条」，表内 22 行（`groups list` 两行、`export` 拆 JSON/CSV、`config get` 两式）。spec §5.4（design L236）要求「19 条命令各一行」。建议合并成 19 行（`groups list --tree` 与 `groups list` 合并、export 合并、config get 合并），或把标题改为「常用命令速查」。
9. **`open --command` 不校验命令存在**：9225-9237 只把 id 透传给 GUI；命令不存在时 CLI 仍返回 `{"ok":true,"data":{"opened":true,…}}`，代理会误判「定位成功」。建议 `open` 在已有 `--command` 时用只读连接 `SELECT 1 FROM Commands WHERE Id=@id`（或 `store.GetCommandAsync`）校验，不存在返回 2 `E_NOT_FOUND`；若坚持不校验，至少在 `meta` 里带 `commandVerified:false`。
10. **`commands export` 人类模式不打印文档**：8022-8025 在无 `--output` 时返回 `HumanText="schemaVersion: 1…"`，`data` 里的完整 JSON 文档只在 `--json` 下可见；人类模式用户看不到导出内容。建议 human 模式直接打印 `document.ToJsonString(WriteIndented)`（或至少提示「加 `--json` 查看完整文档」）。
11. **GUI 定位只切 VM 不切树选中**：`SelectCommandAsync`（9671-9680）只设 `GroupTreeViewModel.SelectedGroup`；真实 `MainWindow.xaml:88-121` 的 TreeView 只挂了 `SelectedItemChanged` 事件，**没有 `SelectedItem`/`IsSelected` 绑定**，所以树上的高亮/展开不会跟着走；另外搜索是全局的（9677-9678 `CommandListViewModel.SearchAsync(keyword)` 不按分组过滤，见 `CommandListViewModel.cs:100-125`），T24 Step11（9731）「列表只剩该命令」只在命令名全局唯一时成立。建议：给 `TreeViewItem` 的 `IsSelected` 加绑定（或改用 `SelectedItem` 双向绑定），并把验收措辞改为「切到该分组 + 搜索框填入名称（同名命令会显示多条，符合 A9）」。
12. **CS1998 两处**：6544 `Delete_requires_force_at_parser_level`、8979 `Set_unknown_key_is_config_key_error` 声明为 `async Task` 但函数体没有 `await`，会产生 `CS1998` 警告；D6 要求构建 0 warning（虽然只约束主工程，但 T29 Step2 期望 CLI 测试全绿时最好干净）。去掉 `async` 即可（`Assert.Throws` 是同步的）。
13. **两处文档级不一致**：7853 的 T19 Interfaces 写 `CliCommandStore.GetCommandSubtree`（不存在），实际是 `GetGroupSubtreeIdsAsync`（4284）；3825 的 T7 注「`CliServices` 在 **Task 10** 一次性引入」实际是 Task 12。建议顺手改掉，避免执行者按错误名字找方法。
14. **T25 Step3 硬编码本机路径**：10026 `Get-Item 'D:\Program Files\BashCommandManager\data\app.db'` 只在这台机器成立；建议改为从注册表 `InstallLocation` 拼路径，或 `Get-Item … -ErrorAction SilentlyContinue` 并允许「未安装」。
15. **`.iss` 的 PATH 追加在空 Path 时会产生前导分号**：10243 的 `ValueData: "{olddata};{app}\cli"`，当 HKCU `Path` 不存在时 `{olddata}` 为空 → 值以 `;` 开头（空 PATH 项在部分 Win32 API 里等价于「当前目录」）。建议用 `[Code]` 的 `NeedsAddPath`/`AddToPath` 惯用法，或拆成两条带 `Check:` 的注册表项（Path 为空/非空各一条）。
16. **T27/T29 对 Lite 的 CLI 产物没有断言**：T29 Step6 只 `publish` 并断言 Full 的 `cli`，Lite 的 `Packaging\InnoSetup\Lite\bin\cli` 若在重跑 Step6 前被 `rm -rf bin/*` 清掉，Lite 安装包会静默缺少 CLI。建议在 Step6 同时发布/断言 `Lite\bin\cli\bcm.exe`（或把 Lite 的 CLI publish 也写进该步骤）。

---

## 风险

| 风险 | 触发场景 | 影响 | 缓解/回滚 |
|---|---|---|---|
| 旧版本 GUI 收到新 `ACTIVATE\t[...]` 消息 | 只升级 CLI，GUI 仍是 1.2.x | 旧 GUI 的 `message == "ACTIVATE"` 判断不成立（真实 `SingleInstanceService.cs:96`），**窗口不会激活**，用户以为 `bcm open --command` 失败；VD-4（计划 61）说「旧 GUI 忽略参数仍启动」并不准确 | SKILL/Troubleshooting 已写「GUI 需与 CLI 同版本」；发布说明强调同时升级；CLI 的 `open` 可考虑在无法探测时返回 `meta.warning` |
| `--wait` 子进程输出并发写 stderr | `ProcessRunner` 两个 `Task.Run(Forward(...))` 同时 `CopyTo` 同一个 `Console.OpenStandardError()` 流（4989-4990、5018-5038） | 输出字节可能交错（非崩溃，但日志可能串行错乱） | 给 `Forward` 加锁，或合并成单线程读两个流；Smoke4 只断言 `Contains("marker")`，测不出交错 |
| 迁移失败/权限不足 | `%LOCALAPPDATA%` 只读、磁盘满、并发启动 | 已有设计：回退旧库 + `meta.warning`（D4.1，T5/T6） | 保留 `BASHCOMMANDMANAGER_DB` 回滚；不要在 CLI 里删除旧库 |
| 单文件 + `SQLite.Interop.dll` | Full/Lite 单文件发布 | 首次运行解包失败或找不到原生库 | `IncludeNativeLibrariesForSelfExtract=true`（T1 已加）；T27 Step3 手工跑 `--version` 只能覆盖「进程能起」，**不能覆盖首次真正打开数据库**；建议把 T29 的 CLI 冒烟改为对发布产物跑一次 `groups list --db <tmp>` |
| 安装包体积 | Full 自带自包含 CLI | 预计 +20–25MB（spec 风险表） | 只发 CLI zip 作为替代；安装包选项默认不勾 PATH |
| `settings.json` 形状漂移 | #9 之前 CLI 写入扁平（B-3） | GUI #9 首次读会迁移（D3 允许旧格式读取），但计划自己的测试期望嵌套 → 先红后绿 | 修 B-3 后统一写嵌套；`Setting_hotkey_keeps_flat_format_flat` 只用于保留旧文件形状 |
| 验收依赖本机已安装实例 | T29 Step4/T25 Step3 引用 `D:\Program Files\BashCommandManager\data\app.db` | 在未安装/异机 CI 上步骤报错或误判 | 改为条件检查（建议 14） |
| `run --admin` 无桌面会话 | CI/服务账户 | UAC 无法弹窗；`Win32Exception 1223` 被正确映射为 `E_ELEVATION_CANCELLED`（3） | SKILL Safety 已写；文档注明无桌面不可用 |
| `groups create` 同级重名 | 并发两个 CLI 同时创建同名分组 | `FindChildGroupByNameAsync` 先查后写，非原子，可能造出重名（DB 无唯一约束） | 与 GUI 现状一致；GUI 的 `CheckGroupNameExistsAsync` 同样先查后写；可在文档标注「并发创建同一分组非强一致」 |
| 版本口径 | GUI csproj 无 `Version`、README 无版本 | `bcm --version` 与安装包/README 可能各自漂移（D6） | 建议 6；发版清单（`.claude/skills/packaging-release`）需同步 |

---

## 修订记录（作者：修订代理 B）

- 修订对象：`docs/superpowers/plans/2026-11-08-cli-and-skill.md`
- 修订范围：第 0.3 节偏差表 + Task 12–29（Task 12 现自第 5938 行起）
- 行号说明：下表行号均为**修订后**当前文件行号；定位一律以「Task + 小节」锚点为准，不按评审报告旧行号数字盲改。
- 边界：只改计划文档，未改源码、未 commit。

### 1. 阻断项修复

| 项 | 锚点（Task / 小节） | 新行号 | 处理 |
|---|---|---|---|
| B-2 | T12 Step1 测试 `Create_uses_root_parent_by_default` | 6168 | `data["sortOrder"]` 期望由 `1` 改为 `0`，并注释「同级从 0 开始：spec L182 + GUI `GroupService` 的 `siblings.Count()`」。全库复查 `sortOrder: 1`/`sortOrder: 2` 与 `"sortOrder":1/2` 均为 0 处；T7 的 4153-4154 已是 0/1，T12 Step8 冒烟 JSON、T13/T14、T25/T26 的 JSON 示例均为 0。 |
| B-3 | T10 Step3 `CliSettingsStore.SetHotkey`（实现）；T22 Step5；T25 Step1 Smoke7 | 5565-5589；9346；10372、10384-10385 | 复核 A 已把缺文件 / 空对象 / 缺 `Hotkey` 节点统一写嵌套 `{"Hotkey":{...}}`，只有旧扁平文档才保持扁平；T22 Step5 的 `settings.json` 期望与 Smoke7 的 `settingsJson["Hotkey"]["Key"]` 断言均已是嵌套，无需再改（本次仅核对并记录）。 |
| B-4 | T25 Interfaces；Step1 `CliProcess.Run`；Step3 | 10115；10162-10235；10410-10424 | `Run` 省略 `settingsDirectory` 时自动创建本次调用专属临时目录，先 `Remove` `BASHCOMMANDMANAGER_DB`/`BASHCOMMANDMANAGER_GUI`/`BASHCOMMANDMANAGER_SETTINGS_DIR` 再显式设置 `BASHCOMMANDMANAGER_SETTINGS_DIR`，`finally` 删除自有临时目录。Smoke3/Smoke4 等不传参数的调用也落临时 settings，8 条冒烟不再读写开发机真实 `settings.json`。Step3 改为断言临时根目录 + 条件式旧库时间戳核对。 |
| B-5 | T20 Step5 | 8917-8918 | 第 3 条由 `run ok --wait --json` 改为 `run definitely-not-exist --wait --db "$tmp\app.db" --json`；同时把期望改为「临时库中找不到命令 → 退出 2（`E_NOT_FOUND`），不会碰真实库」。 |
| B-6 | T24 Step8 | 9989-9995 | 增加显式提示：`MainWindow.xaml.cs` 文件头必须补 `using BashCommandManager.Core.Services;`（#8 已删除该 using），并给出可直接粘贴的 using 代码块，说明否则 `StartupArgumentParser` 报 `CS0246`。 |
| B-1 | T1 Step3 `SharedSources`；T20 Interfaces；Self-Review D2.2 | 255-256；8425；11114 | 复核 A 已在 `ExecutionSettings.cs` 之后加入 `Core/Models/TerminalLaunchPlan.cs` 的 `Compile Include`；T20 Consumes 与 Self-Review 已改为「`TerminalLaunchPlan.cs`（单独链接）、`Core/Services/ExecutionResult.cs`（只含 `ExecutionOptions`/`ExecutionResult`）」，与 D2.2 第 3 条及真实 #9 文件一致，无需再改。 |

### 2. 采纳的改进（逐条落地位置）

| 改进 | 锚点（Task / 小节） | 新行号 | 处理 |
|---|---|---|---|
| T29 补 spec 验收 4 + 修正 Step 编号 | T29 Step7（新增）；Step8/9/10（原 7/8/9 顺延） | 10941；11025；11035；11046 | 新增 PowerShell 脚本按顺序执行 19 条命令，用 `ConvertFrom-Json` + `Assert-Fields` 校验各命令字段；手工验收表按 spec 验收 5/6/7/8/9/10 重排，验收 4 标注由 Step7 自动覆盖；Step10 的「Step 1-9」同步。 |
| README 版本与 D6 对齐 | T28 Step3(c)；Step4 | 10817；10821-10828 | README 标题后显式新增 `> 当前版本：v1.3.0`；Step4 增加 `README v1.3.0` 与两个 csproj `<Version>` 的 grep 断言。 |
| GUI csproj 版本 | T27 Files；Step1(e)；Step5 | 10615；10663；10699-10702 | 新增修改 `BashCommandManager.csproj`，在 `<PropertyGroup>` 增加 `<Version>1.3.0</Version>`，并加入 `git add`。 |
| PACKAGING 章节号冲突 | T28 Step1(b) | 10727 | 新增 `### 4. 发布 CLI（bcm.exe）`，把原 `### 4. 检查生成的安装包` 改为 `### 5.`；说明顶部 TOC 只列 `##` 级无需改。 |
| SKILL「19 条」与实际行数 | T26 Step2 Cheat Sheet | 10519-10544 | 把原 22 行合并为 19 行（`groups list --tree`、`export` JSON/CSV、`config get <key>` 合并进所属命令），标题改为「19 条，与 `bcm --help` 一一对应」，并加注说明；与 `CommandCatalog.Commands.Count == 19` 一致。（选择「合并为 19」而非把标题改成 22：spec 5.4 要求 19 条命令各一行，且必须与 `bcm --help` 一致。） |
| `open --command` 校验命令存在 | T23 Interfaces；Step1 新增测试；Step3 实现；Step5 | 9365；9415-9464（新增 9434、9451）；9537、9567-9586；9604-9615 | `open --command` 先只读打开 `services.DatabaseResolution.DatabasePath` 查 `Commands`，不存在则 `E_NOT_FOUND` 退出 2；库文件不存在时直接返回 2 且不建库。新增 `Open_passes_existing_command_id`、`Open_missing_command_is_not_found_exit_two`、`Open_missing_command_does_not_create_database` 三个用例；Step5 增加 `open --command 999 --db <临时库>` 冒烟。 |
| GUI 侧 `--command` 定位的选中逻辑 | T24 Step9 说明；Step11 期望 | 10043；10085-10086 | 明确列出现有绑定：`ItemsSource="{Binding GroupTreeViewModel.Groups}"`、`SelectedItemChanged="GroupTree_SelectedItemChanged"`、`Tag="{Binding GroupTreeViewModel}"`、`HierarchicalDataTemplate ItemsSource="{Binding Children}"`；没有 `SelectedItem`/`IsSelected` 绑定，`TreeView.SelectedItem` 只读，故无法从 VM 反向驱动高亮。采用任务允许的替代方案：`SelectCommandAsync` 直接设置 `GroupTreeViewModel.SelectedGroup`，并修正 Step11「列表只剩该命令」的不实措辞（同名命令可能多条，符合 A9）。 |
| CS1998 两处 | T13 Step1 `Delete_requires_force_at_parser_level`；T22 Step1 `Set_unknown_key_is_config_key_error` | 6813；9253 | 两处均从 `public async Task` 改为 `public void`（函数体本就没有 `await`，`Assert.Throws` 是同步 API）。 |
| T25 硬编码 `D:\` | T25 Step3；T29 Step4 | 10410-10424；10881-10902 | 改为 `[System.IO.Path]::GetTempPath()` 断言冒烟临时根目录，并用 `$env:ProgramFiles` 推导旧库路径、`Test-Path` 不存在则跳过；T29 Step4 同步改为条件式核对。 |
| `.iss` PATH 空值前导分号 | T27 Step1(d) | 10637-10661 | 拆成两条互斥 `[Registry]` 项：`UserPathHasValue` 时写 `{olddata};{app}\cli`，`UserPathIsEmpty` 时只写 `{app}\cli`；配套 `[Code]` 的 `UserPathHasValue`/`UserPathIsEmpty` 用 `RegQueryStringValue` 读安装前 HKCU Path，不会产生前导分号。 |
| Lite CLI 发布断言 | T27 Step2/Step3；T29 Step6 | 10667；10676-10686；10923-10937 | Step2 期望补「Lite 产物缺失即失败」；Step3 增加 `Test-Path` 断言 + Full/Lite `--version`，并注明 Lite 需本机 .NET 8 Desktop Runtime（报框架缺失属 Lite 设计前提）；T29 Step6 增加 Lite publish、两个产物断言与 Lite `--version`。 |
| D5.3 偏差登记 | 0.3 偏差表 VD-12；T24 Step6；Self-Review D5.3 | 69；9873-9893；11118 | 新增 VD-12：保留 `Initialize(Action)` 作为兼容重载并转发到 `Initialize(Action<string[]>, string[])`；T24 Step6 明确「改动四处」并给出重载代码；Self-Review D5.3 改为「旧签名保留 + 新增重载」。 |
| `commands edit --group` 语义 | 0.3 偏差表 VD-13；CommandCatalog `commands.edit`；T16 Interfaces/测试/实现；T26 SKILL | 70；828、836；7421、7446、7550；10544 | 明确选择「`--group` 为目标分组」：编辑时移动到该分组，可与 `--name`/`--description`/`--file` 同用；`commands move` 专做批量移动；`commands show/run/delete` 的 `--group` 仍先限定候选。命令表摘要与选项说明、`EditAsync` 注释、`Edit_updates_only_given_fields` 的「未给 --group 不改分组」断言、SKILL 注记与 VD-13 口径一致。 |
| T19 方法名笔误 | T19 Interfaces | 8126 | `CliCommandStore.GetCommandSubtree`（不存在）改为 `GetGroupSubtreeIdsAsync`（实际方法）。 |

### 3. 未采纳项与理由

- **建议 2（`--wait` 静默丢弃全局提权）**：涉及产品语义（报 `E_USAGE` 还是返回 `meta.warning`，以及 GUI 设置页文案），不在本轮必须修复/明确采纳清单内，留给 #10 执行前的设计确认，避免擅自改变 `run` 行为。
- **建议 3（`--dry-run` 增加 `verb`）**：会扩展已冻结的 JSON 字段集合，本轮不改，避免与 spec 第 4 节字段契约冲突。
- **建议 4（`TerminalConfigurationException` 映射为 `E_CONFIG_KEY`）**：属运行时行为变更，未在清单内；T20 现有 `E_INTERNAL` 兜底至少不会崩溃，建议在 #10 后续单独决策。
- **建议 10（`commands export` 人类模式打印完整文档）**：UX 变化，且 `--json` 已能拿到完整文档；本轮不改。
- **建议 11 的 TreeView `IsSelected`/`IsExpanded` 绑定**：任务允许的替代方案已采用（通过 `MainViewModel` 直接设置 `GroupTreeViewModel.SelectedGroup`），并在 T24 Step9 写清「不可行」的证据与将来扩展方式；不新增视觉绑定，避免扩大 GUI 改动面。
- **建议 13 的 T7 内「Task 10」笔误（原 3825 行）**：属 A 部分（T7）范围，不在修订代理 B 的 5938 行之后范围内；只修了 B 范围内的 `GetCommandSubtree`（8126）。

### 4. 自检结果

- `grep -n "sortOrder: 1"` → 0；`grep -n "sortOrder: 2"` → 0；`"sortOrder":1/2` → 0。
- `grep -n "BASHCOMMANDMANAGER_SETTINGS_DIR"` → T25 段内命中 10115、10191、10192（另有 T26/T29 的冒烟脚本，属预期）。
- `grep -n "using BashCommandManager.Core.Services"` → T24 段内命中 9992（MainWindow using 说明）。
- 占位符扫描：未完成标记 / 空白待填项 → 0（Self-Review 的扫描句已改为「未完成标记（占位符）」，避免自身被 grep 命中）。
- 结构：29 个 `### Task` 连续；T29 Step 1–10 连续；全文 11142 行。
- `git status`：仅计划文档被修改、评审报告新增本段；未改源码、未 commit。