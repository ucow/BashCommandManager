# 验收证据日志（v1.3.0）

本文件记录每个特性的**实际执行结果**（命令 + 输出摘要），作为 `verification-before-completion` 的证据留档。
只记录真实跑过的命令；未执行的项目明确标注「未执行」。

---

## Issue #8：全局快捷键在托盘隐藏期间失效

**实现提交**（分支 `feat/v1.3.0`）：

| 任务 | commit | 摘要 |
|---|---|---|
| T1 | `e703e9d` | 引入 xunit 测试工程，主 csproj 排除 `tests\**`/`BashCommandManager.Cli\**` |
| T2 | `977b3f1` | `HotkeySettings.Clone()` |
| T3 | `6e592ef` | `WindowTogglePolicy` 决策表 |
| T4 | `af8e8a3` | `IWindowVisibilityController` + `HotkeyToggleCoordinator` |
| T5 | `4d5c1bc` | `HotkeyRegistrationResult` / `HotkeyNativeMethods` / `HotkeyIdAllocator` |
| T6 | `0c35e3a` | message-only `HotkeyMessageWindow` |
| T7 | `bb3c043` | `ForegroundWindowActivator` + `MainWindowVisibilityController` |
| T8 | `57cf412` | 重写 `IGlobalHotkeyService`/`GlobalHotkeyService`，App 唯一注册/注销点 |
| T9 | `3319d2e` | `HotkeyKeyNormalizer` + 捕获窗口归一化 + 先注册后落盘 |
| T10 | `db4dba7` | README / CLAUDE.md 文档 |

**构建**：

```
PS> dotnet build BashCommandManager.csproj -c Release --no-incremental
已成功生成。
    0 个警告
    0 个错误
（00:00:02.07）
```

**单元测试**：

```
PS> dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj
已通过! - 失败: 0，通过: 42，已跳过: 0，总计: 42，持续时间: 19 ms
```

**真机 GUI 冒烟**（`scripts\verify-hotkey-tray.ps1`，真实启动 Release 版并注入按键）：

```
==> 热键: Key=57 Modifiers=6（78）        # 用户 settings.json 中的 Ctrl+Shift+N
==> 启动应用
    OK  启动后主窗口可见
    OK  按热键后主窗口隐藏
    OK  再次按键后主窗口重新显示
    OK  关闭后主窗口隐藏到托盘
    OK  进程未退出
    OK  关闭到托盘后热键仍能唤出窗口      # Issue #8 核心回归
    OK  前台时按热键隐藏到托盘
全部断言通过：热键在托盘隐藏/关闭到托盘场景下均有效。
```

**未执行的手工项**（需人工在托盘菜单/设置窗口交互）：托盘菜单退出后重启、被占用组合冲突提示、捕获 Alt+F1、模态对话框场景等（见计划 Task 10 的 15 条清单）。

---

## Issue #9：批处理执行可配置终端

**实现提交**：

| 任务 | commit | 摘要 |
|---|---|---|
| T1 | `f80319a` | `TerminalKind`/`ExecutionSettings`/`AppSettings`/`TerminalLaunchPlan`/`ExecutionResult` |
| T2 | `1c1a013` | `TerminalCommandBuilder` 纯函数 + B1–B18（24 用例） |
| T3 | `f86a429` | `ExecutableLocator` + `FindOnPath`（13 用例） |
| T4 | `4fd3788` | `SettingsService` 重写（嵌套/迁移/原子写，S1–S9，9 用例） |
| T5 | `7b7b93a` | `CommandExecutor.ExecuteWithResult`（降级/UAC 1223，12+1 用例） |
| T6 | `955f842` | `CommandListViewModel` 接线 + DI 注册 `IExecutableLocator` |
| T7 | `b20fdd7` | `SettingsWindow.xaml` 两个 Tab + `SettingsViewModel`（11 用例） |
| T8 | `d9b3a1b` | README / CLAUDE.md 文档 |

**构建**：`dotnet build BashCommandManager.csproj -c Release --no-incremental` → 0 warning / 0 error。

