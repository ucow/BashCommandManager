using System.Text;

namespace BashCommandManager.Cli.CommandLine;

/// <summary>从 CommandCatalog 生成帮助文本，保证帮助与解析器永不漂移。</summary>
public static class HelpCatalog
{
    public static string Global()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"bcm - BashCommandManager 命令行工具 {CliVersion.Value}");
        builder.AppendLine();
        builder.AppendLine("用法:");
        builder.AppendLine("  bcm [--json] [--db <path>] <命令> [命令选项] [位置参数]");
        builder.AppendLine("  bcm --help");
        builder.AppendLine("  bcm --version");
        builder.AppendLine();
        builder.AppendLine("命令:");
        foreach (var spec in CommandCatalog.Commands)
        {
            var label = spec.Path == "version" ? "bcm --version" : "bcm " + spec.Path.Replace('.', ' ');
            builder.AppendLine("  " + label.PadRight(26) + spec.Summary);
        }

        builder.AppendLine();
        builder.AppendLine("全局选项:");
        builder.AppendLine("  --json                      以 JSON 输出（成功写 stdout，失败写 stderr）");
        builder.AppendLine("  --db <path>                 指定数据库文件路径（默认 %LOCALAPPDATA%\\BashCommandManager\\data\\app.db）");
        builder.AppendLine("  --help                      显示帮助（可与任意命令组合）");
        builder.AppendLine("  --version                   显示版本");
        builder.AppendLine();
        builder.AppendLine("环境变量:");
        builder.AppendLine("  BASHCOMMANDMANAGER_DB        指定数据库路径（等价于 --db，--db 优先）");
        builder.AppendLine("  BASHCOMMANDMANAGER_SETTINGS_DIR");
        builder.AppendLine("                               同时改变 settings.json 与数据库位置：数据库变为 <dir>\\data\\app.db；");
        builder.AppendLine("                               仅供测试/便携，GUI 不读该变量；正常使用请用 BASHCOMMANDMANAGER_DB");
        builder.AppendLine("  BASHCOMMANDMANAGER_GUI       指定 BashCommandManager.exe 路径（等价于 --gui）");
        builder.AppendLine();
        builder.AppendLine("退出码:");
        builder.AppendLine("  0 成功  1 参数/用法错误  2 未找到  3 运行时失败");
        builder.AppendLine();
        builder.AppendLine("示例:");
        builder.AppendLine("  bcm groups list --json");
        builder.AppendLine("  bcm commands search 备份 --json");
        builder.AppendLine("  bcm run 7 --wait");
        builder.AppendLine("  bcm open --command 7");
        return builder.ToString();
    }

    public static string For(string commandPath)
    {
        var spec = CommandCatalog.Find(commandPath)
                   ?? throw new InvalidOperationException($"未知命令: {commandPath}");
        var builder = new StringBuilder();
        builder.AppendLine($"用法: {spec.Usage}");
        builder.AppendLine();
        builder.AppendLine(spec.Summary);
        builder.AppendLine();
        builder.AppendLine("选项:");
        foreach (var option in spec.Options)
        {
            var suffix = string.IsNullOrEmpty(option.Description) ? string.Empty : "  " + option.Description;
            builder.AppendLine("  " + option.HelpLine.PadRight(26) + suffix.TrimStart());
        }

        if (spec.Options.Count == 0)
        {
            builder.AppendLine("  （本命令没有专属选项）");
        }

        builder.AppendLine("  --db <path>                 指定数据库文件路径");
        builder.AppendLine("  --json                      以 JSON 输出");
        builder.AppendLine("  --help                      显示本帮助");
        builder.AppendLine();
        builder.AppendLine("示例:");
        foreach (var example in spec.Examples)
        {
            builder.AppendLine("  " + example);
        }

        return builder.ToString();
    }
}
