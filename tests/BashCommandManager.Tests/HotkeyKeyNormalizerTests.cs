using BashCommandManager.Core.Services;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyKeyNormalizerTests
{
    [Fact]
    public void Normalize_returns_the_system_key_when_wpf_reports_Key_System()
    {
        // Alt 组合（WM_SYSKEYDOWN）下 WPF 把 e.Key 置为 Key.System，真实键在 e.SystemKey；
        // 若原样保存 Key.System，RegisterHotKey 的 vk 会是 0（返回 true 但永不触发）。
        Assert.Equal(Key.N, HotkeyKeyNormalizer.Normalize(Key.System, Key.N));
    }

    [Fact]
    public void Normalize_returns_the_key_unchanged_for_a_plain_key()
    {
        Assert.Equal(Key.N, HotkeyKeyNormalizer.Normalize(Key.N, Key.None));
    }
}
