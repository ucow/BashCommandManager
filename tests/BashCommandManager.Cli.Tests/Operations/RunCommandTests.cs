using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Core.Models;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

public class RunCommandTests
{
    private static ParsedCommand Parse(params string[] args) => CommandLineParser.Parse(args, _ => false);

    private sealed record Arranged(
        TempDatabase Temp,
        DatabaseSession Session,
        CliCommandStore Store,
        CommandResolver Resolver,
        FakeProcessRunner Runner,
        FakeExecutableLocator Locator,
        FakeSettingsStore Settings,
        string ScriptPath,
        int CommandId);

    private static async Task<Arranged> ArrangeAsync(ExecutionSettings? execution = null, string content = "@echo off\r\nexit /b 0\r\n")
    {
        var temp = new TempDatabase();
        var session = new DatabaseSession(temp.DatabasePath);
        var store = new CliCommandStore(session.Connection);
        var script = temp.CreateScript("backup.cmd", content);
        var id = await store.CreateCommandAsync(new Command { Name = "备份", FilePath = script, GroupId = 1 });
        return new Arranged(
            temp,
            session,
            store,
            new CommandResolver(store),
            new FakeProcessRunner(),
            new FakeExecutableLocator(),
            new FakeSettingsStore(execution: execution),
            script,
            id);
    }

    [Fact]
    public async Task Dry_run_returns_cmd_k_line_without_starting_process()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--dry-run"), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["dryRun"]!.GetValue<bool>());
        Assert.Equal("cmd.exe", data["fileName"]!.GetValue<string>());
        Assert.Equal($"/k \"{arranged.ScriptPath}\"", data["arguments"]!.GetValue<string>());
        Assert.Equal(Path.GetDirectoryName(arranged.ScriptPath), data["workingDirectory"]!.GetValue<string>());
        Assert.Empty(arranged.Runner.Requests);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Dry_run_with_wait_uses_c_switch()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", "备份", "--wait", "--dry-run"), services, arranged.Store, arranged.Resolver);

