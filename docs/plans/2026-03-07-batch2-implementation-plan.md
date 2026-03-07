# 批次2：命令管理功能实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** 实现命令移动、批量操作、常用命令虚拟分组功能

**Architecture:** 扩展 CommandService 支持移动命令，添加批量操作模式到 CommandListViewModel，实现常用命令算法并展示前10条

**Tech Stack:** WPF, CommunityToolkit.Mvvm, SQLite, Dapper, HandyControl

---

## 前置条件

确保批次1已完成：
- 数据库已添加 ExecutionCount 和 LastExecutedAt 字段
- Command 模型包含统计字段

需阅读：
- `docs/plans/2026-03-07-batch2-command-management-design.md` - 本批次设计文档
- `ViewModels/CommandListViewModel.cs` - 命令列表视图模型
- `ViewModels/GroupTreeViewModel.cs` - 分组树视图模型
- `Core/Services/CommandService.cs` - 命令服务

---

## 命令移动功能

### Task 1: 扩展 Repository 支持移动命令

**Files:**
- Modify: `Core/Repositories/CommandRepository.cs`

**Step 1: 添加移动到分组方法**

```csharp
public interface ICommandRepository
{
    // ... 现有方法 ...

    // 新增：移动命令到指定分组
    Task MoveToGroupAsync(int commandId, int targetGroupId);
}
```

```csharp
public async Task MoveToGroupAsync(int commandId, int targetGroupId)
{
    var sql = "UPDATE Commands SET GroupId = @TargetGroupId WHERE Id = @CommandId";
    await _db.ExecuteAsync(sql, new { CommandId = commandId, TargetGroupId = targetGroupId });
}
```

**Step 2: 提交**

```bash
git add Core/Repositories/CommandRepository.cs
git commit -m "feat: CommandRepository 添加 MoveToGroupAsync 方法"
```

### Task 2: 扩展 CommandService 支持移动

**Files:**
- Modify: `Core/Services/CommandService.cs`

**Step 1: 添加移动命令接口和方法**

```csharp
public interface ICommandService
{
    // ... 现有方法 ...

    // 新增：移动命令
    Task MoveCommandAsync(int commandId, int targetGroupId);
    Task MoveCommandsAsync(IEnumerable<int> commandIds, int targetGroupId);
}
```

```csharp
public async Task MoveCommandAsync(int commandId, int targetGroupId)
{
    await _repository.MoveToGroupAsync(commandId, targetGroupId);
}

public async Task MoveCommandsAsync(IEnumerable<int> commandIds, int targetGroupId)
{
    foreach (var commandId in commandIds)
    {
        await _repository.MoveToGroupAsync(commandId, targetGroupId);
    }
}
```

**Step 2: 提交**

```bash
git add Core/Services/CommandService.cs
git commit -m "feat: CommandService 添加移动命令功能"
```

### Task 3: 在 CommandListViewModel 添加移动功能

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 添加移动命令方法**

```csharp
// 添加字段
private readonly IGroupService _groupService;

// 修改构造函数
public CommandListViewModel(ICommandService commandService, ICommandExecutor executor, IGroupService groupService)
{
    _commandService = commandService;
    _executor = executor;
    _groupService = groupService;
}

// 添加移动单条命令方法
[RelayCommand]
private async Task MoveCommandAsync(Command? command)
{
    if (command == null) return;

    var groups = await _groupService.GetGroupTreeAsync();
    var targetGroup = await ShowGroupSelectionDialogAsync(groups, command.GroupId);

    if (targetGroup != null && targetGroup.Id != command.GroupId)
    {
        await _commandService.MoveCommandAsync(command.Id, targetGroup.Id);

        // 如果当前正在查看原分组，从列表中移除
        if (_currentGroupId != 0 && _currentGroupId == command.GroupId)
        {
            Commands.Remove(command);
        }

        Growl.Success(new GrowlInfo
        {
            Message = $"命令已移动到 '{targetGroup.Name}'",
            WaitTime = 3
        });
    }
}

// 分组选择对话框（简化版，可用 ComboBox）
private async Task<Group?> ShowGroupSelectionDialogAsync(IEnumerable<Group> groups, int excludeGroupId)
{
    // 展平分组树为列表
    var flatGroups = FlattenGroups(groups).Where(g => g.Id != excludeGroupId).ToList();

    // 创建对话框
    var dialog = new InputDialogControl
    {
        Prompt = "选择目标分组：",
        // 这里简化处理，实际应该使用下拉框
    };
    dialog.DataContext = dialog;

    // 显示分组选择对话框（需要创建专门的分组选择控件）
    // 暂时返回 null，后续创建专门对话框
    return null;
}

private IEnumerable<Group> FlattenGroups(IEnumerable<Group> groups)
{
    foreach (var group in groups)
    {
        yield return group;
        foreach (var child in FlattenGroups(group.Children))
        {
            yield return child;
        }
    }
}
```

