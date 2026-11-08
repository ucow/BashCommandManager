# CLI 调用层与操作 Skill（#10）实施计划 A 部分评审报告

- **评审对象**：`docs/superpowers/plans/2026-11-08-cli-and-skill.md` 第 **1–5668** 行（`## 0. 执行前置条件`、`## File Structure`、Task 1 – Task 11）。
- **行号约定**：下文 `计划 N` 指该文件第 N 行（按 `\n` 切分的实际行号，全文 10624 行；Task 11 结束于 5668，Task 12 起为 B 部分）。
  `#8 N` / `#9 N` 分别指 `plans/2026-11-08-hotkey-tray.md` / `plans/2026-11-08-execution-terminal.md` 第 N 行；`D N` 指 `DECISIONS.md` 第 N 行；`spec N` 指 `specs/2026-11-08-cli-and-skill-design.md` 第 N 行。
- **参照**：spec（345 行）、DECISIONS（105 行）、#9 计划（3712 行）、#8 计划（2827 行）。
- **行号基线**：本报告所有行号对应计划文件 `10624` 行版本（`commit 1db6ac7` 提交的版本）。若计划后续修订，请以本报告引用的代码/文案锚点定位。
- **仓库状态（评审过程中变化，已按最新状态核对）**：分支 `feat/v1.3.0`；评审开始时 HEAD `db4dba7`，结束时已推进到 `1db6ac7`（两次均为 docs/评审提交）。**#8 已合入**（`Core/Services/HotkeyMessageWindow.cs`、`WindowTogglePolicy.cs`、`tests/BashCommandManager.Tests/`、主 csproj 的 `DefaultItemExcludes` 均已存在）；**#9 仍未合入**（`Core/Models/TerminalKind.cs`、`ExecutionSettings.cs`、`TerminalLaunchPlan.cs`、`Core/Services/TerminalCommandBuilder.cs`、`ExecutionResult.cs`、`ExecutableLocator.cs` 均不存在；评审结束时工作区只出现了 #9 T1 的 `tests/BashCommandManager.Tests/ExecutionSettingsTests.cs`）。因此对 #9 产物按 **#9 计划里的最终签名**核对，对 #8 按 **#8 计划 + 当前 HEAD** 双核对。

---

# 结论

**不通过（需修订后重审）。**

计划的骨架质量很高：19 条命令元数据表、手写解析器、输出契约、退出码/错误码映射、`DatabasePathResolver` + `DatabaseMigrator` 的设计与 spec §2/§4 逐条对得上；T1–T11 的任务切分与 TDD 步骤完整；T1 的路径/命名空间/`Link=` 目标（除一个文件外）全部真实存在；CLI 的链接清单在补上缺失文件后确实 WPF-free。**但存在 8 个阻断项，其中 7 项必现、1 项条件触发**：

| 编号 | 一句话摘要 | 类型 |
|---|---|---|
| B-1 | T1 的 `SharedSources` 漏链 `Core/Models/TerminalLaunchPlan.cs`（#9 把它独立成文件），CLI 必然 `CS0246` | 编译必失败 |
| B-2 | T1 步骤 2 的兜底片段把属性写进 `<ItemGroup>`，MSBuild 报 `MSB4232`（已实测） | 构建必失败（条件触发） |
| B-3 | `--db` 相对路径：T2 测试要求「原样保留」，T2 实现与 T3 测试要求「转绝对」，二者互斥 | 测试必失败 |
| B-4 | `TableWriter.DisplayWidth("备份")` 期望 6，实现给出 4 | 测试必失败 |
| B-5 | `OutputWriterTests` 用 `Environment.NewLine` 切分表格，而 `TableWriter` 只输出 `\n`（已实测切分结果） | 测试必失败 |
| B-6 | `CliCommandStore.CreateGroupAsync` 的 `SortOrder` 是 0 基（与 GUI、spec 示例一致），T7 测试期望 1 基 | 测试必失败 |
| B-7 | `CliSettingsStore.SetHotkey` 对「文件不存在」写扁平格式，T10 测试期望嵌套格式（且与 D3「唯一形状」冲突） | 测试必失败 |
| B-8 | `BASHCOMMANDMANAGER_SETTINGS_DIR` 只影响 `settings.json`，不影响数据库路径；只设它跑 `groups/commands/run` 会打开并写**真实** `%LOCALAPPDATA%` 库 | 数据安全 |

B-1/B-2 会直接让构建失败；B-3～B-7 会让「实现正确」的情况下 TDD 环永远变不绿（执行代理最容易被带偏）；B-8 与计划 27 行的隔离承诺矛盾。**8 项修法都很小（合计 10 行左右改动 + 3 处文案），修完可转「通过」。**

### 评审方法与证据

1. 逐行通读计划 1–5668（含 T1–T11 全部测试代码与实现代码），并与 spec / DECISIONS / #9 / #8 逐签名比对。
2. **三个本机探针**（全部在 `%TEMP%` 下进行，未触碰仓库）：
   - **MSB4232**：`<ItemGroup><DefaultItemExcludes>…</DefaultItemExcludes></ItemGroup>` 实测报 `error MSB4232: Target 元素之外的项目必须具有以下操作之一: Include、Update 或 Remove`（B-2）。
   - **换行切分**：`new StringWriter().WriteLine("A\nB")` 得到 `"A\nB\r\n"`，`Split(Environment.NewLine, RemoveEmptyEntries)` 结果为 **1 个元素**（`lines[1]` 抛 `IndexOutOfRangeException`），按 `'\n'` 切分才是 2 个（B-5）。
   - **C# 编译探针**（net8.0，`Nullable/ImplicitUsings=enable`）：验证了 `Retry(Func<Task<T>>)` / `Retry(Func<Task>)` 重载消解、`(_, _) => copied = true` 转 `Action<,>`、`parent.HasValue ? JsonValue.Create(...) : null` 三元、`JsonNodeOptions/JsonDocumentOptions`、record 相等性等**均通过**——这些不是问题点，已排除。
3. **未做**：CLI/测试工程的全量编译（#9 未落地且 A 部分本身缺 `TerminalLaunchPlan.cs`，无法构建），因此 B-1/B-3～B-7 采用「静态推演 + 针对关键语义的独立探针」；结论不依赖未验证的假设。

---

## 阻断性问题

> 每条：**位置 → 事实/证据 → 可直接采用的修法**。修法只动计划文档，不改设计意图。

### B-1 T1 `SharedSources` 漏链 `Core/Models/TerminalLaunchPlan.cs`（编译必失败）

