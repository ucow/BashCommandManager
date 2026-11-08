using BashCommandManager.Cli.Infrastructure;
using System.Text;

namespace BashCommandManager.Cli.Application;

/// <summary>快捷键规格：Key/Modifiers 与 WPF 枚举数值一致，Display 为规范文本。</summary>
public sealed class HotkeySpec
{
    public const int ModifierAlt = 1;
    public const int ModifierControl = 2;
    public const int ModifierShift = 4;
    public const int ModifierWindows = 8;

    private HotkeySpec(int key, int modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public int Key { get; }
    public int Modifiers { get; }
    public string KeyName => HotkeyKeyMap.GetName(Key);
    // 偏差记录（T3）：计划原文为 FormatModifiers(Modifiers) + "+" + KeyName，
    // 但 FormatModifiers 每段已带尾随 '+', 会渲染成 "Ctrl+Shift++N"；此处按测试契约修正。
    public string Display => FormatModifiers(Modifiers) + KeyName;

    public static HotkeySpec Create(int key, int modifiers) => new(key, modifiers);

    public static HotkeySpec Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw CliErrors.InvalidValue("快捷键不能为空", "示例: Ctrl+Shift+N");
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw CliErrors.InvalidValue($"快捷键缺少修饰键: {text}", "示例: Ctrl+Shift+N");
        }

        var modifiers = 0;
        string? keyName = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierControl;
                    break;
                case "alt":
                    modifiers |= ModifierAlt;
                    break;
                case "shift":
                    modifiers |= ModifierShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierWindows;
                    break;
                default:
                    if (keyName is not null)
                    {
                        throw CliErrors.InvalidValue($"快捷键只能包含一个主键: {text}");
                    }
                    if (!HotkeyKeyMap.TryGetValue(part, out _))
                    {
                        throw CliErrors.InvalidValue($"无法识别的按键: {part}",
                            "可用按键: A-Z、D0-D9、F1-F24、Space、Tab、Enter、Escape、Insert、Delete、Home、End、PageUp、PageDown、方向键");
                    }
                    keyName = part;
                    break;
            }
        }

        if (modifiers == 0)
        {
            throw CliErrors.InvalidValue($"快捷键至少需要一个修饰键: {text}", "示例: Ctrl+Shift+N");
        }

        if (keyName is null)
        {
            throw CliErrors.InvalidValue($"快捷键缺少主键: {text}", "示例: Ctrl+Shift+N");
        }

        return new HotkeySpec(HotkeyKeyMap.GetValue(keyName), modifiers);
    }

    public static string FormatModifiers(int modifiers)
    {
        var builder = new StringBuilder();
        if ((modifiers & ModifierControl) != 0)
        {
            builder.Append("Ctrl+");
        }
        if ((modifiers & ModifierAlt) != 0)
        {
            builder.Append("Alt+");
        }
        if ((modifiers & ModifierShift) != 0)
        {
            builder.Append("Shift+");
        }
        if ((modifiers & ModifierWindows) != 0)
        {
            builder.Append("Win+");
        }
        return builder.ToString();
    }
}
