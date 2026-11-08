using System.Diagnostics;

namespace BashCommandManager.Cli.Infrastructure;

/// <summary>进程启动端口：把 Process.Start 抽出来，单测只断言启动参数、不真的起进程。</summary>
public interface IGuiProcessStarter
{
    Process? Start(ProcessStartInfo startInfo);
}

/// <summary>默认实现：直接调用 Process.Start（UseShellExecute=true，与 GUI 行为一致）。</summary>
public sealed class ProcessGuiStarter : IGuiProcessStarter
{
    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}

/// <summary>GUI 可执行文件查找与启动。</summary>
public interface IGuiLauncher
{
    string? ResolveGuiPath(string? explicitPath);

    int Launch(string guiPath, int? commandId);
}

/// <summary>查找顺序：--gui &gt; BASHCOMMANDMANAGER_GUI &gt; {cliDir}\..\BashCommandManager.exe &gt; {cliDir}\BashCommandManager.exe。</summary>
public sealed class GuiLauncher : IGuiLauncher
{
    public const string EnvironmentVariableName = "BASHCOMMANDMANAGER_GUI";
    public const string GuiExecutableName = "BashCommandManager.exe";

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _cliDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly IGuiProcessStarter _processStarter;

    public GuiLauncher(
        Func<string, string?>? getEnvironmentVariable = null,
        string? cliDirectory = null,
        Func<string, bool>? fileExists = null,
        IGuiProcessStarter? processStarter = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _cliDirectory = cliDirectory ?? AppContext.BaseDirectory;
        _fileExists = fileExists ?? File.Exists;
        _processStarter = processStarter ?? new ProcessGuiStarter();
    }

    public string? ResolveGuiPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return _fileExists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var fromEnvironment = _getEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && _fileExists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var parent = Path.GetFullPath(Path.Combine(_cliDirectory, "..", GuiExecutableName));
        if (_fileExists(parent))
        {
            return parent;
        }

        var sibling = Path.Combine(_cliDirectory, GuiExecutableName);
        return _fileExists(sibling) ? sibling : null;
    }

    public int Launch(string guiPath, int? commandId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = guiPath,
            Arguments = commandId is int id ? $"--command {id}" : string.Empty,
            UseShellExecute = true
        };

        var process = _processStarter.Start(startInfo);
        return process?.Id ?? 0;
    }
}