- **位置**：计划 19（可链接清单）、46（0.2 的 #9 产物表）、176（T1 Consumes）、195（T1 步骤 1 门禁文件列表）、267–275（`SharedSources`）、8152（T20 Consumes）、10596（Self-Review）。
- **事实**：#9 的最终形态把 `TerminalLaunchPlan` 放进了**独立文件** `Core/Models/TerminalLaunchPlan.cs`，命名空间 `BashCommandManager.Core.Models`（#9 50/160/352–366），`ExecutionResult.cs` 里只有 `ExecutionOptions` 与 `ExecutionResult`（#9 51/161–162/369–386）；#9 在 75 与 3590 行明确写「#10 链接 `TerminalCommandBuilder.cs` 时**必须同时链接** `TerminalLaunchPlan.cs` 与 `ExecutionResult.cs`，否则编译失败」。
  而计划 267–275 的 `SharedSources` 只链了 `TerminalKind.cs`、`ExecutionSettings.cs`、`TerminalCommandBuilder.cs`、`ExecutionResult.cs`、`ExecutableLocator.cs`，**没有** `TerminalLaunchPlan.cs`；`TerminalCommandBuilder.Build` 的返回类型正是 `TerminalLaunchPlan`（#9 801/843），`RunCommand` 也直接读 `preview.FileName/Arguments/WorkingDirectory`（计划 8490–8496）。CLI 项目在 T1 第一次 `dotnet build` 时即 `CS0246: 未能找到类型或命名空间名"TerminalLaunchPlan"`。
  根因是 DECISIONS 50 行仍写着「`ExecutionResult.cs`（含 `TerminalLaunchPlan`/`ExecutionOptions`/`ExecutionResult` 记录）」——该描述相对 #9 的最终计划已过期（#9 的评审报告 395 行也已提示过同一处不一致）。计划 19/10596 照抄了这句错话，T1 步骤 1 的门禁文件列表（195）也因此漏了该文件。
- **修法**（4 处，均可直接替换）：
  1. 计划 275 之后插入一行：
     ```xml
     <Compile Include="..\Core\Models\TerminalLaunchPlan.cs" Link="Shared\Core\Models\TerminalLaunchPlan.cs" />
     ```
  2. 计划 19 改为：`…、Core/Services/TerminalCommandBuilder.cs、Core/Models/TerminalLaunchPlan.cs、Core/Services/ExecutionResult.cs（ExecutionOptions/ExecutionResult）、Core/Services/ExecutableLocator.cs、…`
  3. 计划 195 的门禁数组补 `'Core\Models\TerminalLaunchPlan.cs'`；计划 46 的表格同步补 `TerminalLaunchPlan.cs`；计划 176 的 Consumes 补 `Core/Models/TerminalLaunchPlan.cs`。
  4. 计划 10596 与 8152 的文件归属改为「`TerminalCommandBuilder.cs` / `ExecutionSettings.cs` / `Core/Models/TerminalLaunchPlan.cs` / `Core/Services/ExecutionResult.cs`」；**并建议主代理同步修 DECISIONS 50 行的措辞**（否则 D2.2 与 #9 计划长期打架，执行者会继续按 D2.2 漏链）。

### B-2 T1 步骤 2 的兜底片段把属性放进 `<ItemGroup>`（MSB4232，条件触发）

- **位置**：计划 208–212；相关叙述在 44–45、204、214–234。
- **事实**：片段是
  ```xml
    <ItemGroup>
      <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
    </ItemGroup>
  ```
  `DefaultItemExcludes` 是**属性**不是项，MSBuild 实测报 `MSB4232`（见「评审方法与证据」）。#8 的正确写法在 `PropertyGroup` 内（#8 202–223，当前 `BashCommandManager.csproj` 第 10 行即为该形态）。
  **当前分支不触发**：`Select-String` 已有匹配（计划 193 的判据），T1 步骤 2 会整段跳过。但计划 0.2 的表格（44–45 行）仍写着「无匹配 / False」，并把它当作可执行路径；一旦在没有 #8 的分支（回滚、worktree、重跑）上执行，主工程构建立即失败。
- **修法**：
  1. 计划 209–212 改为
     ```xml
     <PropertyGroup>
       <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
     </PropertyGroup>
     ```
     （或直接删掉这段兜底，改为「若缺失则**停止**并报告 `#8 未合入`」，与 D1/D2 一致——见 S-1。）
  2. 计划 44–45 的两行更新为「**已匹配**（#8 已落地，`BashCommandManager.csproj:10`）/ **True**」，并把「T1 步骤 2 会补上」改为「本步骤应整体跳过，若缺失即停止」。

### B-3 `--db` 相对路径：T2 测试要求原样、实现与 T3 要求绝对（测试必失败）

- **位置**：测试 1382–1388（`Database_path_is_preserved_verbatim`）↔ 实现 1604–1612 ↔ 测试 1968–1975（`Db_path_is_made_absolute`）。
- **事实**：T2 实现（1604–1611）在返回前无条件做 `databasePath = Path.GetFullPath(databasePath)`；而 T2 的测试断言 `Assert.Equal(@"..\tmp\x.db", parsed.DatabasePath)`。两个断言互斥，`Database_path_is_preserved_verbatim` 必红——T2 步骤 6「全部 Passed」与 T3 步骤 6「全部 Passed」都不可能达成。spec 154–162 行明确「相对路径按 `Environment.CurrentDirectory` 转绝对」，所以**错的是 T2 的测试**。
- **修法**：把计划 1385–1387 改为
  ```csharp
  var parsed = CommandLineParser.Parse(new[] { "groups", "list", "--db", @"..\tmp\x.db" }, _ => false);

  Assert.Equal(Path.GetFullPath(@"..\tmp\x.db"), parsed.DatabasePath);
  ```
  并把测试名改为 `Database_path_is_absolutized_against_current_directory`。（`Db_path_is_made_absolute`（1968–1975）保持不动。）

### B-4 `TableWriter.DisplayWidth("备份")` 期望 6、实现给出 4（测试必失败）

- **位置**：计划 2502–2507（`Counts_cjk_characters_as_two_columns`）↔ 实现 2863–2871。
- **事实**：`DisplayWidth` 对每个 `>= 0x2E80` 的 UTF-16 字符记 2；`"备份"` 是 `U+5907`、`U+4EFD` 两个字符 → **4**，不是 6。同一文件里 `Renders_aligned_columns_with_two_space_gap`（2484–2499）正是按 4 推算列宽才成立，所以是 2505 行单独写错（很可能本意是 `"备份库"`）。
- **修法**：计划 2505 改为两行（保留「2 个汉字 = 4 列」与「3 个汉字 = 6 列」两个宽度断言）：
  ```csharp
  Assert.Equal(4, TableWriter.DisplayWidth("备份"));
  Assert.Equal(6, TableWriter.DisplayWidth("备份库"));
  ```

### B-5 `OutputWriterTests` 用 `Environment.NewLine` 切分表格（测试必失败/抛异常）

- **位置**：计划 2717–2720（`Human_array_renders_table_with_mapped_headers`）↔ 实现 2880–2884 + 计划 2888 的注释。
- **事实**：`TableWriter.Render` 刻意只用 `'\n'`（2883，注释 2888 明说「这样测试可以按 `'\n'` 精确切分」），`OutputWriter` 再用 `WriteLine(text)` 追加一个 `\r\n`。实测 `"A\nB\r\n"` 按 `Environment.NewLine`（`"\r\n"`）切分只得到 **1 个元素**，因此 `lines[0]` 不等于 `"Id  Name  Group  FilePath"`，`lines[1]` 直接 `IndexOutOfRangeException`。
- **修法**：计划 2717 改为按 `\n` 切分：
  ```csharp
  var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
  ```
  （2718–2720 的断言不用改。T4 里另一处按 `Environment.NewLine` 切分的 `Human_error_line_matches_spec_format`（2681）不受影响，因为错误行是逐条 `WriteLine` 的，本来就用 `\r\n`。）

