using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Tests.TestSupport;

namespace BashCommandManager.Cli.Tests.Infrastructure;

public class GuiLauncherTests
{
    private const string CliDir = @"C:\Program Files\BashCommandManager\cli";

    private static GuiLauncher Create(params string[] existing)
        => new(name => name == "BASHCOMMANDMANAGER_GUI" ? @"D:\portable\BashCommandManager.exe" : null,
            CliDir,
            path => existing.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Explicit_path_wins()
    {
        var launcher = Create(@"D:\custom\BashCommandManager.exe");

        Assert.Equal(@"D:\custom\BashCommandManager.exe", launcher.ResolveGuiPath(@"D:\custom\BashCommandManager.exe"));
    }

    [Fact]
    public void Missing_explicit_path_returns_null()
    {
        var launcher = Create(@"C:\Program Files\BashCommandManager\BashCommandManager.exe");

        Assert.Null(launcher.ResolveGuiPath(@"D:\missing\BashCommandManager.exe"));
    }

    [Fact]
    public void Environment_variable_is_second_choice()
    {
        var launcher = Create(@"D:\portable\BashCommandManager.exe", @"C:\Program Files\BashCommandManager\BashCommandManager.exe");

        Assert.Equal(@"D:\portable\BashCommandManager.exe", launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Parent_directory_of_cli_is_third_choice()
    {
        var launcher = Create(@"C:\Program Files\BashCommandManager\BashCommandManager.exe");

        Assert.Equal(@"C:\Program Files\BashCommandManager\BashCommandManager.exe", launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Cli_directory_is_fourth_choice()
    {
        var launcher = Create(@"C:\Program Files\BashCommandManager\cli\BashCommandManager.exe");

        Assert.Equal(@"C:\Program Files\BashCommandManager\cli\BashCommandManager.exe", launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Returns_null_when_nothing_found()
    {
        var launcher = Create();

        Assert.Null(launcher.ResolveGuiPath(null));
    }

    [Fact]
    public void Launch_passes_command_id_as_startup_argument()
    {
        var starter = new FakeGuiProcessStarter();
        var launcher = new GuiLauncher(cliDirectory: CliDir, fileExists: _ => true, processStarter: starter);

        var processId = launcher.Launch(@"C:\Apps\BashCommandManager.exe", 7);

        Assert.Equal(0, processId);
        var startInfo = Assert.Single(starter.StartInfos);
        Assert.Equal(@"C:\Apps\BashCommandManager.exe", startInfo.FileName);
        Assert.Equal("--command 7", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void Launch_without_command_id_passes_empty_arguments()
    {
        var starter = new FakeGuiProcessStarter();
        var launcher = new GuiLauncher(cliDirectory: CliDir, fileExists: _ => true, processStarter: starter);

        launcher.Launch(@"C:\Apps\BashCommandManager.exe", null);

        Assert.Equal(string.Empty, Assert.Single(starter.StartInfos).Arguments);
    }
}
