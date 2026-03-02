using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace BashCommandManager.ViewModels;

public partial class GroupTreeViewModel : ObservableObject
{
    private readonly IGroupService _groupService;

    [ObservableProperty]
    private ObservableCollection<Group> _groups = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    public GroupTreeViewModel(IGroupService groupService)
    {
        _groupService = groupService;
    }

    public async Task LoadGroupsAsync()
    {
        var groups = await _groupService.GetGroupTreeAsync();
        Groups = new ObservableCollection<Group>(groups);
    }

    [RelayCommand]
    private async Task CreateGroupAsync(int? parentId)
    {
        var name = $"新建分组_{DateTime.Now:HHmmss}";
        var group = await _groupService.CreateGroupAsync(name, parentId);
        await LoadGroupsAsync();
    }

    [RelayCommand]
    private async Task RenameGroupAsync(int id)
    {
        var newName = $"重命名_{DateTime.Now:HHmmss}";
        await _groupService.RenameGroupAsync(id, newName);
        await LoadGroupsAsync();
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(int id)
    {
        await _groupService.DeleteGroupAsync(id);
        await LoadGroupsAsync();
    }
}
