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

    [Fact]
    public async Task Export_without_output_returns_document_in_data()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ExportAsync(Parse("commands", "export"), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal(1, data["schemaVersion"]!.GetValue<int>());
        Assert.NotNull(data["exportedAt"]);
        Assert.Single(data["groups"]!.AsArray());
        Assert.Equal(ops, data["groups"]![0]!["id"]!.GetValue<int>());
        Assert.Single(data["commands"]!.AsArray());
        Assert.Equal("运维", data["commands"]![0]!["groupName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Export_group_filter_is_non_recursive_by_default()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = prod });
        using var services = CliServicesFactory.Create(arranged.Session);

        var shallow = await CommandCommands.ExportAsync(Parse("commands", "export", "--group", "运维"), services, arranged.Store, arranged.Resolver);
        var deep = await CommandCommands.ExportAsync(Parse("commands", "export", "--group", "运维", "--recursive"), services, arranged.Store, arranged.Resolver);

        Assert.Single(shallow.Data!["commands"]!.AsArray());
        Assert.Equal(2, deep.Data!["commands"]!.AsArray().Count);
    }

    [Fact]
    public async Task Export_writes_json_file_and_reports_count()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        var output = Path.Combine(arranged.Temp.Root, "backup.json");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ExportAsync(
            Parse("commands", "export", "--format", "json", "--output", output), services, arranged.Store, arranged.Resolver);

        Assert.Equal(output, result.Data!["path"]!.GetValue<string>());
        Assert.Equal("json", result.Data!["format"]!.GetValue<string>());
        Assert.Equal(1, result.Data!["count"]!.GetValue<int>());
        Assert.True(File.Exists(output));
        var document = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Export_writes_csv_with_fixed_columns_and_escaping()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command
        {
            Name = "备份,每日",
            Description = "含 \"引号\" 的描述",
            FilePath = @"D:\bat\备份.bat",
            GroupId = 1
        });
        var output = Path.Combine(arranged.Temp.Root, "backup.csv");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ExportAsync(
            Parse("commands", "export", "--format", "csv", "--output", output), services, arranged.Store, arranged.Resolver);

        Assert.Equal(1, result.Data!["count"]!.GetValue<int>());
        var lines = File.ReadAllLines(output);
        Assert.Equal("id,name,description,filePath,groupId,groupName,sortOrder,executionCount,lastExecutedAt", lines[0]);
        Assert.Contains("\"备份,每日\"", lines[1]);
        Assert.Contains("\"含 \"\"引号\"\" 的描述\"", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Export_csv_is_utf8_without_bom()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        var output = Path.Combine(arranged.Temp.Root, "backup.csv");
        using var services = CliServicesFactory.Create(arranged.Session);

        await CommandCommands.ExportAsync(Parse("commands", "export", "--format", "csv", "--output", output), services, arranged.Store, arranged.Resolver);

        var bytes = File.ReadAllBytes(output);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    // I-2：CSV 公式注入：以 = + - @ \t 开头的文本单元格必须前置 '，Excel 才会当文本。
    [Theory]
    [InlineData("=1+1")]
    [InlineData("-2")]
    [InlineData("@x")]
    [InlineData("+cmd")]
    [InlineData("\tTAB")]
    public async Task Export_csv_neutralizes_formula_prefixes(string name)
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = name, FilePath = @"D:\a.bat", GroupId = 1 });
        var output = Path.Combine(arranged.Temp.Root, "formula.csv");
        using var services = CliServicesFactory.Create(arranged.Session);

        await CommandCommands.ExportAsync(
            Parse("commands", "export", "--format", "csv", "--output", output), services, arranged.Store, arranged.Resolver);

        var lines = File.ReadAllLines(output);
        Assert.Contains(",'" + name + ",", lines[1]);
        Assert.DoesNotContain("," + name + ",", lines[1]);
    }

    [Fact]
    public async Task Export_csv_neutralizes_formula_inside_quoted_cell()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "=1,1", FilePath = @"D:\a.bat", GroupId = 1 });
        var output = Path.Combine(arranged.Temp.Root, "formula-quoted.csv");
        using var services = CliServicesFactory.Create(arranged.Session);

        await CommandCommands.ExportAsync(
            Parse("commands", "export", "--format", "csv", "--output", output), services, arranged.Store, arranged.Resolver);

        var lines = File.ReadAllLines(output);
        Assert.Contains("\"'=1,1\"", lines[1]);
    }
}
