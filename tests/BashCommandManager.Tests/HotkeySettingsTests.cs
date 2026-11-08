using BashCommandManager.Core.Models;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeySettingsTests
{
    [Fact]
    public void Clone_returns_an_equivalent_but_distinct_instance()
    {
        var original = new HotkeySettings
        {
            Enabled = true,
            Key = Key.N,
            Modifiers = ModifierKeys.Control | ModifierKeys.Shift
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Enabled, clone.Enabled);
        Assert.Equal(original.Key, clone.Key);
        Assert.Equal(original.Modifiers, clone.Modifiers);
    }

    [Fact]
    public void Clone_isolates_edits_from_the_original()
    {
        var original = new HotkeySettings
        {
            Enabled = true,
            Key = Key.B,
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt
        };

        var clone = original.Clone();
        clone.Key = Key.N;
        clone.Modifiers = ModifierKeys.Control;
        clone.Enabled = false;

        Assert.Equal(Key.B, original.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, original.Modifiers);
        Assert.True(original.Enabled);
    }

    [Fact]
    public void IsValid_is_false_when_no_modifier_is_pressed()
    {
        var settings = new HotkeySettings { Key = Key.B, Modifiers = ModifierKeys.None };

        Assert.False(HotkeySettings.IsValid(settings));
    }
}
