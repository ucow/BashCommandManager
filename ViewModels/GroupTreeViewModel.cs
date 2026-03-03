using BashCommandManager.Controls;
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Tools.Extension;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager.ViewModels;

public partial class GroupTreeViewModel : ObservableObject
{
    private readonly IGroupService _groupService;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private ObservableCollection<Group> _groups = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    public GroupTreeViewModel(IGroupService groupService, ICommandService commandService)
    {
        _groupService = groupService;
        _commandService = commandService;
    }

    public async Task LoadGroupsAsync(bool incremental = true)
    {
        var groups = await _groupService.GetGroupTreeAsync();

        if (Groups.Count == 0 || !incremental)
        {
            // 首次加载或非增量刷新：重建整个集合
            Groups = new ObservableCollection<Group>(groups);
        }
        else
        {
            // 增量更新：同步树结构
            SyncGroupTree(Groups, groups);
        }
    }

    /// <summary>
    /// 递归同步树结构，增量更新集合以保持展开状态
    /// </summary>
    private void SyncGroupTree(ObservableCollection<Group> current, List<Group> fresh)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        if (fresh == null) throw new ArgumentNullException(nameof(fresh));

        // 标记新数据中的所有节点ID
        var freshIds = new HashSet<int>(fresh.Select(g => g.Id));

        // 删除在新数据中不存在的节点
        var toRemove = current.Where(g => !freshIds.Contains(g.Id)).ToList();
        foreach (var group in toRemove)
        {
            current.Remove(group);
        }

        // 处理新增和更新的节点，保持与 fresh 列表相同的顺序
        int insertIndex = 0;
        foreach (var freshGroup in fresh)
        {
            var existingGroup = current.FirstOrDefault(g => g.Id == freshGroup.Id);

            if (existingGroup == null)
            {
                // 新增节点
                var newGroup = new Group
                {
                    Id = freshGroup.Id,
                    Name = freshGroup.Name,
                    ParentId = freshGroup.ParentId,
                    SortOrder = freshGroup.SortOrder
                };

                if (insertIndex >= current.Count)
                {
                    current.Add(newGroup);
                }
                else
                {
                    current.Insert(insertIndex, newGroup);
                }
                existingGroup = newGroup;
            }
            else
            {
                // 更新现有节点（保持对象引用）
                existingGroup.Name = freshGroup.Name;
                existingGroup.SortOrder = freshGroup.SortOrder;
                existingGroup.ParentId = freshGroup.ParentId;

                // 如果节点位置不对，移动到正确位置
                int currentIndex = current.IndexOf(existingGroup);
                if (currentIndex != insertIndex)
                {
                    current.Move(currentIndex, insertIndex);
                }
            }

            // 递归同步子节点
            SyncGroupTree(existingGroup.Children, freshGroup.Children.ToList());

            insertIndex++;
        }
    }

    [RelayCommand]
    private async Task CreateGroupAsync(object? parameter)
    {
        try
        {
            int? parentId = parameter as int? ?? (parameter is int id ? id : null);
            var dialog = new InputDialogControl
            {
                Prompt = "请输入分组名称："
            };
            dialog.DataContext = dialog; // 设置 DataContext 为控件本身，因为它实现了 IDialogResultable<string>

            var result = await Dialog.Show(dialog).GetResultAsync<string>();
            if (!string.IsNullOrEmpty(result))  // 用户点击了确定
            {
                var name = result;
                if (string.IsNullOrWhiteSpace(name))
                {
                    Growl.Warning("分组名称不能为空");
                    return;
                }

                if (await CheckGroupNameExistsAsync(name, parentId))
                {
                    Growl.Warning("该分组名称已存在");
                    return;
                }

                var group = await _groupService.CreateGroupAsync(name.Trim(), parentId);
                await LoadGroupsAsync(incremental: true);
                Growl.Success("分组创建成功");
            }
            // result == null 表示用户取消了，不需要处理
        }
        catch (Exception ex)
        {
            Growl.Error($"创建分组时发生错误：{ex.Message}");
        }
    }

    [RelayCommand]
    private void StartRenameGroup(Group group)
    {
        if (group == null) return;
        group.IsEditing = true;
    }

    [RelayCommand]
    private async Task FinishRenameAsync(Group group)
    {
        if (group == null) return;

        var newName = group.Name;

        if (string.IsNullOrWhiteSpace(newName))
        {
            Growl.Warning("分组名称不能为空");
            group.Name = await GetOriginalGroupNameAsync(group.Id);
            return;
        }

        var originalName = await GetOriginalGroupNameAsync(group.Id);
        if (newName.Trim() == originalName)
        {
            return;
        }

        if (await CheckGroupNameExistsAsync(newName, group.ParentId, group.Id))
        {
            Growl.Warning("该分组名称已存在");
            group.Name = originalName;
            return;
        }

        await _groupService.RenameGroupAsync(group.Id, newName.Trim());
        await LoadGroupsAsync(incremental: true);
        Growl.Success("重命名成功");
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(Group group)
    {
        if (group == null) return;

        var commands = await _commandService.GetByGroupAsync(group.Id);
        var commandCount = commands.Count();

        string message = commandCount > 0
            ? $"分组 \"{group.Name}\" 下有 {commandCount} 个命令，确定要删除吗？\n删除后这些命令也将被删除。"
            : $"确定要删除分组 \"{group.Name}\" 吗？";

        var dialog = new ConfirmDialogControl
        {
            Title = "确认删除",
            Message = message
        };
        dialog.DataContext = dialog;

        var result = await Dialog.Show(dialog).GetResultAsync<bool>();

        if (result)
        {
            await _groupService.DeleteGroupAsync(group.Id);
            await LoadGroupsAsync(incremental: true);
            Growl.Success("删除成功");
        }
    }

    private async Task<bool> CheckGroupNameExistsAsync(string name, int? parentId, int? excludeId = null)
    {
        var groups = await _groupService.GetGroupTreeAsync();
        var siblings = GetSiblings(groups, parentId);
        return siblings.Any(g => g.Name == name && g.Id != excludeId);
    }

    private List<Group> GetSiblings(IList<Group> groups, int? parentId)
    {
        var result = new List<Group>();
        foreach (var group in groups)
        {
            if (group.ParentId == parentId)
            {
                result.Add(group);
            }
            result.AddRange(GetSiblings(group.Children, group.Id));
        }
        return result;
    }

    private async Task<string> GetOriginalGroupNameAsync(int id)
    {
        var groups = await _groupService.GetGroupTreeAsync();
        return FindGroupName(groups, id) ?? string.Empty;
    }

    private string? FindGroupName(IList<Group> groups, int id)
    {
        foreach (var group in groups)
        {
            if (group.Id == id)
            {
                return group.Name;
            }
            var found = FindGroupName(group.Children, id);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
