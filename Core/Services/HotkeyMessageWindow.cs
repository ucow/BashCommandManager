using System.Diagnostics;
using System.Windows.Interop;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 进程级 message-only 窗口（Parent = HWND_MESSAGE）：不参与枚举、不显示，
/// 但仍是真实 HWND，能接收系统投递的 WM_HOTKEY。生命周期与主窗口无关。
/// </summary>
internal sealed class HotkeyMessageWindow : IDisposable
{
    /// <summary>HWND_MESSAGE，即 (HWND)(-3)。</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    private HwndSource? _source;
    private bool _hookAdded;

    public IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    public event EventHandler<int>? HotkeyIdReceived;

    /// <summary>
    /// 幂等建窗：先尝试 message-only，失败降级为 0×0 隐藏窗口，两者都失败返回 false。
    /// </summary>
    public bool EnsureCreated()
    {
        if (_source is not null)
        {
            return _source.Handle != IntPtr.Zero;
        }

        _source = Create(HwndMessage) ?? Create(IntPtr.Zero);
        if (_source is null)
        {
            return false;
        }

        if (!_hookAdded)
        {
            _source.AddHook(WndProc);
            _hookAdded = true;
        }

        return _source.Handle != IntPtr.Zero;
    }

    private static HwndSource? Create(IntPtr parent)
    {
        try
        {
            var source = new HwndSource(new HwndSourceParameters("BashCommandManager.HotkeyHost")
            {
                WindowStyle = 0,
                ParentWindow = parent,
                Width = 0,
                Height = 0
            });

            if (source.Handle != IntPtr.Zero)
            {
                return source;
            }

            Debug.WriteLine("热键宿主创建失败：HwndSource 句柄为 0");
            source.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"热键宿主创建失败: {ex.Message}");
        }

        return null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNativeMethods.WmHotkey)
        {
            HotkeyIdReceived?.Invoke(this, wParam.ToInt32());
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 必须在创建它的 UI 线程调用；重复调用无副作用。
    /// </summary>
    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        if (_hookAdded)
        {
            _source.RemoveHook(WndProc);
            _hookAdded = false;
        }

        _source.Dispose();
        _source = null;
    }
}
