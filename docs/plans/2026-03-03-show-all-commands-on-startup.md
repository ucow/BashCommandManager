# 启动时展示所有命令 - 实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在应用启动时自动展示数据库中的所有命令，通过左侧分组树顶部的"全部命令"虚拟节点实现。

**Architecture:** 在 GroupTreeViewModel 加载分组时插入 Id=0 的虚拟节点，MainViewModel 根据选中节点的 Id 决定是加载所有命令还是特定分组命令。

**Tech Stack:** WPF, C#, SQLite, Dapper, CommunityToolkit.Mvvm

---

## Task 1: 添加 Repository 层获取所有命令的方法

**Files:**
- Modify: `Core/Repositories/CommandRepository.cs:14` (接口)
- Modify: `Core/Repositories/CommandRepository.cs:62-71` (实现)

**Step 1: 在接口中添加新方法**

```csharp
public interface ICommandRepository
{
    Task<IEnumerable<Command>> GetByGroupIdAsync(int groupId);
    Task<Command?> GetByIdAsync(int id);
    Task<int> CreateAsync(Command command);
    Task UpdateAsync(Command command);
    Task DeleteAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
    Task<IEnumerable<Command>> GetAllAsync();  // 新增
}
```

**Step 2: 在实现类中添加方法**

```csharp
public async Task<IEnumerable<Command>> GetAllAsync()
{
    var sql = "SELECT * FROM Commands ORDER BY Name";
    return await _db.QueryAsync<Command>(sql);
}
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add Core/Repositories/CommandRepository.cs
git commit -m "feat: 添加 GetAllAsync 方法获取所有命令"
```

---

## Task 2: 添加 Service 层获取所有命令的方法

**Files:**
- Modify: `Core/Services/CommandService.cs` (需要查看文件内容确定接口和实现)

**Step 1: 查看现有 CommandService 文件结构**

Run: `cat Core/Services/CommandService.cs`

**Step 2: 在接口 ICommandService 中添加新方法**

```csharp
public interface ICommandService
{
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId);
    Task<Command?> GetByIdAsync(int id);
    Task<Command?> ImportCommandAsync(int groupId);
    Task DeleteCommandAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
    Task<IEnumerable<Command>> GetAllAsync();  // 新增
}
```

**Step 3: 在 CommandService 实现中添加方法**

```csharp
public async Task<IEnumerable<Command>> GetAllAsync()
{
    return await _commandRepository.GetAllAsync();
}
```

**Step 4: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add Core/Services/CommandService.cs
git commit -m "feat: Service 层添加获取所有命令的方法"
```

---

## Task 3: 在 CommandListViewModel 中添加加载所有命令的方法

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs:22-27`

**Step 1: 添加 LoadAllCommandsAsync 方法**

在 `LoadCommandsAsync` 方法之后添加：

```csharp
public async Task LoadAllCommandsAsync()
{
    var commands = await _commandService.GetAllAsync();
    Commands = new ObservableCollection<Command>(commands);
}
```

**Step 2: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: CommandListViewModel 添加加载所有命令的方法"
```

---

## Task 4: 查看 GroupTreeViewModel 当前实现

**Files:**
- Read: `ViewModels/GroupTreeViewModel.cs`

**Step 1: 读取文件内容了解现有结构**

Run: `cat ViewModels/GroupTreeViewModel.cs`

**注意:** 需要确认：
- GroupNode 类的定义（是否有 IsVirtual 属性）
- LoadGroupsAsync 方法的实现
- Groups 集合的类型

---

## Task 5: 修改 GroupNode 模型（如需要）

**Files:**
- Check: `Core/Models/Group.cs` 或 GroupNode 定义位置

**Step 1: 查看 GroupNode/Group 模型定义**

Run: `cat Core/Models/Group.cs`

**Step 2: 如需要，添加 IsVirtual 属性**

如果 GroupNode 没有 IsVirtual 属性，添加它：

```csharp
public class GroupNode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsEditing { get; set; }
    public ObservableCollection<GroupNode> Children { get; set; } = new();
    public bool IsVirtual { get; set; }  // 新增：标记虚拟节点
}
```

或者如果 GroupNode 是独立类，找到它并添加属性。

**Step 3: Commit（如做了修改）**

```bash
git add <修改的文件路径>
git commit -m "feat: GroupNode 添加 IsVirtual 属性标记虚拟节点"
```

---

## Task 6: 修改 GroupTreeViewModel 插入虚拟节点

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs`

**Step 1: 修改 LoadGroupsAsync 方法**

在加载分组列表后，插入虚拟节点：

```csharp
public async Task LoadGroupsAsync()
{
    var groups = await _groupService.GetAllAsync();
    var groupNodes = BuildTree(groups);

    // 插入虚拟"全部命令"节点
    var allCommandsNode = new GroupNode
    {
        Id = 0,
        Name = "全部命令",
        IsVirtual = true
    };
    groupNodes.Insert(0, allCommandsNode);

    Groups = new ObservableCollection<GroupNode>(groupNodes);

    // 自动选中"全部命令"节点
    SelectedGroup = allCommandsNode;
}
```

