using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;
using Microsoft.Win32;
using System.IO;

namespace BashCommandManager.Core.Services;

public interface ICommandService
{
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId);
    Task<Command?> ImportCommandAsync(int groupId);
    Task DeleteCommandAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
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

    public async Task<Command?> ImportCommandAsync(int groupId)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "批处理文件|*.bat;*.cmd|所有文件|*.*",
            Title = "选择批处理文件"
        };

        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            var command = new Command
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Description = "",
                FilePath = filePath,
                GroupId = groupId
            };

            command.Id = await _repository.CreateAsync(command);
            return command;
        }

        return null;
    }

    public async Task DeleteCommandAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task<IEnumerable<Command>> SearchAsync(string keyword)
    {
        return await _repository.SearchAsync(keyword);
    }
}
