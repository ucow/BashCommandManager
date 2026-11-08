using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;

namespace BashCommandManager.Cli.Tests.Application;

public class CliCommandStoreTests
{
    private static CliCommandStore Store(DatabaseSession session) => new(session.Connection);

    [Fact]
    public async Task Creates_groups_with_monotonic_sort_order()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);

        var first = await store.CreateGroupAsync("运维", 1);
        var second = await store.CreateGroupAsync("测试", 1);

        var groups = await store.GetGroupsAsync();
        // SortOrder 是 0 基（与 GUI GroupService.CreateGroupAsync 的 siblings.Count() 一致，spec 182 也写 sortOrder:0）
        Assert.Equal(0, groups.Single(group => group.Id == first).SortOrder);
        Assert.Equal(1, groups.Single(group => group.Id == second).SortOrder);
    }

    [Fact]
    public async Task Finds_child_group_by_name_case_insensitively()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var id = await store.CreateGroupAsync("Ops", 1);

        var found = await store.FindChildGroupByNameAsync(1, "ops");
        var missing = await store.FindChildGroupByNameAsync(1, "nope");

        Assert.Equal(id, found!.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Commands_round_trip_with_sort_and_limit()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var groupId = await store.CreateGroupAsync("运维", 1);
        await store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = groupId, SortOrder = 0 });
        await store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = groupId, SortOrder = 1 });

        var byName = await store.GetCommandsAsync(groupId, SortOption.Name, SortDirection.Ascending, 0);
        var limited = await store.GetCommandsAsync(groupId, SortOption.Name, SortDirection.Ascending, 1);

        Assert.Equal(new[] { "a", "b" }, byName.Select(command => command.Name).ToArray());
        Assert.Single(limited);
        Assert.Equal("a", limited[0].Name);
    }

    [Fact]
    public async Task Search_matches_name_or_description_and_respects_group_scope()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        await store.CreateCommandAsync(new Command { Name = "备份", Description = "每日", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "部署", Description = "备份后部署", FilePath = @"D:\b.bat", GroupId = test });

        var all = await store.SearchCommandsAsync("备份", null, SortOption.Name, SortDirection.Ascending, 0);
        var scoped = await store.SearchCommandsAsync("备份", ops, SortOption.Name, SortDirection.Ascending, 0);

        Assert.Equal(2, all.Count);
        Assert.Single(scoped);
        Assert.Equal("备份", scoped[0].Name);
    }

    [Fact]
    public async Task Moves_commands_between_groups()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        var test = await store.CreateGroupAsync("测试", 1);
        var id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });

        await store.MoveCommandsAsync(new[] { id }, test);

        var moved = await store.GetCommandAsync(id);
        Assert.Equal(test, moved!.GroupId);
    }

    [Fact]
    public async Task Records_execution_count_and_timestamp()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });

        await store.RecordExecutionAsync(id);
        await store.RecordExecutionAsync(id);

        var command = await store.GetCommandAsync(id);
        Assert.Equal(2, command!.ExecutionCount);
        Assert.NotNull(command.LastExecutedAt);
    }

    [Fact]
    public async Task Deleting_group_cascades_and_reports_subtree_ids()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        var child = await store.CreateGroupAsync("生产", ops);
        var commandInOps = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var commandInChild = await store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = child });

        var subtree = await store.GetGroupSubtreeIdsAsync(ops);
        var commandIds = await store.GetCommandIdsInGroupsAsync(subtree);
        await store.DeleteGroupAsync(ops);

        Assert.Equal(new[] { ops, child }.OrderBy(id => id), subtree.OrderBy(id => id));
        Assert.Equal(new[] { commandInOps, commandInChild }.OrderBy(id => id), commandIds.OrderBy(id => id));
        Assert.Null(await store.GetGroupAsync(child));
        Assert.Null(await store.GetCommandAsync(commandInChild));
    }

    [Fact]
    public async Task Counts_commands_and_groups()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var ops = await store.CreateGroupAsync("运维", 1);
        await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        await store.CreateCommandAsync(new Command { Name = "恢复", FilePath = @"D:\b.bat", GroupId = ops });

        Assert.Equal(2, await store.CountCommandsInGroupAsync(ops));
        var counts = await store.GetCommandCountsAsync();
        Assert.Equal(2, counts[ops]);
        var names = await store.FillGroupNamesAsync(await store.GetCommandsAsync(ops, SortOption.Name, SortDirection.Ascending, 0));
        Assert.All(names, command => Assert.Equal("运维", command.GroupName));
    }

    [Fact]
    public async Task Frequent_returns_only_executed_commands_ordered_by_count()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);
        var store = Store(session);
        var first = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = 1 });
        await store.CreateCommandAsync(new Command { Name = "没用过", FilePath = @"D:\c.bat", GroupId = 1 });
        await store.RecordExecutionAsync(first);
        await store.RecordExecutionAsync(second);
        await store.RecordExecutionAsync(second);

        var frequent = await store.GetFrequentlyUsedAsync(10);

        Assert.Equal(new[] { "发布", "备份" }, frequent.Select(command => command.Name).ToArray());
        Assert.All(frequent, command => Assert.False(string.IsNullOrEmpty(command.GroupName)));
    }
}
