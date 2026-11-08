using BashCommandManager.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BashCommandManager.Core.Services;

public interface ISettingsService
{
    HotkeySettings GetHotkeySettings();
    void SaveHotkeySettings(HotkeySettings settings);
    event EventHandler? HotkeySettingsChanged;

    ExecutionSettings GetExecutionSettings();
    void SaveExecutionSettings(ExecutionSettings settings);
    event EventHandler? ExecutionSettingsChanged;
}

/// <summary>settings.json 的唯一读写入口：嵌套 AppSettings、旧格式迁移、原子写与备份。</summary>
public class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string TempFileName = "settings.json.tmp";
    private const string LegacyBackupFileName = "settings.json.v1.bak";
    private const string CorruptBackupFileName = "settings.json.corrupt.bak";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private AppSettings _appSettings;
    private bool _needsMigration;

    public event EventHandler? HotkeySettingsChanged;
    public event EventHandler? ExecutionSettingsChanged;

    public SettingsService(string? settingsDirectory = null)
    {
        _settingsDirectory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BashCommandManager");
        _settingsPath = Path.Combine(_settingsDirectory, SettingsFileName);

        if (!Directory.Exists(_settingsDirectory))
        {
            Directory.CreateDirectory(_settingsDirectory);
        }

        (_appSettings, _needsMigration) = LoadAll();
    }

    public HotkeySettings GetHotkeySettings() => _appSettings.Hotkey;

    public ExecutionSettings GetExecutionSettings() => _appSettings.Execution;

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        _appSettings.Hotkey = settings ?? new HotkeySettings();
        SaveAll();
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveExecutionSettings(ExecutionSettings settings)
    {
        _appSettings.Execution = settings ?? new ExecutionSettings();
        SaveAll();
        ExecutionSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private (AppSettings Settings, bool NeedsMigration) LoadAll()
    {
        if (!File.Exists(_settingsPath))
        {
            return Normalize(new AppSettings(), false);
        }

        string json;
        try
        {
            json = File.ReadAllText(_settingsPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取 settings.json 失败: {ex.Message}");
            BackupCorrupt();
            return Normalize(new AppSettings(), false);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = document.RootElement;
            if (HasProperty(root, "Hotkey") || HasProperty(root, "Execution"))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
                return Normalize(settings, false);
            }

            if (HasProperty(root, "Enabled") || HasProperty(root, "Key") || HasProperty(root, "Modifiers"))
            {
                var hotkey = JsonSerializer.Deserialize<HotkeySettings>(json, SerializerOptions) ?? new HotkeySettings();
                var settings = new AppSettings { Hotkey = hotkey };
                return Normalize(settings, true);
            }

            return Normalize(new AppSettings(), false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解析 settings.json 失败: {ex.Message}");
            BackupCorrupt();
            return Normalize(new AppSettings(), false);
        }
    }

    private static (AppSettings Settings, bool NeedsMigration) Normalize(AppSettings settings, bool needsMigration)
    {
        if (settings.Hotkey is null || !HotkeySettings.IsValid(settings.Hotkey))
        {
            settings.Hotkey = new HotkeySettings();
        }

        if (settings.Execution is null)
        {
            settings.Execution = new ExecutionSettings();
        }

        if (!Enum.IsDefined(settings.Execution.Terminal))
        {
            settings.Execution.Terminal = TerminalKind.Cmd;
        }

        settings.Execution.CustomExecutablePath =
            (settings.Execution.CustomExecutablePath ?? string.Empty).Trim();

        var template = settings.Execution.ArgumentsTemplate;
        settings.Execution.ArgumentsTemplate = string.IsNullOrWhiteSpace(template)
            ? TerminalCommandBuilder.GetDefaultTemplate(
                settings.Execution.Terminal,
                settings.Execution.KeepWindowOpen)
            : template.Trim();

        return (settings, needsMigration);
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void SaveAll()
    {
        Directory.CreateDirectory(_settingsDirectory);

        var migrating = _needsMigration;
        if (migrating)
        {
            BackupLegacy();
        }

        var json = JsonSerializer.Serialize(_appSettings, SerializerOptions);
        var tempPath = Path.Combine(_settingsDirectory, TempFileName);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);

        if (migrating)
        {
            _needsMigration = false;
        }
    }

    private void BackupLegacy()
    {
        var backupPath = Path.Combine(_settingsDirectory, LegacyBackupFileName);
        if (File.Exists(backupPath) || !File.Exists(_settingsPath))
        {
            return;
        }

        File.Copy(_settingsPath, backupPath, overwrite: false);
    }

    private void BackupCorrupt()
    {
        var backupPath = Path.Combine(_settingsDirectory, CorruptBackupFileName);
        if (File.Exists(backupPath) || !File.Exists(_settingsPath))
        {
            return;
        }

        try
        {
            File.Copy(_settingsPath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"备份损坏的 settings.json 失败: {ex.Message}");
        }
    }
}
