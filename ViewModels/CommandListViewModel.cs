using BashCommandManager.Controls;
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;
using HandyControl.Tools.Extension;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace BashCommandManager.ViewModels;

public partial class CommandListViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private readonly ICommandExecutor _executor;

    [ObservableProperty]
    private ObservableCollection<Command> _commands = new();

    // 新增：排序相关属性
    [ObservableProperty]
    private SortOption _currentSortOption = SortOption.Name;

    [ObservableProperty]
    private SortDirection _currentSortDirection = SortDirection.Ascending;

    // 新增：用于 UI 绑定的属性
    public IEnumerable<SortOption> SortOptions => Enum.GetValues<SortOption>();

    // 新增：当前搜索关键词（用于重新应用搜索）
    private string? _currentSearchKeyword;
    private int _currentGroupId = 0; // 0 表示所有命令

    // 新增：用于 UI 显示的排序方向图标
    public string SortDirectionIcon => CurrentSortDirection == SortDirection.Ascending ? "▲" : "▼";

    // 新增：用于绑定 ToggleButton 的 IsChecked
    public bool IsDescending
    {
        get => CurrentSortDirection == SortDirection.Descending;
        set
        {
            if (value && CurrentSortDirection != SortDirection.Descending)
            {
                CurrentSortDirection = SortDirection.Descending;
            }
            else if (!value && CurrentSortDirection != SortDirection.Ascending)
            {
                CurrentSortDirection = SortDirection.Ascending;
            }
        }
    }

    public CommandListViewModel(ICommandService commandService, ICommandExecutor executor)
    {
        _commandService = commandService;
        _executor = executor;
    }

    public async Task LoadCommandsAsync(int groupId)
    {
        _currentGroupId = groupId;
        _currentSearchKeyword = null;
        var commands = await _commandService.GetByGroupAsync(groupId, CurrentSortOption, CurrentSortDirection);
        Commands = new ObservableCollection<Command>(commands);
    }

    public async Task LoadAllCommandsAsync()
    {
        _currentGroupId = 0;
        _currentSearchKeyword = null;
        var commands = await _commandService.GetAllAsync(CurrentSortOption, CurrentSortDirection);
        Commands = new ObservableCollection<Command>(commands);
    }

    public async Task SearchAsync(string keyword)
    {
        _currentSearchKeyword = keyword;
        IEnumerable<Command> commands;

        if (string.IsNullOrWhiteSpace(keyword))
        {
            // 空搜索时恢复到分组视图
            if (_currentGroupId == 0)
            {
                commands = await _commandService.GetAllAsync(CurrentSortOption, CurrentSortDirection);
            }
            else
            {
                commands = await _commandService.GetByGroupAsync(_currentGroupId, CurrentSortOption, CurrentSortDirection);
            }
        }
        else if (_currentGroupId == 0)
        {
            // 在所有命令中搜索
            commands = await _commandService.SearchAsync(keyword, CurrentSortOption, CurrentSortDirection);
        }
        else
        {
            // 在指定分组中搜索
            commands = await _commandService.SearchInGroupAsync(keyword, _currentGroupId, CurrentSortOption, CurrentSortDirection);
        }

        Commands = new ObservableCollection<Command>(commands);
    }

    [RelayCommand]
    private async Task ApplySortAsync()
    {
        // 重新加载当前视图以应用新排序
        if (!string.IsNullOrWhiteSpace(_currentSearchKeyword))
        {
            await SearchAsync(_currentSearchKeyword);
        }
        else if (_currentGroupId == 0)
        {
            await LoadAllCommandsAsync();
        }
        else
        {
            await LoadCommandsAsync(_currentGroupId);
        }
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
            ? SortDirection.Descending
            : SortDirection.Ascending;
        ApplySortCommand.ExecuteAsync(null);
    }

    partial void OnCurrentSortOptionChanged(SortOption value)
    {
        ApplySortCommand.ExecuteAsync(null);
    }

    partial void OnCurrentSortDirectionChanged(SortDirection value)
    {
        OnPropertyChanged(nameof(SortDirectionIcon));
        OnPropertyChanged(nameof(IsDescending));
    }

    public async Task<IEnumerable<Command>> ImportCommandsAsync(int groupId)
    {
        var commands = await _commandService.ImportCommandsAsync(groupId);
        foreach (var command in commands)
        {
            Commands.Add(command);
        }
        return commands;
    }

    [RelayCommand]
    private void ExecuteCommand(Command command)
    {
        try
        {
            _executor.Execute(command, runAsAdmin: false);
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"执行失败: {ex.Message}",
                WaitTime = 3
            });
        }
    }

    [RelayCommand]
    private void ExecuteAsAdmin(Command command)
    {
        try
        {
            _executor.Execute(command, runAsAdmin: true);
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"执行失败: {ex.Message}",
                WaitTime = 3
            });
        }
    }

    [RelayCommand]
    private async Task DeleteCommandAsync(Command? command)
    {
        if (command == null) return;

        var dialog = new ConfirmDialogControl
        {
            Title = "确认删除",
            Message = $"确定要删除命令 '{command.Name}' 吗？\n这只会从列表中移除，不会删除实际文件。"
        };
        dialog.DataContext = dialog;

        var result = await Dialog.Show(dialog).GetResultAsync<bool>();

        if (result)
        {
            await _commandService.DeleteCommandAsync(command.Id);
            Commands.Remove(command);
            Growl.Success(new GrowlInfo
            {
                Message = "删除成功",
                WaitTime = 3
            });
        }
    }

    [RelayCommand]
    private void OpenDirectory(Command? command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.FilePath))
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(command.FilePath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        if (!Directory.Exists(directoryPath))
        {
            Growl.Warning(new GrowlInfo
            {
                Message = $"目录不存在: {directoryPath}",
                WaitTime = 3
            });
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directoryPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Growl.Error(new GrowlInfo
            {
                Message = $"打开目录失败: {ex.Message}",
                WaitTime = 3
            });
        }
    }
}
