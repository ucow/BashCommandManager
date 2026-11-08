using BashCommandManager.Cli.Infrastructure;
using System.Diagnostics;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>记录 ProcessStartInfo、不真的起进程的 IGuiProcessStarter 假实现。</summary>
internal sealed class FakeGuiProcessStarter : IGuiProcessStarter
{
    public List<ProcessStartInfo> StartInfos { get; } = new();

    public Process? Start(ProcessStartInfo startInfo)
    {
        StartInfos.Add(startInfo);
        return null;
    }
}
