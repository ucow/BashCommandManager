using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BashCommandManager.Tests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    [Fact]
    public void S1_旧格式可读且执行段为默认()
    {
        WriteSettings("{\"Enabled\":true,\"Key\":45,\"Modifiers\":3}");

        var service = new SettingsService(_directory);

        Assert.True(service.GetHotkeySettings().Enabled);
        Assert.Equal(Key.B, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, service.GetHotkeySettings().Modifiers);
        Assert.Equal(TerminalKind.Cmd, service.GetExecutionSettings().Terminal);
        Assert.True(service.GetExecutionSettings().KeepWindowOpen);
        Assert.Equal("/k {file}", service.GetExecutionSettings().ArgumentsTemplate);
        Assert.Equal(string.Empty, service.GetExecutionSettings().CustomExecutablePath);
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.v1.bak")));
    }

    [Fact]
    public void S2_旧格式的自定义快捷键原样保留()
    {
        WriteSettings("{\"Enabled\":false,\"Key\":94,\"Modifiers\":4}");

        var service = new SettingsService(_directory);

        Assert.False(service.GetHotkeySettings().Enabled);
        Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Shift, service.GetHotkeySettings().Modifiers);
    }

    [Fact]
    public void S3_新格式字段一致_缺段用默认_未知字段忽略()
    {
        WriteSettings(
            "{\"Hotkey\":{\"Enabled\":false,\"Key\":94,\"Modifiers\":4}," +
            "\"Execution\":{\"Terminal\":3,\"CustomExecutablePath\":\"\",\"ArgumentsTemplate\":\"-d {dir} cmd.exe /k {file}\",\"KeepWindowOpen\":true,\"RunAsAdministrator\":true}," +
            "\"Unknown\":123}");

        var service = new SettingsService(_directory);

        Assert.False(service.GetHotkeySettings().Enabled);
        Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Shift, service.GetHotkeySettings().Modifiers);
        Assert.Equal(TerminalKind.WindowsTerminal, service.GetExecutionSettings().Terminal);
        Assert.Equal("-d {dir} cmd.exe /k {file}", service.GetExecutionSettings().ArgumentsTemplate);
        Assert.True(service.GetExecutionSettings().RunAsAdministrator);

        WriteSettings("{\"Hotkey\":{\"Enabled\":true,\"Key\":45,\"Modifiers\":3}}");
        var missingExecution = new SettingsService(_directory);

        Assert.Equal(TerminalKind.Cmd, missingExecution.GetExecutionSettings().Terminal);
        Assert.Equal("/k {file}", missingExecution.GetExecutionSettings().ArgumentsTemplate);
    }

    [Fact]
    public void S4_损坏_json_不抛异常并备份为_corrupt_bak()
    {
        const string corrupt = "{ this is not json";
        WriteSettings(corrupt);

        var service = new SettingsService(_directory);

        var backupPath = Path.Combine(_directory, "settings.json.corrupt.bak");
        Assert.True(File.Exists(backupPath));
        Assert.Equal(corrupt, File.ReadAllText(backupPath));
        Assert.Equal(Key.B, service.GetHotkeySettings().Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, service.GetHotkeySettings().Modifiers);
        Assert.Equal(TerminalKind.Cmd, service.GetExecutionSettings().Terminal);
    }

    [Fact]
    public void S5_两次保存互不抹掉另一段()
    {
        var service = new SettingsService(_directory);
        var hotkeyChanged = 0;
        var executionChanged = 0;
        service.HotkeySettingsChanged += (_, _) => hotkeyChanged++;
        service.ExecutionSettingsChanged += (_, _) => executionChanged++;

        service.SaveExecutionSettings(new ExecutionSettings
        {
            Terminal = TerminalKind.WindowsTerminal,
            ArgumentsTemplate = "-d {dir} cmd.exe /k {file}"
        });
        service.SaveHotkeySettings(new HotkeySettings
        {
            Enabled = false,
            Key = Key.F5,
            Modifiers = ModifierKeys.Shift
        });

        var reloaded = new SettingsService(_directory);
        Assert.Equal(TerminalKind.WindowsTerminal, reloaded.GetExecutionSettings().Terminal);
        Assert.Equal(Key.F5, reloaded.GetHotkeySettings().Key);
        Assert.Equal(1, hotkeyChanged);
        Assert.Equal(1, executionChanged);

        reloaded.SaveHotkeySettings(new HotkeySettings
        {
            Enabled = true,
            Key = Key.C,
            Modifiers = ModifierKeys.Control
        });

        var second = new SettingsService(_directory);
        Assert.Equal(Key.C, second.GetHotkeySettings().Key);
        Assert.Equal(TerminalKind.WindowsTerminal, second.GetExecutionSettings().Terminal);
    }

    [Fact]
    public void S6_旧格式首次保存生成_v1_bak_且文件变为嵌套格式()
    {
        const string legacyJson = "{\"Enabled\":true,\"Key\":45,\"Modifiers\":3}";
        WriteSettings(legacyJson);
        var service = new SettingsService(_directory);

        service.SaveHotkeySettings(service.GetHotkeySettings());

        var backupPath = Path.Combine(_directory, "settings.json.v1.bak");
        Assert.True(File.Exists(backupPath));
        Assert.Equal(legacyJson, File.ReadAllText(backupPath));

        var savedJson = File.ReadAllText(SettingsPath);
        // 原子写：File.Move 成功后 tmp 会被带走，不残留；settings.json 能被 JsonDocument 整体解析即证明是完整 JSON。
        Assert.False(File.Exists(Path.Combine(_directory, "settings.json.tmp")));
        using var document = JsonDocument.Parse(savedJson);
        Assert.True(document.RootElement.TryGetProperty("Hotkey", out _));
        Assert.True(document.RootElement.TryGetProperty("Execution", out _));
        Assert.False(document.RootElement.TryGetProperty("Enabled", out _));
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("Hotkey").GetProperty("Key").ValueKind);
    }

    [Fact]
    public void S7_非法终端与空白模板被归一化()
    {
        WriteSettings(
            "{\"Hotkey\":{\"Enabled\":true,\"Key\":45,\"Modifiers\":3}," +
            "\"Execution\":{\"Terminal\":99,\"CustomExecutablePath\":\"   \",\"ArgumentsTemplate\":\"  \",\"KeepWindowOpen\":true,\"RunAsAdministrator\":false}}");

        var service = new SettingsService(_directory);

        Assert.Equal(TerminalKind.Cmd, service.GetExecutionSettings().Terminal);
        Assert.Equal("/k {file}", service.GetExecutionSettings().ArgumentsTemplate);
        Assert.Equal(string.Empty, service.GetExecutionSettings().CustomExecutablePath);
    }

    [Fact]
    public void S8_写入失败时抛异常但内存值已更新()
    {
        const string originalJson = "{\"Enabled\":true,\"Key\":45,\"Modifiers\":3}";
        WriteSettings(originalJson);

        var service = new SettingsService(_directory);
        var tempPath = Path.Combine(_directory, "settings.json.tmp");

        // 用 FileShare.None 占用 tmp，让 File.WriteAllText 抛可预期的 IOException。
        // 不要用「把 settings.json 建成目录」制造失败：那种情况 File.Move 抛的是
        // UnauthorizedAccessException，它不继承 IOException，Assert.ThrowsAny<IOException> 必红。
        using var locked = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var newHotkey = new HotkeySettings
        {
            Enabled = false,
            Key = Key.F5,
            Modifiers = ModifierKeys.Shift
        };

        Assert.ThrowsAny<IOException>(() => service.SaveHotkeySettings(newHotkey));

        // 内存值先更新、落盘失败不回滚（假设 Q2）。
        Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
        Assert.False(service.GetHotkeySettings().Enabled);
        // 写失败不破坏原文件内容（原子写语义）。
        Assert.Equal(originalJson, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void S9_保存成功后_tmp_不残留且文件为完整_json()
    {
        var service = new SettingsService(_directory);

        service.SaveExecutionSettings(new ExecutionSettings
        {
            Terminal = TerminalKind.PowerShell7,
            KeepWindowOpen = false
        });

        var tempPath = Path.Combine(_directory, "settings.json.tmp");
        Assert.False(File.Exists(tempPath));

        var savedJson = File.ReadAllText(SettingsPath);
        Assert.EndsWith("}", savedJson.TrimEnd());

        using var document = JsonDocument.Parse(savedJson);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(document.RootElement.TryGetProperty("Hotkey", out _));
        Assert.True(document.RootElement.TryGetProperty("Execution", out var execution));
        Assert.Equal(JsonValueKind.Number, execution.GetProperty("Terminal").ValueKind);
        Assert.False(execution.GetProperty("KeepWindowOpen").GetBoolean());
    }
}
