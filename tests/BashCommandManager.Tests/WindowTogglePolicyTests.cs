using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests;

public class WindowTogglePolicyTests
{
    private readonly WindowTogglePolicy _policy = new();

    [Theory]
    [InlineData(false, false, true, false, WindowToggleAction.ShowAndActivate)]   // 托盘隐藏且状态为最小化
    [InlineData(false, false, false, false, WindowToggleAction.ShowAndActivate)]  // 已 Hide 但状态不是最小化
    [InlineData(false, true, true, false, WindowToggleAction.ShowAndActivate)]    // 可见但最小化
    [InlineData(false, true, false, true, WindowToggleAction.HideToTray)]         // 可见、非最小化、前台
    [InlineData(false, true, false, false, WindowToggleAction.ActivateExisting)]  // 可见、非最小化、非前台
    public void Decide_follows_the_decision_table(
        bool isAppModalOpen,
        bool isVisible,
        bool isMinimized,
        bool isActive,
        WindowToggleAction expected)
    {
        var snapshot = new WindowActivitySnapshot(isAppModalOpen, isVisible, isMinimized, isActive);

        Assert.Equal(expected, _policy.Decide(snapshot));
    }

    [Theory]
    [InlineData(true, false, true)]    // 模态 + 可见 + 非最小化 + 前台
    [InlineData(true, false, false)]   // 模态 + 可见 + 非最小化 + 非前台
    [InlineData(false, true, true)]    // 模态 + 隐藏 + 最小化（托盘状态）+ 前台
    [InlineData(false, true, false)]   // 模态 + 隐藏 + 最小化（托盘状态）+ 非前台
    public void Decide_never_hides_when_an_app_modal_window_is_open(
        bool isVisible,
        bool isMinimized,
        bool isActive)
    {
        var snapshot = new WindowActivitySnapshot(
            IsAppModalOpen: true,
            IsVisible: isVisible,
            IsMinimized: isMinimized,
            IsActive: isActive);

        Assert.Equal(WindowToggleAction.ActivateExisting, _policy.Decide(snapshot));
    }

    [Fact]
    public void Decide_is_stateless_and_returns_the_same_action_for_the_same_snapshot()
    {
        var snapshot = new WindowActivitySnapshot(
            IsAppModalOpen: false,
            IsVisible: true,
            IsMinimized: false,
            IsActive: true);

        Assert.Equal(WindowToggleAction.HideToTray, _policy.Decide(snapshot));
        Assert.Equal(WindowToggleAction.HideToTray, _policy.Decide(snapshot));
    }
}
