using BashCommandManager.Cli;
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Infrastructure;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class ConfigCommandTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static CliServices Services(FakeSettingsStore settings, string? guiPath = @"C:\Program Files\BashCommandManager\BashCommandManager.exe")
        => new(
            new DatabasePathResolution(@"C:\Users\tester\AppData\Local\BashCommandManager\data\app.db", DatabasePathSource.LocalAppData, null, false),
            session: null,
            settings,
            new FakeProcessRunner(),
            new FakeGuiLauncher { ResolvedPath = guiPath },
            new FakeExecutableLocator(),
            File.Exists,
            Directory.Exists,
            @"C:\Program Files\BashCommandManager\cli");

    [Fact]
    public async Task Get_without_key_returns_all_sections()
    {
        using var services = Services(new FakeSettingsStore());

        var result = await ConfigCommand.GetAsync(Parse("config", "get"), services);

        var data = result.Data!.AsObject();
        Assert.Equal("C:\\Users\\tester\\AppData\\Local\\BashCommandManager\\data\\app.db", data["db"]!["path"]!.GetValue<string>());
        Assert.Equal("localAppData", data["db"]!["source"]!.GetValue<string>());
        Assert.Equal(@"C:\fake\settings.json", data["settings"]!["path"]!.GetValue<string>());
        Assert.True(data["hotkey"]!["enabled"]!.GetValue<bool>());
        Assert.Equal(57, data["hotkey"]!["key"]!.GetValue<int>());
        Assert.Equal("N", data["hotkey"]!["keyName"]!.GetValue<string>());
        Assert.Equal(6, data["hotkey"]!["modifiers"]!.GetValue<int>());
        Assert.Equal("Ctrl+Shift+N", data["hotkey"]!["display"]!.GetValue<string>());
        Assert.Equal(@"C:\Program Files\BashCommandManager\BashCommandManager.exe", data["gui"]!["path"]!.GetValue<string>());
        Assert.Equal("1.3.0", data["version"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("db.path", "db")]
    [InlineData("db.source", "db")]
    [InlineData("hotkey", "hotkey")]
    [InlineData("hotkey.enabled", "hotkey")]
    [InlineData("settings.path", "settings")]
    [InlineData("gui.path", "gui")]
    public async Task Get_with_key_returns_containing_section(string key, string expectedSection)
    {
        using var services = Services(new FakeSettingsStore());

        var result = await ConfigCommand.GetAsync(Parse("config", "get", key), services);

        var data = result.Data!.AsObject();
        Assert.Single(data);
        Assert.Equal(expectedSection, data.Single().Key);
    }

    [Fact]
    public async Task Get_version_key_returns_flat_version()
    {
        using var services = Services(new FakeSettingsStore());

        var result = await ConfigCommand.GetAsync(Parse("config", "get", "version"), services);

        Assert.Equal("1.3.0", result.Data!["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task Get_reports_null_gui_path_when_missing()
    {
        using var services = Services(new FakeSettingsStore(), guiPath: null);

        var result = await ConfigCommand.GetAsync(Parse("config", "get", "gui.path"), services);

        Assert.Null(result.Data!["gui"]!["path"]);
    }

    [Theory]
    [InlineData(DatabasePathSource.Argument, "argument")]
    [InlineData(DatabasePathSource.Environment, "environment")]
    [InlineData(DatabasePathSource.LocalAppData, "localAppData")]
    [InlineData(DatabasePathSource.LegacyApplicationDirectory, "legacy")]
    public void Source_names_match_spec(DatabasePathSource source, string expected)
    {
        Assert.Equal(expected, ConfigCommand.SourceName(source));
    }
}
