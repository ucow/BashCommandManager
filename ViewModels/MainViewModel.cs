using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BashCommandManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private GroupTreeViewModel _groupTreeViewModel;

    [ObservableProperty]
    private CommandListViewModel _commandListViewModel;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private string _statusText = "就绪";

    public MainViewModel(
        GroupTreeViewModel groupTreeViewModel,
        CommandListViewModel commandListViewModel)
    {
        _groupTreeViewModel = groupTreeViewModel;
        _commandListViewModel = commandListViewModel;

        _groupTreeViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GroupTreeViewModel.SelectedGroup))
            {
                LoadCommandsForGroup();
            }
        };
    }

    private async void LoadCommandsForGroup()
    {
        if (GroupTreeViewModel.SelectedGroup != null)
        {
            await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            if (GroupTreeViewModel.SelectedGroup != null)
            {
                await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
            }
        }
        else
        {
            await CommandListViewModel.SearchAsync(SearchKeyword);
        }
        UpdateStatus();
    }

    [RelayCommand]
    private async Task ImportCommandAsync()
    {
        if (GroupTreeViewModel.SelectedGroup == null) return;

        await CommandListViewModel.ImportCommandAsync(GroupTreeViewModel.SelectedGroup.Id);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var groupName = GroupTreeViewModel.SelectedGroup?.Name ?? "无";
        var count = CommandListViewModel.Commands.Count;
        StatusText = $"当前分组: {groupName} | 命令数: {count}";
    }
}
