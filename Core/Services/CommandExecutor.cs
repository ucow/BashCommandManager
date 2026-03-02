using BashCommandManager.Core.Models;
using System.Diagnostics;
using System.IO;

namespace BashCommandManager.Core.Services;

public interface ICommandExecutor
{
    void Execute(Command command, bool runAsAdmin = false);
    bool IsRunning(int commandId);
    void RegisterProcess(int commandId, Process process);
    void UnregisterProcess(int commandId);
}

public class CommandExecutor : ICommandExecutor
{
    private readonly Dictionary<int, Process> _runningProcesses = new();

    public void Execute(Command command, bool runAsAdmin = false)
    {
        if (!File.Exists(command.FilePath))
        {
            command.Status = CommandStatus.Failed;
            throw new FileNotFoundException($"文件不存在: {command.FilePath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k \"{command.FilePath}\"",
            WorkingDirectory = Path.GetDirectoryName(command.FilePath),
            UseShellExecute = true
        };

        if (runAsAdmin)
        {
            psi.Verb = "runas";
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.Exited += (s, e) =>
        {
            UnregisterProcess(command.Id);
            command.Status = process.ExitCode == 0 ? CommandStatus.Completed : CommandStatus.Failed;
        };

        process.Start();
        RegisterProcess(command.Id, process);
        command.Status = CommandStatus.Running;
    }

    public bool IsRunning(int commandId) => _runningProcesses.ContainsKey(commandId);

    public void RegisterProcess(int commandId, Process process)
    {
        _runningProcesses[commandId] = process;
    }

    public void UnregisterProcess(int commandId)
    {
        _runningProcesses.Remove(commandId);
    }
}
