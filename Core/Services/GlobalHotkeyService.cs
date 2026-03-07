using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace BashCommandManager.Core.Services;

public interface IGlobalHotkeyService
{
    bool Register(Window window, Key key, ModifierKeys modifiers);
    void Unregister();
    bool UpdateHotkey(Key key, ModifierKeys modifiers);
    event EventHandler? HotkeyPressed;
}

public class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    // Win32 API 定义
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    // Modifier keys
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private Window? _window;
    private HwndSource? _source;
    private Key _currentKey;
    private ModifierKeys _currentModifiers;
    private bool _isRegistered;

    public event EventHandler? HotkeyPressed;

    public bool Register(Window window, Key key, ModifierKeys modifiers)
    {
        _window = window;
        _currentKey = key;
        _currentModifiers = modifiers;

        var helper = new WindowInteropHelper(window);
        var handle = helper.Handle;

        // 确保窗口句柄已创建
        if (handle == IntPtr.Zero)
        {
            // 等待窗口加载完成
            window.SourceInitialized += Window_SourceInitialized;
            return false;
        }

        return RegisterHotkeyInternal(handle);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (_window != null)
        {
            _window.SourceInitialized -= Window_SourceInitialized;
            var helper = new WindowInteropHelper(_window);
            RegisterHotkeyInternal(helper.Handle);
        }
    }

    private bool RegisterHotkeyInternal(IntPtr handle)
    {
        if (_isRegistered)
        {
            UnregisterHotKey(handle, HOTKEY_ID);
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(_currentKey);
        var mod = ConvertModifiers(_currentModifiers);

        _isRegistered = RegisterHotKey(handle, HOTKEY_ID, mod, vk);

        if (_isRegistered)
        {
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(HwndHook);
        }

        return _isRegistered;
    }

    public void Unregister()
    {
        if (_window != null && _isRegistered)
        {
            var helper = new WindowInteropHelper(_window);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            _isRegistered = false;
        }

        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }
    }

    public bool UpdateHotkey(Key key, ModifierKeys modifiers)
    {
        if (_window == null) return false;

        Unregister();

        _currentKey = key;
        _currentModifiers = modifiers;

        var helper = new WindowInteropHelper(_window);
        return RegisterHotkeyInternal(helper.Handle);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint result = MOD_NONE;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            result |= MOD_ALT;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            result |= MOD_CONTROL;
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            result |= MOD_SHIFT;
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            result |= MOD_WIN;
        return result;
    }

    public void Dispose()
    {
        Unregister();
    }
}
