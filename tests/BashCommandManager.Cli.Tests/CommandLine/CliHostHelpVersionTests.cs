using BashCommandManager.Cli;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class CliHostHelpVersionTests
{
    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliHost.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void No_arguments_prints_help_and_exits_zero()
    {
        var result = Run();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("bcm groups list", result.Stdout);
        Assert.Equal("", result.Stderr);
    }

    [Fact]
    public void Help_flag_prints_help_and_exits_zero()
    {
        var result = Run("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("用法", result.Stdout);
    }

    [Fact]
    public void Version_flag_prints_bare_version()
    {
        var result = Run("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1.3.0", result.Stdout.Trim());
    }

    [Fact]
    public void Version_with_json_wraps_in_envelope()
    {
        var result = Run("--version", "--json");

        Assert.Equal(0, result.ExitCode);
        var root = JsonNode.Parse(result.Stdout)!.AsObject();
        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal("1.3.0", root["data"]!["version"]!.GetValue<string>());
        Assert.Null(root["meta"]);
    }

    [Fact]
    public void Unknown_command_exits_one_with_usage_error_on_stderr()
    {
        var result = Run("bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        Assert.Contains("[E_USAGE]", result.Stderr);
        Assert.Contains("bogus", result.Stderr);
    }

    [Fact]
    public void Unknown_command_with_json_emits_json_error_on_stderr()
    {
        var result = Run("bogus", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Stdout);
        var root = JsonNode.Parse(result.Stderr)!.AsObject();
        Assert.False(root["ok"]!.GetValue<bool>());
        Assert.Equal("E_USAGE", root["error"]!["code"]!.GetValue<string>());
        Assert.NotNull(root["error"]!["hint"]);
    }
}
