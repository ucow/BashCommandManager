using BashCommandManager.Core.Models;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyRegistrationResultTests
{
    [Fact]
    public void IsSuccess_is_true_only_for_the_Success_status()
    {
        Assert.True(HotkeyRegistrationResult.Success(Key.B, ModifierKeys.Control).IsSuccess);
        Assert.False(HotkeyRegistrationResult.InvalidCombination(Key.B, ModifierKeys.None).IsSuccess);
        Assert.False(HotkeyRegistrationResult.HostUnavailable(Key.B, ModifierKeys.Control).IsSuccess);
        Assert.False(HotkeyRegistrationResult.NotRegistered(Key.None, ModifierKeys.None).IsSuccess);
        Assert.False(HotkeyRegistrationResult.FromRegisterError(1409, Key.B, ModifierKeys.Control).IsSuccess);
    }

    [Fact]
    public void FromRegisterError_maps_1409_to_Conflict_because_the_combination_is_taken()
    {
        var result = HotkeyRegistrationResult.FromRegisterError(
            1409,
            Key.B,
            ModifierKeys.Control | ModifierKeys.Alt);

        Assert.Equal(HotkeyRegistrationStatus.Conflict, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(87)]
    public void FromRegisterError_maps_every_other_error_code_to_HostUnavailable(int win32Error)
    {
        var result = HotkeyRegistrationResult.FromRegisterError(win32Error, Key.B, ModifierKeys.Control);

        Assert.Equal(HotkeyRegistrationStatus.HostUnavailable, result.Status);
    }

    [Fact]
    public void Result_carries_the_combination_it_was_asked_to_register()
    {
        var result = HotkeyRegistrationResult.Success(Key.N, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal(Key.N, result.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, result.Modifiers);
    }
}
