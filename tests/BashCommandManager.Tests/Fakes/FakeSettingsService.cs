using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeSettingsService : ISettingsService
{
    public HotkeySettings Hotkey { get; set; } = new();

    public ExecutionSettings Execution { get; set; } = new();

    public int SaveHotkeyCalls { get; private set; }

    public int SaveExecutionCalls { get; private set; }

    public event EventHandler? HotkeySettingsChanged;
    public event EventHandler? ExecutionSettingsChanged;

    public HotkeySettings GetHotkeySettings() => Hotkey;

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        SaveHotkeyCalls++;
        Hotkey = settings;
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public ExecutionSettings GetExecutionSettings() => Execution;

    public void SaveExecutionSettings(ExecutionSettings settings)
    {
        SaveExecutionCalls++;
        Execution = settings;
        ExecutionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
