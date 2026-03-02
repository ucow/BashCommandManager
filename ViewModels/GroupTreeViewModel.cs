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

    public async Task LoadGroupsAsync()
    {
        var groups = await _groupService.GetGroupTreeAsync();
        Groups = new ObservableCollection<Group>(groups);
    }

    [RelayCommand]
    private async Task CreateGroupAsync(int? parentId)
    {
        var dialog = new InputDialogControl
        {
            Prompt = "请输入分组名称："
        };

        var result = await Dialog.Show(dialog).GetResultAsync<string>();
        if (result is string name && !string.IsNullOrWhiteSpace(name))
        {
            if (await CheckGroupNameExistsAsync(name, parentId))
            {
                Growl.Warning("该分组名称已存在");
                return;
            }

            var group = await _groupService.CreateGroupAsync(name.Trim(), parentId);
            await LoadGroupsAsync();
            Growl.Success("分组创建成功");
        }
    }

    [RelayCommand]
    private void RenameGroupAsync(Group group)
    {
        if (group == null) return;
        group.IsEditing = true;
    }

    [RelayCommand]
    private async Task FinishRenameAsync(string newName)
    {
        if (SelectedGroup == null) return;

        var group = SelectedGroup;

        if (string.IsNullOrWhiteSpace(newName))
        {
            Growl.Warning("分组名称不能为空");
            group.Name = await GetOriginalGroupNameAsync(group.Id);
            return;
        }

        if (newName.Trim() == group.Name)
        {
            return;
        }

        if (await CheckGroupNameExistsAsync(newName, group.ParentId, group.Id))
        {
            Growl.Warning("该分组名称已存在");
            group.Name = await GetOriginalGroupNameAsync(group.Id);
            return;
        }

        await _groupService.RenameGroupAsync(group.Id, newName.Trim());
        await LoadGroupsAsync();
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

        var result = System.Windows.MessageBox.Show(message, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _groupService.DeleteGroupAsync(group.Id);
            await LoadGroupsAsync();
            Growl.Success("删除成功");
        }
    }

    private async Task<bool> CheckGroupNameExistsAsync(string name, int? parentId, int? excludeId = null)
    {
        var groups = await _groupService.GetGroupTreeAsync();
        var siblings = GetSiblings(groups, parentId);
        return siblings.Any(g => g.Name == name && g.Id != excludeId);
    }

    private List<Group> GetSiblings(List<Group> groups, int? parentId)
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

    private string? FindGroupName(List<Group> groups, int id)
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