### B-6 `CreateGroupAsync` 的 `SortOrder` 是 0 基，T7 测试期望 1 基（测试必失败）

- **位置**：计划 3977–3989（`Creates_groups_with_monotonic_sort_order`）↔ 实现 4262–4272（`var sortOrder = all.Count(group => group.ParentId == parentId);`）。
- **事实**：根分组是 `(Id=1, ParentId=null)`，所以第一次在根下建组时 `count == 0`，第二个是 `1`。测试却断言 `1` 与 `2`。GUI 的 `GroupService.CreateGroupAsync` 用的也是 `siblings.Count()`（0 基，`Core/Services/GroupService.cs:47-53`），spec 的 `groups create` 示例（spec 182）也写 `"sortOrder":0`——三处一致指向 **0 基是对的，T7 的断言写错了**。
- **修法**：计划 3987–3988 改为
  ```csharp
  Assert.Equal(0, groups.Single(group => group.Id == first).SortOrder);
  Assert.Equal(1, groups.Single(group => group.Id == second).SortOrder);
  ```
  顺带把 T12 的手工期望（计划 6446 的 `"id":2,…, "sortOrder":1`）改成 `"sortOrder":0`，避免后续 T12 手工冒烟误判。

### B-7 `CliSettingsStore.SetHotkey` 对「文件不存在」写扁平格式，T10 测试期望嵌套（测试必失败，且与 D3 冲突）

- **位置**：计划 5251–5261（`Writing_to_missing_file_creates_nested_format`）↔ 实现 5379–5395（`var root = LoadRoot() ?? new JsonObject();` 后只在 `root["Hotkey"] is JsonObject` 时写嵌套，否则写扁平 `Enabled/Key/Modifiers`）↔ 计划 5441–5455（`Save`）。
- **事实**：`LoadRoot()` 返回 `null` 时新建空 `JsonObject`，没有 `Hotkey` 节点 → 走 `else` 分支写扁平键，`Save` 后文件是 `{"Enabled":true,"Key":57,"Modifiers":6}`，`root["Hotkey"]` 为 null，测试的 `Assert.NotNull(root["Hotkey"])` 必红。
  更严重的是这与 **D3「`settings.json` 的唯一形状」**（DECISIONS 54–68：根对象 `{Hotkey,Execution}`）冲突——该测试的意图（嵌套）才是对的，是实现漏了；spec 测试策略 301 行写的「文件缺失时写扁平格式」与 D3 也互相矛盾，需要一并更正。
  附带：若文件是嵌套格式但**恰好缺 `Hotkey` 节点**，当前实现会把扁平键混进嵌套文档，GUI（#9 的 `AppSettings`）反序列化时读不到，用户的改键会静默丢失。
- **修法**：计划 5381 改为
  ```csharp
  var root = LoadRoot() ?? new JsonObject { ["Hotkey"] = new JsonObject() };
  ```
  并在 0.3 节补一条偏差登记：「VD-10：文件不存在/缺 `Hotkey` 节点时统一写嵌套 `{Hotkey:{...}}`（对齐 D3），spec 测试策略 301 的『文件缺失时写扁平格式』作废」；`Missing_file_returns_gui_defaults`、`Setting_hotkey_keeps_flat_format_flat`（5214–5225）不受影响。

### B-8 `BASHCOMMANDMANAGER_SETTINGS_DIR` 不参与数据库解析，隔离承诺不成立（数据安全）

- **位置**：计划 27（测试隔离承诺）、63（VD-6）、4152–4167（`CliPaths.SettingsDirectory`）、3146 + 3473–3474（`ResolveForCurrentProcess` 不接受 settings 目录）、4184–4201（`DatabaseSession` 直接 `new DatabaseInitializer(explicitDatabasePath)`）。
- **事实**：`CliPaths.SettingsDirectory` 只被 `CliSettingsStore` 使用；`DatabaseInitializer` → `DatabasePathResolver.ResolveForCurrentProcess(explicit)` → `Resolve(explicit, env, File.Exists)`，用的是**固定的** `%LOCALAPPDATA%\BashCommandManager\data\app.db`。因此「只设 `BASHCOMMANDMANAGER_SETTINGS_DIR` 指向临时目录」并不能隔离数据库：任何不带 `--db`/`BASHCOMMANDMANAGER_DB` 的 `bcm groups create …` 都会**创建/打开真实库并写入**（首次还可能把旧安装库迁移到真实 LocalAppData）。这与计划 27 行「任何测试/冒烟**不得**读写真实安装库……一律用 `--db`、`BASHCOMMANDMANAGER_DB`、`BASHCOMMANDMANAGER_SETTINGS_DIR` 指向临时目录」的措辞直接冲突；spec 的 D4.1 也要求覆盖手段「不会碰真实用户库」。现有 T1–T11 的测试恰好都用了显式临时路径，所以暂时没有踩到，但这是执行代理最容易犯的错（`--db` 漏写一次就污染开发机）。
- **修法**（二选一，推荐 A）：
  - **A. 让覆盖也作用于数据库**（改动最小、语义最一致）：
    1. 计划 3593 的 T6 接口改为
       ```csharp
       public DatabaseInitializer(string? explicitDatabasePath = null, string? settingsDirectory = null)
       ```
       构造函数内改为
       ```csharp
       Resolution = DatabasePathResolver.Resolve(
           explicitDatabasePath,
           Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName),
           File.Exists,
           settingsDirectory);
       ```
    2. 计划 4186 的 `DatabaseSession` 改为
       ```csharp
       var initializer = new DatabaseInitializer(explicitDatabasePath, CliPaths.SettingsDirectory);
       ```
    3. 计划 27/63 的措辞改为「`BASHCOMMANDMANAGER_SETTINGS_DIR` 会同时把 CLI 的 `settings.json` 与（未显式指定时的）数据库重定向到该目录下」，并在 0.3 更新 VD-6。
  - **B. 只改文档**：计划 27 删掉 `BASHCOMMANDMANAGER_SETTINGS_DIR`，写成「数据库隔离只能用 `--db`/`BASHCOMMANDMANAGER_DB`」——不改代码，但把「跑一次 CLI 就污染真实库」的风险留给执行者，不推荐。

---

## 建议改进

> 按影响从大到小；均不构成阻断，但会显著降低执行/维护质量。

