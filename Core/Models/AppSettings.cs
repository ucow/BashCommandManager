namespace BashCommandManager.Core.Models;

/// <summary>settings.json 的根对象：快捷键段 + 执行段。</summary>
public class AppSettings
{
    public HotkeySettings Hotkey { get; set; } = new();

    public ExecutionSettings Execution { get; set; } = new();
}
