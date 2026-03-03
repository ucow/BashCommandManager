# 分组树展开状态保持设计方案

## 问题描述

当前在创建、删除、重命名分组后，分组树会全部折叠起来。这是因为每次操作后都重新创建了 `ObservableCollection<Group>`，导致 WPF 的 TreeView 丢失所有展开状态。

## 目标

- 操作完成后，分组树保持原有的展开状态
- 数据仍然要正确刷新
- 提供流畅的用户体验（无闪烁）

## 解决方案

采用**增量更新集合**方案（方案2）：不重新创建 `ObservableCollection`，而是对比新旧数据，只增删改变化的部分。

### 核心机制

1. **保持对象引用**：TreeViewItem 的展开状态与 Group 对象绑定，保持引用就能保持状态
2. **按 ID 匹配**：用 Group.Id 来识别新旧节点是否对应
3. **递归处理子节点**：对每个层级进行增删改操作

### 实现设计

#### 新增方法

```csharp
/// <summary>
/// 递归同步树结构，增量更新集合以保持展开状态
/// </summary>
private void SyncGroupTree(ObservableCollection<Group> current, List<Group> fresh)
```

算法逻辑：
1. 遍历当前集合和新数据，建立 ID 到对象的映射
2. 识别新增节点（在 fresh 中但不在 current 中）→ 添加到集合
3. 识别删除节点（在 current 中但不在 fresh 中）→ 从集合移除
4. 识别重命名节点（ID 相同但 Name 不同）→ 更新现有对象的 Name 属性
5. 递归处理每个节点的 Children 集合

#### 刷新逻辑变更

原代码：
```csharp
await LoadGroupsAsync();  // 会重置整个集合
```

新代码：
```csharp
var freshGroups = await _groupService.GetGroupTreeAsync();
if (Groups.Count == 0)
{
    Groups = new ObservableCollection<Group>(freshGroups);
}
else
{
    SyncGroupTree(Groups, freshGroups);
}
```

#### 初始加载处理

首次加载时（Groups.Count == 0），仍使用完整加载方式创建初始集合。

### 场景处理

| 操作 | 处理方式 | 展开状态 |
|------|----------|----------|
| 创建分组 | 插入新 Group 对象到对应父节点的 Children 集合 | 新节点默认折叠，其他节点状态不变 |
| 删除分组 | 从对应父节点的 Children 集合移除 | 其他节点状态不变 |
| 重命名分组 | 更新现有 Group 对象的 Name 属性 | 完全保持 |

### 边界情况

1. **删除已展开的节点**：节点被删除后，其所有子节点自然也被移除，父节点保持展开
2. **创建嵌套结构**：递归算法会正确处理多层级的节点插入
3. **空集合**：首次加载时走完整加载路径

## 优点

- 用户体验最佳，无闪烁
- 保持所有展开状态精确不变
- 符合 MVVM 模式

## 缺点

- 实现复杂度略高于方案1
- 需要处理递归树结构的差异对比

## 相关文件

- `ViewModels/GroupTreeViewModel.cs` - 主要修改文件
