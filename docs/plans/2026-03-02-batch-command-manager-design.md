# Windows 批处理命令管理器 - 设计方案

## 项目概述

专为个人开发者设计的 Windows 批处理命令管理工具，用于组织和管理 .bat/.cmd 脚本。

---

## 技术栈

- **.NET 8** - 运行时框架
- **WPF** - UI 框架
- **HandyControl** - WPF UI 控件库（浅色主题）
- **SQLite** - 本地数据库
- **Dapper** - 轻量级 ORM
- **CommunityToolkit.Mvvm** - MVVM 工具包

---

## 数据库设计

### Groups 表
```sql
CREATE TABLE Groups (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ParentId INTEGER NULL,
    SortOrder INTEGER DEFAULT 0,
    FOREIGN KEY (ParentId) REFERENCES Groups(Id) ON DELETE CASCADE
);
```

### Commands 表
```sql
CREATE TABLE Commands (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    FilePath TEXT NOT NULL,
    GroupId INTEGER NOT NULL,
    SortOrder INTEGER DEFAULT 0,
    FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
);
```

---

## 项目结构

```
BashCommandManager/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── Core/
│   ├── Models/
│   │   ├── Group.cs
│   │   └── Command.cs
│   ├── Services/
│   │   ├── DatabaseService.cs
│   │   ├── GroupService.cs
│   │   ├── CommandService.cs
│   │   └── CommandExecutor.cs
│   └── Repositories/
│       ├── GroupRepository.cs
│       └── CommandRepository.cs
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── GroupTreeViewModel.cs
│   └── CommandListViewModel.cs
├── Views/
│   ├── GroupTreeView.xaml
│   └── CommandListView.xaml
└── Infrastructure/
    ├── RelayCommand.cs
    └── DatabaseInitializer.cs
```

---

## 界面布局

### 主窗口结构
```
┌─────────────────────────────────────────────────────────────┐
│ [+导入] [全局搜索框]                          [最小化][关闭] │
├──────────────────┬──────────────────────────────────────────┤
│                  │                                          │
│  📁 分组1        │  ┌─────────────────────────────────────┐ │
│  ├─ 📁 分组1-1   │  │ 📄 命令1.bat    [运行] [▶管理员] [×] │ │
│  └─ 📁 分组1-2   │  │ C:\scripts\build.bat                 │ │
│  📁 分组2        │  ├─────────────────────────────────────┤ │
│                  │  │ 📄 命令2.cmd    [运行] [▶管理员] [×] │ │
│  [右键菜单]       │  │ C:\tools\deploy.cmd                  │ │
│  - 新建分组       │  └─────────────────────────────────────┘ │
│  - 重命名        │                                          │
│  - 删除          │                                          │
│                  │                                          │
├──────────────────┴──────────────────────────────────────────┤
│ 📍 当前: 分组1/分组1-1    命令数: 2    运行中: 1            │
└─────────────────────────────────────────────────────────────┘
```

### HandyControl 组件
- `hc:Window` - 主窗口
- `hc:TreeView` - 分组树形导航
- `hc:Card` - 命令卡片
- `hc:Button` - 操作按钮
- `hc:TextBox` - 搜索框
- `hc:ContextMenu` - 右键菜单
- `hc:Badge` - 状态标记

---

## 核心功能

### 分组管理
- 无限层级嵌套（建议不超过5级）
- 右键菜单：新建、重命名、删除
- 删除确认：是否级联删除子分组和命令
- 拖拽排序
- 展开/折叠
- 当前选中高亮

### 命令管理
- 导入：通过文件对话框选择 .bat/.cmd 文件
- 仅保存文件路径引用（不复制文件）
- 删除：仅从数据库移除记录
- 显示：名称、描述、原始路径

### 命令执行
- 普通执行：直接启动 cmd.exe
- 管理员执行：使用 `Verb = "runas"`
- 状态跟踪：运行中 / 已完成 / 失败
- 独立命令行窗口，保留交互性

---

## 关键实现

### 管理员权限执行
```csharp
var psi = new ProcessStartInfo
{
    FileName = "cmd.exe",
    Arguments = $"/c \"{filePath}\"",
    Verb = runAsAdmin ? "runas" : null,
    UseShellExecute = true
};
Process.Start(psi);
```

### 状态跟踪
```csharp
// 使用 Dictionary 跟踪运行中的进程
private Dictionary<int, Process> _runningCommands = new();

// 监听进程退出事件
process.Exited += (s, e) => {
    Dispatcher.Invoke(() => UpdateStatus(commandId, CommandStatus.Completed));
};
```

### 拖拽排序
使用 HandyControl TreeView 的拖拽事件，更新数据库中的 SortOrder 字段。

---

## 数据存储

- **位置**: 应用安装目录下的 `data/app.db`
- **备份**: 直接复制 .db 文件即可
- **初始化**: 首次启动自动创建表结构

---

## 约束确认

- [x] 单用户本地使用
- [x] 无网络功能
- [x] 无多用户同步
- [x] 无内置编辑器（仅导入外部文件）
- [x] 简洁实用优先
