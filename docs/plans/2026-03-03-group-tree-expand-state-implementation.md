# 分组树展开状态保持实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现增量更新分组树结构，保持 TreeView 的展开状态

**Architecture:** 通过递归对比新旧树结构，在 ObservableCollection 上进行增删改操作，保持 Group 对象引用不变以维持 TreeViewItem 的展开状态

**Tech Stack:** WPF, C#, CommunityToolkit.Mvvm, HandyControl

---

## 前置检查

### Task 0: 检查当前代码状态

**Files:**
- Read: `ViewModels/GroupTreeViewModel.cs`

**Step 1: 确认 LoadGroupsAsync 方法存在**

检查第 31-35 行是否存在：
```csharp
public async Task LoadGroupsAsync()
{
    var groups = await _groupService.GetGroupTreeAsync();
    Groups = new ObservableCollection<Group>(groups);
}
```

**Step 2: 确认三个调用点**

检查以下位置是否调用 `LoadGroupsAsync()`：
- 第 66 行：CreateGroupAsync 方法
- 第 112 行：FinishRenameAsync 方法
- 第 140 行：DeleteGroupAsync 方法

---

## Task 1: 实现树结构同步方法

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs`（在 LoadGroupsAsync 方法之后添加新方法）

**Step 1: 添加 SyncGroupTree 私有方法**

在 `LoadGroupsAsync` 方法后添加以下代码：

```csharp
/// <summary>
/// 递归同步树结构，增量更新集合以保持展开状态
/// </summary>
private void SyncGroupTree(ObservableCollection<Group> current, List<Group> fresh)
{
    // 1. 标记当前所有节点用于检测删除
    var currentIds = new HashSet<int>(current.Select(g => g.Id));
    var freshIds = new HashSet<int>(fresh.Select(g => g.Id));

    // 2. 删除在新数据中不存在的节点
    var toRemove = current.Where(g => !freshIds.Contains(g.Id)).ToList();
    foreach (var group in toRemove)
    {
        current.Remove(group);
    }

    // 3. 处理新增和更新的节点
    int insertIndex = 0;
    foreach (var freshGroup in fresh)
    {
        var existingGroup = current.FirstOrDefault(g => g.Id == freshGroup.Id);

        if (existingGroup == null)
        {
            // 新增节点
            var newGroup = new Group
            {
                Id = freshGroup.Id,
                Name = freshGroup.Name,
                ParentId = freshGroup.ParentId,
                SortOrder = freshGroup.SortOrder,
                Children = new List<Group>()
            };

            if (insertIndex >= current.Count)
            {
                current.Add(newGroup);
            }
            else
            {
                current.Insert(insertIndex, newGroup);
            }
            existingGroup = newGroup;
        }
        else
        {
            // 更新现有节点（保持对象引用）
            existingGroup.Name = freshGroup.Name;
            existingGroup.SortOrder = freshGroup.SortOrder;
            existingGroup.ParentId = freshGroup.ParentId;
        }

        // 4. 递归同步子节点
        var existingChildren = new ObservableCollection<Group>(existingGroup.Children);
        SyncGroupTree(existingChildren, freshGroup.Children);
        existingGroup.Children = existingChildren.ToList();

        insertIndex++;
    }
}
```

**Step 2: 添加 using 语句（如需要）**

确认文件顶部有：
```csharp
using System.Collections.ObjectModel;
```

---

## Task 2: 创建增量刷新方法

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs`（修改 LoadGroupsAsync 方法）

**Step 1: 修改 LoadGroupsAsync 方法**

将原有的 `LoadGroupsAsync` 方法改为支持增量更新：

```csharp
public async Task LoadGroupsAsync(bool incremental = true)
{
    var groups = await _groupService.GetGroupTreeAsync();

    if (Groups.Count == 0 || !incremental)
    {
        // 首次加载或非增量刷新：重建整个集合
        Groups = new ObservableCollection<Group>(groups);
    }
    else
    {
        // 增量更新：同步树结构
        SyncGroupTree(Groups, groups);
    }
}
```

**Step 2: 验证修改**

确认方法签名从：
```csharp
public async Task LoadGroupsAsync()
```
变为：
```csharp
public async Task LoadGroupsAsync(bool incremental = true)
```

---

## Task 3: 更新调用点

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs`

**Step 1: 更新 CreateGroupAsync 方法**

找到第 66 行（在 CreateGroupAsync 方法中）：

**原代码：**
```csharp
await LoadGroupsAsync();
```

**新代码：**
```csharp
await LoadGroupsAsync(incremental: true);
```

**Step 2: 更新 FinishRenameAsync 方法**

找到第 112 行（在 FinishRenameAsync 方法中）：

**原代码：**
```csharp
await LoadGroupsAsync();
```

**新代码：**
```csharp
await LoadGroupsAsync(incremental: true);
```

**Step 3: 更新 DeleteGroupAsync 方法**

找到第 140 行（在 DeleteGroupAsync 方法中）：

**原代码：**
```csharp
await LoadGroupsAsync();
```

**新代码：**
```csharp
await LoadGroupsAsync(incremental: true);
```

---

## Task 4: 手动测试

**Step 1: 编译项目**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 2: 运行应用程序**

Run: `dotnet run` 或从 IDE 启动

**Step 3: 测试展开状态保持**

1. 展开几个分组节点
2. 右键点击某个分组 → 新建子分组
3. 输入名称并确认
4. **验证**：分组树保持原有的展开状态，新分组出现在对应位置
5. 右键点击某个分组 → 重命名
6. 修改名称并确认
7. **验证**：分组树保持原有的展开状态
8. 右键点击某个空分组 → 删除
9. **验证**：分组树保持原有的展开状态（被删除的分组消失，其他不变）

---

## Task 5: 提交代码

**Step 1: 查看更改**

Run: `git diff ViewModels/GroupTreeViewModel.cs`

Expected: 显示 SyncGroupTree 方法添加和 LoadGroupsAsync 方法的修改

**Step 2: 提交**

```bash
git add ViewModels/GroupTreeViewModel.cs
git commit -m "feat: 增量更新分组树，保持展开状态

- 添加 SyncGroupTree 方法递归同步树结构
- 修改 LoadGroupsAsync 支持增量更新
- 创建/删除/重命名后使用增量刷新保持 TreeView 展开状态"
```

---

## 注意事项

1. **对象引用保持**：SyncGroupTree 方法的关键是保持现有 Group 对象的引用不变，只更新属性值
2. **递归深度**：假设分组树层级不会太深（一般 < 10 层），递归性能可接受
3. **并发安全**：此方法在 UI 线程调用，ObservableCollection 的操作是线程安全的

## 回滚方案

如果发现问题，可以临时回滚到非增量模式：

将所有 `await LoadGroupsAsync(incremental: true);` 改为 `await LoadGroupsAsync(incremental: false);`
