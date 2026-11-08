using BashCommandManager.Cli.CommandLine;

namespace BashCommandManager.Cli.Tests.CommandLine;

public class HelpCatalogTests
{
    [Fact]
    public void Catalog_defines_exactly_19_commands()
    {
        Assert.Equal(19, CommandCatalog.Commands.Count);
    }

    [Fact]
    public void Global_help_lists_every_command_usage_and_summary()
    {
        var help = HelpCatalog.Global();

        foreach (var spec in CommandCatalog.Commands)
        {
            if (spec.Path == "version")
            {
                Assert.Contains("--version", help);
            }
            else
            {
                Assert.Contains(spec.Path.Replace('.', ' '), help);
            }
            Assert.Contains(spec.Summary, help);
        }
        Assert.Contains("退出码", help);
        Assert.Contains("0 成功", help);
    }

    [Fact]
    public void Global_help_documents_global_options()
    {
        var help = HelpCatalog.Global();

        Assert.Contains("--json", help);
        Assert.Contains("--db <path>", help);
        Assert.Contains("--help", help);
        Assert.Contains("--version", help);
    }

    // I-4：SETTINGS_DIR 会同时重定向数据库，必须在 --help 里可发现。
    [Fact]
    public void Global_help_documents_environment_variables()
    {
        var help = HelpCatalog.Global();

        Assert.Contains("环境变量", help);
        Assert.Contains("BASHCOMMANDMANAGER_DB", help);
        Assert.Contains("BASHCOMMANDMANAGER_SETTINGS_DIR", help);
        Assert.Contains("BASHCOMMANDMANAGER_GUI", help);
        Assert.Contains("data\\app.db", help);
    }

    [Theory]
    [InlineData("groups.list")]
    [InlineData("groups.create")]
    [InlineData("groups.rename")]
    [InlineData("groups.delete")]
    [InlineData("commands.list")]
    [InlineData("commands.search")]
    [InlineData("commands.show")]
    [InlineData("commands.add")]
    [InlineData("commands.edit")]
    [InlineData("commands.delete")]
    [InlineData("commands.move")]
    [InlineData("commands.import")]
    [InlineData("commands.export")]
    [InlineData("commands.frequent")]
    [InlineData("run")]
    [InlineData("config.get")]
    [InlineData("config.set")]
    [InlineData("open")]
    [InlineData("version")]
    public void Each_command_help_contains_usage_and_example(string path)
    {
        var spec = CommandCatalog.Find(path);
        Assert.NotNull(spec);

        var help = HelpCatalog.For(path);

        Assert.Contains(spec!.Usage, help);
        Assert.Contains(spec.Summary, help);
        Assert.Contains(spec.Examples[0], help);
        if (spec.Options.Count > 0)
        {
            Assert.Contains(spec.Options[0].HelpLine, help);
        }
    }
}
