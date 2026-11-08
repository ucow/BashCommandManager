using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class GroupCommandsTests
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
    public async Task List_returns_root_group_with_command_count()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var result = await GroupCommands.ListAsync(Parse("groups", "list"), arranged.Store);

        var data = result.Data!.AsArray();
        Assert.Single(data);
        var root = data[0]!.AsObject();
        Assert.Equal(1, root["id"]!.GetValue<int>());
        Assert.Equal("根分组", root["name"]!.GetValue<string>());
        Assert.Null(root["parentId"]);
        Assert.Equal(0, root["commandCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task List_counts_direct_commands_per_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });

        var result = await GroupCommands.ListAsync(Parse("groups", "list"), arranged.Store);

        var opsNode = result.Data!.AsArray().Single(node => node!["id"]!.GetValue<int>() == ops)!.AsObject();
        Assert.Equal(1, opsNode["commandCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task List_tree_nests_children()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);

        var result = await GroupCommands.ListAsync(Parse("groups", "list", "--tree"), arranged.Store);

        var root = result.Data!.AsArray()[0]!.AsObject();
        var opsNode = root["children"]!.AsArray().Single(node => node!["id"]!.GetValue<int>() == ops)!.AsObject();
        var prodNode = opsNode["children"]!.AsArray().Single()!.AsObject();
        Assert.Equal(prod, prodNode["id"]!.GetValue<int>());
        Assert.Empty(prodNode["children"]!.AsArray()); // xUnit2013：集合大小断言用 Assert.Empty
        Assert.Contains("生产", result.HumanText);
    }

    [Fact]
    public async Task Create_uses_root_parent_by_default()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var result = await GroupCommands.CreateAsync(Parse("groups", "create", "--name", "运维"), arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal("运维", data["name"]!.GetValue<string>());
        Assert.Equal(1, data["parentId"]!.GetValue<int>());
        Assert.Equal(0, data["sortOrder"]!.GetValue<int>()); // 同级从 0 开始：spec L182 + GUI GroupService 的 siblings.Count() 一致（B-2）
    }

    [Fact]
    public async Task Create_accepts_parent_path_and_none()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);

        var child = await GroupCommands.CreateAsync(Parse("groups", "create", "--name", "生产", "--parent", "运维"), arranged.Store, arranged.Resolver);
        var top = await GroupCommands.CreateAsync(Parse("groups", "create", "--name", "顶级", "--parent", "none"), arranged.Store, arranged.Resolver);

        Assert.Equal(ops, child.Data!["parentId"]!.GetValue<int>());
        Assert.Null(top.Data!["parentId"]);
    }

    [Fact]
    public async Task Create_rejects_duplicate_sibling_name()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        await arranged.Store.CreateGroupAsync("运维", 1);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.CreateAsync(Parse("groups", "create", "--name", "运维"), arranged.Store, arranged.Resolver)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Create_rejects_blank_name()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.CreateAsync(Parse("groups", "create", "--name", "   "), arranged.Store, arranged.Resolver));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Create_reports_missing_parent_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.CreateAsync(Parse("groups", "create", "--name", "x", "--parent", "缺失"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Rename_updates_name_and_keeps_siblings_unique()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.CreateGroupAsync("生产运维", 1);

        var renamed = await GroupCommands.RenameAsync(Parse("groups", "rename", ops.ToString(), "--name", "生产运维2"), arranged.Store, arranged.Resolver);
        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.RenameAsync(Parse("groups", "rename", ops.ToString(), "--name", "生产运维"), arranged.Store, arranged.Resolver));

        Assert.Equal("生产运维2", renamed.Data!["name"]!.GetValue<string>());
        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Rename_accepts_group_path()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);

        var result = await GroupCommands.RenameAsync(Parse("groups", "rename", "运维/生产", "--name", "生产环境"), arranged.Store, arranged.Resolver);

        Assert.Equal(prod, result.Data!["id"]!.GetValue<int>());
        Assert.Equal("生产环境", result.Data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Delete_reports_subtree_group_and_command_ids()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var prod = await arranged.Store.CreateGroupAsync("生产", ops);
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = prod });

        var result = await GroupCommands.DeleteAsync(Parse("groups", "delete", "运维", "--force"), arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        var groupIds = data["deletedGroupIds"]!.AsArray().Select(node => node!.GetValue<int>()).OrderBy(id => id).ToArray();
        var commandIds = data["deletedCommandIds"]!.AsArray().Select(node => node!.GetValue<int>()).OrderBy(id => id).ToArray();
        Assert.Equal(new[] { ops, prod }.OrderBy(id => id), groupIds);
        Assert.Equal(new[] { first, second }.OrderBy(id => id), commandIds);
        Assert.Null(await arranged.Store.GetGroupAsync(prod));
        Assert.Null(await arranged.Store.GetCommandAsync(second));
    }

    [Fact]
    public async Task Delete_refuses_root_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.DeleteAsync(Parse("groups", "delete", "1", "--force"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_ROOT_GROUP", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public void Delete_requires_force_at_parser_level()
    {
        var error = Assert.Throws<CliFailureException>(() => Parse("groups", "delete", "运维"));

        Assert.Equal("E_CONFIRM_REQUIRED", error.Error.Code);
    }

    [Fact]
    public async Task Delete_unknown_group_is_not_found()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            GroupCommands.DeleteAsync(Parse("groups", "delete", "缺失", "--force"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }
}
