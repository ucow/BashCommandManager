using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;
using Microsoft.Win32;
using System.IO;

namespace BashCommandManager.Core.Services;

public interface ICommandService
{
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId);
    Task<IEnumerable<Command>> ImportCommandsAsync(int groupId);
    Task DeleteCommandAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
    Task<IEnumerable<Command>> GetAllAsync();

    // 新增：支持排序的方法
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId, SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> GetAllAsync(SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> SearchAsync(string keyword, SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction);

    // 新增：移动命令
    Task MoveCommandAsync(int commandId, int targetGroupId);
    Task MoveCommandsAsync(IEnumerable<int> commandIds, int targetGroupId);

    // 新增：获取常用命令
    Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10);

    // 新增：记录命令执行
    Task RecordExecutionAsync(int commandId);
}

public class CommandService : ICommandService
{
    private readonly ICommandRepository _repository;

    public CommandService(ICommandRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Command>> GetByGroupAsync(int groupId)
    {
        return await _repository.GetByGroupIdAsync(groupId);
    }

    public async Task<IEnumerable<Command>> ImportCommandsAsync(int groupId)
    {
        return await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "批处理文件|*.bat;*.cmd|所有文件|*.*",
                Title = "选择批处理文件",
                Multiselect = true
            };

            var importedCommands = new List<Command>();

            if (dialog.ShowDialog() == true)
            {
                foreach (var filePath in dialog.FileNames)
                {
                    var command = new Command
                    {
                        Name = Path.GetFileNameWithoutExtension(filePath),
                        Description = "",
                        FilePath = filePath,
                        GroupId = groupId
                    };

                    command.Id = await _repository.CreateAsync(command);
                    importedCommands.Add(command);
                }
            }

            return importedCommands;
        }).Task.Unwrap();
    }

    public async Task DeleteCommandAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task<IEnumerable<Command>> SearchAsync(string keyword)
    {
        return await _repository.SearchAsync(keyword);
    }

    public async Task<IEnumerable<Command>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<IEnumerable<Command>> GetByGroupAsync(int groupId, SortOption sortBy, SortDirection direction)
    {
        return await _repository.GetByGroupIdWithSortAsync(groupId, sortBy, direction);
    }

    public async Task<IEnumerable<Command>> GetAllAsync(SortOption sortBy, SortDirection direction)
    {
        return await _repository.GetAllWithSortAsync(sortBy, direction);
    }

    public async Task<IEnumerable<Command>> SearchAsync(string keyword, SortOption sortBy, SortDirection direction)
    {
        return await _repository.SearchWithSortAsync(keyword, sortBy, direction);
    }

    public async Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction)
    {
        return await _repository.SearchInGroupAsync(keyword, groupId, sortBy, direction);
    }

    public async Task MoveCommandAsync(int commandId, int targetGroupId)
    {
        await _repository.MoveToGroupAsync(commandId, targetGroupId);
    }

    public async Task MoveCommandsAsync(IEnumerable<int> commandIds, int targetGroupId)
    {
        foreach (var commandId in commandIds)
        {
            await _repository.MoveToGroupAsync(commandId, targetGroupId);
        }
    }

    public async Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10)
    {
        return await _repository.GetFrequentlyUsedAsync(limit);
    }

    public async Task RecordExecutionAsync(int commandId)
    {
        var command = await _repository.GetByIdAsync(commandId);
        if (command != null)
        {
            command.ExecutionCount++;
            command.LastExecutedAt = DateTime.Now;
            await _repository.UpdateAsync(command);
        }
    }
}