| 编号 | 位置 | 问题 | 建议 |
|---|---|---|---|
| S-1 | 计划 44–45、204、214–234 | 兜底创建 `tests/BashCommandManager.Tests/…csproj` 与 D1/D2「#8 负责首次搭建测试基础设施」冲突；且兜底内容比 #8 的实际定义（#8 136–160 / 当前文件）少 `RootNamespace`、`IsPackable=false`、`xunit.runner.visualstudio` 的 `IncludeAssets/PrivateAssets`，会造成同名工程两种定义 | 把该步骤改成门禁：「不存在即停止并报告 `#8 未合入`」；若坚持保留兜底，则用 #8 的完整 csproj 内容替换 216–234 |
| S-2 | 计划 543–549（T1 步骤 5） | 期望失败信息只写 `CS0246 CommandCatalog`；但此时 CLI 项目**一个源文件都没有**（步骤 3 只建了 csproj），还会同时报 `CS5001: 程序不包含适合于入口点的静态 "Main" 方法` | 期望改为「`CS5001` + `CS0246`」二者都算红，判据写成「报错来自 CLI/测试工程的类型缺失」 |
| S-3 | 计划 303 | 标题「写失败测试（3 个文件）」但实际列了 4 个（CliVersion/ExitCodes/HelpCatalog/CliHostHelpVersion） | 改为「4 个文件」 |
| S-4 | 计划 4801 | T8 期望 `Passed: 11`，但 `CommandResolverTests` 只有 10 个 `[Fact]`（4485–4631） | 改为 `Passed: 10`，并核对 T12 之后的累计数 |
| S-5 | 计划 3825 | 备注写「`CliServices` 在 **Task 10** 一次性引入」，实际是 **Task 12**（5669） | 改为 Task 12 |
| S-6 | 计划 5264–5272 | 测试名 `Writes_utf8_without_bom_and_keeps_chinese` 但用例里没有任何中文 | 补一段「描述含中文 → 写回再读仍为原文」的断言（对接 D5.6 / 验收 6） |
| S-7 | 计划 2466–2468 vs 2792–2798 | T4 的 Produces 清单只列 `Group/Command/FormatTimestamp`，实现里多出 `JsonShapes.GroupNode`（T12 的 `groups list --tree` 会用） | 把 `GroupNode` 写进 2467 的接口清单，避免后续任务以为是新增 API |
| S-8 | 计划 5500–5501、5512–5568 | `IGuiLauncher.Launch`（含 `--command` 参数拼接、`Process.Start`）完全没有测试，验收 9「不产生第二个窗口」无自动化保障 | 把启动抽成可注入的 `Func<ProcessStartInfo, Process?>` 或断言 `ProcessStartInfo.Arguments == "--command 7"`；至少补 `Launch` 的参数构造单测 |
| S-9 | 计划 2044–2140（`HotkeyKeyMap`） | CLI 手写了一份虚拟键码表，而 #8 已落地 `Core/Services/HotkeyKeyNormalizer.cs`；GUI 侧用 WPF `Key` 枚举，两套映射漂移无人发现 | 在 **GUI 测试工程**（有 WPF）加一条交叉断言：`foreach (var name in HotkeyKeyMap.Names) Assert.Equal((int)Enum.Parse<Key>(name), HotkeyKeyMap.GetValue(name))`——这样 #8/#9 若调整键语义会立刻红 |
| S-10 | 计划 2469–2470、2947–2960 | `CliResult.Meta` 与 `WriteResult(result, meta)` 是两条 meta 通道，T4 的 `OutputWriterTests` 没有覆盖 `result.Meta`；真正的合并只发生在 T12 的 `BuildMeta`（6393–6422） | 在 T4 加一条「`CliResult.Meta` 经 `WriteResult` 后出现在 `meta`」的测试，或让 `WriteResult` 自己合并 `result.Meta`，减少 T12 成为唯一合并点的风险 |
| S-11 | 计划 3541–3553（`TryDelete`） | 只 `catch (IOException)`；`Prepare` 的 catch 分支里再调 `TryDelete`，若删除抛 `UnauthorizedAccessException` 会从 `Prepare` 逃出，违背「失败必须回退旧库」 | `TryDelete` 补 `catch (UnauthorizedAccessException)`（或 `catch (Exception)`），并保证 `Prepare` 的 catch 不再抛 |
| S-12 | 计划 3944–3957 | 通过 `Environment.SetEnvironmentVariable` 改进程级环境变量做测试；xunit 默认按测试类并行，可能与其他 `DatabaseSession`/`CliSettingsStore` 用例互相干扰 | 该用例加 `[Collection("Environment")]` 串行化（或改成 `Resolve` 注入 `environmentValue` 的纯函数测试） |
| S-13 | 计划 54–66（0.3） | 未登记计划自身引入的公共面增量：`DatabaseInitializer.Source`/`Resolution`（3596–3597）、`ParsedCommand.Verb`（1208/1439）、`ICliSettingsStore`（5072）、`JsonShapes.GroupNode`（2792）、`TableWriter.DisplayWidth` 等 | 在 0.3 补一条 `VD-10`（公共类型/成员增量清单），满足 D5.7 与「偏差必须登记」 |
| S-14 | 计划 1101–1165 / 1666–1699 / 3044–3105 / 6287–6423 | `CliHost.cs` 在 T1/T2/T4/T12 被**四次**整文件重写；T2 是「替换片段」、T4/T12 是「完整文件」，执行者容易残留旧分支（例如 T2 的 `args.Contains("--help")`）而不自知 | 在 T2 步骤 5 加一句「替换后全文件搜 `args.Contains` 应为 0 命中」；或在计划中标注「`CliHost.cs` 以每次给出的完整文件为准」 |
| S-15 | spec §4「绝无交互」、D2.2 WPF-free | 两条硬约束都没有自动化守卫 | 在 `BashCommandManager.Cli.Tests` 加一个静态测试：断言 CLI 程序集的 `GetReferencedAssemblies()` 不含 `PresentationFramework`/`PresentationCore`/`System.Windows.Forms`，并在源码层 `Select-String 'ReadLine|ReadKey|MessageBox' BashCommandManager.Cli` 为 0 命中 |

**建议的修复顺序**：B-1 → B-2（决定 T1 能否过第一关）→ B-3～B-7（五个「实现正确也红」的断言，逐个改 1–2 行）→ B-8（涉及 T6/T7 的构造函数与 0.3 文案）→ S-1/S-2/S-4（同一批文本，顺手改）→ 其余建议按需。

---

## 与 #8/#9 的接口对照表

> 判定列：✅ 逐字一致；⚠️ 可用但描述/细节有偏差；❌ 会阻断。

