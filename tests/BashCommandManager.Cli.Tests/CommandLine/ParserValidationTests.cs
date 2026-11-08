using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class ParserValidationTests
{
    private static CliError Fails(params string[] args)
        => Assert.Throws<CliFailureException>(() => CommandLineParser.Parse(args, _ => false)).Error;

    [Fact]
    public void Missing_required_option_lists_an_example()
    {
        var error = Fails("groups", "create", "--parent", "1");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--name", error.Message);
        Assert.Contains("bcm groups create", error.Hint);
    }

    [Fact]
    public void Delete_without_force_requires_confirmation()
    {
        var groups = Fails("groups", "delete", "运维");
        var commands = Fails("commands", "delete", "7");

        Assert.Equal("E_CONFIRM_REQUIRED", groups.Code);
        Assert.Equal(1, groups.ExitCode);
        Assert.Contains("--force", groups.Hint);
        Assert.Equal("E_CONFIRM_REQUIRED", commands.Code);
    }

    [Fact]
    public void Too_many_positionals_is_usage_error()
    {
        var error = Fails("groups", "rename", "2", "3", "--name", "x");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("位置参数", error.Message);
    }

    [Fact]
    public void Missing_positional_is_usage_error()
    {
        var error = Fails("commands", "show");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("bcm commands show", error.Message);
    }

    [Fact]
    public void Illegal_enum_value_is_invalid_value()
    {
        var error = Fails("commands", "list", "--sort", "size");

        Assert.Equal("E_INVALID_VALUE", error.Code);
        Assert.Contains("size", error.Message);
        Assert.Contains("name,last,count", error.Hint);
    }

    [Fact]
    public void Negative_limit_is_invalid_value()
    {
        // 值 "-1" 不以 "--" 开头，会被当作 --limit 的值，随后在语义校验里被判非法
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "list", "--limit", "-1").Code);
    }

    [Fact]
    public void Option_value_starting_with_double_dash_is_treated_as_missing()
    {
        var error = Fails("commands", "search", "--limit", "--json", "备份");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--limit", error.Message);
    }

    [Fact]
    public void Non_numeric_limit_is_invalid_value()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "list", "--limit", "abc").Code);
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "frequent", "--limit", "0").Code);
        Assert.Equal("E_INVALID_VALUE", Fails("commands", "frequent", "--limit", "1001").Code);
    }

    [Fact]
    public void Admin_with_wait_is_usage_error()
    {
        var error = Fails("run", "备份", "--admin", "--wait");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--admin", error.Message);
    }

    [Fact]
    public void Timeout_without_wait_is_usage_error()
    {
        var error = Fails("run", "备份", "--timeout", "30");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--timeout", error.Message);
    }

    [Fact]
    public void Non_positive_timeout_is_invalid_value()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("run", "备份", "--wait", "--timeout", "0").Code);
    }

    [Fact]
    public void Db_pointing_to_directory_is_usage_error()
    {
        var error = Assert.Throws<CliFailureException>(() =>
            CommandLineParser.Parse(new[] { "groups", "list", "--db", @"C:\temp" }, path => path.EndsWith("temp", StringComparison.OrdinalIgnoreCase))).Error;

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--db", error.Message);
    }

    [Fact]
    public void Db_path_is_made_absolute()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "list", "--db", "relative.db" }, _ => false);

        Assert.True(Path.IsPathRooted(parsed.DatabasePath));
        Assert.EndsWith("relative.db", parsed.DatabasePath);
    }

    [Fact]
    public void Edit_without_any_change_is_usage_error()
    {
        var error = Fails("commands", "edit", "7");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--name", error.Message);
    }

    [Fact]
    public void Csv_export_requires_output()
    {
        var error = Fails("commands", "export", "--format", "csv");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("--output", error.Message);
    }

    [Fact]
    public void Unknown_config_key_is_config_key_error()
    {
        Assert.Equal("E_CONFIG_KEY", Fails("config", "get", "nope").Code);
        Assert.Equal("E_CONFIG_KEY", Fails("config", "set", "nope", "x").Code);
    }

    [Fact]
    public void Config_set_hotkey_enabled_requires_boolean()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("config", "set", "hotkey.enabled", "yes").Code);
    }

    [Fact]
    public void Open_command_id_must_be_positive_integer()
    {
        Assert.Equal("E_INVALID_VALUE", Fails("open", "--command", "abc").Code);
        Assert.Equal("E_INVALID_VALUE", Fails("open", "--command", "0").Code);
    }

    [Fact]
    public void Option_not_belonging_to_command_is_usage_error()
    {
        var error = Fails("groups", "list", "--force");

        Assert.Equal("E_USAGE", error.Code);
        Assert.Contains("groups.list", error.Message);
    }

    [Fact]
    public void Delete_help_bypasses_force_requirement()
    {
        var parsed = CommandLineParser.Parse(new[] { "groups", "delete", "--help" }, _ => false);

        Assert.Equal("help.groups.delete", parsed.CommandPath);
    }
}
