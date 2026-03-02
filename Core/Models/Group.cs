namespace BashCommandManager.Core.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    // 导航属性
    public List<Group> Children { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
}