**单元测试**：`dotnet test` → `已通过! 失败: 0，通过: 123，总计: 123`（#8 的 42 + 本特性 81）。

**终端命令行真机校验**（用中文+空格路径的临时 .bat）：

```
路径: %TEMP%\bcm-term-check\工作 目录\我的 脚本.bat
PS> cmd.exe /c "\"<bat>\""        → 输出 HELLO_FROM_BAT，exit=7   ✅ 引号包裹正确
PS> powershell.exe -Command "& '<bat>'" → 输出 HELLO_FROM_BAT        ✅ 单引号字面量正确
wt.exe 存在；pwsh.exe 不存在（降级路径在真机可触发）
```

**GUI 回归**：重跑 `scripts\verify-hotkey-tray.ps1`，#8 的 7 条断言仍全部通过（#9 未破坏热键/托盘行为）。

**未执行**：6 条人工冒烟（设置窗口选 wt 实际运行、wt 缺失降级 Growl、自定义路径校验、取消 UAC 等）——需要人工点击设置界面，留待发布前人工复核。

---

## Issue #10：CLI 调用层与操作 Skill

**阶段 1（T1–T4）提交**：`fc4ce1d`（骨架/命令元数据/帮助/版本/退出码）、`1b1b163`（参数解析器）、`ffd87a0`（参数语义校验）、`9237a97`（输出层）。

**阶段 2（T5–T11）提交**：`45d9809`（DatabasePathResolver/Migrator）、`69b2267`（DatabaseInitializer 注入路径 + FK）、`de740eb`（DatabaseSession/CliCommandStore）、`b1c4439`（CommandResolver）、`231b657`（ProcessRunner）、`af9c555`（CliSettingsStore）、`3b278fd`（GuiLauncher）。

**构建与测试（主代理复验）**：

```
dotnet build BashCommandManager.Cli\BashCommandManager.Cli.csproj -c Release --no-incremental  → 0 warning / 0 error
dotnet build BashCommandManager.csproj -c Release --no-incremental                            → 0 warning / 0 error
dotnet test tests\BashCommandManager.Cli.Tests   → 已通过! 失败: 0，通过: 194，总计: 194
dotnet test tests\BashCommandManager.Tests       → 已通过! 失败: 0，通过: 149，总计: 149
```

**CLI 冒烟（真实进程）**：

```
bcm --version          → 1.3.0
bcm --json --version   → {"ok":true,"data":{"version":"1.3.0"}}
bcm --help             → 正常列出 19 条命令与全局选项
```

**数据隔离证明**（阶段 2 实现代理实测）：跑完全部测试与构建前后，
`%LOCALAPPDATA%\BashCommandManager\settings.json` 与 `D:\Program Files\BashCommandManager\data\app.db`
的 SHA-256 与修改时间均未变化；`%LOCALAPPDATA%\BashCommandManager\data\app.db` 未生成。

（T21–T29 待填充）

**阶段 4（T21–T29）提交**：`8907dc1`（run --wait/配置错误/干跑 verb 修复）、`7edcae3`（config get）、`64fc629`（config set）、`1c083a2`（open）、`d66104f`（GUI `--command` + 单实例参数转发）、`b5b05e2`（8 条端到端冒烟）、`ed1fb4a`（SKILL.md）、`dabce5f`（安装包 PATH + 版本 1.3.0）、`066446d`（文档）。

**主代理复验（最终基线）**：

```
dotnet build BashCommandManager.csproj -c Release --no-incremental          → 0 warning / 0 error
dotnet build BashCommandManager.Cli\...\Cli.csproj -c Release --no-incremental → 0 warning / 0 error
dotnet test tests\BashCommandManager.Tests      → 已通过! 失败: 0，通过: 167，总计: 167
dotnet test tests\BashCommandManager.Cli.Tests  → 已通过! 失败: 0，通过: 302，总计: 302
```

**CLI 端到端（主代理手工，临时库）**：

