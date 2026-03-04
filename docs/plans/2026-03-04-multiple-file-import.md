# 多文件同时导入功能 - 实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将现有的单文件导入功能扩展为支持同时导入多个 `.bat`/`.cmd` 文件

**Architecture:** 修改 `CommandService.ImportCommandAsync` 方法为 `ImportCommandsAsync`，启用 `OpenFileDialog.Multiselect` 属性，遍历选中的多个文件批量导入

**Tech Stack:** WPF, C#, .NET 8, HandyControl

---

## 前置依赖

- [ ] 阅读设计文档：`docs/plans/2026-03-04-multiple-file-import-design.md`
- [ ] 确认当前项目可以正常编译运行

---

### Task 1: 修改 ICommandService 接口

**Files:**
- Modify: `Core/Services/CommandService.cs:10-15`

**Step 1: 修改接口定义**

将 `ImportCommandAsync` 方法的返回类型从 `Task<Command?>` 改为 `Task<IEnumerable<Command>>`：

```csharp
public interface ICommandService
{
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId);
    Task<IEnumerable<Command>> ImportCommandsAsync(int groupId);  // 修改：从单文件改为多文件
    Task DeleteCommandAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
    Task<IEnumerable<Command>> GetAllAsync();
}
```

**Step 2: 验证编译**

Run: `dotnet build`
Expected: 编译失败（因为实现类还未修改）

---

### Task 2: 修改 CommandService 实现

**Files:**
- Modify: `Core/Services/CommandService.cs:31-59`

**Step 1: 修改方法签名**

将 `ImportCommandAsync` 方法重命名为 `ImportCommandsAsync`，并修改实现：

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

**Step 2: 验证编译**

Run: `dotnet build`
Expected: 编译通过（此时 ViewModel 层还未修改）

**Step 3: Commit**

```bash
git add Core/Services/CommandService.cs
git commit -m "feat: 修改 CommandService 支持多文件导入"
```

---

### Task 3: 修改 CommandListViewModel

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs:43-50`

**Step 1: 修改 ImportCommandAsync 方法**

将方法改为 `ImportCommandsAsync`，支持批量添加：

```csharp
public async Task<IEnumerable<Command>> ImportCommandsAsync(int groupId)
{
    var commands = await _commandService.ImportCommandsAsync(groupId);
    foreach (var command in commands)
    {
        Commands.Add(command);
    }
    return commands;
}
```

**Step 2: 验证编译**

Run: `dotnet build`
Expected: 编译失败（MainViewModel 还未修改）

---

### Task 4: 修改 MainViewModel

**Files:**
- Modify: `ViewModels/MainViewModel.cs:78-103`

**Step 1: 修改 ImportCommandAsync 方法**

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

**Step 2: 验证编译**

Run: `dotnet build`
Expected: 编译通过

**Step 3: Commit**

```bash
git add ViewModels/CommandListViewModel.cs ViewModels/MainViewModel.cs
git commit -m "feat: 修改 ViewModel 支持多文件导入并添加结果反馈"
```

---

### Task 5: 功能测试

**Files:**
- Test: 手动测试

**Step 1: 启动应用程序**

Run: `dotnet run`

**Step 2: 测试单文件导入（向后兼容）**

1. 选择一个分组
2. 点击【导入】按钮
3. 选择单个 `.bat` 文件
4. Expected: 文件成功导入，显示"成功导入 1 个命令"

**Step 3: 测试多文件导入**

1. 选择一个分组
2. 点击【导入】按钮
3. 按住 Ctrl 键选择多个 `.bat`/`.cmd` 文件
4. Expected: 所有选中文件成功导入，显示正确的导入数量

**Step 4: 测试取消导入**

1. 选择一个分组
2. 点击【导入】按钮
3. 点击【取消】按钮
4. Expected: 不显示任何消息，无命令被导入

**Step 5: Commit（如有需要）**

---

## 总结

实施后功能：
- 用户可在文件对话框中按住 Ctrl/Shift 键选择多个文件
- 选中文件将批量导入到当前选中的分组
- 导入完成后显示导入成功的数量统计
- 完全向后兼容单文件导入场景
