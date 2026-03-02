# 分组管理功能实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现 TreeView 分组右键菜单功能：新建子分组、重命名、删除

**Architecture:** 创建自定义 EditableTextBlock 控件实现就地编辑，使用 HandyControl Dialog 进行新建分组和删除确认交互，ViewModel 处理业务逻辑

**Tech Stack:** WPF, C#, HandyControl, CommunityToolkit.Mvvm, SQLite

---

## 前置检查

### 检查 1：确认项目结构

**目标：** 确认所有文件存在且路径正确

**命令：**
```bash
git ls-files | grep -E "(MainWindow|GroupTreeViewModel|GroupService|Group)"
```

**预期输出：**
```
Core/Models/Group.cs
Core/Services/GroupService.cs
ViewModels/GroupTreeViewModel.cs
MainWindow.xaml
MainWindow.xaml.cs
```

---

## 任务 1：创建 EditableTextBlock 自定义控件

**Files:**
- Create: `Infrastructure/Controls/EditableTextBlock.xaml`
- Create: `Infrastructure/Controls/EditableTextBlock.xaml.cs`

**步骤 1：创建 XAML 文件**

创建 `Infrastructure/Controls/EditableTextBlock.xaml`：

```xml
&lt;UserControl x:Class="BashCommandManager.Infrastructure.Controls.EditableTextBlock"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:hc="https://handyorg.github.io/handycontrol"
             x:Name="Root">
    &lt;Grid>
        &lt;TextBlock x:Name="DisplayText"
                   Text="{Binding Text, ElementName=Root}"
                   VerticalAlignment="Center"
                   MouseLeftButtonDown="DisplayText_MouseLeftButtonDown"
                   Visibility="{Binding IsEditing, ElementName=Root, Converter={StaticResource BooleanToVisibilityConverter}, ConverterParameter=Inverted}"/>
        &lt;TextBox x:Name="EditText"
                 Text="{Binding Text, ElementName=Root, UpdateSourceTrigger=PropertyChanged}"
                 VerticalAlignment="Center"
                 Visibility="{Binding IsEditing, ElementName=Root, Converter={StaticResource BooleanToVisibilityConverter}}"
                 LostFocus="EditText_LostFocus"
                 KeyDown="EditText_KeyDown"
                 Loaded="EditText_Loaded"/>
    &lt;/Grid>
&lt;/UserControl>
```

**步骤 2：创建代码后置文件**

创建 `Infrastructure/Controls/EditableTextBlock.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BashCommandManager.Infrastructure.Controls;

public partial class EditableTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditableTextBlock),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(EditableTextBlock),
            new PropertyMetadata(false, OnIsEditingChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(EditableTextBlock));

    private string _originalText = string.Empty;

    public EditableTextBlock()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditableTextBlock control && (bool)e.NewValue)
        {
            control._originalText = control.Text;
        }
    }

    private void DisplayText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            StartEdit();
        }
    }

    private void EditText_Loaded(object sender, RoutedEventArgs e)
    {
        EditText.Focus();
        EditText.SelectAll();
    }

    private void EditText_LostFocus(object sender, RoutedEventArgs e)
    {
        EndEdit(true);
    }

    private void EditText_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                EndEdit(true);
                e.Handled = true;
                break;
            case Key.Escape:
                EndEdit(false);
                e.Handled = true;
                break;
        }
    }

    public void StartEdit()
    {
        _originalText = Text;
        IsEditing = true;
    }

    private void EndEdit(bool save)
    {
        if (!IsEditing) return;

        if (!save)
        {
            Text = _originalText;
        }

        IsEditing = false;

        if (save && Command != null && Command.CanExecute(Text))
        {
            Command.Execute(Text);
        }
    }
}
```

**步骤 3：提交**

```bash
git add Infrastructure/Controls/
git commit -m "feat: 添加 EditableTextBlock 自定义控件实现就地编辑"
```

---

## 任务 2：修改 Group 模型添加 IsEditing 属性

**Files:**
- Modify: `Core/Models/Group.cs`

**步骤 1：修改 Group 类**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace BashCommandManager.Core.Models;

public partial class Group : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    [ObservableProperty]
    private bool _isEditing;

    // 导航属性
    public List<Group> Children { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
}
```

**步骤 2：提交**

```bash
git add Core/Models/Group.cs
git commit -m "feat: Group 模型添加 IsEditing 属性支持就地编辑"
```

---

## 任务 3：修改 GroupTreeViewModel 更新命令逻辑

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs`

**步骤 1：添加 HandyControl 引用和新命令**

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using System.Collections.ObjectModel;

namespace BashCommandManager.ViewModels;

public partial class GroupTreeViewModel : ObservableObject
{
    private readonly IGroupService _groupService;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private ObservableCollection<Group> _groups = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    public GroupTreeViewModel(IGroupService groupService, ICommandService commandService)
    {
        _groupService = groupService;
        _commandService = commandService;
    }

