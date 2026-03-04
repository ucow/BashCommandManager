# HandyControl 弹窗样式改造实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将项目中所有 System.Windows.MessageBox 替换为 HandyControl 的 Dialog 和 Growl 样式弹窗

**Architecture:** 确认类弹窗使用 Dialog+ConfirmDialogControl，提示类弹窗使用非阻塞的 Growl（Success/Warning/Error）

**Tech Stack:** WPF, HandyControl 3.5.1, CommunityToolkit.Mvvm

---

### Task 1: 修改 CommandListViewModel 删除命令确认弹窗

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs:80-92`

**Step 1: 添加必要的 using 语句**

在文件顶部添加：
```csharp
using BashCommandManager.Controls;
using HandyControl.Controls;
```

**Step 2: 修改 DeleteCommandAsync 方法**

将第80-92行从：
```csharp
[RelayCommand]
private async Task DeleteCommandAsync(Command command)
{
    var result = System.Windows.MessageBox.Show(
        $"确定要删除命令 '{command.Name}' 吗？\n这只会从列表中移除，不会删除实际文件。",
        "确认删除",
        System.Windows.MessageBoxButton.YesNo);

    if (result == System.Windows.MessageBoxResult.Yes)
    {
        await _commandService.DeleteCommandAsync(command.Id);
        Commands.Remove(command);
    }
}
```

改为：
```csharp
[RelayCommand]
private async Task DeleteCommandAsync(Command command)
{
    var dialog = new ConfirmDialogControl
    {
        Title = "确认删除",
        Message = $"确定要删除命令 '{command.Name}' 吗？\n这只会从列表中移除，不会删除实际文件。"
    };
    dialog.DataContext = dialog;

    var result = await Dialog.Show(dialog).GetResultAsync<bool>();

    if (result)
    {
        await _commandService.DeleteCommandAsync(command.Id);
        Commands.Remove(command);
        Growl.Success("删除成功");
    }
}
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: 构建成功，无错误

**Step 4: Commit**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "refactor: 删除命令确认弹窗改用HandyControl样式"
```

---

### Task 2: 修改 CommandListViewModel 执行失败提示

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs:53-77`

**Step 1: 修改 ExecuteCommand 方法**

将第62行从：
```csharp
System.Windows.MessageBox.Show($"执行失败: {ex.Message}");
```
改为：
```csharp
Growl.Error($"执行失败: {ex.Message}");
```

**Step 2: 修改 ExecuteAsAdmin 方法**

将第75行从：
```csharp
System.Windows.MessageBox.Show($"执行失败: {ex.Message}");
```
改为：
```csharp
Growl.Error($"执行失败: {ex.Message}");
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: 构建成功，无错误

**Step 4: Commit**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "refactor: 命令执行失败提示改用Growl.Error"
```

---

### Task 3: 修改 CommandListViewModel 打开目录相关提示

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs:95-127`

**Step 1: 修改目录不存在提示**

将第110行从：
```csharp
System.Windows.MessageBox.Show($"目录不存在: {directoryPath}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
```
改为：
```csharp
Growl.Warning($"目录不存在: {directoryPath}");
```

**Step 2: 修改打开目录失败提示**

将第125行从：
```csharp
System.Windows.MessageBox.Show($"打开目录失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
```
改为：
```csharp
Growl.Error($"打开目录失败: {ex.Message}");
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: 构建成功，无错误

**Step 4: Commit**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "refactor: 打开目录相关提示改用Growl样式"
```

---

### Task 4: 修改 MainViewModel 导入成功提示

**Files:**
- Modify: `ViewModels/MainViewModel.cs:1-15` (using)
- Modify: `ViewModels/MainViewModel.cs:99-102`

**Step 1: 添加 using 语句**

在文件顶部添加：
```csharp
using HandyControl.Controls;
```

**Step 2: 修改导入成功提示**

将第101行从：
```csharp
System.Windows.MessageBox.Show($"成功导入 {count} 个命令", "导入完成");
```
改为：
```csharp
Growl.Success($"成功导入 {count} 个命令");
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: 构建成功，无错误

**Step 4: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "refactor: 导入成功提示改用Growl.Success"
```

---

### Task 5: 修改 MainViewModel 选择分组提示

**Files:**
- Modify: `ViewModels/MainViewModel.cs:83-94`

**Step 1: 修改未选择分组提示**

将第85行从：
```csharp
System.Windows.MessageBox.Show("请先选择一个分组", "提示");
```
改为：
```csharp
Growl.Warning("请先选择一个分组");
```

**Step 2: 修改虚拟节点提示**

将第92行从：
```csharp
System.Windows.MessageBox.Show("请选择一个具体分组来导入命令", "提示");
```
改为：
```csharp
Growl.Warning("请选择一个具体分组来导入命令");
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: 构建成功，无错误

**Step 4: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "refactor: 选择分组相关提示改用Growl.Warning"
```

---

### Task 6: 修改 MainViewModel 导入失败提示

**Files:**
- Modify: `ViewModels/MainViewModel.cs:106-109`

**Step 1: 修改导入失败提示**

将第108行从：
```csharp
System.Windows.MessageBox.Show($"导入失败: {ex.Message}", "错误");
```
改为：
```csharp
Growl.Error($"导入失败: {ex.Message}");
```

**Step 2: 编译验证**

Run: `dotnet build`
Expected: 构建成功，无错误

**Step 3: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "refactor: 导入失败提示改用Growl.Error"
```

---

### Task 7: 验证所有修改

**Files:**
- Check: `ViewModels/CommandListViewModel.cs`
- Check: `ViewModels/MainViewModel.cs`

**Step 1: 搜索剩余的 MessageBox 使用**

Run: `grep -r "MessageBox" --include="*.cs" ViewModels/`
Expected: 无输出（没有剩余使用）

**Step 2: 完整构建验证**

Run: `dotnet build`
Expected: 构建成功，无警告

**Step 3: 功能验证（可选，如果能运行）**

- 删除命令时显示 HandyControl 样式确认对话框
- 导入成功时右上角显示绿色 Growl 提示
- 各种错误/警告时显示对应的 Growl 提示

---

## 完成总结

所有 System.Windows.MessageBox 弹窗已替换为 HandyControl 样式：
- **Dialog + ConfirmDialogControl**: 删除命令确认
- **Growl.Success**: 导入成功、删除成功
- **Growl.Warning**: 未选择分组、虚拟节点、目录不存在
- **Growl.Error**: 执行失败、打开目录失败、导入失败
