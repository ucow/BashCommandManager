# 批次1：命令列表增强功能实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** 实现命令列表排序、分组筛选搜索、回车键搜索支持功能

**Architecture:** 在 CommandListViewModel 中添加排序和筛选逻辑，修改数据库查询支持分组条件，使用 WPF KeyDown 事件处理回车键

**Tech Stack:** WPF, CommunityToolkit.Mvvm, SQLite, Dapper, HandyControl

---

## 前置条件

确保已阅读：
- `docs/plans/2026-03-07-batch1-command-list-enhancement-design.md` - 本批次设计文档
- `ViewModels/CommandListViewModel.cs` - 命令列表视图模型
- `Core/Services/CommandService.cs` - 命令服务
- `Core/Repositories/CommandRepository.cs` - 命令数据访问

---

## 数据库变更

### Task 1: 添加命令使用统计字段

**Files:**
- Modify: `Infrastructure/DatabaseInitializer.cs` (找到初始化表结构的方法)

**Step 1: 修改数据库初始化，添加新字段**

在 `DatabaseInitializer.cs` 中找到创建 Commands 表的 SQL，修改为：

```csharp
// 找到类似这样的代码块
var createCommandsTable = @"
    CREATE TABLE IF NOT EXISTS Commands (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL,
        Description TEXT,
        FilePath TEXT NOT NULL,
        GroupId INTEGER NOT NULL,
        SortOrder INTEGER DEFAULT 0,
        ExecutionCount INTEGER DEFAULT 0,        -- 新增：执行次数
        LastExecutedAt DATETIME,                  -- 新增：上次执行时间
        FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
    )";
```

**Step 2: 添加迁移逻辑（如果表已存在）**

在同一文件中添加列检查逻辑：

```csharp
// 在初始化方法中添加
await AddColumnIfNotExists("Commands", "ExecutionCount", "INTEGER DEFAULT 0");
await AddColumnIfNotExists("Commands", "LastExecutedAt", "DATETIME");
```

添加辅助方法：

```csharp
private async Task AddColumnIfNotExists(string table, string column, string type)
{
    var checkSql = $@"
        SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
    var exists = await _db.ExecuteScalarAsync<int>(checkSql);
    if (exists == 0)
    {
        await _db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type}");
    }
}
```

**Step 3: 提交数据库变更**

```bash
git add Infrastructure/DatabaseInitializer.cs
git commit -m "feat: 添加命令执行统计字段到数据库

- 添加 ExecutionCount 字段记录执行次数
- 添加 LastExecutedAt 字段记录上次执行时间
- 支持数据库迁移，兼容旧版本"
```

---

## 排序功能

### Task 2: 创建排序枚举和设置

**Files:**
- Create: `Core/Models/SortOptions.cs`

**Step 1: 编写排序选项枚举**

```csharp
namespace BashCommandManager.Core.Models;

public enum SortOption
{
    Name,
    LastExecutedAt,
    ExecutionCount
}

public enum SortDirection
{
    Ascending,
    Descending
}
```

**Step 2: 提交**

```bash
git add Core/Models/SortOptions.cs
git commit -m "feat: 添加排序选项枚举"
```

### Task 3: 修改 Command 模型

**Files:**
- Modify: `Core/Models/Command.cs`

**Step 1: 添加统计字段**

```csharp
public class Command
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int GroupId { get; set; }
    public int SortOrder { get; set; }

    // 新增：执行统计字段
    public int ExecutionCount { get; set; }
    public DateTime? LastExecutedAt { get; set; }

    // 运行时状态（不持久化）
    public CommandStatus Status { get; set; } = CommandStatus.Idle;
}
```

**Step 2: 提交**

```bash
git add Core/Models/Command.cs
git commit -m "feat: Command 模型添加执行统计字段"
```

### Task 4: 扩展 CommandRepository 支持排序

**Files:**
- Modify: `Core/Repositories/CommandRepository.cs`

**Step 1: 修改接口添加排序参数**

