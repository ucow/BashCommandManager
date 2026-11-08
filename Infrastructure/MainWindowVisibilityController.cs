using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure.Interop;
using System.Windows;
using System.Windows.Interop;

namespace BashCommandManager.Infrastructure;

/// <summary>
/// 把热键动作翻译成对主窗口的 WPF 操作。只认识 Window，不认识热键。
/// </summary>
public sealed class MainWindowVisibilityController : IWindowVisibilityController
{
    private readonly Window _window;
    private readonly IForegroundWindowActivator _activator;

    public MainWindowVisibilityController(Window window, IForegroundWindowActivator activator)
    {
        _window = window;
        _activator = activator;
    }

    public WindowActivitySnapshot Capture()
    {
        return new WindowActivitySnapshot(
            IsAppModalOpen: _window.OwnedWindows.OfType<Window>().Any(w => w.IsVisible),
            IsVisible: _window.IsVisible,
            IsMinimized: _window.WindowState == WindowState.Minimized,
            IsActive: _window.IsActive);
    }

    public void ShowAndActivate()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _activator.TryActivate(new WindowInteropHelper(_window).Handle);
    }

    public void HideToTray()
    {
        _window.WindowState = WindowState.Minimized;
        _window.Hide();
    }

    public void ActivateExisting()
    {
        var modalWindow = _window.OwnedWindows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
        if (modalWindow is not null)
        {
            modalWindow.Activate();
            _activator.TryActivate(new WindowInteropHelper(modalWindow).Handle);
            return;
        }

        _window.Activate();
        _activator.TryActivate(new WindowInteropHelper(_window).Handle);
    }
}
