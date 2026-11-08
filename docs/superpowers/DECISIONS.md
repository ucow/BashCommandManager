# 跨特性实现决策（#8 / #9 / #10）

三份设计文档分别由独立代理产出，为了让它们能串行落地且互不打架，主代理在此锁定以下决策。
**实现阶段（plan / 代码）必须遵守本文件；与本文件冲突时以本文件为准，并在提交说明里记录偏差。**

## D1 实施顺序

`#8 热键` → `#9 执行终端` → `#10 CLI + skill`，**严格串行**，不做并行分支合并。

理由：
- 三者都改 `ViewModels/SettingsViewModel.cs` 与 `BashCommandManager.csproj`（#8 简化保存流程、#9 增加「执行」Tab），并行必冲突。
- 三者都要新建 `tests/BashCommandManager.Tests/`，#8 负责**首次搭建**测试基础设施；#9、#10 只在其上追加测试文件，不得重复创建 csproj。
- #9 的 `SettingsService` 重写（嵌套 JSON + 迁移）会改变 `settings.json` 形状，#8 的 `SettingsViewModel` 保存流程需要基于迁移后的服务实现；反过来做会返工。

分支：`feat/v1.3.0`（已从 `master` 拉出）。全部特性完成后合回 `master` 并打 tag。

## D2 测试基础设施（只建一次）

- 唯一测试项目：`tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`
  - `TargetFramework=net8.0-windows10.0.19041.0`、`UseWPF=true`、`IsTestProject=true`
  - `PackageReference`：`Microsoft.NET.Test.Sdk 17.8.0`、`xunit 2.5.3`、`xunit.runner.visualstudio 2.5.3`
  - `ProjectReference`：`..\..\BashCommandManager.csproj`
- 主项目排除测试与 CLI 源码，统一用 **`DefaultItemExcludes`**（同时覆盖 `Compile`/`None`/`Page` 等所有默认项）：
  ```xml
  <ItemGroup>
    <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
  </ItemGroup>
  ```
  不要只用 `<Compile Remove>`（`None`/`Page` 仍会被扫到，且 csproj 一旦被 `dotnet publish` 带上会污染产物）。
- 运行测试命令：`dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj`
- 已验证：xunit 模板包本机可离线还原；测试项目引用 WPF WinExe 可正常 `dotnet test`（探针 2 测试通过）。

### D2.1 多个测试项目

`tests/**` 由主 csproj 统一排除，因此 `tests/` 下允许存在多个测试项目：

- `tests/BashCommandManager.Tests/`（#8 创建，#9 追加用例）：引用主工程，覆盖 Core/ViewModels 逻辑；
- `tests/BashCommandManager.Cli.Tests/`（#10 创建）：引用 CLI 项目，覆盖解析器/输出/路径/CLI 端到端冒烟。

新增测试项目后必须重新验证 `dotnet build -c Release`（主工程）仍只编译主项目。

## D2.2 CLI 的源码复用边界（覆盖 #10 设计文档 1.1 节）

#10 设计文档把 `Core/Services/CommandExecutor.cs` 列入 CLI 源码链接清单，**这条必须作废**：经过 #9 改造后 `CommandExecutor` 依赖 `ISettingsService` → `HotkeySettings`（`System.Windows.Input.Key`），链接进 WPF-free 的 CLI 会编译失败。

修正后的规则：

1. CLI **不链接** `CommandExecutor.cs`、`CommandService.cs`、`SettingsService.cs`、`HotkeySettings.cs`、`SingleInstanceService.cs`、`GlobalHotkeyService.cs`。
2. CLI 的 `run` 子命令自行实现启动逻辑（`--wait`/`--timeout`/`--admin`/`--dry-run` 语义本来就与 GUI 不同）。
3. CLI 可链接（均为 WPF-free）：`Core/Models/Command.cs`、`Group.cs`、`SortOptions.cs`、`TerminalKind.cs`、`ExecutionSettings.cs`、`Core/Repositories/*.cs`、`Core/Services/DatabaseService.cs`、`GroupService.cs`、`TerminalCommandBuilder.cs`、`ExecutableLocator.cs`、`Infrastructure/DatabaseInitializer.cs`、`Infrastructure/DatabasePathResolver.cs`。
4. `Command.cs`/`Group.cs` 依赖 `CommunityToolkit.Mvvm`（`ObservableObject`），CLI 项目必须显式引用 `CommunityToolkit.Mvvm 8.2.2`、`Dapper 2.1.35`、`System.Data.SQLite.Core 1.0.115.5`，否则链接编译失败。`--dry-run` 的命令行构造必须复用 `TerminalCommandBuilder`，保证与 GUI 同源。
5. CLI 读取 `settings.json` 时用 `JsonNode` 直接读 `Execution` 段并映射为 `ExecutionSettings`（不引用 `AppSettings`，因为后者含 WPF 的 `HotkeySettings`）。

