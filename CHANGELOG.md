# Changelog

本文件记录 BashCommandManager 的版本变更。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [1.3.0] - 2026-11-08

### 新增

- **CLI 命令行工具 `bcm`**（Issue #10）
  - 19 条子命令：`groups list/create/rename/delete`、`commands list/search/show/add/edit/delete/move/import/export/frequent`、`run`、`config get/set`、`open`、`--version`
  - 统一 `--json` 输出信封 `{ok,data,meta}`，错误 JSON 写 stderr；退出码 0/1/2/3
  - 与 GUI 共享数据库，支持 `--db`、`BASHCOMMANDMANAGER_DB` 覆盖
  - 随安装包发布到 `{app}\cli`，可选加入 PATH；另提供独立 zip
  - 附带 `.claude/skills/bash-command-manager/SKILL.md`
- **执行终端可配置**（Issue #9）
  - 支持 cmd / Windows PowerShell / PowerShell 7 / Windows Terminal / 自定义程序
  - 参数模板占位符 `{file}` `{dir}` `{filePs}` `{dirPs}` `{fileRaw}` `{dirRaw}`
  - 「保持窗口打开」「默认管理员运行」开关；wt/pwsh 缺失时降级 cmd 并提示

### 修复

- **全局快捷键在最小化/关闭到托盘后失效**（Issue #8）
  - 热键宿主改为进程级 message-only 窗口，注册在 `App.OnStartup`、注销只在 `App.OnExit`
  - 修复捕获带 Alt 的组合键时写入 `Key.System`（虚拟键 0）导致热键永久失效
  - 改键改为「先注册成功再落盘」，冲突时保留原快捷键

### 变更

- 数据库迁移到 `%LOCALAPPDATA%\BashCommandManager\data\app.db`；旧位置 `{应用目录}\data\app.db` 首次使用时自动复制迁移，旧文件保留
- `settings.json` 根对象升级为 `{ "Hotkey": {...}, "Execution": {...} }`；旧扁平格式自动识别并备份为 `settings.json.v1.bak`
- 连接串启用 `Foreign Keys=True`，删除分组会真正级联删除命令
- `commands add/edit` 只接受 `.bat/.cmd`；CSV 导出中和公式注入；`run --wait` 固定使用 `cmd.exe /c` 并返回真实退出码

### 测试

- 新增 xunit 测试工程：GUI 184 个用例、CLI 334 个用例
- 新增真机冒烟脚本 `scripts/verify-hotkey-tray.ps1`

## [1.2.1] - 2026-03-08

- 常用命令使用 Tag 控件展示分组信息
- 调整分组标签为 4px 圆角样式

## [1.2.0] - 2026-03-08

- 命令管理与快捷操作相关改进

## [1.1.0] - 2026-03-05

- 单例启动模式

## [1.0.0] - 2026-03-04

- 初始版本发布
