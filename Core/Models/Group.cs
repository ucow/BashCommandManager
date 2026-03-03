using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BashCommandManager.Core.Models;

public partial class Group : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    [ObservableProperty]
    private bool _isEditing;

    // 导航属性
    [ObservableProperty]
    private ObservableCollection<Group> _children = new();

    public List<Command> Commands { get; set; } = new();
}
