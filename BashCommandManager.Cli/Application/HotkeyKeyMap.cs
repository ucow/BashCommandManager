namespace BashCommandManager.Cli.Application;

/// <summary>键名与虚拟键码映射（与 System.Windows.Input.Key 数值一致，CLI 不引用 WPF）。</summary>
public static class HotkeyKeyMap
{
    private static readonly Dictionary<string, int> ByName = Build();
    private static readonly Dictionary<int, string> ByValue = BuildReverse();

    public static IReadOnlyCollection<string> Names => ByName.Keys;

    public static bool TryGetValue(string name, out int virtualKey)
        => ByName.TryGetValue(Normalize(name), out virtualKey);

    public static int GetValue(string name)
        => TryGetValue(name, out var virtualKey)
            ? virtualKey
            : throw new ArgumentOutOfRangeException(nameof(name), name, "未知按键");

    public static string GetName(int virtualKey)
        => ByValue.TryGetValue(virtualKey, out var name) ? name : virtualKey.ToString();

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    private static Dictionary<string, int> Build()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["space"] = 18,
            ["tab"] = 3,
            ["enter"] = 6,
            ["escape"] = 13,
            ["insert"] = 31,
            ["delete"] = 32,
            ["home"] = 22,
            ["end"] = 21,
            ["pageup"] = 19,
            ["pagedown"] = 20,
            ["left"] = 23,
            ["up"] = 24,
            ["right"] = 25,
            ["down"] = 26
        };

        for (var key = 44; key <= 69; key++)
        {
            map[((char)('A' + key - 44)).ToString().ToLowerInvariant()] = key;
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            map[$"d{digit}"] = 34 + digit;
            map[digit.ToString()] = 34 + digit;
        }

        for (var function = 1; function <= 24; function++)
        {
            map[$"f{function}"] = 89 + function;
        }

        return map;
    }

    private static Dictionary<int, string> BuildReverse()
    {
        var map = new Dictionary<int, string>();
        for (var letter = 0; letter < 26; letter++)
        {
            map[44 + letter] = ((char)('A' + letter)).ToString();
        }
        for (var digit = 0; digit <= 9; digit++)
        {
            map[34 + digit] = "D" + digit;
        }
        for (var function = 1; function <= 24; function++)
        {
            map[89 + function] = "F" + function;
        }
        map[18] = "Space";
        map[3] = "Tab";
        map[6] = "Enter";
        map[13] = "Escape";
        map[31] = "Insert";
        map[32] = "Delete";
        map[22] = "Home";
        map[21] = "End";
        map[19] = "PageUp";
        map[20] = "PageDown";
        map[23] = "Left";
        map[24] = "Up";
        map[25] = "Right";
        map[26] = "Down";
        return map;
    }
}
