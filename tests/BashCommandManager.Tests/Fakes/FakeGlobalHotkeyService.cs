using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.Windows.Input;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
{
    public HotkeyRegistrationResult UpdateResult { get; set; } =
        new(HotkeyRegistrationStatus.Success, Key.B, ModifierKeys.Control | ModifierKeys.Alt);

    public int SuspendCount { get; private set; }

    public int ResumeCount { get; private set; }

    public bool UnregisterCalled { get; private set; }

    /// <summary>测试钩子：注销时触发，用于断言「先落盘再注销」的顺序。</summary>
    public Action? OnUnregister { get; set; }

    public bool IsRegistered { get; private set; }

    public event EventHandler? HotkeyPressed;

    public HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers) => UpdateResult;

    public HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers) => UpdateResult;

    public void Suspend() => SuspendCount++;

    public HotkeyRegistrationResult Resume()
    {
        ResumeCount++;
        // 与 #8 的真实实现一致：Resume() 返回「上次注册的结果」。
        return UpdateResult;
    }

    public void Unregister()
    {
        UnregisterCalled = true;
        IsRegistered = false;
        OnUnregister?.Invoke();
    }

    public void Dispose()
    {
    }

    private void RaiseHotkeyPressed() => HotkeyPressed?.Invoke(this, EventArgs.Empty);
}