| # | 依赖项 | #8/#9 计划（或当前源码）的定义 | 本计划引用（行号） | 判定 | 说明 / 修法 |
|---|---|---|---|---|---|
| 1 | `TerminalKind` 枚举（`Core/Models/TerminalKind.cs`，`Cmd=0…Custom=4`）+ `TerminalKindDisplay.GetDisplayName` | #9 156–157、284–305 | 267（链接）、5072/5178/5466（使用 `TerminalKind.WindowsTerminal`/`Cmd`/`Enum.IsDefined`） | ✅ | 文件路径、命名空间 `Core.Models`、数值均一致 |
| 2 | `ExecutionSettings`（5 属性 + `Clone()` + `WithTerminal()`） | #9 158–159、314–335 | 268（链接）、5412–5419（`new ExecutionSettings { … }`）、5072/5100/5110（接口返回值） | ✅ | 属性名/类型/默认值逐字一致；T10 只读不写，符合 D2.2 第 5 条 |
| 3 | `TerminalLaunchPlan`（`Core/Models/TerminalLaunchPlan.cs`，5 参 record + `RunAsVerb`，命名空间 `Core.Models`） | #9 50/75/160/352–366/3590 | 267–275（**未链接**）；19/46/10596/8152 误称其在 `ExecutionResult.cs` | ❌ | **B-1**：补一行 `<Compile Include="..\Core\Models\TerminalLaunchPlan.cs" …/>` 并改文案 |
| 4 | `ExecutionOptions` / `ExecutionResult`（`Core/Services/ExecutionResult.cs`，命名空间 `Core.Services`） | #9 51/161–162/369–386 | 274（链接） | ✅ | 文件/命名空间一致；CLI 实际不用（无害） |
| 5 | `TerminalCommandBuilder.Build(ExecutionSettings, Command, string? resolvedExecutable = null)` | #9 418、801 | 273（链接）；A 部不调用；T20 8490/8523 调用 | ✅ | 返回类型依赖 #3，见 B-1 |
| 6 | `TerminalCommandBuilder.GetDefaultTemplate(TerminalKind, bool)` | #9 417、785 | T20 8607（`WithoutKeepWindowOpen`） | ✅ | 参数顺序/返回 `string` 一致 |
| 7 | `TerminalCommandBuilder.ValidateTemplate(string) → IReadOnlyList<string>` | #9 419、851 | A 部未引用（B 部也未调用） | ✅ | 无冲突；`Build` 内部已做校验 |
| 8 | `IExecutableLocator.Resolve(TerminalKind, string customExecutablePath)`（`Core/Services`） | #9 999、1204–1207 | T12 5686（`CliServices.Locator`）、5770（Fake）、8508/8513 | ✅ | 接口签名逐字一致 |
| 9 | `ExecutableLocator` 的**公共无参构造** | #9 1215–1223 | T12 6388（`new ExecutableLocator()`）、6426 的备注 | ✅ | #9 确实提供了无参构造，备注的降级方案用不上 |
| 10 | `Command`/`Group`/`SortOption`/`SortDirection`/`CommandRepository.GetFrequentlyUsedAsync`（返回带 `GroupName` 的 `Command`） | 当前源码 `Core/Models/*`、`Core/Repositories/CommandRepository.cs:148-163` | 264–272（链接）、T7 4352（常用命令）、T4 2792（`JsonShapes.Group`） | ✅ | 链接清单与真实文件一一对应；`GetFrequentlyUsedAsync` 已 `LEFT JOIN` 出 `GroupName`，T7 的 `Frequent_…` 断言成立 |
| 11 | D2.2 **禁链**清单（`CommandExecutor`/`CommandService`/`SettingsService`/`HotkeySettings`/`SingleInstanceService`/`GlobalHotkeyService`） | DECISIONS 48 | 18（硬约束）、267–275（清单） | ✅ | 一个都没链；CLI 因此保持 WPF-free（`HotkeyKeyNormalizer` 也未链） |
| 12 | `DefaultItemExcludes` 最终值 | #8 203/217；**当前** `BashCommandManager.csproj:10` | 210 | ✅ | 逐字一致：`$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**`，且已在 `PropertyGroup` 内 |
| 13 | 测试工程路径/属性 | #8 133–160；**当前** `tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj` | 159（验证）、214–234（兜底） | ⚠️ | 路径一致；兜底内容缺 `RootNamespace`/`IsPackable`/runner 的 `IncludeAssets+PrivateAssets` → S-1 |
| 14 | DECISIONS D2.2 的「`ExecutionResult.cs` 含 `TerminalLaunchPlan`」 | DECISIONS 50 | 19、10596 | ❌ | 与 #9 最终计划冲突，是 B-1 的根因；建议主代理同步修订 D2.2 |
| 15 | #8 的 `HotkeySettings` 默认值（`Key.B`=45、`Ctrl+Alt`=3） | 当前 `Core/Models/HotkeySettings.cs:7-9` | T10 5146–5147（`Assert.Equal(45/3, …)`） | ✅ | #8 未改默认值，T10 的默认值断言成立 |
| 16 | #8 的 `HotkeyKeyNormalizer`（WPF `Key` 归一化） | 当前 `Core/Services/HotkeyKeyNormalizer.cs`（`using System.Windows.Input`） | T3 自建 `HotkeyKeyMap`（2044–2140） | ⚠️ | 不能链接（WPF）→ 自建映射合理，但需 S-9 的交叉断言防漂移 |
| 17 | `CliHost.Run` 的签名演进 | 本计划 188（T1）、6308（T12） | — | ✅ | `Run(string[])` + `Run(string[], TextWriter, TextWriter, Stream?)` 前后一致，测试调用方式（473）不会被 T12 破坏 |

---

## 覆盖矩阵（spec A 部分相关条目 → 任务）

> 「A 部分」= Task 1–11。判定：覆盖 / 部分 / 缺口（含阻断项引用）。

