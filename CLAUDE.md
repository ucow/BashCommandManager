# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BashCommandManager 是一个 Windows 桌面应用程序，用于管理批处理命令(.bat/.cmd 文件)。它使用 WPF + MVVM 架构，基于 .NET 8 开发。

### 技术栈

- **框架**: .NET 8 + WPF
- **UI 控件库**: HandyControl 3.5.1
- **MVVM**: CommunityToolkit.Mvvm 8.2.2
- **数据库**: SQLite + Dapper
- **DI**: Microsoft.Extensions.DependencyInjection

## 常用命令

### 开发运行

```bash
# 还原依赖
dotnet restore

# 运行项目
dotnet run

# 运行单元测试
dotnet test tests\BashCommandManager.Tests\BashCommandManager.Tests.csproj

# 构建 Release
dotnet build -c Release
```

### 发布

```bash
# 单文件发布（完整）
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true

# 单文件发布（精简，需系统预装 .NET 8）
dotnet publish -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true
```

### 打包安装程序

使用 `packaging-release` skill 或参考 `docs/PACKAGING.md`。

关键步骤：
1. 更新 `Packaging/InnoSetup/*.iss` 中的版本号
2. Full 版本: `dotnet publish -c Release -r win-x64 -p:SelfContained=true -o Packaging/InnoSetup/Full/bin`
3. Lite 版本: `dotnet publish -c Release -r win-x64 -p:SelfContained=false -o Packaging/InnoSetup/Lite/bin`
4. 编译安装包: `"C:/Program Files (x86)/Inno Setup 6/ISCC.exe" Packaging/InnoSetup/BashCommandManager-Full.iss`

## 项目架构

### 分层结构

```
┌─────────────────────────────────────────────────────────────┐
│  Views (MainWindow.xaml)                                    │
│  - HandyControl 控件库                                       │
│  - 系统托盘支持 (NotifyIcon)                                  │
├─────────────────────────────────────────────────────────────┤
│  ViewModels (MainViewModel, GroupTreeViewModel,             │
│              CommandListViewModel)                          │
│  - CommunityToolkit.Mvvm 自动生成命令和属性                    │
├─────────────────────────────────────────────────────────────┤
│  Core/                                                      │
│  ├── Models (Command, Group)                               │
│  ├── Services (CommandService, GroupService,               │
│  │             CommandExecutor, SingleInstanceService)      │
│  └── Repositories (ICommandRepository, IGroupRepository)   │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure/                                            │
│  ├── DatabaseInitializer (SQLite 初始化)                    │
│  └── Controls (EditableTextBlock 等自定义控件)               │
└─────────────────────────────────────────────────────────────┘
```

### 关键设计决策

#### 1. 依赖注入 (App.xaml.cs)

- `ServiceProvider` 作为静态属性公开，供全局访问
- 生命周期管理：DbConnection 是 Scoped，ViewModels 也是 Scoped
- 数据库在 `OnStartup` 时初始化，连接字符串从 `DatabaseInitializer` 获取

#### 2. 单例启动 (SingleInstanceService)

使用 Mutex + NamedPipe 实现跨进程单例：
- Mutex 确保只有一个实例运行
- NamedPipe 用于第二个实例通知第一个实例激活窗口
- 支持单文件发布（使用 `AppContext.BaseDirectory` 获取路径）

#### 3. 数据持久化

SQLite 数据库存储在应用目录 `data/app.db`：
- 自动创建表和索引
- 外键级联删除
- 初始化时插入根分组（Id=1）

#### 4. MVVM 实现

使用 CommunityToolkit.Mvvm：
- `[ObservableProperty]` 自动生成属性变更通知
- `[RelayCommand]` 自动生成命令
- ViewModel 之间通过事件或 MainViewModel 协调

#### 5. 虚拟节点模式

"所有命令"是一个虚拟分组（`IsVirtual=true`）：
- 不存储在数据库中
- Id=0 表示展示所有命令
- 右键菜单中隐藏新建/重命名/删除选项

#### 6. 终端执行配置

- 全局终端配置存于 `%LOCALAPPDATA%\BashCommandManager\settings.json` 的 `Execution` 段，根对象为 `{ "Hotkey": {...}, "Execution": {...} }`；旧扁平 `{"Enabled":...}` 格式首次保存前自动备份为 `settings.json.v1.bak`。
- 命令行构造集中在纯函数 `Core/Services/TerminalCommandBuilder.Build`（占位符 `{file}` `{dir}` `{filePs}` `{dirPs}` `{fileRaw}` `{dirRaw}`），可执行文件探测在 `Core/Services/ExecutableLocator`；`CommandExecutor` 只负责 `Process.Start` 与状态回写。
- wt/pwsh 缺失时降级 cmd 并 Growl 警告；自定义程序缺失时保存被拦截、执行报错但不崩溃；取消 UAC（Win32 1223）时状态回到 `Idle`。

### 文件组织约定

- **Core/**: 业务逻辑，不依赖 UI
- **ViewModels/**: 视图状态和行为
- **Controls/**: 自定义 WPF 控件
- **Infrastructure/**: 基础设施（数据库、控件实现）
- **docs/plans/**: 开发设计文档（按日期命名）

## 注意事项

### HandyControl 使用

- 窗口继承 `hc:Window` 而非 `Window`
- 使用 `Growl` 显示通知消息
- `Dialog` 用于模态对话框

### 路径处理

支持单文件发布：
- 使用 `AppContext.BaseDirectory` 替代 `Assembly.Location`
- 图标使用 Pack URI: `pack://application:,,,/Assets/app.ico`

### 系统托盘

- 最小化时隐藏窗口（Hide），而非关闭
- 关闭按钮默认最小化到托盘
- 真正退出需通过托盘菜单"退出"

### 全局快捷键

- 热键宿主是进程级的 message-only 窗口（`Core/Services/HotkeyMessageWindow.cs`，`HwndSource` + `Parent=HWND_MESSAGE`），与主窗口 `Show/Hide` 无关
- 注册在 `App.OnStartup`，生命周期的注销只在 `App.OnExit`；`MainWindow` 不再持有 `IGlobalHotkeyService`
- 「快照 → 决策 → 执行」是可单测的纯逻辑：`WindowTogglePolicy`、`HotkeyToggleCoordinator`；Win32 只在 `HotkeyNativeMethods` 与 `ForegroundWindowActivator` 两个薄层
- 点 X 隐藏到托盘走 `MainWindow.OnClosing`（`e.Cancel = true`），不会注销热键

## 开发计划

设计文档保存在 `docs/plans/`，命名格式：`YYYY-MM-DD-<feature>-design.md`

已完成：
- 分组管理、命令导入执行、系统托盘、单实例启动
- 多文件导入、确认删除对话框、应用图标
- 终端可配置（cmd/PowerShell/wt/自定义，settings.json 嵌套格式与旧格式迁移）

待完成：
- 开机自启动、主题切换
