namespace BashCommandManager.Core.Services;

/// <summary>
/// 一次热键触发时的窗口状态快照。刻意不引用任何 WPF 类型，便于单测。
/// </summary>
public readonly record struct WindowActivitySnapshot(
    bool IsAppModalOpen,
    bool IsVisible,
    bool IsMinimized,
    bool IsActive);

public enum WindowToggleAction
{
    ShowAndActivate,
    HideToTray,
    ActivateExisting
}

public interface IWindowTogglePolicy
{
    WindowToggleAction Decide(WindowActivitySnapshot snapshot);
}

/// <summary>
/// 纯决策表：模态窗口优先激活，其次隐藏/最小化则显示，再次前台则收进托盘，其余只激活。
/// </summary>
public sealed class WindowTogglePolicy : IWindowTogglePolicy
{
    public WindowToggleAction Decide(WindowActivitySnapshot snapshot)
    {
        if (snapshot.IsAppModalOpen)
        {
            return WindowToggleAction.ActivateExisting;
        }

        if (!snapshot.IsVisible || snapshot.IsMinimized)
        {
            return WindowToggleAction.ShowAndActivate;
        }

        if (snapshot.IsActive)
        {
            return WindowToggleAction.HideToTray;
        }

        return WindowToggleAction.ActivateExisting;
    }
}
