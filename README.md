# BashCommandManager

<p align="center">
  <img src="Assets/app.ico" width="128" height="128" alt="BashCommandManager Logo">
</p>

<p align="center">
  <strong>一个简洁高效的 Windows 批处理命令管理工具</strong>
</p>

<p align="center">
  <a href="#功能特性">功能特性</a> •
  <a href="#安装说明">安装说明</a> •
  <a href="#使用说明">使用说明</a> •
  <a href="#项目结构">项目结构</a>
</p>

---

## 📖 项目简介

**BashCommandManager** 是一款运行在 Windows 平台上的批处理命令管理程序。它可以帮助你轻松管理大量的 `.bat` 批处理文件，支持分组组织、批量导入、快速执行等功能，让批处理脚本的管理变得井井有条。

无论你是开发者、系统管理员还是高级用户，这个工具都能帮你更高效地管理和使用批处理脚本。

---

## ✨ 功能特性

### 🗂️ 分组管理
- 支持创建多层级的命令分组
- 树形结构展示，清晰直观
- 支持分组的新建、重命名、删除
- 默认"所有命令"视图展示全部内容

### 📥 批量导入
- 支持从文件夹批量导入 `.bat` 文件
- 保持原有文件组织结构
- 自动提取文件名作为命令名称
- 支持多文件同时选择导入

### ⚡ 快速执行
- 双击即可执行批处理命令
- 实时显示命令输出结果
- 支持停止正在运行的命令
- 命令执行状态清晰反馈

### 📝 命令管理
- 添加、编辑、删除命令
- 自定义命令名称和描述
- 命令搜索功能（快速定位）
- 命令分组自由调整

### 🖥️ 系统托盘
- 最小化到系统托盘
- 双击托盘图标快速恢复窗口
- 右键菜单快捷操作
- 开机自启动支持

### 🎨 界面特性
- 基于 WPF 的现代化界面
- 使用 HandyControl 控件库
- 支持深色/浅色主题
- 响应式布局设计

---

## 🛠️ 技术栈

| 技术 | 版本 | 说明 |
|------|------|------|
| .NET | 8.0 | 开发框架 |
| WPF | - | Windows 桌面应用框架 |
| HandyControl | 3.5.1 | WPF 控件库 |
| CommunityToolkit.Mvvm | 8.2.2 | MVVM 工具包 |
| SQLite | 1.0.115.5 | 本地数据库 |
| Dapper | 2.1.35 | ORM 工具 |

---

## 📦 安装说明

### 环境要求

- Windows 10/11 操作系统
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高版本

### 从源码运行

1. **克隆仓库**
   ```bash
   git clone https://github.com/你的用户名/BashCommandManager.git
   cd BashCommandManager
   ```

2. **还原依赖**
   ```bash
   dotnet restore
   ```

3. **运行项目**
   ```bash
   dotnet run
   ```

### 发布为可执行文件

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布后的文件将在 `bin/Release/net8.0-windows/win-x64/publish/` 目录中。

---

## 📖 使用说明

### 快速开始

1. **启动程序**：运行 `BashCommandManager.exe`
2. **创建分组**：在左侧树形菜单右键创建新分组
3. **导入命令**：
   - 点击"导入命令"按钮
   - 选择包含 `.bat` 文件的文件夹
   - 或选择多个 `.bat` 文件
4. **执行命令**：双击列表中的命令即可执行

### 界面说明

```
┌─────────────────────────────────────────────────────────┐
│  BashCommandManager                    [搜索框] [导入]  │
├──────────────┬──────────────────────────────────────────┤
│              │                                          │
│  📁 所有命令  │  命令列表                                  │
│  📁 分组1    │  ┌─────────────────────────────────────┐  │
│    📁 子分组 │  │ 命令1    描述...              [运行] │  │
│  📁 分组2    │  │ 命令2    描述...              [运行] │  │
│              │  │ 命令3    描述...              [运行] │  │
│  [+ 新建分组]│  └─────────────────────────────────────┘  │
│              │                                          │
├──────────────┴──────────────────────────────────────────┤
│  输出控制台                                              │
│  > 命令执行输出将显示在这里...                             │
└─────────────────────────────────────────────────────────┘
```

### 快捷键

| 快捷键 | 功能 |
|--------|------|
| `Ctrl + F` | 搜索命令 |
| `F5` | 刷新列表 |
| `Delete` | 删除选中的命令/分组 |
| `F2` | 重命名选中的分组 |

---

## 📁 项目结构

```
BashCommandManager/
├── Assets/                    # 资源文件（图标等）
├── Controls/                  # 自定义控件
├── Core/                      # 核心业务逻辑
│   ├── Models/               # 数据模型
│   ├── Services/             # 业务服务
│   └── Repositories/         # 数据访问层
├── Infrastructure/           # 基础设施
│   └── Database/             # 数据库相关
├── ViewModels/               # MVVM 视图模型
├── docs/                     # 项目文档
│   └── plans/               # 开发计划文档
├── logs/                     # 日志文件
├── App.xaml                  # 应用程序入口
├── App.xaml.cs
├── MainWindow.xaml           # 主窗口
├── MainWindow.xaml.cs
├── BashCommandManager.csproj # 项目文件
└── README.md                 # 项目说明
```

---

## 🖼️ 截图

> TODO: 添加程序运行截图

<!--
### 主界面
![主界面](docs/images/main-window.png)

### 分组管理
![分组管理](docs/images/group-management.png)

### 命令执行
![命令执行](docs/images/command-execution.png)
-->

---

## 📝 开发计划

查看 [docs/plans](docs/plans/) 目录了解详细的开发计划和设计文档。

主要功能开发进度：
- [x] 基础框架搭建
- [x] 分组管理功能
- [x] 命令导入功能
- [x] 命令执行功能
- [x] 系统托盘支持
- [x] 确认删除对话框
- [x] 多文件导入
- [x] 应用图标
- [ ] 命令搜索功能
- [ ] 开机自启动
- [ ] 主题切换

---

## 🤝 贡献指南

欢迎提交 Issue 和 Pull Request！

1. Fork 本仓库
2. 创建你的特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交你的修改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 打开一个 Pull Request

---

## 📄 许可证

本项目基于 [MIT](LICENSE) 许可证开源。

---

## 💡 致谢

- [HandyControl](https://github.com/HandyOrg/HandyControl) - 优秀的 WPF 控件库
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM 工具包
- [Dapper](https://github.com/DapperLib/Dapper) - 轻量级 ORM

---

<p align="center">
  Made with ❤️ in C#
</p>
