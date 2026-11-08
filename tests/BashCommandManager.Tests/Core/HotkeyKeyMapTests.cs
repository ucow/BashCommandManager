using BashCommandManager.Cli.Application;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests.Core;

/// <summary>把 CLI 手写键码表与 WPF Key 枚举逐值交叉断言；键语义若漂移会立刻红。</summary>
public class HotkeyKeyMapTests
{
    [Theory]
    [InlineData("A", 44)]
    [InlineData("Z", 69)]
    [InlineData("D0", 34)]
    [InlineData("F1", 90)]
    [InlineData("Space", 18)]
    [InlineData("Enter", 6)]
    public void Sample_keys_match_wpf_enum(string name, int expected)
    {
        Assert.Equal((int)Enum.Parse<Key>(name, ignoreCase: true), HotkeyKeyMap.GetValue(name));
        Assert.Equal(expected, HotkeyKeyMap.GetValue(name));
    }

    [Fact]
    public void Every_named_key_matches_wpf_enum()
    {
        // "0".."9" 是 CLI 额外接受的数字别名，不是 Key 的枚举名，跳过
        foreach (var name in HotkeyKeyMap.Names.Where(name => !char.IsDigit(name[0])))
        {
            Assert.Equal((int)Enum.Parse<Key>(name, ignoreCase: true), HotkeyKeyMap.GetValue(name));
        }
    }
}
