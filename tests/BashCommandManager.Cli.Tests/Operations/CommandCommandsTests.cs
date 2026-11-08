using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class CommandCommandsTests
{
    internal static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    internal static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        return (temp, session, store, new CommandResolver(store));
    }

    [Fact]
    public async Task List_returns_all_commands_with_group_names()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", Description = "每日", FilePath = @"D:\bat\备份.bat", GroupId = ops });

        var result = await CommandCommands.ListAsync(Parse("commands", "list"), arranged.Store, arranged.Resolver);

        var item = result.Data!.AsArray().Single()!.AsObject();
        Assert.Equal("备份", item["name"]!.GetValue<string>());
        Assert.Equal("运维", item["groupName"]!.GetValue<string>());
        Assert.Equal(0, item["executionCount"]!.GetValue<int>());
        Assert.Null(item["lastExecutedAt"]);
    }

    [Fact]
    public async Task List_filters_by_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var test = await arranged.Store.CreateGroupAsync("测试", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "部署", FilePath = @"D:\b.bat", GroupId = test });

        var result = await CommandCommands.ListAsync(Parse("commands", "list", "--group", "运维"), arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!.AsArray());
        Assert.Equal("备份", result.Data![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task List_sorts_by_count_desc_and_applies_limit()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = 1 });
        await arranged.Store.RecordExecutionAsync(second);
        await arranged.Store.RecordExecutionAsync(second);
        await arranged.Store.RecordExecutionAsync(first);

        var result = await CommandCommands.ListAsync(Parse("commands", "list", "--sort", "count", "--order", "desc", "--limit", "1"), arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!.AsArray());
        Assert.Equal("b", result.Data![0]!["name"]!.GetValue<string>());
        Assert.Equal(2, result.Data![0]!["executionCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Search_matches_name_and_description()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", Description = "", FilePath = @"D:\a.bat", GroupId = 1 });
        await arranged.Store.CreateCommandAsync(new Command { Name = "部署", Description = "备份完成后部署", FilePath = @"D:\b.bat", GroupId = 1 });
        await arranged.Store.CreateCommandAsync(new Command { Name = "清理", Description = "", FilePath = @"D:\c.bat", GroupId = 1 });

        var result = await CommandCommands.SearchAsync(Parse("commands", "search", "备份"), arranged.Store, arranged.Resolver);

        Assert.Equal(2, result.Data!.AsArray().Count);
    }

    [Fact]
    public async Task Search_respects_group_scope()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var test = await arranged.Store.CreateGroupAsync("测试", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\b.bat", GroupId = test });

        var result = await CommandCommands.SearchAsync(Parse("commands", "search", "备份", "--group", "运维"), arranged.Store, arranged.Resolver);

        Assert.Single(result.Data!.AsArray());
        Assert.Equal("运维", result.Data![0]!["groupName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Search_without_match_returns_empty_array()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var result = await CommandCommands.SearchAsync(Parse("commands", "search", "没有"), arranged.Store, arranged.Resolver);

        Assert.Empty(result.Data!.AsArray());
    }

    [Fact]
    public async Task Human_output_uses_default_key_value_when_human_text_is_absent()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\bat\备份.bat", GroupId = 1 });

        var result = await CommandCommands.ListAsync(Parse("commands", "list"), arranged.Store, arranged.Resolver);

        Assert.Null(result.HumanText);
        Assert.IsType<JsonArray>(result.Data);
        Assert.Equal("备份", result.Data![0]!["name"]!.GetValue<string>());
    }
}
