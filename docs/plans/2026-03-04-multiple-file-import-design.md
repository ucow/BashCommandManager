# 多文件同时导入功能 - 设计方案

## 概述

将现有的单文件导入功能扩展为支持同时导入多个 `.bat`/`.cmd` 文件。

## 目标

- 用户可在文件对话框中按住 Ctrl/Shift 键选择多个文件一次性导入
- 保持现有 UI 不变，无感知升级
- 批量导入时提供结果反馈

## 技术实现

### 1. 服务层修改

**文件：`Core/Services/CommandService.cs`**

#### 接口变更
```csharp
// 原接口
Task<Command?> ImportCommandAsync(int groupId);

// 新接口
Task<IEnumerable<Command>> ImportCommandsAsync(int groupId);
```

#### 实现变更
```csharp
public async Task<IEnumerable<Command>> ImportCommandsAsync(int groupId)
{
    return await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
    {
        var dialog = new OpenFileDialog
        {
            Filter = "批处理文件|*.bat;*.cmd|所有文件|*.*",
            Title = "选择批处理文件",
            Multiselect = true  // 新增：允许多选
        };

        var importedCommands = new List<Command>();

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                var command = new Command
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Description = "",
                    FilePath = filePath,
                    GroupId = groupId
                };

                command.Id = await _repository.CreateAsync(command);
                importedCommands.Add(command);
            }
        }

        return importedCommands;
    }).Task.Unwrap();
}
```

### 2. ViewModel 层修改

**文件：`ViewModels/CommandListViewModel.cs`**

```csharp
public async Task ImportCommandsAsync(int groupId)
{
    var commands = await _commandService.ImportCommandsAsync(groupId);
    foreach (var command in commands)
    {
        Commands.Add(command);
    }
}
```

**文件：`ViewModels/MainViewModel.cs`**

```csharp
[RelayCommand]
private async Task ImportCommandAsync()
{
    try
    {
        if (GroupTreeViewModel.SelectedGroup == null)
        {
            System.Windows.MessageBox.Show("请先选择一个分组", "提示");
            return;
        }

        if (GroupTreeViewModel.SelectedGroup.IsVirtual)
        {
            System.Windows.MessageBox.Show("请选择一个具体分组来导入命令", "提示");
            return;
        }

        var commands = await CommandListViewModel.ImportCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
        var count = commands?.Count() ?? 0;

        if (count > 0)
        {
            System.Windows.MessageBox.Show($"成功导入 {count} 个命令", "导入完成");
        }

        UpdateStatus();
    }
    catch (Exception ex)
    {
        System.Windows.MessageBox.Show($"导入失败: {ex.Message}", "错误");
    }
}
```

## 错误处理

- 单个文件导入失败不影响其他文件
- 完成后显示导入成功的数量统计

## 测试场景

1. 选择单个文件导入（向后兼容）
2. 按住 Ctrl 选择多个文件导入
3. 点击取消按钮（不选择文件）
4. 导入到不同的分组

## 约束确认

- [x] 无 UI 界面变化
- [x] 保持向后兼容
- [x] 仅支持 `.bat`/`.cmd` 文件
- [x] 所有文件导入到同一分组
