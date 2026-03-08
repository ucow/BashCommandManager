using System.ComponentModel.DataAnnotations;

namespace BashCommandManager.Core.Models;

public enum SortOption
{
    [Display(Name = "名称")]
    Name,

    [Display(Name = "最近执行时间")]
    LastExecutedAt,

    [Display(Name = "执行次数")]
    ExecutionCount
}

public enum SortDirection
{
    Ascending,
    Descending
}
