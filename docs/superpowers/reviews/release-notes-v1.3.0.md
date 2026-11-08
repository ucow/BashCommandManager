## 更新内容

### 新增功能

- **CLI 命令行工具 `bcm`**（Issue #10）
  - 19 条子命令覆盖分组、命令、执行、配置与 GUI 唤起：`groups list/create/rename/delete`、`commands list/search/show/add/edit/delete/move/import/export/frequent`、`run`、`config get/set`、`open`、`--version`
  - 统一 `--json` 输出信封 `{ok,data,meta}`，错误 JSON 写 stderr；退出码 `0` 成功 / `1` 参数错误 / `2` 未找到 / `3` 运行时失败
  - 与 GUI 共享同一数据库，支持 `--db` 与 `BASHCOMMANDMANAGER_DB` 覆盖；非交互、UTF-8、可由脚本与 AI 代理调用
  - 附带 `.claude/skills/bash-command-manager/SKILL.md`，让 AI 代理可以按安全约束使用 CLI

- **批处理执行终端可配置**（Issue #9）
  - 支持 cmd、Windows PowerShell、PowerShell 7、Windows Terminal、自定义程序
  - 参数模板占位符 `{file}` `{dir}` `{filePs}` `{dirPs}` `{fileRaw}` `{dirRaw}`，自动处理空格/中文/引号
  - 可配置「保持窗口打开」「默认以管理员身份运行」；wt/pwsh 未安装时自动降级 cmd 并提示
  - 设置窗口新增「执行」页，保存后下一次执行立即生效

### 修复

- **全局快捷键在最小化到托盘后失效**（Issue #8）
  - 根因：点关闭按钮隐藏到托盘时会触发 `Closing`，导致热键被注销且事件退订，而注册只在窗口 `Loaded` 时发生一次
  - 修复：热键宿主改为进程级 message-only 窗口，注册移到 `App.OnStartup`、注销只在 `App.OnExit`；主窗口隐藏/显示/重建都不影响热键
  - 顺带修复：捕获带 Alt 的组合键时写入 `Key.System`（虚拟键 0），会导致热键永久失效
  - 新增可单测的 `WindowTogglePolicy` / `HotkeyToggleCoordinator`，以及「先注册成功再落盘」的改键流程

### 数据与兼容性

- 数据库迁移到 `%LOCALAPPDATA%\BashCommandManager\data\app.db`（原位置 `程序目录\data\app.db` 会在首次启动时自动复制迁移，旧文件保留）
- 可用 `BASHCOMMANDMANAGER_DB` 指定数据库、`--db` 覆盖单次调用
- 设置文件根对象升级为 `{ "Hotkey": {...}, "Execution": {...} }`；旧扁平格式自动识别，首次保存前备份为 `settings.json.v1.bak`
- 数据库连接启用 `Foreign Keys=True`，删除分组时命令会真正级联删除（与删除确认对话框文案一致）

## 下载

### BashCommandManager-v1.3.0-Full-Setup.exe
完整安装包，包含 .NET 8 运行时，适合新用户。

### BashCommandManager-v1.3.0-Lite-Setup.exe
精简安装包（约 7MB），需要系统已安装 .NET 8：
https://dotnet.microsoft.com/download/dotnet/8.0

### BashCommandManager-Cli-v1.3.0-win-x64.zip
仅 CLI 的独立包，适合 CI/AI 代理环境（无需 GUI）。

## 使用 CLI

安装时勾选「Add bcm CLI to PATH」，或在安装目录的 `cli\bcm.exe` 直接调用：

```bash
bcm --json groups list
bcm --json commands search 备份
bcm run 备份 --wait
bcm open --command 7
```

## 注意事项

- 从 v1.2.x 升级后，首次启动会把旧数据库复制到 `%LOCALAPPDATA%\BashCommandManager\data\app.db`，旧库不会被删除
- 全局快捷键设置保持不变（默认 `Ctrl+Alt+B`），可在设置中修改
- 完整变更与设计文档见 `docs/superpowers/`（specs / plans / reviews）
