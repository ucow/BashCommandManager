using BashCommandManager.Cli;
using BashCommandManager.Cli.Tests.TestSupport;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class CliHostDatabaseTests
{
    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliHost.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Groups_list_creates_database_and_reports_meta()
    {
        using var temp = new TempDatabase();

        var result = Run("groups", "list", "--db", temp.DatabasePath, "--json");

        Assert.Equal(0, result.ExitCode);
        var root = JsonNode.Parse(result.Stdout)!.AsObject();
        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal(Path.GetFullPath(temp.DatabasePath), root["meta"]!["dbPath"]!.GetValue<string>());
        Assert.True(root["meta"]!["created"]!.GetValue<bool>());
        Assert.Equal(1, root["meta"]!["count"]!.GetValue<int>());
        Assert.NotNull(root["meta"]!["durationMs"]);
    }

    [Fact]
    public void Second_call_reports_created_false()
    {
        using var temp = new TempDatabase();
        Run("groups", "list", "--db", temp.DatabasePath, "--json");

        var result = Run("groups", "list", "--db", temp.DatabasePath, "--json");

        var root = JsonNode.Parse(result.Stdout)!.AsObject();
        Assert.False(root["meta"]!["created"]!.GetValue<bool>());
    }

    [Fact]
    public void Groups_create_round_trips_through_cli()
    {
        using var temp = new TempDatabase();

        var create = Run("groups", "create", "--name", "运维", "--db", temp.DatabasePath, "--json");
        var list = Run("groups", "list", "--db", temp.DatabasePath, "--json");

        Assert.Equal(0, create.ExitCode);
        var createRoot = JsonNode.Parse(create.Stdout)!.AsObject();
        Assert.Equal("运维", createRoot["data"]!["name"]!.GetValue<string>());
        Assert.Equal(2, JsonNode.Parse(list.Stdout)!["meta"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void Human_list_renders_table()
    {
        using var temp = new TempDatabase();

        var result = Run("groups", "list", "--db", temp.DatabasePath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Id", result.Stdout);
        Assert.Contains("根分组", result.Stdout);
    }

    [Fact]
    public void Errors_still_exit_one_with_json_body()
    {
        using var temp = new TempDatabase();

        var result = Run("groups", "create", "--db", temp.DatabasePath, "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        Assert.Equal("E_USAGE", JsonNode.Parse(result.Stderr)!["error"]!["code"]!.GetValue<string>());
    }
}
