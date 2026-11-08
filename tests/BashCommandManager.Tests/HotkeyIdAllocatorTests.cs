using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyIdAllocatorTests
{
    [Fact]
    public void Next_returns_consecutive_ids_starting_at_0x9000()
    {
        var allocator = new HotkeyIdAllocator();

        Assert.Equal(0x9000, allocator.Next());
        Assert.Equal(0x9001, allocator.Next());
        Assert.Equal(0x9002, allocator.Next());
    }

    [Fact]
    public void Next_returns_0x90FF_last_and_then_wraps_back_to_0x9000()
    {
        var allocator = new HotkeyIdAllocator();

        var last = 0;
        for (var i = 0; i < 256; i++)
        {
            last = allocator.Next();
        }

        Assert.Equal(0x90FF, last);
        Assert.Equal(0x9000, allocator.Next());
    }
}
