using BashCommandManager.Cli.Output;

namespace BashCommandManager.Cli.Tests.Output;

public class TableWriterTests
{
    [Fact]
    public void Renders_aligned_columns_with_two_space_gap()
    {
        var text = TableWriter.Render(
            new[] { "Id", "Name" },
            new IReadOnlyList<string>[]
            {
                new[] { "7", "备份" },
                new[] { "10", "deploy" }
            });

        var lines = text.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("Id  Name", lines[0]);
        Assert.Equal("7   备份", lines[1]);
        Assert.Equal("10  deploy", lines[2]);
    }

    [Fact]
    public void Counts_cjk_characters_as_two_columns()
    {
        Assert.Equal(2, TableWriter.DisplayWidth("备"));
        Assert.Equal(4, TableWriter.DisplayWidth("备份"));
        Assert.Equal(6, TableWriter.DisplayWidth("备份库"));
        Assert.Equal(3, TableWriter.DisplayWidth("abc"));
    }

    [Fact]
    public void Empty_rows_render_only_headers()
    {
        var text = TableWriter.Render(new[] { "Id" }, Array.Empty<IReadOnlyList<string>>());

        Assert.Equal("Id", text);
    }
}
