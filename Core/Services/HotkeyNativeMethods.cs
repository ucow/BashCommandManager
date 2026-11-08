using System.Runtime.InteropServices;
using System.Windows.Input;

namespace BashCommandManager.Core.Services;

/// <summary>
/// user32 的最薄封装：只放常量、P/Invoke 与纯换算，任何决策都不在这里。
/// </summary>
public static class HotkeyNativeMethods
{
    public const int WmHotkey = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;

        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            result |= ModAlt;
        }

        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            result |= ModControl;
        }

        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            result |= ModShift;
        }

        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
        {
            result |= ModWin;
        }

        return result;
    }

    public static int ToVirtualKey(Key key) => KeyInterop.VirtualKeyFromKey(key);
}
