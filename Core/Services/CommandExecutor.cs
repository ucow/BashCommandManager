using BashCommandManager.Core.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BashCommandManager.Core.Services;

public interface ICommandExecutor
{
    void Execute(Command command, bool runAsAdmin = false);

    ExecutionResult ExecuteWithResult(Command command, ExecutionOptions options);

    bool IsRunning(int commandId);
    void RegisterProcess(int commandId, Process process);
    void UnregisterProcess(int commandId);
}

/// <summary>
/// 启动批处理脚本；终端选择、模板展开与降级由 TerminalCommandBuilder / ExecutableLocator 承担，
/// 本类只负责组装 ProcessStartInfo、启动进程与回写 CommandStatus。
/// </summary>
public class CommandExecutor : ICommandExecutor
{
    private readonly Dictionary<int, Process> _runningProcesses = new();
    private readonly ISettingsService _settingsService;
    private readonly IExecutableLocator _executableLocator;

    public CommandExecutor(ISettingsService settingsService, IExecutableLocator executableLocator)
    {
        _settingsService = settingsService;
        _executableLocator = executableLocator;
    }

    public void Execute(Command command, bool runAsAdmin = false)
    {
        var result = ExecuteWithResult(command, new ExecutionOptions(runAsAdmin));
        if (result.Started)
        {
            return;
        }

        if (result.Error is not null)
        {
            throw result.Error;
        }

        throw new OperationCanceledException(result.Notice ?? "执行已取消");
    }

    public ExecutionResult ExecuteWithResult(Command command, ExecutionOptions options)
    {
        if (!File.Exists(command.FilePath))
        {
            command.Status = CommandStatus.Failed;
            throw new FileNotFoundException($"文件不存在: {command.FilePath}");
        }

        var settings = _settingsService.GetExecutionSettings();
        if (options.RunAsAdmin && !settings.RunAsAdministrator)
        {
            settings = settings.Clone();
            settings.RunAsAdministrator = true;
        }

        var resolved = _executableLocator.Resolve(settings.Terminal, settings.CustomExecutablePath);
        string? notice = null;
        var fallbackApplied = false;

        if (resolved is null && IsFallbackTerminal(settings.Terminal))
        {
            var originalTerminal = settings.Terminal;
            settings = settings.WithTerminal(TerminalKind.Cmd);
            resolved = _executableLocator.Resolve(TerminalKind.Cmd, settings.CustomExecutablePath);
            fallbackApplied = true;
            notice = $"未检测到 {originalTerminal.GetDisplayName()}，已改用 cmd 启动";
        }
        else if (resolved is null && settings.Terminal == TerminalKind.Custom)
        {
            command.Status = CommandStatus.Failed;
            return new ExecutionResult(
                false,
                TerminalKind.Custom,
                false,
                null,
                new FileNotFoundException($"自定义程序不存在: {settings.CustomExecutablePath}"));
        }

        TerminalLaunchPlan plan;
        try
        {
            plan = TerminalCommandBuilder.Build(settings, command, resolved);
        }
        catch (TerminalConfigurationException ex)
        {
            command.Status = CommandStatus.Failed;
            return new ExecutionResult(false, settings.Terminal, fallbackApplied, notice, ex);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            Arguments = plan.Arguments,
            WorkingDirectory = plan.WorkingDirectory ?? string.Empty,
            UseShellExecute = plan.UseShellExecute
        };

        if (plan.Verb is not null)
        {
            startInfo.Verb = plan.Verb;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            UnregisterProcess(command.Id);
            command.Status = process.ExitCode == 0 ? CommandStatus.Completed : CommandStatus.Failed;
        };

        try
        {
            StartProcess(process);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            command.Status = CommandStatus.Idle;
            return new ExecutionResult(false, settings.Terminal, fallbackApplied, "已取消管理员授权", null);
        }
        catch (Exception ex)
        {
            command.Status = CommandStatus.Failed;
            return new ExecutionResult(false, settings.Terminal, fallbackApplied, notice, ex);
        }

        RegisterProcess(command.Id, process);
        command.Status = CommandStatus.Running;

        return new ExecutionResult(true, settings.Terminal, fallbackApplied, notice, null);
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

    /// <summary>真实启动进程；测试重写此方法以拦截 Process.Start，避免弹窗与 UAC。</summary>
    protected virtual void StartProcess(Process process) => process.Start();

    private static bool IsFallbackTerminal(TerminalKind kind) =>
        kind is TerminalKind.PowerShell7 or TerminalKind.WindowsTerminal;
}
