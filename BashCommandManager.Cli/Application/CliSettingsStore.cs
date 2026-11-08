using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Application;

/// <summary>settings.json 中的 Hotkey 段（与 GUI 的 HotkeySettings 同字段，纯整数避免 WPF 依赖）。</summary>
public sealed record HotkeyConfig(bool Enabled, int Key, int Modifiers);

/// <summary>CLI 的 settings.json 读写端口。</summary>
public interface ICliSettingsStore
{
    string SettingsPath { get; }

    HotkeyConfig GetHotkey();

    void SetHotkey(HotkeyConfig hotkey);

    void SetHotkeyEnabled(bool enabled);

    ExecutionSettings GetExecutionSettings();
}

/// <summary>用 JsonNode 就地读写：嵌套 {Hotkey,Execution} 与旧扁平格式都支持，且不覆盖其他节点。</summary>
public sealed class CliSettingsStore : ICliSettingsStore
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string> _readAllText;
    private readonly Action<string, string> _writeAllText;

    public CliSettingsStore(
        string settingsDirectory,
        Func<string, bool>? fileExists = null,
        Func<string, string>? readAllText = null,
        Action<string, string>? writeAllText = null)
    {
        SettingsPath = Path.Combine(settingsDirectory, "settings.json");
        _fileExists = fileExists ?? File.Exists;
        _readAllText = readAllText ?? (path => File.ReadAllText(path, Encoding.UTF8));
        _writeAllText = writeAllText ?? ((path, content) =>
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, content, new UTF8Encoding(false));
        });
    }

    public string SettingsPath { get; }

    public HotkeyConfig GetHotkey()
    {
        var root = LoadRoot();
        if (root is null)
        {
            return new HotkeyConfig(true, 45, 3);
        }

        if (root["Hotkey"] is JsonObject nested)
        {
            return new HotkeyConfig(
                ReadBool(nested["Enabled"], true),
                ReadInt(nested["Key"], 45),
                ReadInt(nested["Modifiers"], 3));
        }

        return new HotkeyConfig(
            ReadBool(root["Enabled"], true),
            ReadInt(root["Key"], 45),
            ReadInt(root["Modifiers"], 3));
    }

    public void SetHotkey(HotkeyConfig hotkey)
    {
        var root = LoadRoot() ?? new JsonObject();
        if (IsFlatHotkeyDocument(root))
        {
            // 旧扁平文档：保持扁平（迁移由 #9 的 SettingsService 负责），避免突然改形状
            root["Enabled"] = hotkey.Enabled;
            root["Key"] = hotkey.Key;
            root["Modifiers"] = hotkey.Modifiers;
        }
        else
        {
            // D3 唯一形状：文件不存在 / 空对象 / 已是嵌套文档（即使缺 Hotkey 节点）都写 { "Hotkey": {...} }
            var nested = root["Hotkey"] as JsonObject;
            if (nested is null)
            {
                nested = new JsonObject();
                root["Hotkey"] = nested;
            }
            nested["Enabled"] = hotkey.Enabled;
            nested["Key"] = hotkey.Key;
            nested["Modifiers"] = hotkey.Modifiers;
        }
        Save(root);
    }

    public void SetHotkeyEnabled(bool enabled)
    {
        var hotkey = GetHotkey() with { Enabled = enabled };
        SetHotkey(hotkey);
    }

    public ExecutionSettings GetExecutionSettings()
    {
        var root = LoadRoot();
        var section = root?["Execution"] as JsonObject;
        if (section is null)
        {
            return new ExecutionSettings();
        }

        return new ExecutionSettings
        {
            Terminal = ReadTerminal(section["Terminal"]),
            CustomExecutablePath = ReadString(section["CustomExecutablePath"]),
            ArgumentsTemplate = ReadString(section["ArgumentsTemplate"]),
            KeepWindowOpen = ReadBool(section["KeepWindowOpen"], true),
            RunAsAdministrator = ReadBool(section["RunAsAdministrator"], false)
        };
    }

    private JsonObject? LoadRoot()
    {
        if (!_fileExists(SettingsPath))
        {
            return null;
        }

        var text = _readAllText(SettingsPath);
        try
        {
            return JsonNode.Parse(text, NodeOptions, DocumentOptions) as JsonObject
                   ?? throw CliErrors.ConfigKey($"settings.json 根节点不是对象: {SettingsPath}");
        }
        catch (JsonException ex)
        {
            throw CliErrors.ConfigKey($"settings.json 解析失败: {SettingsPath} ({ex.Message})");
        }
    }

    private void Save(JsonObject root)
    {
        try
        {
            _writeAllText(SettingsPath, root.ToJsonString(WriteOptions));
        }
        catch (IOException ex)
        {
            throw CliErrors.Internal($"写入 settings.json 失败: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw CliErrors.Internal($"写入 settings.json 失败: {ex.Message}");
        }
    }

    // 旧扁平格式：根上直接有 Enabled/Key/Modifiers，且没有 Hotkey/Execution 段。
    // 其余情况（空文件、{}、只有 Execution、或已是嵌套）一律按 D3 的嵌套形状写。
    private static bool IsFlatHotkeyDocument(JsonObject root)
        => root["Hotkey"] is not JsonObject
           && root["Execution"] is not JsonObject
           && (root.ContainsKey("Enabled") || root.ContainsKey("Key") || root.ContainsKey("Modifiers"));

    private static int ReadInt(JsonNode? node, int fallback)
        => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    private static bool ReadBool(JsonNode? node, bool fallback)
        => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static string ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text?.Trim() ?? string.Empty : string.Empty;

    private static TerminalKind ReadTerminal(JsonNode? node)
    {
        var number = ReadInt(node, 0);
        return Enum.IsDefined(typeof(TerminalKind), number) ? (TerminalKind)number : TerminalKind.Cmd;
    }
}
