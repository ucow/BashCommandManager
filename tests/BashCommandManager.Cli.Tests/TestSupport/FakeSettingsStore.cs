using BashCommandManager.Cli.Application;
using BashCommandManager.Core.Models;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>内存版 settings，用于处理器单测（不碰真实 settings.json）。</summary>
internal sealed class FakeSettingsStore : ICliSettingsStore
{
    public FakeSettingsStore(
        HotkeyConfig? hotkey = null,
        ExecutionSettings? execution = null,
        string settingsPath = @"C:\fake\settings.json")
    {
        Hotkey = hotkey ?? new HotkeyConfig(true, 57, 6);
        Execution = execution ?? new ExecutionSettings();
        SettingsPath = settingsPath;
    }

    public HotkeyConfig Hotkey { get; private set; }

    public ExecutionSettings Execution { get; }

    public string SettingsPath { get; }

    public HotkeyConfig GetHotkey() => Hotkey;

    public void SetHotkey(HotkeyConfig hotkey) => Hotkey = hotkey;

    public void SetHotkeyEnabled(bool enabled) => Hotkey = Hotkey with { Enabled = enabled };

    public ExecutionSettings GetExecutionSettings() => Execution;
}
