using CommunityToolkit.Mvvm.ComponentModel;

namespace BashCommandManager.Core.Models;

public partial class Group : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    [ObservableProperty]
    private bool _isEditing;

    // 导航属性
    public List<Group> Children { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
}
