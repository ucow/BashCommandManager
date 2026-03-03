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
                await LoadGroupsAsync();
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
