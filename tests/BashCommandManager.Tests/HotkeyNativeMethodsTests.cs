using BashCommandManager.Core.Services;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyNativeMethodsTests
{
    [Theory]
    [InlineData(ModifierKeys.None, 0x0000u)]
    [InlineData(ModifierKeys.Alt, 0x0001u)]
    [InlineData(ModifierKeys.Control, 0x0002u)]
    [InlineData(ModifierKeys.Shift, 0x0004u)]
    [InlineData(ModifierKeys.Windows, 0x0008u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt, 0x0003u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, 0x0006u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, 0x0007u)]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, 0x000Fu)]
    public void ToWin32Modifiers_maps_each_modifier_to_its_user32_flag(ModifierKeys modifiers, uint expected)
    {
        Assert.Equal(expected, HotkeyNativeMethods.ToWin32Modifiers(modifiers));
    }

    [Fact]
    public void ToVirtualKey_maps_a_wpf_key_to_its_win32_virtual_key()
    {
        Assert.Equal(0x42, HotkeyNativeMethods.ToVirtualKey(Key.B));
    }
}