**Step 2: 提交**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: CommandListViewModel 添加移动命令功能"
```

### Task 4: 创建分组选择对话框

**Files:**
- Create: `Controls/GroupSelectionDialog.xaml`
- Create: `Controls/GroupSelectionDialog.xaml.cs`

**Step 1: 创建 XAML**

```xml
<UserControl x:Class="BashCommandManager.Controls.GroupSelectionDialog"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:hc="https://handyorg.github.io/handycontrol"
             xmlns:models="clr-namespace:BashCommandManager.Core.Models"
             Width="300"
             Height="400">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   Text="{Binding Title}"
                   FontSize="16"
                   FontWeight="Bold"
                   Margin="0,0,0,10"/>

        <TreeView Grid.Row="1"
                  ItemsSource="{Binding Groups}"
                  SelectedItemChanged="TreeView_SelectedItemChanged">
            <TreeView.ItemTemplate>
                <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                    <TextBlock Text="{Binding Name}"/>
                </HierarchicalDataTemplate>
            </TreeView.ItemTemplate>
        </TreeView>

        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,10,0,0">
            <Button Content="确定"
                    Command="{Binding ConfirmCommand}"
                    IsEnabled="{Binding CanConfirm}"
                    Margin="0,0,10,0"/>
            <Button Content="取消"
                    Command="{Binding CancelCommand}"
                    Style="{StaticResource ButtonDefault}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

**Step 2: 创建 Code-Behind**

```csharp
using BashCommandManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Tools.Extension;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager.Controls;

public partial class GroupSelectionDialog : UserControl, IDialogResultable<Group?>
{
    public GroupSelectionDialog()
    {
        InitializeComponent();
        DataContext = new GroupSelectionDialogViewModel();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GroupSelectionDialogViewModel vm)
        {
            vm.SelectedGroup = e.NewValue as Group;
        }
    }
}

public partial class GroupSelectionDialogViewModel : ObservableObject, IDialogResultable<Group?>
{
    [ObservableProperty]
    private string _title = "选择目标分组";

    [ObservableProperty]
    private ObservableCollection<Group> _groups = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    [ObservableProperty]
    private int? _excludeGroupId;

    public bool CanConfirm => SelectedGroup != null && SelectedGroup.Id != ExcludeGroupId;

    public Group? Result { get; set; }
    public Action? CloseAction { get; set; }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedGroup != null && SelectedGroup.Id != ExcludeGroupId)
        {
            Result = SelectedGroup;
            CloseAction?.Invoke();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseAction?.Invoke();
    }

    partial void OnSelectedGroupChanged(Group? value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }
}
```

**Step 3: 提交**

```bash
git add Controls/GroupSelectionDialog.xaml Controls/GroupSelectionDialog.xaml.cs
git commit -m "feat: 创建分组选择对话框"
```

### Task 5: 更新 CommandListViewModel 使用新对话框

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 修改 ShowGroupSelectionDialogAsync 方法**

```csharp
private async Task<Group?> ShowGroupSelectionDialogAsync(IEnumerable<Group> groups, int excludeGroupId)
{
    var dialog = new GroupSelectionDialog
    {
        DataContext = new GroupSelectionDialogViewModel
        {
            Groups = new ObservableCollection<Group>(groups.ToList()),
            ExcludeGroupId = excludeGroupId,
            Title = "选择目标分组"
        }
    };

    var result = await Dialog.Show(dialog).GetResultAsync<Group?>();
    return result;
}
```

**Step 2: 提交**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "refactor: 使用新的分组选择对话框"
```

---

## 批量操作功能

### Task 6: 在 CommandListViewModel 添加批量操作支持

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 添加批量操作属性**

```csharp
// 在类开头添加
[ObservableProperty]
private bool _isBatchMode;

[ObservableProperty]
private ObservableCollection<Command> _selectedCommands = new();

// 只读属性：选中数量
public int SelectedCount => SelectedCommands.Count;

