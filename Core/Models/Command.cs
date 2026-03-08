using CommunityToolkit.Mvvm.ComponentModel;

namespace BashCommandManager.Core.Models;

public enum CommandStatus
{
    Idle,
    Running,
    Completed,
    Failed
}

public partial class Command : ObservableObject
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
    [ObservableProperty]
    private CommandStatus _status = CommandStatus.Idle;

    // 批量选择状态（不持久化）
    [ObservableProperty]
    private bool _isSelected;
}
