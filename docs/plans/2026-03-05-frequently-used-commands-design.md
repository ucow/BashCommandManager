# 常用命令功能设计文档

## 概述
将"全部命令"虚拟节点改为"常用命令"，显示按执行次数排序的最近10个命令。

## 需求
1. 将分组树中的"全部命令"改为"常用命令"
2. 选中常用命令后，显示执行次数最多的10个命令
3. 显示命令信息时，同时展示命令所在的分组

## 设计方案

### 1. 数据库变更

修改 `Commands` 表，添加两个字段：
- `UsageCount` INTEGER DEFAULT 0 - 命令执行次数
- `LastUsedAt` DATETIME - 最后使用时间（为将来扩展保留）

### 2. 数据模型变更

**Command.cs**
```csharp
public class Command
{
    // ... 现有字段
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

**新增: CommandWithGroupDto.cs**
```csharp
public class CommandWithGroupDto : Command
{
    public string GroupName { get; set; } = string.Empty;
}
```

### 3. 业务逻辑

**数据库初始化** (`DatabaseInitializer.cs`)
- 添加 ALTER TABLE 语句更新现有表结构
- 初始化新字段值为 0 和 null

**ICommandService** 新增方法：
- `Task<IEnumerable<CommandWithGroupDto>> GetFrequentlyUsedAsync(int limit = 10)` - 获取常用命令
- `Task IncrementUsageAsync(int commandId)` - 增加命令使用次数

**CommandExecutor**
- 执行命令后调用 `_commandService.IncrementUsageAsync(commandId)` 增加计数

### 4. UI 变更

**GroupTreeViewModel.cs**
- 修改虚拟节点名称："全部命令" → "常用命令"

**MainViewModel.cs**
- 修改 `LoadCommandsForGroup()` 逻辑：
  - 当 `Id == 0`（虚拟节点）时，调用 `GetFrequentlyUsedAsync()`
  - 如果返回空列表，显示提示信息

**MainWindow.xaml**
- 命令列表项模板更新，右侧显示分组名标签
- 空状态显示提示文本

### 5. 数据流

```
用户执行命令
    ↓
CommandExecutor.Execute()
    ↓
_commandService.IncrementUsageAsync(commandId)
    ↓
UPDATE Commands SET UsageCount = UsageCount + 1, LastUsedAt = @now WHERE Id = @id

用户点击"常用命令"
    ↓
LoadCommandsForGroup() 检测到 Id == 0
    ↓
GetFrequentlyUsedAsync(10)
    ↓
SELECT c.*, g.Name as GroupName
FROM Commands c
JOIN Groups g ON c.GroupId = g.Id
WHERE c.UsageCount > 0
ORDER BY c.UsageCount DESC
LIMIT 10
```

### 6. 空状态处理

当常用命令列表为空时，显示友好提示：
"暂无常用命令，请开始使用命令吧"

## 决策记录

- **统计逻辑**：按执行次数最多的10个
- **数据库方案**：给 Commands 表添加 UsageCount 和 LastUsedAt 字段
- **UI展示**：列表项右侧标签显示分组名
