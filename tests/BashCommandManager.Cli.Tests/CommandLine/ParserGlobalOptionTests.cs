using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class ParserGlobalOptionTests
{
    [Fact]
    public void Json_alone_without_command_means_help()
    {
        var parsed = CommandLineParser.Parse(new[] { "--json" }, _ => false);

        Assert.Equal("help", parsed.CommandPath);
        Assert.True(parsed.Json);
    }

    [Fact]
    public void Version_wins_over_help()
    {
        var parsed = CommandLineParser.Parse(new[] { "--help", "--version" }, _ => false);

        Assert.Equal("version", parsed.CommandPath);
    }

    [Fact]
    public void Database_path_is_absolutized_against_current_directory()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "list", "--db", @"..\tmp\x.db" }, _ => false);

        // 实现（T2 步骤 4 的 Path.GetFullPath）与 spec 154-162「相对路径按 CurrentDirectory 转绝对」一致
        Assert.Equal(Path.GetFullPath(@"..\tmp\x.db"), parsed.DatabasePath);
    }

    [Fact]
    public void Duplicate_global_option_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(
            () => CommandLineParser.Parse(new[] { "groups", "list", "--json", "--json" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--json", error.Message);
    }
}
