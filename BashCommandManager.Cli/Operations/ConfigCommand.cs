using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Infrastructure;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>config get/set：只读写 settings.json 与只读路径信息。</summary>
public static class ConfigCommand
{
    public static Task<CliResult> GetAsync(ParsedCommand command, CliServices services)
    {
        var key = command.Arguments.Count == 1 ? command.Arguments[0] : null;
        var hotkey = services.Settings.GetHotkey();
        var spec = HotkeySpec.Create(hotkey.Key, hotkey.Modifiers);
        var guiPath = services.Launcher.ResolveGuiPath(null);

        var db = new JsonObject
        {
            ["path"] = services.DatabaseResolution.DatabasePath,
            ["source"] = SourceName(services.MetaSource)
        };
        var settings = new JsonObject { ["path"] = services.Settings.SettingsPath };
        var hotkeyNode = new JsonObject
        {
            ["enabled"] = hotkey.Enabled,
            ["key"] = hotkey.Key,
            ["keyName"] = spec.KeyName,
            ["modifiers"] = hotkey.Modifiers,
            ["display"] = spec.Display
        };
        var gui = new JsonObject { ["path"] = guiPath is null ? null : guiPath };

        var full = new JsonObject
        {
            ["db"] = db,
            ["settings"] = settings,
            ["hotkey"] = hotkeyNode,
            ["gui"] = gui,
            ["version"] = CliVersion.Value
        };

        JsonNode data = key switch
        {
            null => full,
            "hotkey" or "hotkey.enabled" => new JsonObject { ["hotkey"] = hotkeyNode.DeepClone() },
            "db.path" or "db.source" => new JsonObject { ["db"] = db.DeepClone() },
            "settings.path" => new JsonObject { ["settings"] = settings.DeepClone() },
            "gui.path" => new JsonObject { ["gui"] = gui.DeepClone() },
            "version" => new JsonObject { ["version"] = CliVersion.Value },
            _ => throw CliErrors.ConfigKey($"未知配置键: {key}")
        };

        return Task.FromResult(new CliResult(data, null, RenderHuman(data)));
    }

    public static Task<CliResult> SetAsync(ParsedCommand command, CliServices services)
    {
        var key = command.Arguments[0];
        var value = command.Arguments[1];

        switch (key)
        {
            case "hotkey":
            {
                var spec = HotkeySpec.Parse(value);
                var current = services.Settings.GetHotkey();
                var updated = new HotkeyConfig(current.Enabled, spec.Key, spec.Modifiers);
                services.Settings.SetHotkey(updated);
                return Task.FromResult(HotkeyResult(updated));
            }
            case "hotkey.enabled":
            {
                var enabled = bool.Parse(value);
                services.Settings.SetHotkeyEnabled(enabled);
                return Task.FromResult(HotkeyResult(services.Settings.GetHotkey()));
            }
            default:
                throw CliErrors.ConfigKey($"未知配置键: {key}", "可用键: hotkey,hotkey.enabled");
        }
    }

    public static string SourceName(DatabasePathSource source) => source switch
    {
        DatabasePathSource.Argument => "argument",
        DatabasePathSource.Environment => "environment",
        DatabasePathSource.LegacyApplicationDirectory => "legacy",
        _ => "localAppData"
    };

    private static CliResult HotkeyResult(HotkeyConfig hotkey)
    {
        var spec = HotkeySpec.Create(hotkey.Key, hotkey.Modifiers);
        var node = new JsonObject
        {
            ["enabled"] = hotkey.Enabled,
            ["key"] = hotkey.Key,
            ["keyName"] = spec.KeyName,
            ["modifiers"] = hotkey.Modifiers,
            ["display"] = spec.Display
        };
        return new CliResult(new JsonObject { ["hotkey"] = node }, null,
            $"hotkey: {spec.Display}{Environment.NewLine}enabled: {hotkey.Enabled}");
    }

    private static string RenderHuman(JsonNode data)
    {
        var lines = new List<string>();

        void Flatten(JsonObject node, string prefix)
        {
            foreach (var pair in node)
            {
                var label = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
                if (pair.Value is JsonObject nested)
                {
                    Flatten(nested, label);
                    continue;
                }

                var text = pair.Value is JsonValue value && value.TryGetValue<string>(out var stringValue)
                    ? stringValue
                    : pair.Value?.ToJsonString() ?? string.Empty;
                lines.Add($"{label}: {text}");
            }
        }

        if (data is JsonObject root)
        {
            Flatten(root, string.Empty);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
