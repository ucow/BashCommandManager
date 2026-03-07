using BashCommandManager.Core.Models;
using System.IO;
using System.Text.Json;

namespace BashCommandManager.Core.Services;

public interface ISettingsService
{
    HotkeySettings GetHotkeySettings();
    void SaveHotkeySettings(HotkeySettings settings);
    event EventHandler? HotkeySettingsChanged;
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private HotkeySettings _hotkeySettings;

    public event EventHandler? HotkeySettingsChanged;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BashCommandManager");

        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _hotkeySettings = LoadHotkeySettings();
    }

    public HotkeySettings GetHotkeySettings()
    {
        return _hotkeySettings;
    }

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        _hotkeySettings = settings;
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private HotkeySettings LoadHotkeySettings()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<HotkeySettings>(json);
                if (settings != null && HotkeySettings.IsValid(settings))
                {
                    return settings;
                }
            }
            catch
            {
                // 读取失败时使用默认设置
            }
        }

        return new HotkeySettings();
    }
}
