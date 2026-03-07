# 批次1：命令列表增强功能设计文档

## 概述

本批次处理3个相关的命令列表功能增强issue，提升用户在浏览和搜索命令时的体验。

## 关联Issue

| Issue | 标题 | 描述 |
|-------|------|------|
| #1 | 批处理文件排序 | 按名称、上次运行时间、运行次数进行升序/降序排序 |
| #4 | 命令搜索分组筛选 | 搜索时在当前选中的分组下进行 |
| #5 | 回车键搜索支持 | 输入搜索文本后按回车键触发搜索 |

## 设计决策

### 1. 排序功能 (#1)

**位置**：命令列表工具栏

**UI设计**：
```
[排序下拉框] ▼  [升序/降序切换] ↕
```

- 排序选项：名称、上次运行时间、运行次数
- 默认：名称升序
- 状态持久化：保存用户上次选择的排序方式到设置

**实现要点**：
- 在 `CommandListViewModel` 中添加 `SortOption` 和 `SortDirection` 属性
- 使用 `CollectionViewSource` 或内存中排序
- 排序状态改变时刷新列表

### 2. 分组筛选搜索 (#4)

**行为**：
- 搜索框输入文本时，同时考虑当前选中的分组
- 如果选中的是虚拟分组"所有命令"，则搜索全部
- 如果选中的是具体分组，则只在该分组下搜索

**实现要点**：
- 修改搜索逻辑，传入当前选中分组ID
- 数据库查询添加 `WHERE GroupId = @groupId` 条件（当groupId > 0时）
- 实时搜索（输入即搜）保持不变

### 3. 回车键搜索支持 (#5)

**行为**：
- 搜索框获得焦点时，按回车键立即触发搜索
- 支持 `KeyDown` 事件监听 `Enter` 键
- 不干扰现有的实时搜索功能

**实现要点**：
- 在搜索框XAML中添加 `KeyDown` 事件处理
- 或者直接使用 `TextBox` 的 `AcceptsReturn="False"` 配合命令

## 数据模型变更

### Command 模型
确保已有字段：
- `Name`: string - 命令名称
- `GroupId`: int - 所属分组ID
- `LastExecutedAt`: DateTime? - 上次执行时间
- `ExecutionCount`: int - 执行次数

### 新增：用户设置
```csharp
public class UserSettings
{
    public SortOption DefaultSortOption { get; set; } = SortOption.Name;
    public SortDirection DefaultSortDirection { get; set; } = SortDirection.Ascending;
}

public enum SortOption { Name, LastExecutedAt, ExecutionCount }
public enum SortDirection { Ascending, Descending }
```

## UI布局变更

### MainWindow.xaml - 命令列表区域

```xml
<!-- 工具栏区域 -->
<ToolPanel>
    <!-- 搜索框 -->
    <hc:SearchBar x:Name="SearchTextBox"
                  KeyDown="SearchTextBox_KeyDown"
                  TextChanged="SearchTextBox_TextChanged"/>

    <!-- 排序控件 -->
    <ComboBox x:Name="SortComboBox"
              ItemsSource="{Binding SortOptions}"
              SelectedItem="{Binding SelectedSortOption}"/>

    <ToggleButton x:Name="SortDirectionButton"
                  IsChecked="{Binding IsDescending}"
                  Content="{Binding SortDirectionIcon}"/>
</ToolPanel>

<!-- 命令列表 -->
<DataGrid ItemsSource="{Binding FilteredCommands}"
          Sorting="DataGrid_Sorting"/>
```

## 交互流程

```
用户选择分组
    ↓
[如果是具体分组] 加载该分组命令
[如果是"所有命令"] 加载全部命令
    ↓
应用当前排序设置
    ↓
显示命令列表
    ↓
用户输入搜索文本
    ↓
实时过滤 + 按回车确认
    ↓
在当前分组范围内显示搜索结果
```

## 测试要点

1. **排序功能**
   - 切换排序选项后列表正确排序
   - 切换排序方向后列表正确排序
   - 排序设置重启后保持

2. **分组筛选**
   - 选中具体分组时搜索只返回该分组结果
   - 选中"所有命令"时搜索返回全部结果
   - 切换分组时搜索自动刷新

3. **回车搜索**
   - 搜索框按回车触发搜索
   - 不干扰实时搜索
   - 空搜索时显示全部命令

## 验收标准

- [ ] 用户可以按名称/运行时间/运行次数排序
- [ ] 用户可以切换升序/降序
- [ ] 搜索时自动限制在当前选中的分组
- [ ] 按回车键可以触发搜索
- [ ] 所有功能在单文件发布模式下正常工作

## 依赖关系

- 依赖现有 `CommandService` 和 `GroupService`
- 可能需要新增 `ISettingsService` 接口
- 复用现有的 `CommandListViewModel`

## 风险评估

| 风险 | 等级 | 缓解措施 |
|------|------|----------|
| 排序性能（大量命令时） | 低 | 使用数据库排序或延迟加载 |
| 搜索与分组筛选的组合逻辑 | 低 | 清晰的条件判断逻辑 |
| 设置持久化 | 低 | 使用现有的JSON设置存储 |
