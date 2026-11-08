namespace BashCommandManager.Core.Models;

/// <summary>执行批处理脚本时使用的终端类型。</summary>
public enum TerminalKind
{
    Cmd = 0,
    WindowsPowerShell = 1,
    PowerShell7 = 2,
    WindowsTerminal = 3,
    Custom = 4
}

/// <summary>终端类型的中文显示名，供设置界面与 Growl 提示复用。</summary>
public static class TerminalKindDisplay
{
    public static string GetDisplayName(this TerminalKind kind) => kind switch
    {
        TerminalKind.Cmd => "命令提示符 (cmd)",
        TerminalKind.WindowsPowerShell => "Windows PowerShell",
        TerminalKind.PowerShell7 => "PowerShell 7 (pwsh)",
        TerminalKind.WindowsTerminal => "Windows Terminal (wt)",
        TerminalKind.Custom => "自定义程序",
        _ => "命令提示符 (cmd)"
    };
}