```
bcm groups list/create（多级 parentId 正确）
bcm commands add --file "...\工作 目录\备份.bat"
bcm run 备份 --dry-run --json   → {"dryRun":true,"fileName":"cmd.exe","arguments":"/k \"…工作 目录\\备份.bat\""}
bcm run 备份 --wait --json      → {"started":true,"waited":true,"exitCode":0}
bcm run nope                    → exit 2  E_NOT_FOUND
bcm groups delete 根分组 --force → exit 1  E_ROOT_GROUP
bcm open --gui <gui> --command 1 → exit 0，GUI 启动且仅 1 个实例
再次启动 GUI（--command）        → 第二个进程立即退出，实例数仍为 1（单实例 + 参数转发）
```

**打包产物**：

| 产物 | 大小 |
|---|---|
| `BashCommandManager-v1.3.0-Full-Setup.exe` | 75.4 MB |
| `BashCommandManager-v1.3.0-Lite-Setup.exe` | 7.2 MB |
| `BashCommandManager-Cli-v1.3.0-win-x64.zip` | 29.5 MB |
| `Full\bin\cli\bcm.exe`（自包含） | 66.95 MB，`--version` = 1.3.0 |
| `Lite\bin\cli\bcm.exe`（框架依赖） | 2.74 MB，`--version` = 1.3.0 |

### 发布前代码评审与修复

| 评审 | 结论 | 阻断项 | 处理 |
|---|---|---|---|
| GUI + 共享基础设施 | 需修复后发布 | B-1 迁移候选路径 / B-2 降级模板 | `580c4d9`、`85aac55` 已修，另有 `62ae78f`（设置页/快捷键）、`f771ecb`（.iss PATH）、`9ddafd4`（I-5 测试） |
| CLI | 阻断 | B-1 同迁移路径 / B-2 `run --wait` 未强制 cmd | `a622bd5`、`735e326`、`7dcdd30`、`149f5dc`、`a68f282`、`49e9202` 已修 |

**修复后最终基线**：GUI 测试 **184/184**、CLI 测试 **334/334**，两侧 `dotnet build -c Release` 均 0 warning / 0 error。

**模拟安装布局迁移实测**（`{sim}\cli\bcm.exe` + `{sim}\data\app.db` + 临时 `SETTINGS_DIR`）：

```
legacy created: True
before canonical exists: False
bcm groups list --json → {"created":false, "count":1, "commandCount":1}
bcm commands list --json → 旧库命令可见
after canonical exists: True；legacy still exists: True
```

证明：即使升级后**先运行 CLI**，也会把 GUI 旧库迁移到新位置，不会创建空库。

**安装包**：Full / Lite 均重新编译成功（`Successful compile`），`cli\bcm.exe` 已在发布目录中随 `[Files]` 递归打入 `{app}\cli`。本机会话非管理员，未执行真实安装（会触发 UAC 弹窗），安装/卸载行为留待人工或 CI 验证。

---

## 发布结果

| 项 | 结果 |
|---|---|
| 版本 | `1.3.0`（GUI csproj、CLI csproj、两个 `.iss`、README 一致） |
| 分支 | `feat/v1.3.0` → `master`（merge commit `15ea37a`），已推送 |
| Tag | `v1.3.0`（annotated），已推送 |
| GitHub Release | https://github.com/ucow/BashCommandManager/releases/tag/v1.3.0 |
| 资产 | Full 75.4 MB / Lite 7.2 MB / CLI zip 29.5 MB |
| 测试 | GUI 184、CLI 334 全绿；两侧构建 0 warning 0 error |
| Issue | #8/#9/#10 的修复已随 Release 发布；**当前 GitHub token 没有 Issues 写权限，无法由代理关闭 issue，需仓库所有者手动关闭** |

## 发布门槛总检查

| 项 | 状态 |
|---|---|
| `dotnet build -c Release` 0 warning 0 error | #8 通过；#9/#10 待验证 |
| `dotnet test` 全绿 | #8 42/42 通过 |
| CLI 冒烟 | 待执行 |
| GUI 冒烟 | #8 通过 |
| 安装包 Full/Lite | 待构建 |
| GitHub Release | 待发布 |