| spec / DECISIONS 条目 | 计划任务（行号锚点） | 判定 | 备注 |
|---|---|---|---|
| spec §1.1 独立 CLI 项目 + 源码链接（不 `ProjectReference` 主工程） | T1（240–279） | 部分 | 链接清单缺 `TerminalLaunchPlan.cs` → **B-1** |
| spec §1.1 主工程排除（`tests/**`、`BashCommandManager.Cli/**`） | T1 步骤 2（204–212）；#8 已落地（csproj:10） | 覆盖 | 值逐字一致；兜底片段非法 → **B-2**（条件触发） |
| spec §1.1 CLI 目录结构（31 文件） | T1/T2/T3/T4/T7/T8/T9/T10/T11 | 覆盖 | A 部建 20 个，其余在 B 部；父目录/命名空间/`Link=` 均真实可建 |
| spec §2 `DatabasePathResolver`（枚举/记录/优先级/`Resolve` 5 参） | T5（3143–3475） | 覆盖 | 优先级 Argument > Env > LocalAppData > Legacy > 创建，与 spec 154–163 一致；`RequiresMigration` 判定与 spec 166 一致 |
| spec §2 `DatabaseMigrator`（`BackupDatabase` 快照 → 临时文件 → `Move` → 目标已存在视为并发成功 → 失败回退 + warning） | T5（3478–3554） | 覆盖 | 6 条测试对齐 spec 164–169；`TryDelete` 的异常面见 S-11 |
| spec §2 `DatabaseInitializer` 改造（ctor 注入、`DatabasePath`、`MigrationWarning`、`Foreign Keys=True`、`Initialize` 不变） | T6（3583–3803） | 覆盖 | 额外新增 `Source`/`Resolution` 未登记 → S-13；`App.xaml.cs` 的替换锚点（3766–3779）在当前 HEAD 仍逐字存在 |
| spec §2 CLI 的 `settings.json` 用 `DatabasePathResolver.SettingsDirectory` | T7 `CliPaths`（4144–4167）+ T10 | 部分 | 加了 `BASHCOMMANDMANAGER_SETTINGS_DIR`（VD-6 已登记）；但该覆盖不影响数据库 → **B-8** |
| spec §3 19 条命令元数据/用法/示例 | T1 `CommandCatalog`（741–946） | 覆盖 | 条目数 19 与 spec 179–199 一一对应，`HelpCatalogTests` 有 19 条 `InlineData` |
| spec §3 参数语义（`<id\|name>`、`<id\|path>`、逗号列表、枚举、冲突、数值、`--db` 目录） | T2（1456–1664）+ T3（2259–2423） | 部分 | 语义实现覆盖；`--db` 相对路径断言自相矛盾 → **B-3** |
| spec §3 `config set hotkey` 语法（修饰键任意顺序 + 单键 + 至少一个修饰键） | T3 `HotkeyKeyMap`/`HotkeySpec`（2042–2256） | 覆盖 | 键值与 WPF `Key` 一致；建议补交叉断言 S-9 |
| spec §4 输出契约（`{ok,data,meta}` / `{ok,error}` / stdout-stderr 分离 / UTF-8 / 中文不转义 / 时间格式） | T1 `JsonEnvelope`（1033–1083）+ T4 `OutputWriter`/`TableWriter`/`JsonShapes`（2753–3040） | 部分 | 实现与 spec 213–216 一致；两条测试断言写错 → **B-4/B-5** |
| spec §4 退出码 0/1/2/3 与 14 个固定错误码 | T1 `CliErrorCodes`/`CliErrors`/`ExitCodeFor`（588–668）+ `ExitCodesTests`（325–370） | 覆盖 | 15 个常量 = spec 218 的 14 个 + `E_INTERNAL`（VD-5 已登记）；`FileNotFound` 的 1/3 双档 = VD-2 |
| spec §4 并发 `Busy/Locked` 200/500/1000ms 重试 3 次 | T7 `CliCommandStore.Retry`（4394–4427） | 覆盖 | 三次延迟后落 `E_DB_BUSY`；`PRAGMA busy_timeout=3000`（4200）有测试（3930–3939） |
| spec §4「绝无交互」（不读 stdin、不弹窗） | 无 | 缺口 | 无自动化守卫 → S-15 |
| spec 测试策略：Parser/PathResolver/Migrator/JsonEnvelope/TableWriter/OutputWriter/HotkeySpec/CliSettingsStore/CommandResolver/CliCommandStore 单测 | T1–T11 各自的 `Step 1` | 部分 | 类齐全、用例完整；5 处断言与实现互斥 → **B-3～B-7** |
| spec 测试策略：`CliSmokeTests`（真实进程 + 临时库） | T25（B 部分，9742 起） | 不在 A 范围 | 依赖 A 部的 `DatabaseSession`/`CliHost` 接线 |
| spec 验收 1（根目录 `publish` 只出 `BashCommandManager.exe`、无 `bcm`） | T1 步骤 2 + T6 步骤 6（3790–3796） | 覆盖（手工） | `DefaultItemExcludes` 已落地（#8），T6 步骤 6 会实测 |
| spec 验收 3（GUI 与 CLI 的 `db.path` 相同，旧库不删） | T5/T6（解析 + 迁移） | 覆盖 | `DatabasePathResolver.CanonicalDatabasePath` 有断言（3226–3238）；`只复制不删除` 有断言（3281–3296） |
| spec 验收 6（中文往返无损） | T4（2570–2576）+ T10 | 部分 | JSON 侧有断言；settings 侧缺中文用例 → S-6 |
| spec 验收 8（`--force`、级联计数一致） | T3（1871–1880）+ T7（4076–4094） | 覆盖 | `deletedGroupIds/deletedCommandIds` 的取数逻辑（4284–4300/4377–4382）已就绪 |
| D3 `settings.json` 唯一形状（`{Hotkey,Execution}`） | T10 | 部分 | 新建文件写扁平 → **B-7**；需登记 VD |
| D4.1 数据库迁移（规范位置/只复制/失败回退/`BASHCOMMANDMANAGER_DB`/单测注入） | T5 + T6 | 覆盖 | 失败回退、并发成功、旧库保留都有测试；隔离覆盖缺口 → **B-8** |
| D5.1 根目录 `dotnet build/publish` 只构建主工程 | T1 步骤 2 + T6 步骤 6 | 覆盖 | 当前 csproj 已验证；`Repository` 根只有 1 个 csproj，`dotnet build` 不会歧义 |
| D5.4 每任务先失败测试 | T1–T11 全部 `Step 1` 先写测试 | 部分 | 结构成立，但 B-3～B-7 使 5 个任务的红→绿闭环断掉 |
| D5.5 不引入新第三方依赖 | T1（256–260、292–296） | 覆盖 | CLI 仅 Dapper/SQLite/CommunityToolkit；测试项目仅 xunit/Test.Sdk/runner |
| D5.6 中文不乱码 | T1–T11 全部代码块 | 覆盖 | 抽查中文文案无 `鏂`/`\uFFFD` 类乱码；UTF-8 无 BOM 有测试（5270–5271） |
| 0.3 偏差登记（VD-1…VD-9） | 54–66 | 部分 | VD-1～VD-9 齐全；但缺 B-1（DECISIONS 措辞）、B-7（settings 形状）、T6 新增属性等的登记 → S-13 |

---

## 附：A 部分与 B 部分的边界确认（避免误改）

- Task 1–11 结束时，`CliDispatcher`/`CliServices`/`GroupCommands`/`CommandCommands`/`RunCommand`/`ConfigCommand`/`OpenCommand` 都**还不存在**（T12 起创建）；T4 的 `CliHost` 对任何业务命令抛「命令尚未接线」（3084），这是**预期**，不是缺口。
- 因此 A 部分的验收口径是：`bcm --help` / `bcm --version` / `bcm bogus` 三条行为正确 + T1–T11 单测全绿 + 主工程构建不受影响；业务命令的端到端验收在 B 部分（T25）。

---

## 修订记录（作者：修订代理）

- **修订对象**：`docs/superpowers/plans/2026-11-08-cli-and-skill.md`。**下列行号均为修订后（10891 行版本）的实际行号**；本报告上文引用的 10624 行版本行号仅用于定位锚点。
- **修订范围**：A 部分（旧行号 1–5668，修订后 `Task 12` 从 5936 行开始）全部 + 经主代理授权追加的 4 处 B 部分连带修改（见文末「越界但必要的连带修改」）。
- **约束遵守**：只改计划文档与本报告；**未改源码、未 commit**。`git status` 中属于本次修订的只有计划文档（M）与本报告（??）；工作区另有其他代理产生的未跟踪评审文件（`2026-11-08-cli-plan-B-review.md`、`verification-log.md`），本次未触碰。
- **结果**：B-1…B-8 全部修复；任务书点名的 10 条建议逐条落地（其中 S-1/S-3/S-5/S-7 为顺手修正，见「额外修正」）；另有 6 项未采纳或仅部分采纳（见文末「未采纳项与理由」）。

### 一、阻断项（B-1…B-8）