// 批量操作是否可用
public bool CanBatchOperate => IsBatchMode && SelectedCommands.Count > 0;
```

**Step 2: 添加批量操作方法**

```csharp
[RelayCommand]
private void EnterBatchMode()
{
    IsBatchMode = true;
    SelectedCommands.Clear();
    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(CanBatchOperate));
}

[RelayCommand]
private void ExitBatchMode()
{
    IsBatchMode = false;
    SelectedCommands.Clear();
    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(CanBatchOperate));
}

[RelayCommand]
private void ToggleCommandSelection(Command? command)
{
    if (command == null) return;

    if (SelectedCommands.Contains(command))
    {
        SelectedCommands.Remove(command);
    }
    else
    {
        SelectedCommands.Add(command);
    }

    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(CanBatchOperate));
}

[RelayCommand]
private void SelectAll()
{
    SelectedCommands.Clear();
    foreach (var command in Commands)
    {
        SelectedCommands.Add(command);
    }
    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(CanBatchOperate));
}

[RelayCommand]
private void ClearSelection()
{
    SelectedCommands.Clear();
    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(CanBatchOperate));
}
```

**Step 3: 添加批量删除方法**

```csharp
[RelayCommand(CanExecute = nameof(CanBatchOperate))]
private async Task BatchDeleteAsync()
{
    if (SelectedCommands.Count == 0) return;

    var dialog = new ConfirmDialogControl
    {
        Title = "确认批量删除",
        Message = $"确定要删除选中的 {SelectedCommands.Count} 个命令吗？"
    };
    dialog.DataContext = dialog;

    var result = await Dialog.Show(dialog).GetResultAsync<bool>();

    if (result)
    {
        foreach (var command in SelectedCommands.ToList())
        {
            await _commandService.DeleteCommandAsync(command.Id);
            Commands.Remove(command);
        }

        SelectedCommands.Clear();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanBatchOperate));

        Growl.Success(new GrowlInfo
        {
            Message = $"成功删除 {SelectedCommands.Count} 个命令",
            WaitTime = 3
        });
    }
}
```

**Step 4: 添加批量移动方法**

```csharp
[RelayCommand(CanExecute = nameof(CanBatchOperate))]
private async Task BatchMoveAsync()
{
    if (SelectedCommands.Count == 0) return;

    // 获取所有命令的分组ID
    var currentGroupIds = SelectedCommands.Select(c => c.GroupId).Distinct().ToList();

    var groups = await _groupService.GetGroupTreeAsync();
    // 排除所有选中命令所在的分组
    var excludedIds = new HashSet<int>(currentGroupIds);
    var availableGroups = FlattenGroups(groups).Where(g => !excludedIds.Contains(g.Id));

    var dialog = new GroupSelectionDialog
    {
        DataContext = new GroupSelectionDialogViewModel
        {
            Groups = new ObservableCollection<Group>(availableGroups.ToList()),
            Title = "选择目标分组"
        }
    };

    var targetGroup = await Dialog.Show(dialog).GetResultAsync<Group?>();

    if (targetGroup != null)
    {
        var commandIds = SelectedCommands.Select(c => c.Id).ToList();
        await _commandService.MoveCommandsAsync(commandIds, targetGroup.Id);

        // 如果当前正在查看某个分组，需要刷新
        if (_currentGroupId != 0)
        {
            await LoadCommandsAsync(_currentGroupId);
        }
        else
        {
            await LoadAllCommandsAsync();
        }

        SelectedCommands.Clear();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanBatchOperate));

        Growl.Success(new GrowlInfo
        {
            Message = $"成功移动 {commandIds.Count} 个命令到 '{targetGroup.Name}'",
            WaitTime = 3
        });
    }
}
```

**Step 5: 提交**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: 添加批量操作功能

- 进入/退出批量模式
- 选择/取消选择命令
- 批量删除命令
- 批量移动命令到指定分组"
```

---

## 常用命令虚拟分组

### Task 7: 扩展 Repository 支持常用命令查询

**Files:**
- Modify: `Core/Repositories/CommandRepository.cs`

**Step 1: 添加常用命令查询**

```csharp
public interface ICommandRepository
{
    // ... 现有方法 ...

    // 新增：获取常用命令
    Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10);
}
```

