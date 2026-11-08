using System.Windows.Input;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 把捕获到的 WPF 键归一化成可注册的真实键。
/// Alt 组合走 WM_SYSKEYDOWN 时 e.Key 是 Key.System、真实键在 e.SystemKey；
/// 若把 Key.System 直接交给 RegisterHotKey，虚拟键会是 0（注册返回 true 但永不触发）。
/// </summary>
public static class HotkeyKeyNormalizer
{
    /// <summary>
    /// e.Key == Key.System 时返回 systemKey（WPF 在该分支保证 systemKey 是真实键），否则原样返回 key。
    /// </summary>
    public static Key Normalize(Key key, Key systemKey)
    {
        return key == Key.System ? systemKey : key;
    }
}
