# 批次2：命令管理功能设计文档

## 概述

本批次处理命令管理相关的功能，包括移动命令、批量操作，以及新增"常用命令"虚拟分组功能。

## 关联Issue

| Issue | 标题 | 描述 |
|-------|------|------|
| #2 | 移动命令到分组 | 将指定命令移动到指定分组下 |
| #3 | 批量操作功能 | 批量删除、批量移动分组 |
| #7 | 常用命令分组 | 将"所有命令"改为"常用命令"，显示最近常用的10条命令 |

## 设计决策

### 1. 移动命令功能 (#2)

**触发方式**：
- 右键菜单：命令列表项右键 → "移动到..."
- 拖拽：支持拖拽命令到左侧分组树

**UI设计**：
- 右键菜单项："移动到分组 >"
- 子菜单显示所有可用分组（排除当前分组）
- 选择后执行移动

**实现要点**：
- `CommandService.MoveCommand(int commandId, int targetGroupId)`
- 更新数据库中的 `GroupId` 字段
- 移动成功后刷新列表并显示通知

### 2. 批量操作功能 (#3)

**触发方式**：
- 工具栏添加"批量模式"按钮
- 进入批量模式后显示复选框列

**UI设计**：
```
[批量模式 ▼]
  ├── 批量删除
  ├── 批量移动到...
  └── 全选/取消全选
```

**批量模式界面**：
- 命令列表前显示复选框列
- 底部显示选中数量
- 操作按钮：删除、移动

**实现要点**：
- `CommandListViewModel` 添加 `IsBatchMode` 属性
- 添加 `SelectedCommands` 集合
- 批量操作使用事务保证数据一致性

### 3. 常用命令虚拟分组 (#7)

**概念**：
- 将现有的"所有命令"虚拟分组改名为"常用命令"
- 基于使用频率和时间算法展示最近常用的10条命令

**算法**：
```
得分 = (执行次数 × 权重1) + (最近使用时间衰减 × 权重2)
```
- 最近7天内执行过的命令优先
- 执行次数越多得分越高
- 时间越近得分越高

**UI变更**：
- 虚拟分组名称从"所有命令"改为"常用命令"
- 列表显示增加"所属分组"列（仅在此分组显示）
- 显示命令使用统计信息

**实现要点**：
- 修改 `GroupTreeViewModel` 中的虚拟节点
- 新增查询方法 `GetFrequentlyUsedCommands(int limit = 10)`
- 排序算法在内存中计算得分

## 数据模型变更

### Command 模型（复用现有字段）
- `LastExecutedAt`: DateTime? - 上次执行时间
- `ExecutionCount`: int - 执行次数

### 新增：CommandUsageScore（内部计算用）
```csharp
public class CommandUsageScore
{
    public Command Command { get; set; }
    public double Score { get; set; }
}
```

## UI布局变更

### 命令列表右键菜单
```xml
<ContextMenu>
    <MenuItem Header="执行" Command="{Binding ExecuteCommand}" />
    <MenuItem Header="编辑" Command="{Binding EditCommand}" />
    <MenuItem Header="移动到分组">
        <MenuItem.ItemsSource>
            <Binding Path="AvailableGroups" />
        </MenuItem.ItemsSource>
    </MenuItem>
    <Separator />
    <MenuItem Header="删除" Command="{Binding DeleteCommand}" />
</ContextMenu>
```

### 批量模式工具栏
```xml
<StackPanel Visibility="{Binding IsBatchMode, Converter={StaticResource BoolToVisibility}}">
    <TextBlock Text="{Binding SelectedCount, StringFormat='选中 {0} 项'}" />
    <Button Content="批量删除" Command="{Binding BatchDeleteCommand}" />
    <Button Content="批量移动" Command="{Binding BatchMoveCommand}" />
    <Button Content="完成" Command="{Binding ExitBatchModeCommand}" />
</StackPanel>
```

## 交互流程

### 移动命令
```
用户右键点击命令
    ↓
选择"移动到分组 >"
    ↓
显示目标分组子菜单
    ↓
用户选择目标分组
    ↓
更新数据库
    ↓
刷新列表
    ↓
显示成功通知
```

### 批量操作
```
用户点击"批量模式"
    ↓
列表显示复选框
    ↓
用户选择多条命令
    ↓
用户选择操作（删除/移动）
    ↓
显示确认对话框
    ↓
执行批量操作
    ↓
退出批量模式，刷新列表
```

### 常用命令
```
用户点击"常用命令"分组
    ↓
计算所有命令使用得分
    ↓
取前10条
    ↓
按得分排序显示
    ↓
列表额外显示分组信息列
```

## 测试要点

1. **移动命令**
   - 单条命令可以移动到不同分组
   - 不能移动到当前所在分组
   - 移动后原分组不再显示该命令
   - 支持拖拽方式移动

2. **批量操作**
   - 可以进入/退出批量模式
   - 可以全选/取消全选
   - 批量删除需要确认对话框
   - 批量移动可以选择目标分组
   - 批量操作后列表正确刷新

3. **常用命令**
   - 显示最近常用的10条命令
   - 列表显示分组信息
   - 执行命令后常用命令列表自动更新
   - 排序符合预期（使用频率+时间）

## 验收标准

- [ ] 可以右键移动单条命令到指定分组
- [ ] 支持拖拽移动命令
- [ ] 支持批量选择和批量删除
- [ ] 支持批量移动多条命令
- [ ] "常用命令"分组显示最近常用的10条命令
- [ ] 常用命令列表显示所属分组信息
- [ ] 所有操作在单文件发布模式下正常工作

## 依赖关系

- 依赖批次1完成（共享命令列表UI）
- 依赖 `CommandService` 和 `GroupService`
- 可能需要更新数据库索引优化查询

## 风险评估

| 风险 | 等级 | 缓解措施 |
|------|------|----------|
| 批量操作数据一致性 | 中 | 使用数据库事务 |
| 常用命令算法效果 | 低 | 可调整权重参数 |
| 拖拽功能实现复杂度 | 低 | 使用WPF原生拖拽支持 |