```csharp
public interface ICommandRepository
{
    Task<IEnumerable<Command>> GetByGroupIdAsync(int groupId);
    Task<Command?> GetByIdAsync(int id);
    Task<int> CreateAsync(Command command);
    Task UpdateAsync(Command command);
    Task DeleteAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
    Task<IEnumerable<Command>> GetAllAsync();

    // 新增：支持排序的查询
    Task<IEnumerable<Command>> GetByGroupIdWithSortAsync(int groupId, SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> GetAllWithSortAsync(SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> SearchWithSortAsync(string keyword, SortOption sortBy, SortDirection direction);

    // 新增：搜索时分组筛选
    Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction);
}
```

**Step 2: 实现排序查询方法**

```csharp
public async Task<IEnumerable<Command>> GetByGroupIdWithSortAsync(int groupId, SortOption sortBy, SortDirection direction)
{
    var orderBy = GetOrderByClause(sortBy, direction);
    var sql = $@"SELECT * FROM Commands WHERE GroupId = @GroupId {orderBy}";
    return await _db.QueryAsync<Command>(sql, new { GroupId = groupId });
}

public async Task<IEnumerable<Command>> GetAllWithSortAsync(SortOption sortBy, SortDirection direction)
{
    var orderBy = GetOrderByClause(sortBy, direction);
    var sql = $@"SELECT * FROM Commands {orderBy}";
    return await _db.QueryAsync<Command>(sql);
}

public async Task<IEnumerable<Command>> SearchWithSortAsync(string keyword, SortOption sortBy, SortDirection direction)
{
    var orderBy = GetOrderByClause(sortBy, direction);
    var sql = $@"
        SELECT * FROM Commands
        WHERE Name LIKE @Keyword OR Description LIKE @Keyword
        {orderBy}";
    return await _db.QueryAsync<Command>(sql, new { Keyword = $"%{keyword}%" });
}

public async Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction)
{
    var orderBy = GetOrderByClause(sortBy, direction);
    var sql = $@"
        SELECT * FROM Commands
        WHERE (Name LIKE @Keyword OR Description LIKE @Keyword)
        AND GroupId = @GroupId
        {orderBy}";
    return await _db.QueryAsync<Command>(sql, new { Keyword = $"%{keyword}%", GroupId = groupId });
}

private string GetOrderByClause(SortOption sortBy, SortDirection direction)
{
    var column = sortBy switch
    {
        SortOption.Name => "Name",
        SortOption.LastExecutedAt => "LastExecutedAt",
        SortOption.ExecutionCount => "ExecutionCount",
        _ => "Name"
    };
    var dir = direction == SortDirection.Ascending ? "ASC" : "DESC";
    return $"ORDER BY {column} {dir}";
}
```

**Step 3: 提交**

```bash
git add Core/Repositories/CommandRepository.cs
git commit -m "feat: CommandRepository 支持排序查询和分组筛选搜索"
```

### Task 5: 扩展 CommandService

**Files:**
- Modify: `Core/Services/CommandService.cs`

**Step 1: 修改接口**

```csharp
public interface ICommandService
{
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId);
    Task<IEnumerable<Command>> ImportCommandsAsync(int groupId);
    Task DeleteCommandAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
    Task<IEnumerable<Command>> GetAllAsync();

    // 新增：支持排序的方法
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId, SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> GetAllAsync(SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> SearchAsync(string keyword, SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction);
}
```

**Step 2: 实现新方法**

```csharp
public async Task<IEnumerable<Command>> GetByGroupAsync(int groupId, SortOption sortBy, SortDirection direction)
{
    return await _repository.GetByGroupIdWithSortAsync(groupId, sortBy, direction);
}

public async Task<IEnumerable<Command>> GetAllAsync(SortOption sortBy, SortDirection direction)
{
    return await _repository.GetAllWithSortAsync(sortBy, direction);
}

public async Task<IEnumerable<Command>> SearchAsync(string keyword, SortOption sortBy, SortDirection direction)
{
    return await _repository.SearchWithSortAsync(keyword, sortBy, direction);
}

public async Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction)
{
    return await _repository.SearchInGroupAsync(keyword, groupId, sortBy, direction);
}
```

