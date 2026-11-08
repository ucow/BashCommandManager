using BashCommandManager.Cli.Tests.TestSupport;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Smoke;

public class CliSmokeTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    [Fact]
    public void Smoke1_groups_list_creates_temp_database()
    {
        using var temp = new TempDatabase();

        var result = CliProcess.Run(new[] { "--db", temp.DatabasePath, "groups", "list", "--json" });

        Assert.Equal(0, result.ExitCode);
        var root = Json(result.Stdout);
        Assert.True(root["meta"]!["created"]!.GetValue<bool>());
        Assert.Equal("根分组", root["data"]![0]!["name"]!.GetValue<string>());
        Assert.True(File.Exists(temp.DatabasePath));
    }

    [Fact]
    public void Smoke2_full_group_and_command_lifecycle()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("t.cmd");

        Assert.Equal(0, CliProcess.Run(new[] { "groups", "create", "--name", "运维", "--db", temp.DatabasePath, "--json" }).ExitCode);
        var added = CliProcess.Run(new[] { "commands", "add", "--name", "t", "--file", script, "--group", "运维", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(0, added.ExitCode);
        var commandId = Json(added.Stdout)["data"]!["id"]!.GetValue<int>();

        var list = CliProcess.Run(new[] { "commands", "list", "--json", "--db", temp.DatabasePath });
        Assert.Contains("t", list.Stdout);

        Assert.Equal(0, CliProcess.Run(new[] { "groups", "create", "--name", "测试", "--db", temp.DatabasePath, "--json" }).ExitCode);
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "move", commandId.ToString(), "--to", "测试", "--db", temp.DatabasePath, "--json" }).ExitCode);
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "delete", commandId.ToString(), "--force", "--db", temp.DatabasePath, "--json" }).ExitCode);

        var after = CliProcess.Run(new[] { "commands", "list", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(0, after.ExitCode);
        Assert.Empty(Json(after.Stdout)["data"]!.AsArray());
    }

    [Fact]
    public void Smoke3_dry_run_wait_exit_code_and_not_found()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("fail.cmd", "@echo off\r\nexit /b 7\r\n");
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "add", "--name", "fail", "--file", script, "--db", temp.DatabasePath, "--json" }).ExitCode);
        var id = Json(CliProcess.Run(new[] { "commands", "list", "--db", temp.DatabasePath, "--json" }).Stdout)["data"]![0]!["id"]!.GetValue<int>();

        var dryRun = CliProcess.Run(new[] { "run", id.ToString(), "--dry-run", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(0, dryRun.ExitCode);
        Assert.Equal("cmd.exe", Json(dryRun.Stdout)["data"]!["fileName"]!.GetValue<string>());
        Assert.Contains(script, Json(dryRun.Stdout)["data"]!["arguments"]!.GetValue<string>());

        var wait = CliProcess.Run(new[] { "run", id.ToString(), "--wait", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(3, wait.ExitCode);
        Assert.Equal("E_COMMAND_FAILED", Json(wait.Stderr)["error"]!["code"]!.GetValue<string>());
        Assert.Equal(7, Json(wait.Stderr)["error"]!["details"]!["exitCode"]!.GetValue<int>());

        var missing = CliProcess.Run(new[] { "run", "9999", "--db", temp.DatabasePath, "--json" });
        Assert.Equal(2, missing.ExitCode);
        Assert.Equal("E_NOT_FOUND", Json(missing.Stderr)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Smoke4_wait_forwards_child_output_to_stderr_and_keeps_json_clean()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("echo.cmd", "@echo off\r\necho marker\r\nexit /b 0\r\n");
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "add", "--name", "echo", "--file", script, "--db", temp.DatabasePath, "--json" }).ExitCode);
        var id = Json(CliProcess.Run(new[] { "commands", "list", "--db", temp.DatabasePath, "--json" }).Stdout)["data"]![0]!["id"]!.GetValue<int>();

        var result = CliProcess.Run(new[] { "run", id.ToString(), "--wait", "--db", temp.DatabasePath, "--json" });

        Assert.Equal(0, result.ExitCode);
        var root = Json(result.Stdout); // stdout 必须仍是干净 JSON
        Assert.Equal(0, root["data"]!["exitCode"]!.GetValue<int>());
        Assert.Contains("marker", result.Stderr);
        Assert.DoesNotContain("marker", result.Stdout);
    }

    [Fact]
    public void Smoke5_csv_export_writes_fixed_header()
    {
        using var temp = new TempDatabase();
        var script = temp.CreateScript("e.cmd");
        Assert.Equal(0, CliProcess.Run(new[] { "commands", "add", "--name", "e", "--file", script, "--db", temp.DatabasePath, "--json" }).ExitCode);
        var output = Path.Combine(temp.Root, "export.csv");

        var result = CliProcess.Run(new[] { "commands", "export", "--format", "csv", "--output", output, "--db", temp.DatabasePath, "--json" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, Json(result.Stdout)["data"]!["count"]!.GetValue<int>());
        var lines = File.ReadAllLines(output);
        Assert.Equal("id,name,description,filePath,groupId,groupName,sortOrder,executionCount,lastExecutedAt", lines[0]);
        Assert.Contains("e", lines[1]);
    }

    [Fact]
    public void Smoke6_delete_without_force_exits_one()
    {
        using var temp = new TempDatabase();

        var result = CliProcess.Run(new[] { "groups", "delete", "根分组", "--db", temp.DatabasePath, "--json" });

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("E_CONFIRM_REQUIRED", Json(result.Stderr)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Smoke7_config_set_hotkey_writes_settings_in_temp_directory()
    {
        using var temp = new TempDatabase();
        var settingsDirectory = temp.CreateDirectory("settings");

        var set = CliProcess.Run(new[] { "config", "set", "hotkey", "Ctrl+Shift+N", "--json" }, settingsDirectory);
        Assert.Equal(0, set.ExitCode);
        Assert.Equal(57, Json(set.Stdout)["data"]!["hotkey"]!["key"]!.GetValue<int>());

        var get = CliProcess.Run(new[] { "config", "get", "hotkey", "--json" }, settingsDirectory);
        Assert.Equal("Ctrl+Shift+N", Json(get.Stdout)["data"]!["hotkey"]!["display"]!.GetValue<string>());

        var settingsJson = JsonObject.Parse(File.ReadAllText(Path.Combine(settingsDirectory, "settings.json")))!.AsObject();
        Assert.Equal(57, settingsJson["Hotkey"]!["Key"]!.GetValue<int>());
        Assert.Equal(6, settingsJson["Hotkey"]!["Modifiers"]!.GetValue<int>());
    }

    [Fact]
    public void Smoke8_help_is_fast_and_lists_all_commands()
    {
        var result = CliProcess.Run(new[] { "--help" });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bcm commands export", result.Stdout);
        Assert.Contains("bcm run", result.Stdout);
    }
}
