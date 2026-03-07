# 常用命令功能实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将"全部命令"改为"常用命令"，显示执行次数最多的10个命令，并展示命令所属分组。

**Architecture:** 通过给 Commands 表添加 UsageCount 字段记录执行次数，新增 CommandWithGroupDto 返回命令及分组信息，修改虚拟节点名称和加载逻辑。

**Tech Stack:** WPF, SQLite, Dapper, CommunityToolkit.Mvvm

---

### Task 1: 数据库结构更新

**Files:**
- Modify: `Infrastructure/DatabaseInitializer.cs:37-58`

**Step 1: 添加 ALTER TABLE 语句更新 Commands 表**

在 `Initialize()` 方法的 `CREATE INDEX` 之后添加字段更新：

```csharp
// 添加新字段（如果不存在）
var alterTableSql = @"
    ALTER TABLE Commands ADD COLUMN UsageCount INTEGER DEFAULT 0;
    ALTER TABLE Commands ADD COLUMN LastUsedAt DATETIME;
";

try
{
    using var alterCmd = new SQLiteCommand(alterTableSql, connection);
    alterCmd.ExecuteNonQuery();
}
catch (SQLiteException)
{
    // 字段已存在时会抛出异常，忽略
}
```

**Step 2: 验证数据库初始化**

运行应用检查是否正常启动无错误。

**Step 3: Commit**

```bash
git add Infrastructure/DatabaseInitializer.cs
git commit -m "feat: 添加 Commands 表 UsageCount 和 LastUsedAt 字段"
```

---

### Task 2: 更新 Command 模型

**Files:**
- Modify: `Core/Models/Command.cs:11-22`

**Step 1: 添加新属性**

在 `Command` 类中添加：

```csharp
public int UsageCount { get; set; }
public DateTime? LastUsedAt { get; set; }
```

**Step 2: Commit**

```bash
git add Core/Models/Command.cs
git commit -m "feat: Command 模型添加 UsageCount 和 LastUsedAt 属性"
```

---

### Task 3: 创建 CommandWithGroupDto

**Files:**
- Create: `Core/Models/CommandWithGroupDto.cs`

**Step 1: 创建 DTO 类**

```csharp
namespace BashCommandManager.Core.Models;

public class CommandWithGroupDto : Command
{
    public string GroupName { get; set; } = string.Empty;
}
```

**Step 2: Commit**

```bash
git add Core/Models/CommandWithGroupDto.cs
git commit -m "feat: 添加 CommandWithGroupDto 用于返回命令及分组信息"
```

---

### Task 4: 更新 ICommandService 接口

**Files:**
- Modify: `Core/Services/ICommandService.cs`

**Step 1: 添加新方法定义**

```csharp
Task<IEnumerable<CommandWithGroupDto>> GetFrequentlyUsedAsync(int limit = 10);
Task IncrementUsageAsync(int commandId);
```

**Step 2: Commit**

```bash
git add Core/Services/ICommandService.cs
git commit -m "feat: ICommandService 添加常用命令相关接口方法"
```

---

### Task 5: 实现 CommandService 新方法

**Files:**
- Modify: `Core/Services/CommandService.cs`

**Step 1: 添加 GetFrequentlyUsedAsync 实现**

```csharp
public async Task<IEnumerable<CommandWithGroupDto>> GetFrequentlyUsedAsync(int limit = 10)
{
    const string sql = @"
        SELECT c.*, g.Name as GroupName
        FROM Commands c
        JOIN Groups g ON c.GroupId = g.Id
        WHERE c.UsageCount > 0
        ORDER BY c.UsageCount DESC
        LIMIT @Limit";

    using var connection = _connectionFactory();
    return await connection.QueryAsync<CommandWithGroupDto>(sql, new { Limit = limit });
}
```

**Step 2: 添加 IncrementUsageAsync 实现**

```csharp
public async Task IncrementUsageAsync(int commandId)
{
    const string sql = @"
        UPDATE Commands
        SET UsageCount = UsageCount + 1,
            LastUsedAt = @Now
        WHERE Id = @Id";

    using var connection = _connectionFactory();
    await connection.ExecuteAsync(sql, new { Id = commandId, Now = DateTime.Now });
}
```

**Step 3: Commit**

```bash
git add Core/Services/CommandService.cs
git commit -m "feat: 实现常用命令查询和使用次数统计功能"
```

---

### Task 6: 修改 CommandExecutor 记录使用

**Files:**
- Modify: `Core/Services/CommandExecutor.cs`

**Step 1: 注入 ICommandService**

修改构造函数添加依赖：

```csharp
private readonly ICommandService _commandService;

public CommandExecutor(ICommandService commandService)
{
    _commandService = commandService;
}
```

**Step 2: 在执行后增加计数**

在 `Execute` 方法成功启动进程后（约第 45 行）：

```csharp
// 增加使用计数
await _commandService.IncrementUsageAsync(command.Id);
```

**Step 3: Commit**

```bash
git add Core/Services/CommandExecutor.cs
git commit -m "feat: 命令执行后自动增加使用次数"
```

---

### Task 7: 修改 GroupTreeViewModel 节点名称

**Files:**
- Modify: `ViewModels/GroupTreeViewModel.cs:37-42`

**Step 1: 修改虚拟节点名称**

```csharp
var allCommandsNode = new Group
{
    Id = 0,
    Name = "常用命令",
    IsVirtual = true
};
```

**Step 2: Commit**

```bash
git add ViewModels/GroupTreeViewModel.cs
git commit -m "feat: 将虚拟节点名称从'全部命令'改为'常用命令'"
```

---

### Task 8: 更新 DI 注册

**Files:**
- Modify: `App.xaml.cs`

