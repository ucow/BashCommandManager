using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class ImportExportTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        return (temp, session, store, new CommandResolver(store));
    }

    [Fact]
    public async Task Import_expands_directory_for_bat_and_cmd_only()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "b.cmd"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "c.txt"), "not a script");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(
            Parse("commands", "import", "--group", "1", folder), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal(2, data["imported"]!.AsArray().Count);
        Assert.Empty(data["skipped"]!.AsArray()); // xUnit2013
        Assert.Equal(2, data["total"]!.GetValue<int>());
        Assert.False(data["dryRun"]!.GetValue<bool>());
        Assert.Equal(2, (await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0)).Count);
    }

    [Fact]
    public async Task Import_recurses_only_with_recursive_flag()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        var nested = Path.Combine(folder, "sub");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(folder, "top.bat"), "@echo off");
        File.WriteAllText(Path.Combine(nested, "deep.bat"), "@echo off");
        using var services = CliServicesFactory.Create(arranged.Session);

        var shallow = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", folder), services, arranged.Store, arranged.Resolver);
        var deep = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", folder, "--recursive"), services, arranged.Store, arranged.Resolver);

        Assert.Single(shallow.Data!["imported"]!.AsArray()); // xUnit2013
        Assert.Equal(2, deep.Data!["imported"]!.AsArray().Count + deep.Data!["skipped"]!.AsArray().Count);
    }

    [Fact]
    public async Task Import_skips_duplicate_file_paths()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("a.bat");
        await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", script), services, arranged.Store, arranged.Resolver);

        var skipped = result.Data!["skipped"]!.AsArray();
        Assert.Single(skipped);
        Assert.Equal("duplicate", skipped[0]!["reason"]!.GetValue<string>());
        Assert.Empty(result.Data!["imported"]!.AsArray()); // xUnit2013
        Assert.Equal(1, result.Data!["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Import_allow_duplicates_imports_again()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("a.bat");
        await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(
            Parse("commands", "import", "--group", "1", script, "--allow-duplicates"), services, arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!["imported"]!.AsArray());
        Assert.Equal(2, (await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0)).Count);
    }

    [Fact]
    public async Task Import_dry_run_does_not_write()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(
            Parse("commands", "import", "--group", "1", folder, "--dry-run"), services, arranged.Store, arranged.Resolver);

        Assert.True(result.Data!["dryRun"]!.GetValue<bool>());
        Assert.Null(result.Data!["imported"]![0]!["id"]);
        Assert.Empty(await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0));
    }

    [Fact]
    public async Task Import_skips_explicit_file_with_unsupported_extension()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var text = Path.Combine(arranged.Temp.Root, "notes.txt");
        File.WriteAllText(text, "notes");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", text), services, arranged.Store, arranged.Resolver);

        Assert.Equal("unsupported-extension", result.Data!["skipped"]![0]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Import_missing_path_fails_fast()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", @"Z:\missing"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Import_assigns_incrementing_sort_order()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        File.WriteAllText(Path.Combine(folder, "b.bat"), "@echo off");
        await arranged.Store.CreateCommandAsync(new Command { Name = "existing", FilePath = @"D:\existing.bat", GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "1", folder), services, arranged.Store, arranged.Resolver);

        var commands = await arranged.Store.GetCommandsAsync(1, SortOption.Name, SortDirection.Ascending, 0);
        Assert.Equal(new[] { 0, 1, 2 }, commands.Select(command => command.SortOrder).OrderBy(order => order).ToArray());
    }

    [Fact]
    public async Task Import_reports_group_in_data()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var folder = arranged.Temp.CreateDirectory("bat");
        File.WriteAllText(Path.Combine(folder, "a.bat"), "@echo off");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ImportAsync(Parse("commands", "import", "--group", "运维", folder), services, arranged.Store, arranged.Resolver);

        Assert.Equal(ops, result.Data!["group"]!["id"]!.GetValue<int>());
        Assert.Equal("运维", result.Data!["group"]!["name"]!.GetValue<string>());
    }
}
