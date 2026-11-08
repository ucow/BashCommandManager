using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeSettingsService : ISettingsService
{
    public HotkeySettings Hotkey { get; set; } = new();

    public ExecutionSettings Execution { get; set; } = new();

    public int SaveHotkeyCalls { get; private set; }

    public int SaveExecutionCalls { get; private set; }

    /// <summary>测试钩子：保存热键时触发，用于断言保存/注销顺序。</summary>
    public Action<HotkeySettings>? OnSaveHotkey { get; set; }

    public Exception? SaveHotkeyError { get; set; }

    public Exception? SaveExecutionError { get; set; }

    public event EventHandler? HotkeySettingsChanged;
    public event EventHandler? ExecutionSettingsChanged;

    public HotkeySettings GetHotkeySettings() => Hotkey;

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        SaveHotkeyCalls++;
        OnSaveHotkey?.Invoke(settings);

        if (SaveHotkeyError is not null)
        {
            throw SaveHotkeyError;
        }

        Hotkey = settings;
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ExecutionSettings GetExecutionSettings() => Execution;

    public void SaveExecutionSettings(ExecutionSettings settings)
    {
        SaveExecutionCalls++;

        if (SaveExecutionError is not null)
        {
            throw SaveExecutionError;
        }

        Execution = settings;
        ExecutionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
