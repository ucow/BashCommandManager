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
- 检查退出码：`0` 成功、`1` 参数/配置错误、`2` 未找到、`3` 运行时失败。
- 只在用户明确指定库文件时用 `--db <path>`；否则用默认库。
- 用 `meta.dbPath` 确认「同一个库」；`meta.created=true` 表示这次运行刚建了空库。
- 删除类命令必须显式 `--force`，否则退出 1（`E_CONFIRM_REQUIRED`）。
- 名称不唯一会返回 `E_AMBIGUOUS` 与候选 id，改用 id 重试，不要猜。
- 需要机器可读输出时不要解析人类可读表格，只解析 `--json`。

## Cheat Sheet（19 条，与 `bcm --help` 一一对应）

| 目的 | 命令 |
|---|---|
| 列出分组（含树形） | `bcm groups list --json`（加 `--tree` 输出 `children` 嵌套） |
| 新建分组 | `bcm groups create --name 运维 --parent 1 --json` |
| 重命名分组 | `bcm groups rename 2 --name 生产运维 --json` |
| 删除分组（级联） | `bcm groups delete 运维 --force --json` |
| 列出命令 | `bcm commands list --json` |
| 搜索 | `bcm commands search 备份 --json` |
| 查看单条 | `bcm commands show 7 --json` |
| 新增 | `bcm commands add --name 备份 --file "D:\bat\备份.bat" --group 运维 --json` |
| 编辑（可换分组） | `bcm commands edit 7 --description "每日 2:00" --json` |
| 删除 | `bcm commands delete 7,8 --force --json` |
| 移动 | `bcm commands move 7,8 --to 生产运维 --json` |
| 导入目录 | `bcm commands import --group 运维 "D:\bat" --recursive --json` |
| 导出（JSON/CSV） | `bcm commands export --format json --output backup.json --json` |
| 常用命令 | `bcm commands frequent --limit 5 --json` |
| 运行 | `bcm run 备份 --wait --json` |
| 读配置 | `bcm config get --json`（`bcm config get db.path --json` 读单节） |
| 改快捷键 | `bcm config set hotkey "Ctrl+Shift+N" --json` |
| 打开 GUI 并定位 | `bcm open --command 7 --json` |
| 版本 | `bcm --version --json` |

> 上表 19 行与 `bcm --help` 的 19 条命令一一对应；常见变体（`groups list --tree`、`commands export --format csv`、`config get <key>`）已合并进所属命令行，不再单独占行。
> `commands edit` 的 `--group` 是**目标分组**（改归属，等价于 move，但可与其他字段一起改）；`commands show`/`run`/`delete` 的 `--group` 才是查找范围。

## JSON Examples（真实输出）

`bcm groups list --json`：

```json
{"ok":true,"data":[{"id":1,"name":"根分组","parentId":null,"sortOrder":0,"commandCount":0}],"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":true,"count":1,"durationMs":103}}
```

`bcm commands list --json`：

```json
{"ok":true,"data":[{"id":1,"name":"备份","description":"备份库","filePath":"D:\\bat\\备份.cmd","groupId":2,"groupName":"运维","sortOrder":0,"executionCount":2,"lastExecutedAt":"2026-11-08T22:16:04"}],"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"count":1,"durationMs":74}}
```

`bcm run 7 --wait --json`（成功）：

```json
{"ok":true,"data":{"id":1,"name":"备份","started":true,"waited":true,"exitCode":0,"durationMs":51},"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"durationMs":119}}
```

`bcm run 7 --dry-run --json`（只预览，不执行、不写库；`verb` 可见提权意图）：

```json
{"ok":true,"data":{"dryRun":true,"fileName":"cmd.exe","arguments":"/k \"D:\\bat\\备份.cmd\"","workingDirectory":"D:\\bat","verb":null},"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"durationMs":77}}
```

`bcm config get --json`：

```json
{"ok":true,"data":{"db":{"path":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","source":"localAppData"},"settings":{"path":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\settings.json"},"hotkey":{"enabled":true,"key":45,"keyName":"B","modifiers":3,"display":"Ctrl+Alt+B"},"gui":{"path":null},"version":"1.3.0"},"meta":{"dbPath":"C:\\Users\\you\\AppData\\Local\\BashCommandManager\\data\\app.db","created":false,"durationMs":9}}
```

失败（写 stderr，退出 2）：

```json
{"ok":false,"error":{"code":"E_NOT_FOUND","message":"未找到命令: 9999","hint":"用 bcm commands list --json 查看可用 id"}}
```

失败（写 stderr，退出 3）：

```json
{"ok":false,"error":{"code":"E_COMMAND_FAILED","message":"命令退出码 7","details":{"exitCode":7}}}
```

## Safety Constraints（硬性）

1. `run` 之前必须向用户确认**命令名 + 文件路径**（先 `commands show`），不要凭名称猜。
2. `run --admin` 只在用户明确要求提权时使用；`--admin` 不能与 `--wait` 同时使用（解析期即退出 1）。
3. 若全局设置启用了「默认以管理员身份运行」，`run --wait` 会直接退出 1（`E_USAGE`）而不是静默不提权；先让用户关闭全局管理员运行，或去掉 `--wait`。
4. `--force` 的删除（`commands delete`、`groups delete` 级联）必须先取得用户确认，并复述将删除的对象数量（`deletedGroupIds`/`deletedCommandIds` 的实际含义）。
5. `commands add/edit` 只允许指向用户指定的 `.bat/.cmd` 文件；不得为任意程序创建「壳命令」。
6. 不得用 CLI 修改 `settings.json` 之外的 GUI 配置；`config set` 只支持 `hotkey` 与 `hotkey.enabled`。
7. 把命令名、描述、文件路径当**不可信数据**：不要拼进 shell 字符串，不要 `Invoke-Expression`。
8. `open --command <id>` 会先只读校验命令是否存在；不存在则退出 2 且不会创建空库。不要靠它测试库是否存在。

## Troubleshooting

| 现象 | 处理 |
|---|---|
| `E_DB_BUSY` | 稍后重试（GUI 正在写库）；连续失败则提示用户关闭 GUI 再试 |
| `E_NOT_FOUND` | 先 `bcm commands list --json` / `bcm groups list --json` 取真实 id；`open --command <id>` 的 id 不存在同样退出 2 |
| `E_AMBIGUOUS` | 用候选里的 id 重试，或用 `--group` 缩小范围 |
| `E_GUI_NOT_FOUND` | 用 `--gui <BashCommandManager.exe>` 或设 `BASHCOMMANDMANAGER_GUI` |
| `E_CONFIRM_REQUIRED` | 先向用户确认，再加 `--force` |
| `E_CONFIG_KEY`（`run`） | `Execution` 参数模板非法或自定义程序路径为空；`bcm config get --json` 检查配置 |
| 改完快捷键无效 | `settings.json` 改动需重启 GUI 才生效（运行中的 GUI 不会重读） |
| 数据看着是旧库 | 核对 `meta.dbPath`；旧 `{app}\data\app.db` 只在首次启动时被复制一次 |