**Step 3: 提交**

```bash
git add Core/Services/CommandService.cs
git commit -m "feat: CommandService 支持排序和分组筛选"
```

### Task 6: 在 CommandListViewModel 中添加排序功能

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 添加排序属性和字段**

在类开头添加：

```csharp
public partial class CommandListViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private readonly ICommandExecutor _executor;

    [ObservableProperty]
    private ObservableCollection<Command> _commands = new();

    // 新增：排序相关属性
    [ObservableProperty]
    private SortOption _currentSortOption = SortOption.Name;

    [ObservableProperty]
    private SortDirection _currentSortDirection = SortDirection.Ascending;

    // 新增：用于 UI 绑定的属性
    public IEnumerable<SortOption> SortOptions => Enum.GetValues<SortOption>();

    // 新增：当前搜索关键词（用于重新应用搜索）
    private string? _currentSearchKeyword;
    private int _currentGroupId = 0; // 0 表示所有命令

    // 构造函数保持不变...
```

**Step 2: 修改加载方法支持排序**

修改 `LoadCommandsAsync`：

```csharp
public async Task LoadCommandsAsync(int groupId)
{
    _currentGroupId = groupId;
    _currentSearchKeyword = null;
    var commands = await _commandService.GetByGroupAsync(groupId, CurrentSortOption, CurrentSortDirection);
    Commands = new ObservableCollection<Command>(commands);
}
```

修改 `LoadAllCommandsAsync`：

```csharp
public async Task LoadAllCommandsAsync()
{
    _currentGroupId = 0;
    _currentSearchKeyword = null;
    var commands = await _commandService.GetAllAsync(CurrentSortOption, CurrentSortDirection);
    Commands = new ObservableCollection<Command>(commands);
}
```

修改 `SearchAsync`：

```csharp
public async Task SearchAsync(string keyword)
{
    _currentSearchKeyword = keyword;
    IEnumerable<Command> commands;

    if (string.IsNullOrWhiteSpace(keyword))
    {
        // 空搜索时恢复到分组视图
        if (_currentGroupId == 0)
        {
            commands = await _commandService.GetAllAsync(CurrentSortOption, CurrentSortDirection);
        }
        else
        {
            commands = await _commandService.GetByGroupAsync(_currentGroupId, CurrentSortOption, CurrentSortDirection);
        }
    }
    else if (_currentGroupId == 0)
    {
        // 在所有命令中搜索
        commands = await _commandService.SearchAsync(keyword, CurrentSortOption, CurrentSortDirection);
    }
    else
    {
        // 在指定分组中搜索
        commands = await _commandService.SearchInGroupAsync(keyword, _currentGroupId, CurrentSortOption, CurrentSortDirection);
    }

    Commands = new ObservableCollection<Command>(commands);
}
```

**Step 3: 添加排序变更命令**

```csharp
[RelayCommand]
private async Task ApplySortAsync()
{
    // 重新加载当前视图以应用新排序
    if (!string.IsNullOrWhiteSpace(_currentSearchKeyword))
    {
        await SearchAsync(_currentSearchKeyword);
    }
    else if (_currentGroupId == 0)
    {
        await LoadAllCommandsAsync();
    }
    else
    {
        await LoadCommandsAsync(_currentGroupId);
    }
}

[RelayCommand]
private void ToggleSortDirection()
{
    CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
        ? SortDirection.Descending
        : SortDirection.Ascending;
    ApplySortCommand.ExecuteAsync(null);
}
```

**Step 4: 添加部分属性变更处理**

```csharp
partial void OnCurrentSortOptionChanged(SortOption value)
{
    ApplySortCommand.ExecuteAsync(null);
}
```

**Step 5: 提交**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: CommandListViewModel 添加排序功能支持

