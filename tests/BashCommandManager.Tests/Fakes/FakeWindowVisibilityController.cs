using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

/// <summary>
/// 记录调用顺序的假控制器：动作会改变自身快照，从而能验证协调器没有缓存决策结果。
/// </summary>
public sealed class FakeWindowVisibilityController : IWindowVisibilityController
{
    public WindowActivitySnapshot Snapshot { get; set; } =
        new(IsAppModalOpen: false, IsVisible: true, IsMinimized: false, IsActive: true);

    public List<string> Calls { get; } = new();

    public Exception? ThrowOnCapture { get; set; }
    public Exception? ThrowOnShowAndActivate { get; set; }
    public Exception? ThrowOnHideToTray { get; set; }
    public Exception? ThrowOnActivateExisting { get; set; }

    public WindowActivitySnapshot Capture()
    {
        Calls.Add(nameof(Capture));
        if (ThrowOnCapture is not null)
        {
            throw ThrowOnCapture;
        }

        return Snapshot;
    }

    public void ShowAndActivate()
    {
        Calls.Add(nameof(ShowAndActivate));
        if (ThrowOnShowAndActivate is not null)
        {
            throw ThrowOnShowAndActivate;
        }

        Snapshot = Snapshot with { IsVisible = true, IsMinimized = false, IsActive = true };
    }

    public void HideToTray()
    {
        Calls.Add(nameof(HideToTray));
        if (ThrowOnHideToTray is not null)
        {
            throw ThrowOnHideToTray;
        }

        Snapshot = Snapshot with { IsVisible = false, IsMinimized = true, IsActive = false };
    }

    public void ActivateExisting()
    {
        Calls.Add(nameof(ActivateExisting));
        if (ThrowOnActivateExisting is not null)
        {
            throw ThrowOnActivateExisting;
        }

        Snapshot = Snapshot with { IsActive = true };
    }
}
