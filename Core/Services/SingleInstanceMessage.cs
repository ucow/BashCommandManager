using System.Text.Json;

namespace BashCommandManager.Core.Services;

/// <summary>单实例管道消息编解码：兼容旧 "ACTIVATE" 与带参数 "ACTIVATE\t[args]"。</summary>
public static class SingleInstanceMessage
{
    public const string ActivateVerb = "ACTIVATE";
    private const char Separator = '\t';

    public static string Format(string[]? args)
        => args is { Length: > 0 } ? ActivateVerb + Separator + JsonSerializer.Serialize(args) : ActivateVerb;

    public static bool TryParse(string? message, out string[] args)
    {
        args = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var text = message.Trim();
        if (string.Equals(text, ActivateVerb, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = ActivateVerb + Separator;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            args = JsonSerializer.Deserialize<string[]>(text[prefix.Length..]) ?? Array.Empty<string>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
