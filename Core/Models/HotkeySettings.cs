using System.Windows.Input;

namespace BashCommandManager.Core.Models;

public class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    public Key Key { get; set; } = Key.B;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;

    public string ToDisplayString()
    {
        var modifierString = Modifiers switch
        {
            ModifierKeys.Control => "Ctrl",
            ModifierKeys.Alt => "Alt",
            ModifierKeys.Shift => "Shift",
            ModifierKeys.Windows => "Win",
            ModifierKeys.Control | ModifierKeys.Alt => "Ctrl+Alt",
            ModifierKeys.Control | ModifierKeys.Shift => "Ctrl+Shift",
            ModifierKeys.Control | ModifierKeys.Windows => "Ctrl+Win",
            ModifierKeys.Alt | ModifierKeys.Shift => "Alt+Shift",
            ModifierKeys.Alt | ModifierKeys.Windows => "Alt+Win",
            ModifierKeys.Shift | ModifierKeys.Windows => "Shift+Win",
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift => "Ctrl+Alt+Shift",
            _ => Modifiers.ToString()
        };
        return $"{modifierString}+{Key}";
    }

    public static bool IsValid(HotkeySettings settings)
    {
        // 至少需要有一个修饰键
        return settings.Modifiers != ModifierKeys.None;
    }
}
