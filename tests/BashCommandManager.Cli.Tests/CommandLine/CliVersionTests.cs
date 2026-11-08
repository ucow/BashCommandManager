using BashCommandManager.Cli.CommandLine;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class CliVersionTests
{
    [Fact]
    public void Version_matches_plan_version()
    {
        // 版本三处同步（D6）：csproj、两个 .iss、README 都是 1.3.0
        Assert.Equal("1.3.0", CliVersion.Value);
    }
}
