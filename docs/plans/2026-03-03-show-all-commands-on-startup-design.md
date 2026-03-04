# 启动时展示所有命令 - 设计方案

## 概述

在应用启动时自动展示数据库中的所有命令，而不是等待用户选择分组。通过在左侧分组树顶部添加一个"全部命令"虚拟节点实现。

---

## 技术方案

### 方案选择

选择**方案 1：虚拟节点 + 特殊 GroupId**

- 实现简单，复用现有架构
- 不需要修改数据库表结构
- 与现有分组逻辑保持一致

---

## 具体实现

### 1. 界面修改

**文件**: `MainWindow.xaml`

在分组树顶部添加虚拟节点"📋 全部命令"，此节点不来自数据库，是 UI 层静态添加的。

```
左侧分组树：
├─ 📋 全部命令    ← 新增虚拟节点（Id = 0）
├─ 📁 分组1
│  └─ 📁 子分组
└─ 📁 分组2
```

### 2. 数据层修改

**文件**: `Core/Repositories/CommandRepository.cs`

添加新方法获取所有命令：

```csharp
Task<IEnumerable<Command>> GetAllAsync();
```

实现逻辑：
```sql
SELECT * FROM Commands ORDER BY Name
```

### 3. 服务层修改

**文件**: `Core/Services/CommandService.cs`

添加方法调用 Repository 的 `GetAllAsync()`，暴露给 ViewModel 使用。

### 4. ViewModel 修改

**文件**: `ViewModels/GroupTreeViewModel.cs`

- 在加载分组列表时，手动插入"全部命令"虚拟节点（Id=0）
- 确保虚拟节点始终位于列表顶部

**文件**: `ViewModels/MainViewModel.cs`

- 修改 `LoadCommandsForGroup()` 逻辑
- 当 `SelectedGroup.Id == 0`（表示"全部命令"）时，调用 `GetAllAsync()`
- 否则保持原有逻辑

**文件**: `ViewModels/CommandListViewModel.cs`

- 添加 `LoadAllCommandsAsync()` 方法
- 调用 Service 层获取所有命令

### 5. 启动时自动选中

**文件**: `MainWindow.xaml.cs`

- 在窗口加载完成后，自动选中"全部命令"节点
- 触发加载所有命令的显示

---

## 关键代码示例

### CommandRepository.cs

```csharp
public async Task<IEnumerable<Command>> GetAllAsync()
{
    var sql = "SELECT * FROM Commands ORDER BY Name";
    return await _db.QueryAsync<Command>(sql);
}
```

### GroupTreeViewModel.cs

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
        IsVirtual = true  // 标记为虚拟节点，禁止删除/重命名
    };
    groupNodes.Insert(0, allCommandsNode);

    Groups = new ObservableCollection<GroupNode>(groupNodes);

    // 自动选中"全部命令"节点
    SelectedGroup = allCommandsNode;
}
```

### MainViewModel.cs

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

---

## 约束确认

- ✅ 不修改数据库表结构
- ✅ 复用现有 UI 组件和逻辑
- ✅ 保持向后兼容（现有功能不受影响）
- ✅ 虚拟节点不可被删除/重命名（因为它是特殊的，Id=0）
- ✅ 单用户本地使用场景

---

## 测试要点

1. 启动时自动显示"全部命令"节点并加载所有命令
2. 点击普通分组，正常显示该分组下的命令
3. 点击"全部命令"节点，显示所有命令
4. 虚拟节点不应显示删除/重命名菜单项（或点击时提示无法操作）
5. 导入命令时仍需选择具体分组（不能导入到"全部命令"虚拟节点）

---

## 后续扩展

- 可考虑为虚拟节点添加特殊图标区分（如使用不同颜色或图标）
- 可考虑添加显示每个分组中的命令数量
