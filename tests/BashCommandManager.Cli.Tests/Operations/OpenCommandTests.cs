using BashCommandManager.Cli;
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli.Tests.Operations;

public class OpenCommandTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static CliServices Services(FakeGuiLauncher launcher, string databasePath = @"C:\db\app.db")
        => new(
            new DatabasePathResolution(databasePath, DatabasePathSource.LocalAppData, null, false),
            session: null,
            new FakeSettingsStore(),
            new FakeProcessRunner(),
            launcher,
            new FakeExecutableLocator(),
            File.Exists,
            Directory.Exists,
            @"C:\Program Files\BashCommandManager\cli");

    [Fact]
    public async Task Open_launches_gui_without_command()
    {
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher);

        var result = await OpenCommand.ExecuteAsync(Parse("open"), services);

        Assert.True(result.Data!["opened"]!.GetValue<bool>());
        Assert.Equal(@"C:\fake\BashCommandManager.exe", result.Data!["guiPath"]!.GetValue<string>());
        Assert.Null(result.Data!["commandId"]);
        var launch = Assert.Single(launcher.Launches);
        Assert.Null(launch.CommandId);
    }

    [Fact]
    public async Task Open_passes_existing_command_id()
    {
        using var temp = new TempDatabase();
        int id;
        using (var session = new DatabaseSession(temp.DatabasePath))
        {
            var store = new CliCommandStore(session.Connection);
            id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        }
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher, temp.DatabasePath);

        var result = await OpenCommand.ExecuteAsync(Parse("open", "--command", id.ToString()), services);

        Assert.Equal(id, result.Data!["commandId"]!.GetValue<int>());
        Assert.Equal(id, Assert.Single(launcher.Launches).CommandId);
    }

    [Fact]
    public async Task Open_missing_command_is_not_found_exit_two()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        _ = session.Connection;
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher, temp.DatabasePath);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            OpenCommand.ExecuteAsync(Parse("open", "--command", "999"), services));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task Open_missing_command_does_not_create_database()
    {
        using var temp = new TempDatabase();
        var missing = Path.Combine(temp.Root, "missing.db");
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher, missing);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            OpenCommand.ExecuteAsync(Parse("open", "--command", "7"), services));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task Open_uses_explicit_gui_path_when_launcher_accepts_it()
    {
        var launcher = new FakeGuiLauncher();
        using var services = Services(launcher);

        await OpenCommand.ExecuteAsync(Parse("open", "--gui", @"D:\custom\BashCommandManager.exe"), services);

        Assert.Equal(@"D:\custom\BashCommandManager.exe", Assert.Single(launcher.Launches).GuiPath);
    }

    [Fact]
    public async Task Open_without_gui_is_not_found_exit_two()
    {
        var launcher = new FakeGuiLauncher { ResolvedPath = null };
        using var services = Services(launcher);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => OpenCommand.ExecuteAsync(Parse("open"), services));

        Assert.Equal("E_GUI_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Open_with_missing_explicit_gui_reports_specific_message()
    {
        var launcher = new FakeGuiLauncher { ResolvedPath = null };
        using var services = Services(launcher);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            OpenCommand.ExecuteAsync(Parse("open", "--gui", @"D:\missing\BashCommandManager.exe"), services));

        Assert.Equal("E_GUI_NOT_FOUND", error.Error.Code);
        Assert.Contains(@"D:\missing\BashCommandManager.exe", error.Error.Message);
    }
}
