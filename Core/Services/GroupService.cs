using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;

namespace BashCommandManager.Core.Services;

public interface IGroupService
{
    Task<List<Group>> GetGroupTreeAsync();
    Task<Group> CreateGroupAsync(string name, int? parentId);
    Task RenameGroupAsync(int id, string newName);
    Task DeleteGroupAsync(int id, bool cascade = false);
    Task MoveGroupAsync(int id, int? newParentId);
}

public class GroupService : IGroupService
{
    private readonly IGroupRepository _repository;

    public GroupService(IGroupRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<Group>> GetGroupTreeAsync()
    {
        var allGroups = await _repository.GetAllAsync();
        return BuildTree(allGroups.ToList(), null);
    }

    private List<Group> BuildTree(List<Group> groups, int? parentId)
    {
        var result = groups.Where(g => g.ParentId == parentId).ToList();
        foreach (var group in result)
        {
            var children = BuildTree(groups, group.Id);
            group.Children.Clear();
            foreach (var child in children)
            {
                group.Children.Add(child);
            }
        }
        return result;
    }

    public async Task<Group> CreateGroupAsync(string name, int? parentId)
    {
        var siblings = await _repository.GetChildrenAsync(parentId);
        var group = new Group
        {
            Name = name,
            ParentId = parentId,
            SortOrder = siblings.Count()
        };
        group.Id = await _repository.CreateAsync(group);
        return group;
    }

    public async Task RenameGroupAsync(int id, string newName)
    {
        var group = await _repository.GetByIdAsync(id);
        if (group != null)
        {
            group.Name = newName;
            await _repository.UpdateAsync(group);
        }
    }

    public async Task DeleteGroupAsync(int id, bool cascade = false)
    {
        // SQLite 外键约束会处理级联删除
        await _repository.DeleteAsync(id);
    }

    public async Task MoveGroupAsync(int id, int? newParentId)
    {
        var group = await _repository.GetByIdAsync(id);
        if (group != null)
        {
            group.ParentId = newParentId;
            await _repository.UpdateAsync(group);
        }
    }
}
