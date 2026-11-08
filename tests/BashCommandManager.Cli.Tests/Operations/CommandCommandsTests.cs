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

    [Fact]
    public async Task Show_returns_file_exists_flag()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.ShowAsync(Parse("commands", "show", id.ToString()), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["fileExists"]!.GetValue<bool>());
        Assert.Equal("备份", data["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Show_missing_command_is_not_found()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.ShowAsync(Parse("commands", "show", "999"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Add_creates_command_with_sort_order()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.AddAsync(
            Parse("commands", "add", "--name", "备份", "--file", script, "--description", "每日"),
            services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.Equal("备份", data["name"]!.GetValue<string>());
        Assert.Equal(script, data["filePath"]!.GetValue<string>());
        Assert.Equal(1, data["groupId"]!.GetValue<int>());
        var stored = await arranged.Store.GetCommandAsync(data["id"]!.GetValue<int>());
        Assert.Equal("每日", stored!.Description);
    }

    [Fact]
    public async Task Add_requires_existing_file()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.AddAsync(Parse("commands", "add", "--name", "x", "--file", @"Z:\missing\x.cmd"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    // I-7：add/edit 只接受 .bat/.cmd（与 import/SKILL 的硬性约束一致）。
    [Fact]
    public async Task Add_rejects_non_batch_file()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var exe = arranged.Temp.CreateScript("tool.exe", "MZ");
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.AddAsync(Parse("commands", "add", "--name", "x", "--file", exe), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
        Assert.Contains(".bat/.cmd", error.Error.Hint);
        Assert.Empty(await arranged.Store.GetAllCommandsAsync());
    }

    [Fact]
    public async Task Edit_rejects_non_batch_file()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        var exe = arranged.Temp.CreateScript("tool.EXE", "MZ");
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.EditAsync(Parse("commands", "edit", id.ToString(), "--file", exe), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
        Assert.Contains(".bat/.cmd", error.Error.Hint);
        Assert.Equal(script, (await arranged.Store.GetCommandAsync(id))!.FilePath);
    }

    [Theory]
    [InlineData("script.BAT")]
    [InlineData("script.Cmd")]
    [InlineData("script.bat")]
    public async Task Add_accepts_batch_extensions_case_insensitively(string fileName)
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript(fileName);
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.AddAsync(
            Parse("commands", "add", "--name", "x", "--file", script), services, arranged.Store, arranged.Resolver);

        Assert.Equal(script, result.Data!["filePath"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("script.BAT")]
    [InlineData("script.CMD")]
    public async Task Edit_accepts_batch_extensions_case_insensitively(string fileName)
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var original = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = original, GroupId = 1 });
        var replacement = arranged.Temp.CreateScript(fileName);
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.EditAsync(
            Parse("commands", "edit", id.ToString(), "--file", replacement), services, arranged.Store, arranged.Resolver);

        Assert.Equal(replacement, result.Data!["filePath"]!.GetValue<string>());
    }

    [Fact]
    public async Task Add_resolves_group_path_and_returns_group_id()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var script = arranged.Temp.CreateScript("backup.cmd");
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.AddAsync(
            Parse("commands", "add", "--name", "备份", "--file", script, "--group", "运维"),
            services, arranged.Store, arranged.Resolver);

        Assert.Equal(ops, result.Data!["groupId"]!.GetValue<int>());
    }

    [Fact]
    public async Task Edit_updates_only_given_fields()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", Description = "旧", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.EditAsync(
            Parse("commands", "edit", id.ToString(), "--description", "每日 2:00"),
            services, arranged.Store, arranged.Resolver);

        var stored = await arranged.Store.GetCommandAsync(id);
        Assert.Equal("每日 2:00", stored!.Description);
        Assert.Equal("备份", stored.Name);
        Assert.Equal(script, stored.FilePath);
        Assert.Equal(1, stored.GroupId); // 未给 --group 时不改分组（--group 是目标分组，不是查找范围，见 VD-13）
        Assert.Equal("每日 2:00", result.Data!["description"]!.GetValue<string>());
        Assert.True(result.Data!["fileExists"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Edit_can_move_to_another_group_and_rename()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var result = await CommandCommands.EditAsync(
            Parse("commands", "edit", id.ToString(), "--name", "每日备份", "--group", "运维"),
            services, arranged.Store, arranged.Resolver);

        Assert.Equal("每日备份", result.Data!["name"]!.GetValue<string>());
        Assert.Equal(ops, result.Data!["groupId"]!.GetValue<int>());
        Assert.Equal(ops, (await arranged.Store.GetCommandAsync(id))!.GroupId);
    }

    [Fact]
    public async Task Edit_validates_new_file_exists()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.EditAsync(Parse("commands", "edit", id.ToString(), "--file", @"Z:\missing\x.cmd"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Edit_blank_name_is_invalid()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var script = arranged.Temp.CreateScript("backup.cmd");
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        using var services = CliServicesFactory.Create(arranged.Session);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.EditAsync(Parse("commands", "edit", id.ToString(), "--name", "  "), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_INVALID_VALUE", error.Error.Code);
    }

    [Fact]
    public async Task Delete_removes_multiple_commands_by_id_list()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = 1 });

        var result = await CommandCommands.DeleteAsync(Parse("commands", "delete", $"{first},{second}", "--force"), arranged.Store, arranged.Resolver);

        var deleted = result.Data!["deleted"]!.AsArray().Select(node => node!.GetValue<int>()).ToArray();
        Assert.Equal(new[] { first, second }, deleted);
        Assert.Null(await arranged.Store.GetCommandAsync(first));
        Assert.Null(await arranged.Store.GetCommandAsync(second));
    }

    [Fact]
    public async Task Delete_by_name_resolves_uniquely()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = 1 });

        var result = await CommandCommands.DeleteAsync(Parse("commands", "delete", "备份", "--force"), arranged.Store, arranged.Resolver);

        Assert.Equal(id, result.Data!["deleted"]!.AsArray()[0]!.GetValue<int>());
    }

    [Fact]
    public async Task Move_reports_moved_ids_and_target_group()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var target = await arranged.Store.CreateGroupAsync("生产运维", 1);
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = ops });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = ops });

        var result = await CommandCommands.MoveAsync(Parse("commands", "move", $"{first},{second}", "--to", "生产运维"), arranged.Store, arranged.Resolver);

        Assert.Equal(new[] { first, second }, result.Data!["moved"]!.AsArray().Select(node => node!.GetValue<int>()).ToArray());
        Assert.Equal(target, result.Data!["targetGroupId"]!.GetValue<int>());
        Assert.Equal(target, (await arranged.Store.GetCommandAsync(second))!.GroupId);
    }

    [Fact]
    public async Task Move_to_missing_group_is_not_found()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var id = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            CommandCommands.MoveAsync(Parse("commands", "move", id.ToString(), "--to", "缺失"), arranged.Store, arranged.Resolver));

        Assert.Equal("E_NOT_FOUND", error.Error.Code);
        Assert.Equal(2, error.Error.ExitCode);
    }

    [Fact]
    public async Task Frequent_returns_executed_commands_desc_with_default_limit_10()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "备份", FilePath = @"D:\a.bat", GroupId = ops });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "发布", FilePath = @"D:\b.bat", GroupId = ops });
        await arranged.Store.CreateCommandAsync(new Command { Name = "未用过", FilePath = @"D:\c.bat", GroupId = ops });
        await arranged.Store.RecordExecutionAsync(first);
        await arranged.Store.RecordExecutionAsync(second);
        await arranged.Store.RecordExecutionAsync(second);

        var result = await CommandCommands.FrequentAsync(Parse("commands", "frequent"), arranged.Store);

        var names = result.Data!.AsArray().Select(node => node!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "发布", "备份" }, names);
        Assert.Equal("运维", result.Data![0]!["groupName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Frequent_respects_limit()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var first = await arranged.Store.CreateCommandAsync(new Command { Name = "a", FilePath = @"D:\a.bat", GroupId = 1 });
        var second = await arranged.Store.CreateCommandAsync(new Command { Name = "b", FilePath = @"D:\b.bat", GroupId = 1 });
        await arranged.Store.RecordExecutionAsync(first);
        await arranged.Store.RecordExecutionAsync(second);

        var result = await CommandCommands.FrequentAsync(Parse("commands", "frequent", "--limit", "1"), arranged.Store);

        Assert.Single(result.Data!.AsArray());
    }
}
