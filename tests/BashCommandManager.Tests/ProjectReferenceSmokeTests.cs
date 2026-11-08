using BashCommandManager.Core.Models;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class ProjectReferenceSmokeTests
{
    [Fact]
    public void Test_project_resolves_types_from_the_main_application()
    {
        var settings = new HotkeySettings();

        Assert.True(settings.Enabled);
        Assert.Equal(Key.B, settings.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, settings.Modifiers);
    }
}
