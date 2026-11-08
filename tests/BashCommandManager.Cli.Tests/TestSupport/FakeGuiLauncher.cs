using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>GUI 启动端口的内存替身：只记录启动参数，不真的起进程。</summary>
internal sealed class FakeGuiLauncher : IGuiLauncher
{
    public string? ResolvedPath { get; set; } = @"C:\fake\BashCommandManager.exe";

    public List<(string GuiPath, int? CommandId, string? DatabasePath)> Launches { get; } = new();

    // 偏差记录（T23）：计划要求「显式 --gui 原样透传」与「ResolvedPath=null 时显式路径视为不存在」两条
    // 用例同时成立。ResolvedPath 代表「本机是否有可用 GUI」：非空时显式路径视为存在并原样返回；
    // 为空时无论是否有显式路径都返回 null（模拟未安装 GUI）。
    public string? ResolveGuiPath(string? explicitPath)
        => explicitPath is not null && ResolvedPath is not null ? explicitPath : ResolvedPath;

    public int Launch(string guiPath, int? commandId, string? databasePath = null)
    {
        Launches.Add((guiPath, commandId, databasePath));
        return 5555;
    }
}
