using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;

namespace BashCommandManager.Cli.Tests.Operations;

public class CommandResolverTests
{
    private static async Task<(TempDatabase Temp, DatabaseSession Session, CliCommandStore Store, CommandResolver Resolver, int OpsId, int ProdId)> ArrangeAsync()
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var ops = await store.CreateGroupAsync("运维", 1);
        var prod = await store.CreateGroupAsync("生产", ops);
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = prod });
        return (temp, session, store, new CommandResolver(store), ops, prod);
    }

    [Fact]
    public async Task Resolves_group_by_id()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var group = await arranged.Resolver.ResolveGroupAsync(arranged.OpsId.ToString());

        Assert.Equal(arranged.OpsId, group.Id);
    }

    [Fact]
    public async Task Resolves_group_by_path()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var group = await arranged.Resolver.ResolveGroupAsync("运维/生产");

        Assert.Equal(arranged.ProdId, group.Id);
    }

    [Fact]
    public async Task Resolves_group_by_name_case_insensitively()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        Assert.Equal("运维", (await arranged.Resolver.ResolveGroupAsync(" 运维 ")).Name);
    }

    [Fact]
    public async Task Missing_group_is_not_found_with_exit_two()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() => arranged.Resolver.ResolveGroupAsync("不存在"));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Ambiguous_group_lists_candidates_and_exits_one()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var first = await store.CreateGroupAsync("同名", 1);
        var second = await store.CreateGroupAsync("其他", 1);
        await store.CreateGroupAsync("同名", first);
        var resolver = new CommandResolver(store);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveGroupAsync("同名"));

        Assert.Equal("E_AMBIGUOUS", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
        var candidates = error.Error.Details!["candidates"]!.AsArray();
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public async Task Resolves_command_by_id_and_name()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var commands = await arranged.Store.GetCommandsAsync(null, SortOption.Name, SortDirection.Ascending, 0);
        var backup = commands.Single(command => command.Name == "备份");

        Assert.Equal(backup.Id, (await arranged.Resolver.ResolveCommandAsync(backup.Id.ToString(), null)).Id);
        Assert.Equal(backup.Id, (await arranged.Resolver.ResolveCommandAsync("备份", null)).Id);
        Assert.Equal(backup.Id, (await arranged.Resolver.ResolveCommandAsync(" 备份 ", null)).Id);
    }

    [Fact]
    public async Task Group_scope_narrows_name_lookup()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        var inOps = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var resolver = new CommandResolver(store);

        var resolved = await resolver.ResolveCommandAsync("备份", "运维");
        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveCommandAsync("备份", "测试"));

        Assert.Equal(inOps, resolved.Id);
        Assert.Equal("E_NOT_FOUND", error.Error.Code);
    }

    [Fact]
    public async Task Duplicate_command_name_is_ambiguous()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\b.bat", GroupId = test });
        var resolver = new CommandResolver(store);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveCommandAsync("备份", null));

        Assert.Equal("E_AMBIGUOUS", error.Error.Code);
        Assert.Equal(2, error.Error.Details!["candidates"]!.AsArray().Count);
    }

    [Fact]
    public async Task Comma_separated_list_resolves_each_entry()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var commands = await arranged.Store.GetCommandsAsync(null, SortOption.Name, SortDirection.Ascending, 0);
        var ids = string.Join(",", commands.OrderBy(command => command.Id).Select(command => command.Id));

        var resolved = await arranged.Resolver.ResolveCommandsAsync(ids, null);

        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public async Task Comma_separated_names_are_split_so_use_ids_for_such_names()
    {
        var temp = new TempDatabase();
        using var __ = temp;
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        await store.CreateCommandAsync(new Command { Name = "a,b", FilePath = @"D:\a.bat", GroupId = 1 });
        var resolver = new CommandResolver(store);

        var error = await Assert.ThrowsAsync<CliFailureException>(() => resolver.ResolveCommandsAsync("a,b", null));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
    }
}
