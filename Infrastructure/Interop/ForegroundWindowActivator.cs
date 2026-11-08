using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BashCommandManager.Infrastructure.Interop;

public interface IForegroundWindowActivator
{
    bool TryActivate(IntPtr windowHandle);
}

/// <summary>
/// 尽力把窗口拉到前台：先 SetForegroundWindow；被前台锁拒绝时把本线程输入状态临时
/// AttachThreadInput 到前台线程再试，finally 必须解挂。
/// </summary>
public sealed class ForegroundWindowActivator : IForegroundWindowActivator
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    public bool TryActivate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (GetForegroundWindow() == windowHandle)
        {
            return true;
        }

        if (SetForegroundWindow(windowHandle))
        {
            return true;
        }

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
                       && foregroundThread != currentThread
                       && AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
            SetFocus(windowHandle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        var activated = GetForegroundWindow() == windowHandle;
        if (!activated)
        {
            Debug.WriteLine($"前台激活未能生效: hwnd=0x{windowHandle.ToInt64():X}（窗口仍在任务栏可见）");
        }

        return activated;
    }
}
