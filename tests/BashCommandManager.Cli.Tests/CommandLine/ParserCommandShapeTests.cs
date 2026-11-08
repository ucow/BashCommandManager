using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class ParserCommandShapeTests
{
    public static IEnumerable<object[]> Shapes => new[]
    {
        new object[] { new[] { "groups", "list" }, "groups.list" },
        new object[] { new[] { "groups", "list", "--tree" }, "groups.list" },
        new object[] { new[] { "groups", "create", "--name", "运维", "--parent", "1" }, "groups.create" },
        new object[] { new[] { "groups", "rename", "2", "--name", "生产运维" }, "groups.rename" },
        new object[] { new[] { "groups", "delete", "运维", "--force" }, "groups.delete" },
        new object[] { new[] { "commands", "list", "--group", "运维" }, "commands.list" },
        new object[] { new[] { "commands", "search", "备份" }, "commands.search" },
        new object[] { new[] { "commands", "show", "7" }, "commands.show" },
        new object[] { new[] { "commands", "add", "--name", "备份", "--file", @"D:\bat\备份.bat" }, "commands.add" },
        new object[] { new[] { "commands", "edit", "7", "--description", "每日 2:00" }, "commands.edit" },
        new object[] { new[] { "commands", "delete", "7,8", "--force" }, "commands.delete" },
        new object[] { new[] { "commands", "move", "7,8", "--to", "生产运维" }, "commands.move" },
        new object[] { new[] { "commands", "import", "--group", "运维", @"D:\bat", "--recursive" }, "commands.import" },
        new object[] { new[] { "commands", "export", "--format", "json", "--output", "backup.json" }, "commands.export" },
        new object[] { new[] { "commands", "frequent", "--limit", "5" }, "commands.frequent" },
        new object[] { new[] { "run", "备份", "--wait" }, "run" },
        new object[] { new[] { "config", "get", "db.path" }, "config.get" },
        new object[] { new[] { "config", "set", "hotkey", "Ctrl+Shift+N" }, "config.set" },
        new object[] { new[] { "open", "--command", "7" }, "open" },
        new object[] { new[] { "--version" }, "version" }
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Parses_command_shape(string[] args, string expectedPath)
    {
        var parsed = CommandLineParser.Parse(args, _ => false);

        Assert.Equal(expectedPath, parsed.CommandPath);
    }

    [Fact]
    public void Groups_create_keeps_name_with_spaces()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "create", "--name", "生产 运维", "--parent", "1" }, _ => false);

        Assert.Equal("生产 运维", parsed.GetValue("name"));
        Assert.Equal("1", parsed.GetValue("parent"));
        Assert.Empty(parsed.Arguments);
    }

    [Fact]
    public void Flags_and_values_are_separated()
    {
        var parsed = CommandLineParser.Parse(new[] { "run", "备份", "--wait", "--timeout", "30", "--dry-run" }, _ => false);

        Assert.True(parsed.HasFlag("wait"));
        Assert.True(parsed.HasFlag("dry-run"));
        Assert.False(parsed.HasFlag("admin"));
        Assert.Equal("30", parsed.GetValue("timeout"));
        Assert.Null(parsed.GetValue("wait"));
        Assert.Single(parsed.Arguments);
        Assert.Equal("备份", parsed.Arguments[0]);
    }

    [Fact]
    public void Supports_inline_value_with_equals()
    {
        var parsed = CommandLineParser.Parse(new[] { "commands", "add", "--name=备份", "--file=D:\\bat\\a.cmd" }, _ => false);

        Assert.Equal("备份", parsed.GetValue("name"));
        Assert.Equal(@"D:\bat\a.cmd", parsed.GetValue("file"));
    }

    [Fact]
    public void Global_options_may_appear_before_or_after_the_command()
    {
        var before = CommandLineParser.Parse(new[] { "--json", "--db", @"C:\tmp\a.db", "groups", "list" }, _ => false);
        var after = CommandLineParser.Parse(new[] { "groups", "list", "--json", "--db", @"C:\tmp\a.db" }, _ => false);

        Assert.True(before.Json);
        Assert.True(after.Json);
        Assert.Equal(@"C:\tmp\a.db", before.DatabasePath);
        Assert.Equal(before.DatabasePath, after.DatabasePath);
        Assert.Equal("groups.list", before.CommandPath);
        Assert.Equal("groups.list", after.CommandPath);
    }

    [Fact]
    public void Help_flag_targets_the_specific_command()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "delete", "--help" }, _ => false);

        Assert.Equal("help.groups.delete", parsed.CommandPath);
    }

    [Fact]
    public void Bare_help_means_global_help()
    {
        Assert.Equal("help", CommandLineParser.Parse(new[] { "--help" }, _ => false).CommandPath);
        Assert.Equal("help", CommandLineParser.Parse(Array.Empty<string>(), _ => false).CommandPath);
    }

    [Fact]
    public void Unknown_verb_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "bogus" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Equal(1, error.ExitCode);
        Assert.Contains("bogus", error.Message);
    }

    [Fact]
    public void Unknown_subcommand_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "groups", "bogus" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("groups bogus", error.Message);
    }

    [Fact]
    public void Unknown_option_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "groups", "list", "--bogus" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--bogus", error.Message);
    }

    [Fact]
    public void Option_missing_value_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(new[] { "commands", "add", "--name" }, _ => false)).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--name", error.Message);
    }
}
