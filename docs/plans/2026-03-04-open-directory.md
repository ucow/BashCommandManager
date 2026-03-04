# 打开所在目录功能 - 实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use @superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** 将命令卡片中的文件路径文本变成可点击的链接，点击后在资源管理器中打开该 bat 文件所在的目录。

**Architecture:** 在 CommandListViewModel 中添加 OpenDirectoryCommand，使用 Process.Start 启动 explorer.exe 打开目录。在 MainWindow.xaml 中使用 HandyControl 的 hc:Interaction 将 MouseLeftButtonUp 事件绑定到 Command。

**Tech Stack:** WPF, HandyControl, CommunityToolkit.Mvvm, .NET 8

---

## Task 1: 在 CommandListViewModel 中添加 OpenDirectoryCommand

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 添加 using 语句**

在文件顶部添加：

```csharp
using System.Diagnostics;
using System.IO;
```

**Step 2: 添加 OpenDirectoryCommand 方法**

在 DeleteCommand 方法后添加：

```csharp
[RelayCommand]
private void OpenDirectory(Command? command)
{
    if (command == null || string.IsNullOrWhiteSpace(command.FilePath))
    {
        return;
    }

    var directoryPath = Path.GetDirectoryName(command.FilePath);
    if (string.IsNullOrWhiteSpace(directoryPath))
    {
        return;
    }

    if (!Directory.Exists(directoryPath))
    {
        System.Windows.MessageBox.Show($"目录不存在: {directoryPath}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        return;
    }

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directoryPath}\"",
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        System.Windows.MessageBox.Show($"打开目录失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
}
```

**Step 3: 验证编译**

运行: `dotnet build`
预期: Build succeeded

**Step 4: Commit**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: 添加 OpenDirectoryCommand 用于打开命令所在目录"
```

---

## Task 2: 在 MainWindow.xaml 中修改文件路径文本样式

**Files:**
- Modify: `MainWindow.xaml`

**Step 1: 找到文件路径的 TextBlock**

定位到大约第 96 行的文件路径 TextBlock：

```xml
<TextBlock Text="{Binding FilePath}"
           FontSize="12"
           Foreground="{DynamicResource TertiaryTextBrush}"
           ToolTip="{Binding FilePath}"/>
```

**Step 2: 修改为可点击的样式**

修改为：

```xml
<TextBlock Text="{Binding FilePath}"
           FontSize="12"
           Foreground="{DynamicResource TertiaryTextBrush}"
           ToolTip="{Binding FilePath}"
           Cursor="Hand">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TertiaryTextBrush}"/>
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                </Trigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
    <hc:Interaction.Triggers>
        <hc:EventTrigger EventName="MouseLeftButtonUp">
            <hc:EventToCommand Command="{Binding DataContext.CommandListViewModel.OpenDirectoryCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                               CommandParameter="{Binding}"/>
        </hc:EventTrigger>
    </hc:Interaction.Triggers>
</TextBlock>
```

**Step 3: 验证 XAML 语法**

运行: `dotnet build`
预期: Build succeeded

**Step 4: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: 文件路径文本支持点击打开所在目录"
```

---

## Task 3: 功能测试

**Files:**
- 无需修改文件

**Step 1: 运行应用程序**

运行: `dotnet run`

**Step 2: 测试正常情况**

1. 选择一个包含有效 bat 文件路径的命令
2. 将鼠标悬停在文件路径文本上
3. 验证：鼠标变成手型，文字颜色变亮
4. 点击文件路径文本
5. 验证：资源管理器打开该文件所在的目录

**Step 3: 测试错误情况**

1. 手动修改数据库中的文件路径为不存在的目录
2. 刷新并点击该命令的文件路径
3. 验证：显示错误提示对话框，不崩溃

**Step 4: 测试空路径情况**

1. 确保代码中处理空路径不会崩溃
2. 已通过代码中的空检查实现

**Step 5: Commit（如发现问题并修复）**

如需修复，提交修复：
```bash
git add <modified-files>
git commit -m "fix: 修复打开目录功能的边界情况"
```

---

## 完成检查清单

- [x] CommandListViewModel 添加了 OpenDirectoryCommand
- [x] MainWindow.xaml 文件路径文本支持点击
- [x] 鼠标悬停时有视觉反馈（手型光标 + 颜色变化）
- [x] 点击后能正确打开资源管理器
- [x] 目录不存在时显示错误提示
- [x] 所有修改已提交到 git