```csharp
public async Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10)
{
    // 获取有执行记录的命令，按执行次数和最近执行时间排序
    var sql = @"
        SELECT * FROM Commands
        WHERE ExecutionCount > 0 OR LastExecutedAt IS NOT NULL
        ORDER BY ExecutionCount DESC, LastExecutedAt DESC
        LIMIT @Limit";
    return await _db.QueryAsync<Command>(sql, new { Limit = limit });
}
```

**Step 2: 提交**

```bash
git add Core/Repositories/CommandRepository.cs
git commit -m "feat: 添加获取常用命令的数据库查询"
```

### Task 8: 扩展 CommandService 支持常用命令

**Files:**
- Modify: `Core/Services/CommandService.cs`

**Step 1: 添加常用命令接口和方法**

```csharp
public interface ICommandService
{
    // ... 现有方法 ...

    // 新增：获取常用命令
    Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10);

    // 新增：记录命令执行
    Task RecordExecutionAsync(int commandId);
}
```

```csharp
public async Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10)
{
    return await _repository.GetFrequentlyUsedAsync(limit);
}

public async Task RecordExecutionAsync(int commandId)
{
    var command = await _repository.GetByIdAsync(commandId);
    if (command != null)
    {
        command.ExecutionCount++;
        command.LastExecutedAt = DateTime.Now;
        await _repository.UpdateAsync(command);
    }
}
```

**Step 2: 提交**

```bash
git add Core/Services/CommandService.cs
git commit -m "feat: CommandService 支持常用命令查询和执行记录"
```

### Task 9: 修改 GroupTreeViewModel 虚拟分组名称

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs`

**Step 1: 修改虚拟分组名称**

```csharp
// 找到这行代码
var allCommandsNode = new Group
{
    Id = 0,
    Name = "全部命令",  // 改为 "常用命令"
    IsVirtual = true
};

// 修改为
var allCommandsNode = new Group
{
    Id = 0,
    Name = "常用命令",
    IsVirtual = true
};
```

**Step 2: 提交**

```bash
git add ViewModels/GroupTreeViewModel.cs
git commit -m "refactor: 虚拟分组名称从'全部命令'改为'常用命令'"
```

### Task 10: 在 CommandListViewModel 添加常用命令加载

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 添加常用命令加载方法**

```csharp
[RelayCommand]
public async Task LoadFrequentlyUsedAsync()
{
    _currentGroupId = 0;
    _currentSearchKeyword = null;
    var commands = await _commandService.GetFrequentlyUsedAsync(10);
    Commands = new ObservableCollection<Command>(commands);
}
```

**Step 2: 修改执行命令时记录统计**

```csharp
[RelayCommand]
private async Task ExecuteCommand(Command command)
{
    try
    {
        _executor.Execute(command, runAsAdmin: false);
        await _commandService.RecordExecutionAsync(command.Id);
    }
    catch (Exception ex)
    {
        Growl.Error(new GrowlInfo
        {
            Message = $"执行失败: {ex.Message}",
            WaitTime = 3
        });
    }
}

[RelayCommand]
private async Task ExecuteAsAdmin(Command command)
{
    try
    {
        _executor.Execute(command, runAsAdmin: true);
        await _commandService.RecordExecutionAsync(command.Id);
    }
    catch (Exception ex)
    {
        Growl.Error(new GrowlInfo
        {
            Message = $"执行失败: {ex.Message}",
            WaitTime = 3
        });
    }
}
```

**Step 3: 提交**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: 加载常用命令并记录执行统计"
```

---

## UI 更新

### Task 11: 更新 MainWindow.xaml 支持批量操作

**Files:**
- Modify: `MainWindow.xaml`

**Step 1: 在工具栏添加批量模式按钮**

在 ToolBar 中添加批量操作按钮：

```xml
<ToolBar Grid.Row="0" Margin="5">
    <hc:ButtonGroup>
        <Button Content="导入命令"
                Command="{Binding ImportCommandCommand}"/>
    </hc:ButtonGroup>

    <Separator/>

    <!-- 新增：批量模式按钮 -->
    <Button Content="批量操作"
            Command="{Binding CommandListViewModel.EnterBatchModeCommand}"
            Visibility="{Binding CommandListViewModel.IsBatchMode, Converter={StaticResource InverseBoolToVisibility}}"/>

    <StackPanel Orientation="Horizontal"
                Visibility="{Binding CommandListViewModel.IsBatchMode, Converter={StaticResource BoolToVisibility}}">
        <TextBlock Text="{Binding CommandListViewModel.SelectedCount, StringFormat='选中 {0} 项'}"
                  VerticalAlignment="Center"
                  Margin="0,0,10,0"/>
        <Button Content="批量删除"
                Command="{Binding CommandListViewModel.BatchDeleteCommand}"
                Margin="0,0,5,0"/>
        <Button Content="批量移动"
                Command="{Binding CommandListViewModel.BatchMoveCommand}"
                Margin="0,0,5,0"/>
        <Button Content="全选"
                Command="{Binding CommandListViewModel.SelectAllCommand}"
                Margin="0,0,5,0"/>
        <Button Content="完成"
                Command="{Binding CommandListViewModel.ExitBatchModeCommand}"/>
    </StackPanel>

    <Separator/>

    <!-- 排序控件... -->
</ToolBar>
```

