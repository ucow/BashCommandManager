using BashCommandManager.Core.Models;
using System.IO;

namespace BashCommandManager.Core.Services;

/// <summary>把终端类型解析为可直接启动的可执行文件路径。</summary>
public interface IExecutableLocator
{
    string? Resolve(TerminalKind kind, string customExecutablePath);
}

/// <summary>按固定探测顺序查找终端程序；环境变量与文件存在性可注入以便单测。</summary>
public class ExecutableLocator : IExecutableLocator
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;

    public ExecutableLocator() : this(null, null)
    {
    }

    public ExecutableLocator(Func<string, string?>? getEnvironmentVariable, Func<string, bool>? fileExists)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
    }

    public string? Resolve(TerminalKind kind, string customExecutablePath) => kind switch
    {
        TerminalKind.Cmd => ResolveCmd(),
        TerminalKind.WindowsPowerShell => ResolveWindowsPowerShell(),
        TerminalKind.PowerShell7 => ResolvePowerShell7(),
        TerminalKind.WindowsTerminal => ResolveWindowsTerminal(),
        TerminalKind.Custom => ResolveCustom(customExecutablePath),
        _ => ResolveCmd()
    };

    /// <summary>在 PATH 中查找文件名；忽略空条目与包含引号的目录。</summary>
    public static string? FindOnPath(string fileName, string? pathVariable, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator))
        {
            var directory = entry.Trim();
            if (directory.Length == 0)
            {
                continue;
            }

            if (directory.Contains('"'))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string ResolveCmd()
    {
        var comspec = _getEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrWhiteSpace(comspec))
        {
            return comspec!;
        }

        var systemRoot = _getEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            var candidate = Path.Combine(systemRoot!, "System32", "cmd.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return "cmd.exe";
    }

    private string ResolveWindowsPowerShell()
    {
        var systemRoot = _getEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrWhiteSpace(systemRoot))
        {
            var candidate = Path.Combine(systemRoot!, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return "powershell.exe";
    }

    private string? ResolvePowerShell7()
    {
        foreach (var candidate in GetWellKnownPwshPaths())
        {
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("pwsh.exe", _getEnvironmentVariable("PATH"), _fileExists);
    }

    private IEnumerable<string> GetWellKnownPwshPaths()
    {
        var programFiles = _getEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles!, "PowerShell", "7", "pwsh.exe");
        }

        var programFilesX86 = _getEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86!, "PowerShell", "7", "pwsh.exe");
        }

        var localAppData = _getEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData!, "Microsoft", "WindowsApps", "pwsh.exe");
        }
    }

    private string? ResolveWindowsTerminal()
    {
        var localAppData = _getEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var candidate = Path.Combine(localAppData!, "Microsoft", "WindowsApps", "wt.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("wt.exe", _getEnvironmentVariable("PATH"), _fileExists);
    }

    private string? ResolveCustom(string customExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(customExecutablePath))
        {
            return null;
        }

        var path = customExecutablePath.Trim();
        return _fileExists(path) ? path : null;
    }
}
