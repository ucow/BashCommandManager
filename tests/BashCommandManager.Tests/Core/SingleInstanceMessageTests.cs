using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests.Core;

public class SingleInstanceMessageTests
{
    [Fact]
    public void Empty_args_format_is_plain_activate()
    {
        Assert.Equal("ACTIVATE", SingleInstanceMessage.Format(Array.Empty<string>()));
        Assert.Equal("ACTIVATE", SingleInstanceMessage.Format(null));
    }

    [Fact]
    public void Args_format_is_json_after_tab()
    {
        Assert.Equal("ACTIVATE\t[\"--command\",\"7\"]", SingleInstanceMessage.Format(new[] { "--command", "7" }));
    }

    [Fact]
    public void Parses_legacy_activate_without_args()
    {
        Assert.True(SingleInstanceMessage.TryParse("ACTIVATE", out var args));
        Assert.Empty(args);
    }

    [Fact]
    public void Parses_activate_with_args()
    {
        Assert.True(SingleInstanceMessage.TryParse("ACTIVATE\t[\"--command\",\"7\"]", out var args));
        Assert.Equal(new[] { "--command", "7" }, args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SOMETHING")]
    [InlineData("ACTIVATE\tnot-json")]
    public void Rejects_unknown_messages(string? message)
    {
        Assert.False(SingleInstanceMessage.TryParse(message, out var args));
        Assert.Empty(args);
    }

    [Fact]
    public void Round_trips()
    {
        var original = new[] { "--command", "42" };

        Assert.True(SingleInstanceMessage.TryParse(SingleInstanceMessage.Format(original), out var parsed));
        Assert.Equal(original, parsed);
    }
}