**Step 1: 更新 CommandExecutor 注册**

确保 `CommandExecutor` 的 DI 注册包含 `ICommandService` 依赖：

```csharp
services.AddSingleton<ICommandExecutor, CommandExecutor>();
```

注意：如果已经是 Singleton 作用域，确保顺序正确（ICommandService 在 ICommandExecutor 之前注册）。

**Step 2: Commit**

```bash
git add App.xaml.cs
git commit -m "chore: 确保 CommandExecutor DI 注册正确"
```

---

### Task 9: 修改 MainViewModel 加载逻辑

**Files:**
- Modify: `ViewModels/MainViewModel.cs:38-54`

**Step 1: 修改 LoadCommandsForGroup 方法**

```csharp
private async void LoadCommandsForGroup()
{
    if (GroupTreeViewModel.SelectedGroup == null)
        return;

    if (GroupTreeViewModel.SelectedGroup.Id == 0 || GroupTreeViewModel.SelectedGroup.IsVirtual)
    {
        // 加载常用命令
        await CommandListViewModel.LoadFrequentlyUsedAsync();
    }
    else
    {
        // 加载特定分组命令
        await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
    }
    UpdateStatus();
}
```

**Step 2: 修改 SearchAsync 中的相同逻辑**

在 `SearchAsync` 方法中（约第 61-70 行）做相同修改。

**Step 3: Commit**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat: MainViewModel 支持加载常用命令"
```

---

### Task 10: 修改 CommandListViewModel

**Files:**
- Modify: `ViewModels/CommandListViewModel.cs`

**Step 1: 添加 LoadFrequentlyUsedAsync 方法**

```csharp
public async Task LoadFrequentlyUsedAsync()
{
    var commands = await _commandService.GetFrequentlyUsedAsync(10);
    Commands = new ObservableCollection<Command>(commands);
}
```

**Step 2: Commit**

```bash
git add ViewModels/CommandListViewModel.cs
git commit -m "feat: CommandListViewModel 添加加载常用命令方法"
```

---

### Task 11: 更新 MainWindow.xaml 列表模板

**Files:**
- Modify: `MainWindow.xaml`

**Step 1: 找到命令列表的 ItemTemplate**

查找 `ListBox` 或 `ItemsControl` 的 `ItemTemplate`。

**Step 2: 修改模板添加分组标签**

在模板中添加分组名显示：

```xml
<Border Style="{StaticResource TagBorderStyle}"
        Background="{DynamicResource SecondaryRegionBrush}"
        CornerRadius="4"
        Padding="4,2"
        HorizontalAlignment="Right">
    <TextBlock Text="{Binding GroupName}"
               FontSize="11"
               Foreground="{DynamicResource SecondaryTextBrush}"/>
</Border>
```

或者如果模板中无法直接访问 GroupName（当类型是 Command 而非 CommandWithGroupDto 时），需要添加类型检查或转换。

**注意**：如果 Commands 集合类型是 `ObservableCollection<Command>`，而 `CommandWithGroupDto` 继承自 `Command`，则需要确保绑定能正确解析 GroupName。

**Step 3: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: 命令列表项显示所属分组"
```

---

### Task 12: 添加空状态提示

**Files:**
- Modify: `MainWindow.xaml`
- Modify: `CommandListViewModel.cs`

**Step 1: 添加 IsEmpty 属性到 ViewModel**

```csharp
public bool IsEmpty => Commands.Count == 0;
```

并在 Commands 属性变更时触发 `OnPropertyChanged(nameof(IsEmpty))`。

**Step 2: 在 XAML 中添加空状态显示**

```xml
<Grid>
    <!-- 原有列表 -->
    <ListBox ... Visibility="{Binding IsEmpty, Converter={StaticResource BooleanToVisibilityConverter}, ConverterParameter=Inverted}"/>

    <!-- 空状态提示 -->
    <TextBlock Text="暂无常用命令，请开始使用命令吧"
               HorizontalAlignment="Center"
               VerticalAlignment="Center"
               Foreground="{DynamicResource SecondaryTextBrush}"
               Visibility="{Binding IsEmpty, Converter={StaticResource BooleanToVisibilityConverter}}"/>
</Grid>
```

注意：可能需要添加反向布尔转换器，或使用 `!IsEmpty` 语法。

**Step 3: Commit**

```bash
git add MainWindow.xaml ViewModels/CommandListViewModel.cs
git commit -m "feat: 常用命令列表添加空状态提示"
```

---

### Task 13: 测试验证

**Files:**
- 所有修改的文件

**Step 1: 构建项目**

```bash
dotnet build -c Release
```

**Step 2: 运行应用测试**

1. 启动应用，检查"常用命令"节点名称是否正确
2. 执行几个命令，检查是否报错
3. 点击"常用命令"，检查是否显示刚执行的命令
4. 检查分组标签是否正确显示
5. 清空数据后检查空状态提示是否显示

**Step 3: Commit（如有修复）**

```bash
git commit -m "fix: 修复测试中发现的问题"
```

---

## 实施检查清单

- [ ] Commands 表有 UsageCount 和 LastUsedAt 字段
- [ ] Command 模型有新属性
- [ ] CommandWithGroupDto 已创建
- [ ] ICommandService 有新方法
- [ ] CommandService 已实现新方法
- [ ] CommandExecutor 执行后增加计数
- [ ] 虚拟节点名称为"常用命令"
- [ ] MainViewModel 加载常用命令逻辑正确
- [ ] CommandListViewModel 有 LoadFrequentlyUsedAsync 方法
- [ ] 命令列表显示分组标签
- [ ] 空状态有提示
- [ ] 应用正常启动运行