- 添加 CurrentSortOption 和 CurrentSortDirection 属性
- 修改加载和搜索方法支持排序
- 添加 ApplySort 和 ToggleSortDirection 命令"
```

### Task 7: 修改 MainWindow.xaml 添加排序 UI

**Files:**
- Modify: `MainWindow.xaml` (ToolBar 区域)

**Step 1: 添加排序控件**

找到 ToolBar 部分，在搜索框之前添加排序控件：

```xml
<ToolBar Grid.Row="0" Margin="5">
    <hc:ButtonGroup>
        <Button Content="导入命令"
                Command="{Binding ImportCommandCommand}"/>
    </hc:ButtonGroup>
    <Separator/>

    <!-- 新增：排序控件 -->
    <ComboBox Width="120"
              ItemsSource="{Binding CommandListViewModel.SortOptions}"
              SelectedItem="{Binding CommandListViewModel.CurrentSortOption}"
              hc:InfoElement.Placeholder="排序方式"/>

    <ToggleButton Width="30"
                  IsChecked="{Binding CommandListViewModel.IsDescending}"
                  Command="{Binding CommandListViewModel.ToggleSortDirectionCommand}"
                  Content="{Binding CommandListViewModel.SortDirectionIcon}"/>

    <Separator/>

    <TextBox x:Name="SearchBox"
             Width="200"
             hc:InfoElement.Placeholder="搜索命令..."
             Text="{Binding SearchKeyword, UpdateSourceTrigger=PropertyChanged}"
             KeyDown="SearchBox_KeyDown"/>
    <Button Content="搜索"
            Command="{Binding SearchCommand}"/>
</ToolBar>
```

**Step 2: 提交**

```bash
git add MainWindow.xaml
git commit -m "feat: 主界面添加排序控件"
```

---

## 回车键搜索功能

### Task 8: 实现回车键搜索

**Files:**
- Modify: `MainWindow.xaml.cs` (添加 KeyDown 事件处理)

**Step 1: 添加 KeyDown 事件处理**

```csharp
private void SearchBox_KeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Enter)
    {
        // 触发搜索命令
        if (DataContext is MainViewModel vm)
        {
            vm.SearchCommand.Execute(null);
        }
    }
}
```

**Step 2: 提交**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: 搜索框支持回车键触发搜索"
```

---

## 验证和测试

### Task 9: 构建和运行测试

**Step 1: 构建项目**

```bash
dotnet build
```

期望：无错误

**Step 2: 运行应用并测试**

```bash
dotnet run
```

**手动测试清单：**

- [ ] 排序下拉框显示正常（名称、上次运行时间、运行次数）
- [ ] 切换排序选项，列表正确排序
- [ ] 点击排序方向按钮切换升序/降序
- [ ] 选中具体分组时，搜索只返回该分组结果
- [ ] 选中"全部命令"时，搜索返回全部结果
- [ ] 搜索框输入文字后按回车触发搜索
- [ ] 空搜索时恢复显示所有命令

**Step 3: 修复任何问题**

如果有编译错误或运行时问题，修复后：

```bash
git add .
git commit -m "fix: 修复批次1功能的 bug"
```

---

## 批次1完成总结

完成后应有以下修改：

1. **数据库**：Commands 表添加 ExecutionCount 和 LastExecutedAt 字段
2. **模型**：Command.cs 添加统计字段，新增 SortOptions.cs 枚举
3. **数据访问**：CommandRepository 支持排序和分组筛选
4. **服务层**：CommandService 暴露排序接口
5. **视图模型**：CommandListViewModel 实现排序逻辑
6. **UI**：MainWindow.xaml 添加排序控件，支持回车搜索

提交所有更改：

```bash
git add .
git commit -m "feat: 批次1 - 命令列表增强功能完成

- 实现命令排序（名称/运行时间/执行次数）
- 实现分组筛选搜索
- 实现回车键搜索支持
- 所有功能测试通过"
```
