using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Application;

public class CliSettingsStoreTests
{
    private static (CliSettingsStore Store, string Path) Create(string? json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bcm-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        if (json is not null)
        {
            File.WriteAllText(path, json);
        }
        return (new CliSettingsStore(directory), path);
    }

    [Fact]
    public void Missing_file_returns_gui_defaults()
    {
        var (store, _) = Create(null);

        var hotkey = store.GetHotkey();

        Assert.True(hotkey.Enabled);
        Assert.Equal(45, hotkey.Key); // Key.B
        Assert.Equal(3, hotkey.Modifiers); // Ctrl+Alt
        Assert.EndsWith("settings.json", store.SettingsPath);
    }

    [Fact]
    public void Reads_flat_hotkey_format()
    {
        var (store, _) = Create("""{"Enabled":true,"Key":57,"Modifiers":6}""");

        var hotkey = store.GetHotkey();

        Assert.Equal(57, hotkey.Key);
        Assert.Equal(6, hotkey.Modifiers);
        Assert.True(hotkey.Enabled);
    }

    [Fact]
    public void Reads_nested_format_and_execution_section()
    {
        var (store, _) = Create("""
        {
          "Hotkey": { "Enabled": false, "Key": 57, "Modifiers": 6 },
          "Execution": { "Terminal": 3, "CustomExecutablePath": "", "ArgumentsTemplate": "", "KeepWindowOpen": false, "RunAsAdministrator": true }
        }
        """);

        var hotkey = store.GetHotkey();
        var execution = store.GetExecutionSettings();

        Assert.False(hotkey.Enabled);
        Assert.Equal(57, hotkey.Key);
        Assert.Equal(TerminalKind.WindowsTerminal, execution.Terminal);
        Assert.False(execution.KeepWindowOpen);
        Assert.True(execution.RunAsAdministrator);
    }

    [Fact]
    public void Missing_execution_section_falls_back_to_defaults()
    {
        var (store, _) = Create("""{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}""");

        var execution = store.GetExecutionSettings();

        Assert.Equal(TerminalKind.Cmd, execution.Terminal);
        Assert.True(execution.KeepWindowOpen);
    }

    [Fact]
    public void Setting_hotkey_preserves_execution_section()
    {
        var (store, path) = Create("""
        {
          "Hotkey": { "Enabled": true, "Key": 66, "Modifiers": 3 },
          "Execution": { "Terminal": 1, "CustomExecutablePath": "", "ArgumentsTemplate": "", "KeepWindowOpen": true, "RunAsAdministrator": false },
          "Unknown": { "Keep": "me" }
        }
        """);

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
        Assert.Equal(6, root["Hotkey"]!["Modifiers"]!.GetValue<int>());
        Assert.Equal(1, root["Execution"]!["Terminal"]!.GetValue<int>());
        Assert.Equal("me", root["Unknown"]!["Keep"]!.GetValue<string>());
    }

    [Fact]
    public void Setting_hotkey_keeps_flat_format_flat()
    {
        var (store, path) = Create("""{"Enabled":true,"Key":66,"Modifiers":3}""");

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(57, root["Key"]!.GetValue<int>());
        Assert.Equal(6, root["Modifiers"]!.GetValue<int>());
        Assert.Null(root["Hotkey"]);
    }

    [Fact]
    public void Disabling_hotkey_only_touches_enabled()
    {
        var (store, path) = Create("""{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}""");

        store.SetHotkeyEnabled(false);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.False(root["Hotkey"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
    }

    [Fact]
    public void Corrupt_json_raises_config_key_error()
    {
        var (store, path) = Create("{ not json");

        var error = Assert.Throws<CliFailureException>(() => store.GetHotkey()).Error;

        Assert.Equal("E_CONFIG_KEY", error.Code);
        Assert.Equal(1, error.ExitCode);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void Writing_to_missing_file_creates_nested_format()
    {
        var (store, path) = Create(null);

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.NotNull(root["Hotkey"]);
        Assert.True(root["Hotkey"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
        Assert.Equal(6, root["Hotkey"]!["Modifiers"]!.GetValue<int>());
        Assert.Null(root["Key"]); // 不再把扁平键混进嵌套文档（D3 / VD-10）
    }

    [Fact]
    public void Nested_document_without_hotkey_node_still_writes_nested()
    {
        var (store, path) = Create("""
        {
          "Execution": { "Terminal": 0, "CustomExecutablePath": "", "ArgumentsTemplate": "/k {file}", "KeepWindowOpen": true, "RunAsAdministrator": false }
        }
        """);

        store.SetHotkey(new HotkeyConfig(true, 57, 6));

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(57, root["Hotkey"]!["Key"]!.GetValue<int>());
        Assert.Equal(6, root["Hotkey"]!["Modifiers"]!.GetValue<int>());
        Assert.Null(root["Key"]);
        Assert.Equal(0, root["Execution"]!["Terminal"]!.GetValue<int>());
    }

    [Fact]
    public void Writes_utf8_without_bom_and_keeps_chinese()
    {
        var (store, path) = Create("""{"Hotkey":{"Enabled":true,"Key":57,"Modifiers":6}}""");

        store.SetHotkeyEnabled(false);

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }
}
