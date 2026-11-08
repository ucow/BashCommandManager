using BashCommandManager.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BashCommandManager.Core.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly Dispatcher _dispatcher;
    private readonly HotkeyMessageWindow _host = new();
    private readonly HotkeyIdAllocator _idAllocator = new();

    private Key _currentKey = Key.None;
    private ModifierKeys _currentModifiers = ModifierKeys.None;
    private int _currentId;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkeyService()
    {
        // RegisterHotKey 线程亲和：必须在有消息泵的 UI 线程创建与调用。
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _host.HotkeyIdReceived += OnHotkeyIdReceived;
    }

    public bool IsRegistered => _currentId != 0;

    public HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers)
    {
        _dispatcher.VerifyAccess();

        if (key == Key.None || modifiers == ModifierKeys.None)
        {
            return HotkeyRegistrationResult.InvalidCombination(key, modifiers);
        }

        if (!_host.EnsureCreated())
        {
            return HotkeyRegistrationResult.HostUnavailable(key, modifiers);
        }

        // 先注册新组合，成功后才注销旧 id：改键失败时用户不会变成「没有热键」。
        var newId = _idAllocator.Next();
        if (!RegisterNative(_host.Handle, newId, key, modifiers, out var win32Error))
        {
            return HotkeyRegistrationResult.FromRegisterError(win32Error, key, modifiers);
        }

        UnregisterNative(_host.Handle, _currentId);

        _currentId = newId;
        _currentKey = key;
        _currentModifiers = modifiers;
        return HotkeyRegistrationResult.Success(key, modifiers);
    }

    public HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers)
    {
        _dispatcher.VerifyAccess();

        // 同一组合且已注册：直接成功，避免自冲突（1409）。
        if (IsRegistered && key == _currentKey && modifiers == _currentModifiers)
        {
            return HotkeyRegistrationResult.Success(key, modifiers);
        }

        return TryRegister(key, modifiers);
    }

    public void Suspend()
    {
        _dispatcher.VerifyAccess();

        if (_currentId == 0)
        {
            return;
        }

        UnregisterNative(_host.Handle, _currentId);
        _currentId = 0;
    }

    public HotkeyRegistrationResult Resume()
    {
        _dispatcher.VerifyAccess();

        if (_currentKey == Key.None || _currentModifiers == ModifierKeys.None)
        {
            return HotkeyRegistrationResult.NotRegistered(_currentKey, _currentModifiers);
        }

        if (_currentId != 0)
        {
            return HotkeyRegistrationResult.Success(_currentKey, _currentModifiers);
        }

        return TryRegister(_currentKey, _currentModifiers);
    }

    public void Unregister()
    {
        _dispatcher.VerifyAccess();

        UnregisterNative(_host.Handle, _currentId);
        _currentId = 0;
        _currentKey = Key.None;
        _currentModifiers = ModifierKeys.None;
    }

    private void OnHotkeyIdReceived(object? sender, int id)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(new Action(() => OnHotkeyIdReceived(sender, id)));
            return;
        }

        // 只认当前注册的 id：改键/挂起期间漏进来的旧 id 消息必须忽略。
        if (_currentId == 0 || id != _currentId)
        {
            return;
        }

        HotkeyPressed?.Invoke(this, EventArgs.Empty);
    }

    private static bool RegisterNative(IntPtr hwnd, int id, Key key, ModifierKeys modifiers, out int win32Error)
    {
        var registered = HotkeyNativeMethods.RegisterHotKey(
            hwnd,
            id,
            HotkeyNativeMethods.ToWin32Modifiers(modifiers),
            (uint)HotkeyNativeMethods.ToVirtualKey(key));

        win32Error = registered ? 0 : Marshal.GetLastWin32Error();
        return registered;
    }

    private static void UnregisterNative(IntPtr hwnd, int id)
    {
        if (id == 0 || hwnd == IntPtr.Zero)
        {
            return;
        }

        if (HotkeyNativeMethods.UnregisterHotKey(hwnd, id))
        {
            return;
        }

        // 注销失败只记日志，但仍清空内部状态，避免僵尸注册挡住后续尝试。
        Debug.WriteLine($"注销热键失败: id={id}, hwnd=0x{hwnd.ToInt64():X}, error={Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        Unregister();
        _host.Dispose();
    }
}
