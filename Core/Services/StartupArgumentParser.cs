namespace BashCommandManager.Core.Services;

/// <summary>解析 GUI 启动参数中的 --command &lt;id&gt;（纯函数，便于单测）。</summary>
public static class StartupArgumentParser
{
    public const string CommandOption = "--command";

    public static bool TryGetCommandId(string[] args, out int commandId)
    {
        commandId = 0;
        if (args is null)
        {
            return false;
        }

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], CommandOption, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(args[index + 1], out var parsed) && parsed > 0)
            {
                commandId = parsed;
                return true;
            }

            return false;
        }

        return false;
    }
}
