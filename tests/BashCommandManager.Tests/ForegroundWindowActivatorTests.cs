using BashCommandManager.Infrastructure.Interop;
using Xunit;

namespace BashCommandManager.Tests;

public class ForegroundWindowActivatorTests
{
    [Fact]
    public void TryActivate_returns_false_for_a_zero_handle_without_calling_win32()
    {
        var activator = new ForegroundWindowActivator();

        Assert.False(activator.TryActivate(IntPtr.Zero));
    }
}
