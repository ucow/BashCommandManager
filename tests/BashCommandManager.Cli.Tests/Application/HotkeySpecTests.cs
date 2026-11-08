using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.Application;

public class HotkeySpecTests
{
    [Theory]
    [InlineData("A", 44)]
    [InlineData("Z", 69)]
    [InlineData("N", 57)]
    [InlineData("D0", 34)]
    [InlineData("D9", 43)]
    [InlineData("F1", 90)]
    [InlineData("F24", 113)]
    [InlineData("Space", 18)]
    [InlineData("Enter", 6)]
    [InlineData("Tab", 3)]
    [InlineData("Escape", 13)]
    [InlineData("Insert", 31)]
    [InlineData("Delete", 32)]
    [InlineData("Home", 22)]
    [InlineData("End", 21)]
    [InlineData("PageUp", 19)]
    [InlineData("PageDown", 20)]
    [InlineData("Left", 23)]
    [InlineData("Up", 24)]
    [InlineData("Right", 25)]
    [InlineData("Down", 26)]
    public void Maps_key_names_to_virtual_keys(string name, int expected)
    {
        Assert.True(HotkeyKeyMap.TryGetValue(name, out var virtualKey));
        Assert.Equal(expected, virtualKey);
        Assert.Equal(expected, HotkeyKeyMap.GetValue(name.ToLowerInvariant()));
    }

    [Fact]
    public void Parses_ctrl_shift_n_like_the_gui_writes_it()
    {
        var spec = HotkeySpec.Parse("Ctrl+Shift+N");

        Assert.Equal(57, spec.Key);
        Assert.Equal(6, spec.Modifiers);
        Assert.Equal("Ctrl+Shift+N", spec.Display);
    }

    [Fact]
    public void Parses_ctrl_alt_b()
    {
        var spec = HotkeySpec.Parse("ctrl+alt+b");

        Assert.Equal(45, spec.Key);
        Assert.Equal(3, spec.Modifiers);
        Assert.Equal("Ctrl+Alt+B", spec.Display);
    }

    [Fact]
    public void Modifier_order_is_normalized()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+N", HotkeySpec.Parse("Win+Shift+Alt+Ctrl+N").Display);
    }

    [Fact]
    public void Missing_modifier_is_invalid()
    {
        var error = Assert.Throws<CliFailureException>(() => HotkeySpec.Parse("N")).Error;

        Assert.Equal("E_INVALID_VALUE", error.Code);
        Assert.Equal(1, error.ExitCode);
    }

    [Fact]
    public void Unknown_key_is_invalid()
    {
        var error = Assert.Throws<CliFailureException>(() => HotkeySpec.Parse("Ctrl+Shift+Nope")).Error;

        Assert.Equal("E_INVALID_VALUE", error.Code);
        Assert.Contains("Nope", error.Message);
    }

    [Fact]
    public void Two_main_keys_are_invalid()
    {
        var error = Assert.Throws<CliFailureException>(() => HotkeySpec.Parse("Ctrl+N+M")).Error;

        Assert.Equal("E_INVALID_VALUE", error.Code);
    }

    [Fact]
    public void Create_round_trips_a_gui_written_pair()
    {
        var spec = HotkeySpec.Create(57, 6);

        Assert.Equal("N", spec.KeyName);
        Assert.Equal("Ctrl+Shift+N", spec.Display);
    }
}
