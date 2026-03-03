# 删除分组确认对话框实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在删除分组前显示 HandyControl 确认对话框，防止误操作

**Architecture:** 创建一个新的 ConfirmDialogControl 用户控件，使用 HandyControl 的 Dialog 显示确认消息，在 GroupTreeViewModel 的 DeleteGroupAsync 方法中集成

**Tech Stack:** C#, WPF, HandyControl, CommunityToolkit.MVVM

---

## Task 1: 创建 ConfirmDialogControl 控件

**Files:**
- Create: `Controls/ConfirmDialogControl.xaml`
- Create: `Controls/ConfirmDialogControl.xaml.cs`

**Step 1: 创建 XAML 文件**

创建 `Controls/ConfirmDialogControl.xaml`:

```xml
<UserControl x:Class="BashCommandManager.Controls.ConfirmDialogControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:hc="https://handyorg.github.io/handycontrol"
             Width="400"
             Height="160"
             Background="{DynamicResource RegionBrush}">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock x:Name="TitleTextBlock"
                   Grid.Row="0"
                   FontSize="16"
                   FontWeight="Bold"
                   Margin="0,0,0,10" />

        <TextBlock x:Name="MessageTextBlock"
                   Grid.Row="1"
                   TextWrapping="Wrap"
                   VerticalAlignment="Center"
                   Margin="0,5,0,10" />

        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right">
            <Button Content="确定"
                    Width="80"
                    Margin="0,0,10,0"
                    Click="OnConfirmClick"
                    IsDefault="True" />
            <Button Content="取消"
                    Width="80"
                    Click="OnCancelClick"
                    IsCancel="True" />
        </StackPanel>
    </Grid>
</UserControl>
```

**Step 2: 创建 Code-Behind 文件**

创建 `Controls/ConfirmDialogControl.xaml.cs`:

```csharp
using HandyControl.Controls;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager.Controls;

public partial class ConfirmDialogControl : UserControl, IDialogResultable<bool>
{
    public ConfirmDialogControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleTextBlock.Text;
        set => TitleTextBlock.Text = value;
    }

    public string Message
    {
        get => MessageTextBlock.Text;
        set => MessageTextBlock.Text = value;
    }

    public bool Result { get; set; } = false;

    public Action CloseAction { get; set; } = () => { };

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Result = true;
        CloseAction?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = false;
        CloseAction?.Invoke();
    }
}
```

**Step 3: 编译项目验证**

Run: `dotnet build BashCommandManager.csproj`
Expected: 编译成功，无错误

**Step 4: Commit**

```bash
git add Controls/ConfirmDialogControl.xaml Controls/ConfirmDialogControl.xaml.cs
git commit -m "feat: 添加确认对话框控件"
```

---

## Task 2: 在 GroupTreeViewModel 中集成确认对话框

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs:116-124`

**Step 1: 修改 DeleteGroupAsync 方法**

修改 `ViewModels/GroupTreeViewModel.cs` 中的 `DeleteGroupAsync` 方法:

```csharp
[RelayCommand]
private async Task DeleteGroupAsync(Group group)
{
    if (group == null) return;

    var commands = await _commandService.GetByGroupAsync(group.Id);
    var commandCount = commands.Count();

    string message = commandCount > 0
        ? $"分组 \"{group.Name}\" 下有 {commandCount} 个命令，确定要删除吗？\n删除后这些命令也将被删除。"
        : $"确定要删除分组 \"{group.Name}\" 吗？";

    var dialog = new ConfirmDialogControl
    {
        Title = "确认删除",
        Message = message
    };

    var result = await Dialog.Show(dialog).GetResultAsync<bool>();

    if (result)
    {
        await _groupService.DeleteGroupAsync(group.Id);
        await LoadGroupsAsync();
        Growl.Success("删除成功");
    }
}
```

**Step 2: 编译项目验证**

Run: `dotnet build BashCommandManager.csproj`
Expected: 编译成功，无错误

**Step 3: Commit**

```bash
git add ViewModels/GroupTreeViewModel.cs
git commit -m "feat: 删除分组前添加确认对话框"
```

---

## 测试验证步骤

1. 右键点击一个空分组，选择"删除"
2. 验证确认对话框显示正确消息
3. 点击"取消"，验证分组未被删除
4. 再次右键删除，点击"确定"，验证分组被删除
5. 右键点击一个有命令的分组，验证对话框显示命令数量警告
6. 点击"确定"，验证分组和命令都被删除
