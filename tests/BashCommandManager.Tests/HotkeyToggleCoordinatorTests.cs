using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using Xunit;

namespace BashCommandManager.Tests;

public class HotkeyToggleCoordinatorTests
{
    private static (HotkeyToggleCoordinator Coordinator, FakeWindowVisibilityController Controller) Create(
        WindowActivitySnapshot snapshot)
    {
        var controller = new FakeWindowVisibilityController { Snapshot = snapshot };
        return (new HotkeyToggleCoordinator(new WindowTogglePolicy(), controller), controller);
    }

    [Fact]
    public void Execute_only_shows_and_activates_a_hidden_window()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, false, true, false));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.ShowAndActivate, action);
        Assert.Equal(new[] { "Capture", "ShowAndActivate" }, controller.Calls);
    }

    [Fact]
    public void Execute_only_hides_a_foreground_window_to_the_tray()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, true));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.HideToTray, action);
        Assert.Equal(new[] { "Capture", "HideToTray" }, controller.Calls);
    }

    [Fact]
    public void Execute_only_activates_a_visible_background_window()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, false));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.ActivateExisting, action);
        Assert.Equal(new[] { "Capture", "ActivateExisting" }, controller.Calls);
    }

    [Fact]
    public void Execute_never_hides_when_a_modal_window_is_open()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(true, true, false, true));

        var action = coordinator.Execute();

        Assert.Equal(WindowToggleAction.ActivateExisting, action);
        Assert.DoesNotContain(nameof(IWindowVisibilityController.HideToTray), controller.Calls);
    }

    [Fact]
    public void Execute_swallows_action_failures_so_the_ui_thread_survives()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, true));
        controller.ThrowOnHideToTray = new InvalidOperationException("boom");

        var exception = Record.Exception(() => coordinator.Execute());

        Assert.Null(exception);
        Assert.Contains(nameof(IWindowVisibilityController.HideToTray), controller.Calls);
    }

    [Fact]
    public void Execute_swallows_capture_failures_too()
    {
        var (coordinator, controller) = Create(new WindowActivitySnapshot(false, true, false, true));
        controller.ThrowOnCapture = new InvalidOperationException("capture failed");

        var exception = Record.Exception(() => coordinator.Execute());

        Assert.Null(exception);
        Assert.Equal(new[] { "Capture" }, controller.Calls);
    }

    [Fact]
    public void Execute_reports_the_action_it_performed_and_does_not_cache_the_decision()
    {
        var (coordinator, _) = Create(new WindowActivitySnapshot(false, true, false, true));

        Assert.Equal(WindowToggleAction.HideToTray, coordinator.Execute());
        Assert.Equal(WindowToggleAction.ShowAndActivate, coordinator.Execute());
    }
}
