using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>GUI 启动端口的内存替身：只记录启动参数，不真的起进程。</summary>
internal sealed class FakeGuiLauncher : IGuiLauncher
{
    public string? ResolvedPath { get; set; } = @"C:\fake\BashCommandManager.exe";

    public List<(string GuiPath, int? CommandId)> Launches { get; } = new();

    public string? ResolveGuiPath(string? explicitPath) => ResolvedPath;

    public int Launch(string guiPath, int? commandId)
    {
        Launches.Add((guiPath, commandId));
        return 5555;
    }
}