| 编号 | 位置（小节 + 修订后行号） | 实际修改 |
|---|---|---|
| **B-1** | `Global Constraints` 19；`0.2` 46/50/52；`Task 1` 178、192–204、253–254；连带 `Task 20` 8419、`Self-Review` 10863 | 允许链接清单补 `Core/Models/TerminalLaunchPlan.cs`，并把 `ExecutionResult.cs` 的说明改为「只含 `ExecutionOptions`/`ExecutionResult`，不含 `TerminalLaunchPlan`」；0.2 的 #9 产物行补第六个文件并改为「5/6 已落地、`ExecutableLocator.cs` 仍缺」；D2.2 行改为 DECISIONS 现行措辞；T1 Consumes 与步骤 1 门禁补 `TerminalLaunchPlan.cs`/`ExecutionResult.cs`，期望从「5 个」改「6 个」；`SharedSources` 新增 `<Compile Include="..\Core\Models\TerminalLaunchPlan.cs" …/>`（带漏链说明注释）；T20 Consumes 与 Self-Review 的文件归属同步拆分 |
| **B-2** | `Global Constraints` 25；`0.2` 42/44/45；`Task 1` 159–161、206–219 | 0.2 表头改「最近核实结果」；两行改为「**已匹配**（`BashCommandManager.csproj:8` 的 `PropertyGroup` 内）/ **True**（#8 已落地）」；步骤 2 改为「已落地则整体跳过；缺失即停止并先合入 #8」，兜底 XML 从 `<ItemGroup>` 移到 `<PropertyGroup>`（并写明 `MSB4232` 原因）；同时删除「按 D2 重建 GUI 测试 csproj」的兜底块（采 B-2 修法二，等价 S-1，避免与 D1/D2 冲突）；Files 与 Global Constraints 的「缺失才补」措辞同步改为「缺失即停」 |
| **B-3** | `Task 2` 1377–1384 | 测试更名为 `Database_path_is_absolutized_against_current_directory`，断言改为 `Path.GetFullPath(@"..\tmp\x.db")`，并加注释说明与 spec 154–162 一致；`Db_path_is_made_absolute`（T3）未动 |
| **B-4** | `Task 4` 2545–2552 | `DisplayWidth("备份")` 期望 6 → **4**，新增 `DisplayWidth("备份库") == 6`（2 个汉字 = 4 列、3 个汉字 = 6 列都保留） |
| **B-5** | `Task 4` 2761–2763 | 表格切分改为 `Split('\n', StringSplitOptions.RemoveEmptyEntries)`，并加「TableWriter 只用 `\n`」注释；`Human_error_line_matches_spec_format`（2726，逐行 `WriteLine` 的错误行）保持 `Environment.NewLine` |
| **B-6** | `Task 7` 4140–4153；连带 `Task 12` 6713 | `Creates_groups_with_monotonic_sort_order` 断言改为 `0`/`1`（0 基），加注释指向 GUI `GroupService` 与 spec 182；T12 手工冒烟期望的 `"sortOrder":1` 改为 `"sortOrder":0` 并注明「同级第一个子分组是 0 基」 |
| **B-7** | `0.3` 67（VD-10）；`Task 10` 5416–5447、5565–5589、5651–5656、5681 | `SetHotkey` 改为「旧扁平文档保持扁平；其余（文件不存在 / `{}` / 只有 `Execution` / 已是嵌套但缺 `Hotkey`）一律写嵌套 `{ "Hotkey": {...} }`」，新增 `IsFlatHotkeyDocument` 判定；缺文件用例加强为断言嵌套的 `Enabled/Key/Modifiers` 且根上无 `Key`，新增 `Nested_document_without_hotkey_node_still_writes_nested`；T10 期望 `Passed: 10 → 11`；0.3 新增 VD-10 登记「spec 301 的扁平写法作废，以 D3 嵌套为准」 |
| **B-8** | `Global Constraints` 27；`0.3` 63（VD-6）；`Task 5` 3194、3523–3525；`Task 6` 3667–3678、3692–3693、3770–3792、3808–3822、3844–3846；`Task 7` 3940–3947、4014–4015、4085–4109、4112–4124、4348–4351；连带 `Task 12` 6612 | `DatabaseInitializer` 增加可选 `settingsDirectory`（默认 `null` → `DatabasePathResolver.SettingsDirectory`）；`ResolveForCurrentProcess` 增加同名可选参并透传给 5 参 `Resolve`；`DatabaseSession` 传 `CliPaths.SettingsDirectory`；T6/T7 各加一条「settings 目录覆盖把数据库重定向到 `<dir>\data\app.db`」测试；27/VD-6 改写为「`--db` > `BASHCOMMANDMANAGER_DB` > settings 目录 > `%LOCALAPPDATA%`，settings 目录同时隔离 `settings.json` 与数据库」 |

### 二、任务书点名的建议项（逐条落地）

| 建议 | 位置（小节 + 修订后行号） | 落地方式 |
|---|---|---|
| T1 门禁文件列表补 6 个 #9 文件 | `Task 1` 195–204 | 门禁数组按 `TerminalKind/ExecutionSettings/TerminalLaunchPlan/TerminalCommandBuilder/ExecutionResult/ExecutableLocator` 顺序排列，期望改「6 个全部 True」 |
| T8 的 `Passed 11` 改为实际数量 | `Task 8` 4966 | 改为 `Passed: 10`，并注明与 10 个 `[Fact]` 一一对应 |
| T1 步骤 5 可能先报 `CS5001` | `Task 1` 533–544 | 期望改为「`CS5001`（无 `Main`）+ `CS0246`（缺类型）二者都算预期红」，判据是「报错来自类型缺失而非 NuGet 还原」 |
| `HotkeyKeyMap` 与 WPF `Key` 交叉断言 | `Task 3` 1732–1733（Files）、2032–2075（GUI 测试 + csproj 链接）、2475（Step 6 双工程转绿）、2491（git add）；`0.3` 68（VD-11）；File Structure 149 | 新增 `tests/BashCommandManager.Tests/Core/HotkeyKeyMapTests.cs`：6 个采样键（A=44、Z=69、D0=34、F1=90、Space=18、Enter=6）+ 遍历 `Names`（跳过 `0`–`9` 别名）逐值与 `Enum.Parse<Key>` 比对；GUI 测试工程用 `<Compile Link="Shared\HotkeyKeyMap.cs">` 引入 CLI 同一份源码 |
| `IGuiLauncher.Launch` 可测缝 | `Task 11` 5694–5706、5774–5821、5831–5919、5927 | 新增 `IGuiProcessStarter` + `ProcessGuiStarter`，`GuiLauncher` 构造注入 `processStarter`；新增 `FakeGuiProcessStarter` 与 2 个 `Launch` 用例（断言 `FileName`/`Arguments == "--command 7"`/`UseShellExecute`/空参数）；期望 `Passed: 6 → 8` |
| `TryDelete` 异常面写清 | `Task 5` 3589–3627、3630–3632 | `TryDelete` 吞 `IOException` + `UnauthorizedAccessException`，上抛 `ArgumentException`/`NotSupportedException`；`Prepare` 的 catch 改用永不抛的 `TryDeleteQuietly`；代码下方加异常面说明段落 |
| 环境变量测试不可并行 | `Task 6` 3692–3693、3808–3822；`Task 7` 4014–4015、4112–4124 | GUI 与 CLI 测试工程各加 `TestSupport/EnvironmentCollection.cs`（`[CollectionDefinition("Environment", DisableParallelization = true)]`）；`DatabaseInitializerTests` 与 `DatabaseSessionTests` 标 `[Collection(EnvironmentCollection.Name)]` |
| `CliHost` 只允许一次整文件定义 | `Task 1` 1096；`Task 4` 3088–3154 | T1 标注「全文唯一一次整文件定义」；T4 由「完整文件」改为 3 处局部修改（加 `using` / 替换 `Run` 方法 / 删除 `WriteError`），并给出替换后自检（`WriteError(`、`args.Contains` 均 0 命中） |