        Assert.Equal($"/c \"{arranged.ScriptPath}\"", result.Data!["arguments"]!.GetValue<string>());
    }

    [Fact]
    public async Task Default_run_starts_process_records_execution_and_reports_pid()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["started"]!.GetValue<bool>());
        Assert.False(data["waited"]!.GetValue<bool>());
        Assert.Equal(4321, data["pid"]!.GetValue<int>());
        var request = Assert.Single(arranged.Runner.Requests);
        Assert.Equal("cmd.exe", request.FileName);
        Assert.True(request.UseShellExecute);
        Assert.Null(request.Verb);
        Assert.Equal(Path.GetDirectoryName(arranged.ScriptPath), request.WorkingDirectory);
        Assert.Equal(1, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Admin_run_uses_runas_verb()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        await RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--admin"), services, arranged.Store, arranged.Resolver);

        Assert.Equal("runas", Assert.Single(arranged.Runner.Requests).Verb);
    }

    [Fact]
    public async Task Wait_success_reports_exit_code_and_duration()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, 0, false);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--wait"), services, arranged.Store, arranged.Resolver);

        var data = result.Data!.AsObject();
        Assert.True(data["waited"]!.GetValue<bool>());
        Assert.Equal(0, data["exitCode"]!.GetValue<int>());
        Assert.NotNull(data["durationMs"]);
        var request = Assert.Single(arranged.Runner.Requests);
        Assert.False(request.UseShellExecute);
        Assert.True(request.RedirectOutput);
        Assert.EndsWith("/c \"" + arranged.ScriptPath + "\"", request.Arguments);
    }

    [Fact]
    public async Task Wait_nonzero_exit_raises_command_failed_with_details()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, 7, false);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--wait"), services, arranged.Store, arranged.Resolver)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("E_COMMAND_FAILED", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Equal("命令退出码 7", error.Error.Message);
        Assert.Equal(7, error.Error.Details!["exitCode"]!.GetValue<int>());
    }

    [Fact]
    public async Task Wait_timeout_raises_timeout_and_does_not_record_execution()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, null, true);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--wait", "--timeout", "3"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_TIMEOUT", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Missing_script_file_is_exit_three()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        File.Delete(arranged.ScriptPath);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
    }

    [Fact]
    public async Task Elevation_cancelled_is_reported()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.StartException = new Win32Exception(1223, "操作已被用户取消");
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--admin"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_ELEVATION_CANCELLED", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Missing_custom_terminal_is_exit_three()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { Terminal = TerminalKind.Custom, CustomExecutablePath = @"Z:\missing\app.exe" });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Locator.Resolver = (_, _) => null;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_FILE_NOT_FOUND", error.Error.Code);
        Assert.Equal(3, error.Error.ExitCode);
        Assert.Contains("自定义程序不存在", error.Error.Message);
    }

    [Fact]
    public async Task Missing_wt_falls_back_to_cmd_with_warning()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Locator.Resolver = (kind, _) => kind == TerminalKind.Cmd ? @"C:\Windows\System32\cmd.exe" : null;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver);

        // 偏差记录（VD-15）：计划断言 "cmd.exe"，但该用例把 Cmd 的解析结果显式设为绝对路径，
        // 实现（正确地）使用 locator 的解析结果，故断言改为该绝对路径。
        Assert.Equal(@"C:\Windows\System32\cmd.exe", Assert.Single(arranged.Runner.Requests).FileName);
        Assert.NotNull(result.Meta);
        Assert.Contains("已改用 cmd", result.Meta!["warning"]!.GetValue<string>());
    }

    [Fact]
    public async Task Resolved_executable_from_locator_is_used()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Locator.Resolver = (_, _) => @"C:\Fake\cmd.exe";
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        await RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString()), services, arranged.Store, arranged.Resolver);

        Assert.Equal(@"C:\Fake\cmd.exe", Assert.Single(arranged.Runner.Requests).FileName);
    }

    [Fact]
    public async Task Dry_run_reports_null_verb_when_not_elevated()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--dry-run"), services, arranged.Store, arranged.Resolver);

        Assert.True(result.Data!.AsObject().ContainsKey("verb"));
        Assert.Null(result.Data!["verb"]);
    }

    [Fact]
    public async Task Dry_run_reports_runas_verb_when_admin_requested()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--admin", "--dry-run"), services, arranged.Store, arranged.Resolver);

        Assert.Equal("runas", result.Data!["verb"]!.GetValue<string>());
    }

    [Fact]
    public async Task Wait_with_global_admin_is_usage_error()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { RunAsAdministrator = true });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--wait"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_USAGE", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
        Assert.Contains("--wait 不支持管理员运行", error.Error.Message);
        Assert.Contains("关闭全局管理员运行", error.Error.Message);
        Assert.Empty(arranged.Runner.Requests);
        Assert.Equal(0, (await arranged.Store.GetCommandAsync(arranged.CommandId))!.ExecutionCount);
    }

    [Fact]
    public async Task Wait_dry_run_with_global_admin_is_usage_error_too()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { RunAsAdministrator = true });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--wait", "--dry-run"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_USAGE", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
    }

    [Fact]
    public async Task Wait_without_global_admin_still_runs_normally()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { RunAsAdministrator = false });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        arranged.Runner.RunResult = new ProcessRunResult(true, 4321, 0, false);
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(
            Parse("run", arranged.CommandId.ToString(), "--wait"), services, arranged.Store, arranged.Resolver);

        Assert.True(result.Data!["waited"]!.GetValue<bool>());
        Assert.Single(arranged.Runner.Requests);
    }

    [Fact]
    public async Task Invalid_template_is_config_key_error()
    {
        var arranged = await ArrangeAsync(new ExecutionSettings { ArgumentsTemplate = "/k no-placeholder" });
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var error = await Assert.ThrowsAsync<CliFailureException>(() =>
            RunCommand.ExecuteAsync(Parse("run", arranged.CommandId.ToString(), "--dry-run"), services, arranged.Store, arranged.Resolver));

        Assert.Equal("E_CONFIG_KEY", error.Error.Code);
        Assert.Equal(1, error.Error.ExitCode);
        Assert.Contains("参数模板必须包含", error.Error.Message);
        Assert.Equal("检查 Execution 配置或运行 bcm config get", error.Error.Hint);
    }

    [Fact]
    public async Task Name_lookup_with_group_scope_works()
    {
        var arranged = await ArrangeAsync();
        using var _ = arranged.Temp;
        using var __ = arranged.Session;
        var ops = await arranged.Store.CreateGroupAsync("运维", 1);
        await arranged.Store.UpdateCommandAsync(new Command
        {
            Id = arranged.CommandId,
            Name = "备份",
            FilePath = arranged.ScriptPath,
            GroupId = ops,
            SortOrder = 0
        });
        using var services = CliServicesFactory.Create(arranged.Session, arranged.Settings, arranged.Runner, locator: arranged.Locator);

        var result = await RunCommand.ExecuteAsync(Parse("run", "备份", "--group", "运维"), services, arranged.Store, arranged.Resolver);

        Assert.True(result.Data!["started"]!.GetValue<bool>());
    }
}