**Step 2: 在命令卡片添加选择复选框**

修改 ItemsControl 的 ItemTemplate：

```xml
<ItemsControl ItemsSource="{Binding CommandListViewModel.Commands}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <hc:Card Margin="0,5" BorderThickness="1">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>  <!-- 新增：复选框列 -->
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- 新增：批量选择复选框 -->
                    <CheckBox Grid.Column="0"
                              Margin="10,0"
                              VerticalAlignment="Center"
                              Visibility="{Binding DataContext.CommandListViewModel.IsBatchMode, RelativeSource={RelativeSource AncestorType=ItemsControl}, Converter={StaticResource BoolToVisibility}}"
                              IsChecked="{Binding DataContext.CommandListViewModel.SelectedCommands, RelativeSource={RelativeSource AncestorType=ItemsControl}, Converter={StaticResource ContainsConverter}, ConverterParameter={Binding}}"
                              Command="{Binding DataContext.CommandListViewModel.ToggleCommandSelectionCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                              CommandParameter="{Binding}"/>

                    <!-- 原有内容移到 Column="1" -->
                    <StackPanel Grid.Column="1" Margin="10">
                        <TextBlock Text="{Binding Name}"
                                   FontSize="16"
                                   FontWeight="Bold"/>
                        <TextBlock Text="{Binding Description}"
                                   Foreground="{DynamicResource SecondaryTextBrush}"
                                   TextTrimming="CharacterEllipsis"/>
                        <TextBlock Text="{Binding FilePath}"
                                   FontSize="12"
                                   TextTrimming="CharacterEllipsis"
                                   ToolTip="{Binding FilePath}"/>
                    </StackPanel>

                    <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center" Margin="10">
                        <Button Content="运行" Command="..." Margin="0,0,5,0"/>
                        <Button Content="管理员运行" Command="..." Margin="0,0,5,0"/>
                        <Button Content="删除" Command="..." Style="{StaticResource ButtonDanger}"/>
                    </StackPanel>
                </Grid>
            </hc:Card>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**注意：** 复选框绑定可能需要更简单的处理方式，可以考虑在 Command 模型中添加 IsSelected 属性。

**Step 3: 提交**

```bash
git add MainWindow.xaml
git commit -m "feat: 主界面添加批量操作UI"
```

---

## 验证和测试

### Task 12: 构建和测试

**Step 1: 构建项目**

```bash
dotnet build
```

**Step 2: 运行并测试**

```bash
dotnet run
```

**手动测试清单：**

- [ ] 可以右键移动单条命令到指定分组
- [ ] 可以进入/退出批量模式
- [ ] 批量模式下显示复选框
- [ ] 可以选择多条命令
- [ ] 可以批量删除选中的命令
- [ ] 可以批量移动选中的命令
- [ ] "常用命令"分组显示最近使用的10条命令
- [ ] 执行命令后常用命令列表更新
- [ ] 常用命令列表显示命令的分组信息

**Step 3: 提交最终代码**

```bash
git add .
git commit -m "feat: 批次2 - 命令管理功能完成

- 实现命令移动到指定分组
- 实现批量选择、批量删除、批量移动
- 实现常用命令虚拟分组
- 执行命令时记录执行统计
- 所有功能测试通过"
```

---

## 批次2完成总结

完成后应有以下修改：

1. **数据访问**：CommandRepository 添加 MoveToGroupAsync、GetFrequentlyUsedAsync
2. **服务层**：CommandService 添加移动、常用命令、执行记录功能
3. **视图模型**：CommandListViewModel 添加批量操作、常用命令加载
4. **对话框**：新增 GroupSelectionDialog 用于选择目标分组
5. **UI**：MainWindow.xaml 添加批量操作控件
6. **虚拟分组**：名称改为"常用命令"
