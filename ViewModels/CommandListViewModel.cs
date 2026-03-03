using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

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

    public async Task ImportCommandAsync(int groupId)
    {
        var command = await _commandService.ImportCommandAsync(groupId);
        if (command != null)
        {
            Commands.Add(command);
        }
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
            System.Windows.MessageBox.Show($"执行失败: {ex.Message}");
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
            System.Windows.MessageBox.Show($"执行失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteCommandAsync(Command command)
    {
        var result = System.Windows.MessageBox.Show(
            $"确定要删除命令 '{command.Name}' 吗？\n这只会从列表中移除，不会删除实际文件。",
            "确认删除",
            System.Windows.MessageBoxButton.YesNo);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await _commandService.DeleteCommandAsync(command.Id);
            Commands.Remove(command);
        }
    }
}