### 三、额外修正（A 评审未列，但为保证可执行性）

| 修正 | 位置 | 理由 |
|---|---|---|
| CLI 测试工程补 `<Using Include="Xunit" />` | `Task 1` 278–281 | 计划里所有 CLI 测试片段只有 `[Fact]/Assert` 而没有逐文件 `using Xunit;`，不补则 T1 起全部编译失败 |
| GUI 测试片段补 `using Xunit;` | `Task 5` 3203、3305；`Task 6` 3688；`Task 3` 2037 | 既有 GUI 测试工程（#8）没有全局 using，测试片段必须自带 |
| `JsonShapes.GroupNode` 写进 Produces | `Task 4` 2511 | 实现已有（2838），T12 的 `groups list --tree` 会消费，S-7 |
| `CliServices` 的引入任务 T10 → T12 | `Task 7` 3949 | S-5：实际由 T12 引入 |
| Step 4 标题 3 → 4 个文件 | `Task 1` 293 | S-3：实际列出 4 个测试文件 |
| File Structure 计数 | 127、147–149 | CLI 测试 33 → 35（+`EnvironmentCollection.cs`、`FakeGuiProcessStarter.cs`）；GUI 测试 5 → 7 个测试文件 + csproj 追加 |

### 四、越界但必要的连带修改（主代理授权）

> 这 4 处位于 Task 12 / Task 20 / Self-Review（超出 1–5668 的范围），是 B-1/B-6/B-8 的必然牵连，已按主代理明确授权修改。

1. **`Task 12` 6612**：`ResolveForCurrentProcess(parsed.DatabasePath)` → `ResolveForCurrentProcess(parsed.DatabasePath, CliPaths.SettingsDirectory)`，保证非 DB 命令的 `meta.dbPath` 与 `DatabaseSession` 的解析一致（B-8 牵连）。
2. **`Task 12` 6713**：手工冒烟期望的 `"sortOrder":1` → `"sortOrder":0`（B-6 牵连）。
3. **`Task 20` 8419**：Consumes 的 `TerminalLaunchPlan` 来源拆为 `Core/Models/TerminalLaunchPlan.cs`，并注明 `ExecutionResult.cs` 只含 `ExecutionOptions`/`ExecutionResult`（B-1 牵连）。
4. **`Self-Review` 10863**：D2.2 检查项的文件归属改为「`Core/Models/TerminalLaunchPlan.cs`（独立文件）与 `Core/Services/ExecutionResult.cs`（`ExecutionOptions`/`ExecutionResult`）分别加入」（B-1 牵连）。

### 五、自检结果

| 检查 | 命令 | 结果 |
|---|---|---|
| `TerminalLaunchPlan.cs` 出现在 `SharedSources` | `Select-String -Pattern 'TerminalLaunchPlan'`（计划 253–254） | ✅ 1 条 `<Compile Include="..\Core\Models\TerminalLaunchPlan.cs" …/>` |
| `DefaultItemExcludes` 所在块是 `PropertyGroup` | 计划 214–218 | ✅ `<PropertyGroup>` 开、`</PropertyGroup>` 闭，无 `<ItemGroup>` |
| 表格切分处 `Environment.NewLine` 0 命中 | 计划 2450–2900 段逐行核对 | ✅ 两个表格切分点（2494、2763）都是 `Split('\n')`；2726 是「错误行逐条 `WriteLine`」的测试（应保留 `Environment.NewLine`），3039/3045/3066 是 `OutputWriter` 人类可读渲染的内部拼接，均非表格切分 |
| `Split('\n')` ≥ 1 命中 | `Select-String -SimpleMatch "Split('\n'"` | ✅ 2 处（2538 既有、2763 新增） |
| 全文无占位符 | 搜 `TBD` / `TODO` / `待补充` / `待定` / `FIXME` | ✅ 唯一命中是 Self-Review 10875 行「本计划不含 TBD、TODO…」的**否定陈述**，非占位符 |
| 代码围栏配平 | 统计以三反引号开头的行数 | ✅ 512 个（偶数） |
| 结构/编号连续 | 目检 `### Task N`、`Step N` | ✅ Task 1–29 与 0.1–0.4 标题未变；T1/T3/T4/T6/T10/T11 的 Step 编号未重排（新增内容并入现有 Step） |
| 未改源码 / 未 commit | `git status --short` | ✅ 本次只有 `docs/…plans/…cli-and-skill.md`（M）与本报告（??）；其余未跟踪文件属其他代理 |

### 六、未采纳项与理由

| 编号 | 未采纳内容 | 理由 |
|---|---|---|
| S-6 | `CliSettingsStoreTests` 补中文描述往返 | 会改变 T10 的用例集合/计数（本次已因 B-7 从 10 改为 11）；中文 JSON 往返已由 T4 的 `Chinese_is_not_escaped` 覆盖，settings 侧留待后续补测 |
| S-10 | `CliResult.Meta` 经 `WriteResult` 合并的测试 | 会让 `OutputWriter.WriteResult` 兼管 meta 合并，与 T12 `BuildMeta` 是唯一合并点的现有设计冲突；属行为设计变更，不在本次修订范围 |
| S-12 | 把环境变量测试改成「注入 environmentValue 的纯函数」 | 只采纳了串行化（`[Collection]` + `DisableParallelization`）；改成注入会改动 `DatabaseInitializer`/`DatabaseSession` 的公共签名，超出 B-8 的最小改动 |
| S-13 | 0.3 单列「公共类型/成员增量清单」 | 只登记了本次必需的 VD-10（settings 形状）与 VD-11（键码表跨工程断言）；完整的公共面清单需与 B 部分（T12–T29）一起核对，A 部分单列会得到半份清单；B-8 的构造函数变更已并入 VD-6 |
| S-14 后半 | T12 的 `CliHost` 整文件重写改为「局部追加」 | T12 属 B 部分，主代理本次只授权 4 处越界修改；A 部分已做到「一次整文件 + 两次局部替换」，T12 的重写留给 B 部分修订 |
| S-15 | CLI 程序集的 WPF-free / 无交互静态守卫测试 | 新增测试工程/程序集反射断言，超出本次 A 评审修订范围 |
| — | T24 两个 GUI 测试片段缺 `using Xunit;`（B 部分） | 与 S-6 同理属 B 部分；本次只修 A 部分 5 处测试片段，B 部分修订时需要同样补 |

### 七、给 B 部分修订的提醒（不阻塞本次通过）

1. **T12 的 `CliHost` 仍是整文件定义**（计划 6554 起）：若严格执行「只允许一次整文件定义」，B 部分修订应把它改成「在 T4 版本上追加 `DatabaseSession`/`CliServices`/`CliDispatcher`/`BuildMeta` 接线」，否则 A 部分的两处局部替换会在 T12 被覆盖。
2. **T24 的 `StartupArgumentParserTests`/`SingleInstanceMessageTests` 缺 `using Xunit;`**，且这两个文件在 B 部分才创建。
3. 若 B 部分新增 `settingsDirectory` 相关消费点（如 T25 冒烟脚本只用 `BASHCOMMANDMANAGER_SETTINGS_DIR` 而不写 `--db`），现在已可依赖 B-8 的数据库重定向。
