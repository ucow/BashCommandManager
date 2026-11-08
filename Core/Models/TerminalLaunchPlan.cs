namespace BashCommandManager.Core.Models;

/// <summary>一次终端启动所需的全部 ProcessStartInfo 字段，由纯函数构造。</summary>
public sealed record TerminalLaunchPlan(
    string FileName,
    string Arguments,
    string? WorkingDirectory,
    bool UseShellExecute,
    string? Verb)
{
    public const string RunAsVerb = "runas";
}
