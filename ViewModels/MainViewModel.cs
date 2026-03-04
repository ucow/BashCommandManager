using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;

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
        if (GroupTreeViewModel.SelectedGroup == null)
            return;

        if (GroupTreeViewModel.SelectedGroup.Id == 0 || GroupTreeViewModel.SelectedGroup.IsVirtual)
        {
            // 加载所有命令
            await CommandListViewModel.LoadAllCommandsAsync();
        }
        else
        {
            // 加载特定分组命令
            await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
        }
        UpdateStatus();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            if (GroupTreeViewModel.SelectedGroup != null)
            {
                if (GroupTreeViewModel.SelectedGroup.Id == 0 || GroupTreeViewModel.SelectedGroup.IsVirtual)
                {
                    await CommandListViewModel.LoadAllCommandsAsync();
                }
                else
                {
                    await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
                }
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
        try
        {
            if (GroupTreeViewModel.SelectedGroup == null)
            {
                Growl.Warning(new GrowlInfo
                {
                    Message = "请先选择一个分组",
                    WaitTime = 3
                });
                return;
            }

            // 检查是否选择了虚拟节点
            if (GroupTreeViewModel.SelectedGroup.IsVirtual)
            {
                Growl.Warning(new GrowlInfo
                {
                    Message = "请选择一个具体分组来导入命令",
                    WaitTime = 3
                });
                return;
            }

            var commands = await CommandListViewModel.ImportCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
            var count = commands?.Count() ?? 0;

            if (count > 0)
            {
                Growl.Success(new GrowlInfo
                {
                    Message = $"成功导入 {count} 个命令",
                    WaitTime = 3
                });
            }

            UpdateStatus();
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"导入失败: {ex.Message}",
                WaitTime = 3
            });
        }
    }

    private void UpdateStatus()
    {
        var groupName = GroupTreeViewModel.SelectedGroup?.Name ?? "无";
        var count = CommandListViewModel.Commands.Count;
        StatusText = $"当前分组: {groupName} | 命令数: {count}";
    }
}