## D3 `settings.json` 的唯一形状

由 **#9** 定义并负责迁移，**#8 与 #10 不得再定义第二套格式**：

```json
{
  "Hotkey":    { "Enabled": true, "Key": 66, "Modifiers": 3 },
  "Execution": { "Terminal": 0, "CustomExecutablePath": "", "ArgumentsTemplate": "/k {file}", "KeepWindowOpen": true, "RunAsAdministrator": false }
}
```

- 旧扁平格式（`{"Enabled":...}`）读取时迁移，首次保存前生成 `settings.json.v1.bak`。
- 枚举继续写数字（不注册 `JsonStringEnumConverter`）。
- `ISettingsService` 是唯一读写入口；`SettingsViewModel` 只调用它，不直接碰文件。
- 若 #8 先落地（它只在内存里改保存顺序），它必须**不改** JSON 形状；#9 落地时才切换形状。

## D4 服务接口归属

| 接口 | 归属 | 说明 |
| --- | --- | --- |
| `IGlobalHotkeyService` | #8 | `TryRegister/UpdateHotkey/Suspend/Resume/Unregister/IsRegistered` + `HotkeyPressed`，注册宿主为 `HotkeyMessageWindow` |
| `ISettingsService` | #9 扩展 | 保留 `GetHotkeySettings/SaveHotkeySettings/HotkeySettingsChanged`；新增 `GetExecutionSettings/SaveExecutionSettings/ExecutionSettingsChanged`；构造函数支持注入目录以便测试 |
| `ICommandExecutor` | #9 扩展 | 保留原 4 个成员；新增 `ExecuteWithResult(Command, ExecutionOptions)` |
| `IExecutableLocator` | #9 | 终端可执行文件探测 |
| CLI 入口 | #10 | 独立项目，不新增 WPF 依赖到 Core |

## D4.1 数据库位置迁移（#10 引入，需显式记录）

- 规范位置：`%LOCALAPPDATA%\BashCommandManager\data\app.db`（与 `settings.json` 同目录），旧位置 `{应用目录}\data\app.db` 仅作迁移源，**只复制不删除**。
- 迁移失败（权限/磁盘/占用）必须回退旧库并继续启动，GUI 只写 `Debug.WriteLine`，CLI 在 `meta.warning` + stderr 提示。
- 提供 `BASHCOMMANDMANAGER_DB` 环境变量与 `--db` 显式覆盖，作为回滚/便携手段。
- 该迁移必须在 **#9 完成后** 才实施（#9 会改 `SettingsService` 路径常量），且迁移逻辑要写单元测试（注入目录与文件系统委托）。

## D5 不变量（所有实现必须满足）

1. `dotnet build -c Release` 在仓库根目录仍然只构建主项目且成功（测试/CLI 目录被排除）。
2. 现有 GUI 行为不回归：「运行」「管理员运行」默认仍是 `cmd.exe /k "<脚本>"`。
3. 不删除、不改名任何已有公开类/方法（只允许新增）；`SettingsViewModel` 的 `SaveCommand`、`CancelCommand` 名称不变。
4. 每个任务都必须先有失败测试，再有实现；提交信息用 `feat(...)`/`test(...)`/`chore(...)` 前缀。
5. 不引入新的第三方 NuGet 依赖（xunit 仅测试项目）。
6. 中文 UI 文案与现有风格一致；不出现乱码（历史文件里已有 GBK 残留注释，**新代码一律 UTF-8，中文注释/字符串不得出现 `?` 或 `鏂` 这类乱码**）。
7. 任何新增的 `public` 类型都要有 XML 之外的简短中文注释说明职责（与仓库风格一致，可选）。

## D6 发布门槛（对应最终 Release）

- 三个特性的验收清单全部勾选；
- `dotnet build -c Release` 0 warning 0 error；
- `dotnet test` 全绿；
- CLI 冒烟脚本通过（`--help`、`groups list --json`、`commands list --json`、`run` 用临时数据库）；
- 手工验收清单（#8 的 11 条、#9 的 6 条）由主代理在真实桌面环境执行并记录；
- 版本号统一为 `1.3.0`（csproj `Version`、Inno Setup `MyAppVersion`、README、Release 标题）；
- 产出 `Full` / `Lite` 两个安装包并附到 GitHub Release。