**Step 2: 确保 BuildTree 返回的是 List<GroupNode> 或可插入的集合**

如果 BuildTree 返回的是 ObservableCollection，需要调整：

```csharp
var groupNodes = BuildTree(groups).ToList();
```

**Step 3: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add ViewModels/GroupTreeViewModel.cs
git commit -m "feat: 分组树插入全部命令虚拟节点并默认选中"
```

---

## Task 7: 修改 MainViewModel 处理虚拟节点的命令加载

**Files:**
- Modify: `ViewModels/MainViewModel.cs:36-43`

**Step 1: 修改 LoadCommandsForGroup 方法**

```csharp
private async void LoadCommandsForGroup()
{
    if (GroupTreeViewModel.SelectedGroup == null)
        return;

    if (GroupTreeViewModel.SelectedGroup.Id == 0)
    {
        // 加载所有命令
        await CommandListViewModel.LoadAllCommandsAsync();
    }
    else
    {
        // 加载特定分组命令
        await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
    }
    UpdateStatus();
}
```

**Step 2: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat: 根据选中节点类型加载所有命令或分组命令"
```

---

## Task 8: 处理虚拟节点的右键菜单（禁止删除/重命名）

**Files:**
- Modify: `MainWindow.xaml:54-74` (TreeView ItemContainerStyle)
- Modify: `MainWindow.xaml.cs` (添加菜单打开事件处理)

**Step 1: 查看 MainWindow.xaml.cs 现有代码**

Run: `cat MainWindow.xaml.cs`

**Step 2: 为虚拟节点添加特殊处理**

在 `TreeViewItem_ContextMenuOpening` 事件处理中（或添加新的事件处理），检查是否是虚拟节点：

```csharp
private void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
{
    if (sender is TreeViewItem treeViewItem && treeViewItem.DataContext is GroupNode node)
    {
        if (node.IsVirtual)
        {
            // 虚拟节点：隐藏删除和重命名菜单项
            var contextMenu = treeViewItem.ContextMenu;
            foreach (var item in contextMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (menuItem.Header?.ToString() == "重命名" ||
                        menuItem.Header?.ToString() == "删除")
                    {
                        menuItem.Visibility = Visibility.Collapsed;
                    }
                    else if (menuItem.Header?.ToString() == "新建子分组")
                    {
                        // 虚拟节点也不允许新建子分组
                        menuItem.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }
    }
}
```

或者更简单的方式：在菜单点击时判断：

```csharp
private void RenameGroupMenuItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is MenuItem menuItem && menuItem.DataContext is GroupNode node)
    {
        if (node.IsVirtual)
        {
            MessageBox.Show("'全部命令' 是系统节点，不能重命名", "提示");
            return;
        }
        // 原有逻辑...
    }
}
```

**注意:** 需要根据现有代码结构选择合适的方式。

**Step 3: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: 虚拟节点禁止删除和重命名操作"
```

---

## Task 9: 导入命令时检查是否选择了虚拟节点

**Files:**
- Modify: `ViewModels/MainViewModel.cs:62-80`

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

        // 检查是否选择了虚拟节点
        if (GroupTreeViewModel.SelectedGroup.IsVirtual)
        {
            System.Windows.MessageBox.Show("请选择一个具体分组来导入命令", "提示");
            return;
        }

        await CommandListViewModel.ImportCommandAsync(GroupTreeViewModel.SelectedGroup.Id);
        UpdateStatus();
    }
    catch (Exception ex)
    {
        System.Windows.MessageBox.Show($"导入失败: {ex.Message}", "错误");
    }
}
```

**Step 2: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat: 导入命令时检查不能选择虚拟节点"
```

---

## Task 10: 运行应用测试

**Step 1: 运行应用**

Run: `dotnet run`

**Step 2: 验证功能**

- [ ] 启动时左侧树顶部显示"全部命令"节点
- [ ] 启动时自动选中"全部命令"节点
- [ ] 右侧列表显示数据库中所有命令
- [ ] 点击普通分组，显示该分组下的命令
- [ ] 点击"全部命令"，显示所有命令
- [ ] 右键"全部命令"节点，没有删除/重命名菜单项（或提示不能操作）
- [ ] 选择"全部命令"节点时点击导入，提示选择具体分组

**Step 3: 如发现问题，修复后 Commit**

```bash
git add <修改的文件>
git commit -m "fix: 修复启动时展示所有命令的问题"
```

---

## 实施完成

所有任务完成后，功能已实现：
1. ✅ 启动时自动显示"全部命令"虚拟节点
2. ✅ 自动加载所有命令到右侧列表
3. ✅ 点击普通分组正常工作
4. ✅ 虚拟节点受保护，不能删除/重命名
5. ✅ 导入命令时需选择具体分组
