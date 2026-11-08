namespace BashCommandManager.Core.Services;

/// <summary>
/// 热键要执行的窗口动作。WPF 实现在 Infrastructure，测试用假实现。
/// </summary>
public interface IWindowVisibilityController
{
    WindowActivitySnapshot Capture();

    void ShowAndActivate();

    void HideToTray();

    void ActivateExisting();
}
