using System.Diagnostics;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 「取快照 → 决策 → 执行」。异常一律吞掉并写 Debug，绝不让热键回调把 UI 线程带崩。
/// </summary>
public sealed class HotkeyToggleCoordinator
{
    private readonly IWindowTogglePolicy _policy;
    private readonly IWindowVisibilityController _controller;

    public HotkeyToggleCoordinator(IWindowTogglePolicy policy, IWindowVisibilityController controller)
    {
        _policy = policy;
        _controller = controller;
    }

    public WindowToggleAction Execute()
    {
        // 兜底为非破坏性动作：快照都取不到时不要动窗口。
        var action = WindowToggleAction.ActivateExisting;

        try
        {
            action = _policy.Decide(_controller.Capture());

            switch (action)
            {
                case WindowToggleAction.ShowAndActivate:
                    _controller.ShowAndActivate();
                    break;
                case WindowToggleAction.HideToTray:
                    _controller.HideToTray();
                    break;
                case WindowToggleAction.ActivateExisting:
                    _controller.ActivateExisting();
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"热键切换窗口失败（动作 {action}）: {ex}");
        }

        return action;
    }
}
