# 打开所在目录功能 - 设计方案

## 功能概述

将命令卡片中的文件路径文本变成可点击的链接，点击后在资源管理器中打开该 bat 文件所在的目录。

## 设计决策

- **交互方式**：点击文件路径文本打开目录（选项 D）
- **视觉反馈**：鼠标变成手型光标，文字颜色稍微变亮（选项 B）
- **打开方式**：直接打开目录，不选中文件（选项 A）

## 架构设计

### 1. ViewModel 层修改

在 `CommandListViewModel` 中添加 `OpenDirectoryCommand`：

- Command 接收 Command 对象参数
- 提取 FilePath 的目录部分（使用 `Path.GetDirectoryName`）
- 使用 `Process.Start("explorer.exe", directoryPath)` 打开目录
- 包含错误处理：如果目录不存在则显示提示

### 2. View 层修改（MainWindow.xaml）

文件路径的 `TextBlock` 需要添加：

- `Cursor="Hand"` - 鼠标悬停显示手型
- `MouseLeftButtonUp` 事件绑定到 Command（通过 `hc:Interaction` 行为）
- `Foreground` 使用可触发变化的颜色资源

样式修改：
- 添加 `Style` 实现悬停时颜色变化效果
- 使用 `TextBlock.Foreground` 的多重触发器或 `MouseOver` 视觉状态

### 3. 视觉样式

- 默认：保持使用 `TertiaryTextBrush`（灰色）
- 悬停：颜色变为 `PrimaryTextBrush` 或稍微变亮
- 光标：`Cursor="Hand"`

## 数据流

```
用户点击文件路径
    ↓
TextBlock MouseLeftButtonUp 事件
    ↓
CommandListViewModel.OpenDirectoryCommand
    ↓
提取目录路径 → Process.Start("explorer.exe", path)
    ↓
Windows 资源管理器打开目录
```

## 错误处理

- 如果文件路径为空或无效，不执行任何操作
- 如果目录不存在，使用 HandyControl 的 `Growl` 显示警告提示

## 技术细节

### 使用的 WPF/HandyControl 功能

- `hc:Interaction.Triggers` - 连接事件到 Command
- `MouseLeftButtonUp` 事件
- `Process.Start` 启动资源管理器
- `Path.GetDirectoryName` 提取目录路径

### 依赖

- 无新增 NuGet 包依赖
- 使用现有的 HandyControl 和 CommunityToolkit.Mvvm