    public async Task LoadGroupsAsync()
    {
        var groups = await _groupService.GetGroupTreeAsync();
        Groups = new ObservableCollection<Group>(groups);
    }

    [RelayCommand]
    private async Task CreateGroupAsync(int? parentId)
    {
        // 使用 HandyControl Dialog 输入名称
        var dialog = new TextDialog
        {
            Title = "新建分组",
            Content = "请输入分组名称："
        };

        var result = await Dialog.Show(dialog);
        if (result is string name && !string.IsNullOrWhiteSpace(name))
        {
            // 检查名称是否已存在
            if (await CheckGroupNameExistsAsync(name, parentId))
            {
                Growl.Warning("该分组名称已存在");
                return;
            }

            var group = await _groupService.CreateGroupAsync(name.Trim(), parentId);
            await LoadGroupsAsync();
            Growl.Success("分组创建成功");
        }
    }

    [RelayCommand]
    private void RenameGroupAsync(Group group)
    {
        if (group == null) return;

        group.IsEditing = true;
    }

    [RelayCommand]
    private async Task FinishRenameAsync((Group group, string newName) parameter)
    {
        var (group, newName) = parameter;

        if (string.IsNullOrWhiteSpace(newName))
        {
            Growl.Warning("分组名称不能为空");
            group.Name = await GetOriginalGroupNameAsync(group.Id);
            return;
        }

        if (newName.Trim() == group.Name)
        {
            return;
        }

        // 检查名称是否已存在
        if (await CheckGroupNameExistsAsync(newName, group.ParentId, group.Id))
        {
            Growl.Warning("该分组名称已存在");
            group.Name = await GetOriginalGroupNameAsync(group.Id);
            return;
        }

        await _groupService.RenameGroupAsync(group.Id, newName.Trim());
        await LoadGroupsAsync();
        Growl.Success("重命名成功");
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(Group group)
    {
        if (group == null) return;

        // 获取分组下的命令数量
        var commands = await _commandService.GetCommandsByGroupIdAsync(group.Id);
        var commandCount = commands.Count();

        string message = commandCount > 0
            ? $"分组 \"{group.Name}\" 下有 {commandCount} 个命令，确定要删除吗？\n删除后这些命令也将被删除。"
            : $"确定要删除分组 \"{group.Name}\" 吗？";

        var result = await Dialog.Show(new MessageBoxDialog
        {
            Title = "确认删除",
            Content = message,
            Buttons = new[] { "确定", "取消" }
        });

        if (result is string btn && btn == "确定")
        {
            await _groupService.DeleteGroupAsync(group.Id);
            await LoadGroupsAsync();
            Growl.Success("删除成功");
        }
    }

    private async Task<bool> CheckGroupNameExistsAsync(string name, int? parentId, int? excludeId = null)
    {
        var groups = await _groupService.GetGroupTreeAsync();
        var siblings = GetSiblings(groups, parentId);
        return siblings.Any(g => g.Name == name && g.Id != excludeId);
    }

    private List<Group> GetSiblings(List<Group> groups, int? parentId)
    {
        var result = new List<Group>();
        foreach (var group in groups)
        {
            if (group.ParentId == parentId)
            {
                result.Add(group);
            }
            result.AddRange(GetSiblings(group.Children, group.Id));
        }
        return result;
    }

    private async Task<string> GetOriginalGroupNameAsync(int id)
    {
        // 从数据库获取原始名称
        var groups = await _groupService.GetGroupTreeAsync();
        return FindGroupName(groups, id) ?? string.Empty;
    }

    private string? FindGroupName(List<Group> groups, int id)
    {
        foreach (var group in groups)
        {
            if (group.Id == id)
            {
                return group.Name;
            }
            var found = FindGroupName(group.Children, id);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
```

**步骤 2：提交**

```bash
git add ViewModels/GroupTreeViewModel.cs
git commit -m "feat: 更新 GroupTreeViewModel 实现新建、重命名、删除分组功能"
```

---

## 任务 4：修改 MainWindow.xaml 使用 EditableTextBlock

**Files:**
- Modify: `MainWindow.xaml`

**步骤 1：添加命名空间引用**

```xml
&lt;hc:Window x:Class="BashCommandManager.MainWindow"
           xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
           xmlns:hc="https://handyorg.github.io/handycontrol"
           xmlns:vm="clr-namespace:BashCommandManager.ViewModels"
           xmlns:local="clr-namespace:BashCommandManager.Infrastructure.Controls"
           Title="批处理命令管理器"
           Height="600" Width="900"
           WindowStartupLocation="CenterScreen">
```

**步骤 2：修改 TreeView 的 ItemTemplate**

```xml
&lt;TreeView x:Name="GroupTree"
          ItemsSource="{Binding GroupTreeViewModel.Groups}"
          SelectedItemChanged="GroupTree_SelectedItemChanged">
    &lt;TreeView.ItemTemplate>
        &lt;HierarchicalDataTemplate ItemsSource="{Binding Children}">
            &lt;local:EditableTextBlock Text="{Binding Name}"
                                     IsEditing="{Binding IsEditing, Mode=TwoWay}"
                                     Command="{Binding DataContext.GroupTreeViewModel.FinishRenameCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                                     CommandParameter="{Binding}"/
        &lt;/HierarchicalDataTemplate>
    &lt;/TreeView.ItemTemplate>
    &lt;TreeView.ContextMenu>
        &lt;ContextMenu>
            &lt;MenuItem Header="新建子分组"
                      Command="{Binding DataContext.GroupTreeViewModel.CreateGroupCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                      CommandParameter="{Binding Id}"/>
            &lt;MenuItem Header="重命名"
                      Command="{Binding DataContext.GroupTreeViewModel.RenameGroupCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                      CommandParameter="{Binding}"/>
            &lt;Separator/
            &lt;MenuItem Header="删除"
                      Command="{Binding DataContext.GroupTreeViewModel.DeleteGroupCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                      CommandParameter="{Binding}"/>
        &lt;/ContextMenu>
    &lt;/TreeView.ContextMenu>
&lt;/TreeView>
```

**步骤 3：提交**

```bash
git add MainWindow.xaml
git commit -m "feat: MainWindow 使用 EditableTextBlock 替换 TextBlock"
```

---

## 任务 5：修复 EditableTextBlock 的命令参数传递

**Files:**
- Modify: `Infrastructure/Controls/EditableTextBlock.xaml.cs`

**步骤 1：修改命令参数传递方式**

需要修改以正确传递 Group 和 newName。使用多值转换器或简化设计。

```csharp
// 修改 EndEdit 方法
private void EndEdit(bool save)
{
    if (!IsEditing) return;

    if (!save)
    {
        Text = _originalText;
    }

    IsEditing = false;

    if (save && Command != null)
    {
        // 使用当前 Text 作为参数
        if (Command.CanExecute(Text))
        {
            Command.Execute(Text);
        }
    }
}
```

**注意：** 由于 MVVM 绑定限制，我们需要修改 ViewModel 命令来接收字符串参数。

**步骤 2：更新 ViewModel 命令签名**

修改 `FinishRenameCommand` 接收 string，然后通过 SelectedGroup 获取 Group：

```csharp
[RelayCommand]
private async Task FinishRenameAsync(string newName)
{
    if (SelectedGroup == null) return;

    var group = SelectedGroup;
    // ... 其余逻辑
}
```

**步骤 3：提交**

```bash
git add Infrastructure/Controls/EditableTextBlock.xaml.cs ViewModels/GroupTreeViewModel.cs
git commit -m "fix: 修复 EditableTextBlock 命令参数传递"
```

---

## 任务 6：添加 ICommandService 依赖注入

**Files:**
- Modify: `App.xaml.cs`

**步骤 1：确保 ICommandService 已注册**

```csharp
// 检查是否已有以下代码
services.AddSingleton&lt;ICommandService, CommandService&gt;();
services.AddSingleton&lt;IGroupService, GroupService&gt;();
```

**步骤 2：提交（如果需要修改）**

```bash
git add App.xaml.cs
git commit -m "chore: 注册 ICommandService 依赖"
```

---

## 任务 7：编译测试

**步骤 1：编译项目**

```bash
dotnet build
```

**预期输出：**
```
生成成功。
    0 个警告
    0 个错误
```

**步骤 2：运行测试（如果有）**

```bash
dotnet test
```

**步骤 3：提交**

```bash
git commit -m "test: 编译测试通过"
```

---

## 验证清单

### 功能测试

- [ ] 右键点击分组，显示菜单（新建子分组、重命名、删除）
- [ ] 点击"新建子分组"，弹出输入框
- [ ] 输入名称后创建成功，树刷新
- [ ] 点击"重命名"，分组名称变成可编辑文本框
- [ ] 输入新名称，按 Enter 保存
- [ ] 按 ESC 取消编辑，恢复原名称
- [ ] 点击"删除"，弹出确认对话框
- [ ] 确认后分组删除，树刷新

### 边界测试

- [ ] 新建分组名称为空，显示警告
- [ ] 新建分组名称重复，显示警告
- [ ] 重命名为空，显示警告
- [ ] 删除有命令的分组，显示确认对话框并提示命令数量
- [ ] 删除空分组，显示简洁确认对话框

---

## 已知限制

1. **双击编辑：** EditableTextBlock 支持双击进入编辑模式，但如果不需要可以在代码中移除
2. **命令参数：** 由于 XAML 绑定限制，FinishRenameCommand 使用 SelectedGroup 而非直接传递 Group
3. **性能：** 每次操作后重新加载整棵树，对于大量分组可能有性能问题

---

## 后续优化建议

1. 添加分组拖拽排序功能
2. 添加分组移动功能（从一个父分组移动到另一个）
3. 优化树刷新逻辑，只刷新变更的部分
