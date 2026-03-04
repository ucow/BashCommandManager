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

    public CommandListViewModel(ICommandService commandService, ICommandExecutor executor)
    {
        _commandService = commandService;
        _executor = executor;
    }

    public async Task LoadCommandsAsync(int groupId)
    {
        var commands = await _commandService.GetByGroupAsync(groupId);
        Commands = new ObservableCollection<Command>(commands);
    }

    public async Task LoadAllCommandsAsync()
    {
        var commands = await _commandService.GetAllAsync();
        Commands = new ObservableCollection<Command>(commands);
    }

    public async Task SearchAsync(string keyword)
    {
        var commands = await _commandService.SearchAsync(keyword);
        Commands = new ObservableCollection<Command>(commands);
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
