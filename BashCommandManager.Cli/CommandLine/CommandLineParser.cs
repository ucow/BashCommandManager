using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.CommandLine;

/// <summary>手写参数解析器：全局选项任意位置、两级动词、选项表来自 CommandCatalog。</summary>
public static class CommandLineParser
{
    private static readonly HashSet<string> GlobalFlags = new(StringComparer.Ordinal) { "json", "help", "version" };

    private static readonly Dictionary<string, bool> OptionArity = BuildOptionArity();

    public static ParsedCommand Parse(string[] args) => Parse(args, Directory.Exists);

    public static ParsedCommand Parse(string[] args, Func<string, bool> directoryExists)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var optionSeen = new HashSet<string>(StringComparer.Ordinal);
        var positionals = new List<string>();
        string? databasePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var raw = args[index] ?? string.Empty;
            if (!raw.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(raw);
                continue;
            }

            var name = raw[2..];
            string? inlineValue = null;
            var equalsAt = name.IndexOf('=');
            if (equalsAt >= 0)
            {
                inlineValue = name[(equalsAt + 1)..];
                name = name[..equalsAt];
            }

            if (name.Length == 0)
            {
                throw CliErrors.Usage("选项名不能为空");
            }

            if (GlobalFlags.Contains(name))
            {
                if (inlineValue is not null)
                {
                    throw CliErrors.Usage($"选项 --{name} 不接受值", "用 bcm --help 查看用法");
                }
                if (!flags.Add(name))
                {
                    throw CliErrors.Usage($"选项重复: --{name}");
                }
                continue;
            }

            if (string.Equals(name, "db", StringComparison.Ordinal))
            {
                if (!optionSeen.Add(name))
                {
                    throw CliErrors.Usage("选项重复: --db");
                }
                databasePath = inlineValue ?? TakeNextValue(args, ref index, name);
                if (string.IsNullOrWhiteSpace(databasePath))
                {
                    throw CliErrors.InvalidValue("--db 不能为空");
                }
                continue;
            }

            if (!OptionArity.TryGetValue(name, out var takesValue))
            {
                throw CliErrors.Usage($"未知选项: --{name}", "用 bcm --help 查看全部选项");
            }

            if (!optionSeen.Add(name))
            {
                throw CliErrors.Usage($"选项重复: --{name}");
            }

            if (takesValue)
            {
                values[name] = inlineValue ?? TakeNextValue(args, ref index, name);
            }
            else
            {
                if (inlineValue is not null)
                {
                    throw CliErrors.Usage($"选项 --{name} 不接受值");
                }
                flags.Add(name);
            }
        }

        if (flags.Contains("version"))
        {
            return new ParsedCommand("version", "version", values, flags, Array.Empty<string>(), flags.Contains("json"), databasePath);
        }

        var verb = positionals.Count > 0 ? positionals[0] : null;
        if (verb is null)
        {
            return new ParsedCommand("help", "help", values, flags, Array.Empty<string>(), flags.Contains("json"), databasePath);
        }

        CommandSpec? spec;
        var consumed = 1;
        if (NeedsSubVerb(verb))
        {
            var subVerb = positionals.Count > 1 ? positionals[1] : null;
            if (subVerb is null)
            {
                if (flags.Contains("help"))
                {
                    return new ParsedCommand("help", verb, values, flags, Array.Empty<string>(), flags.Contains("json"), databasePath);
                }
                throw CliErrors.Usage($"缺少子命令: bcm {verb} <{string.Join("|", SubVerbs(verb))}>");
            }
            spec = CommandCatalog.Find(verb, subVerb);
            if (spec is null)
            {
                throw CliErrors.Usage($"未知子命令: bcm {verb} {subVerb}",
                    $"可用子命令: {string.Join(", ", SubVerbs(verb))}");
            }
            consumed = 2;
        }
        else
        {
            if (CommandCatalog.Find(verb, null) is null)
            {
                throw CliErrors.Usage($"未知命令: {verb}", "用 bcm --help 查看可用命令");
            }
            spec = CommandCatalog.Find(verb, null);
        }

        var arguments = positionals.Skip(consumed).ToList();
        EnsureOptionsBelongTo(spec!, values, flags);
        var commandPath = spec!.Path;

        if (flags.Contains("help"))
        {
            return new ParsedCommand("help." + commandPath, spec.Verb, values, flags, arguments, flags.Contains("json"), databasePath);
        }

        if (databasePath is not null)
        {
            var fullPath = Path.GetFullPath(databasePath);
            if (directoryExists(fullPath))
            {
                throw CliErrors.Usage($"--db 指向目录: {databasePath}", "请指定 .db 文件路径");
            }
            databasePath = fullPath;
        }

        return new ParsedCommand(commandPath, spec.Verb, values, flags, arguments, flags.Contains("json"), databasePath);
    }

    private static string TakeNextValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw CliErrors.Usage($"选项 --{name} 缺少值", "用 bcm --help 查看用法");
        }

        index++;
        return args[index];
    }

    private static void EnsureOptionsBelongTo(CommandSpec spec, IReadOnlyDictionary<string, string> values, IReadOnlySet<string> flags)
    {
        foreach (var name in values.Keys.Concat(flags))
        {
            if (name is "json" or "help" or "version")
            {
                continue;
            }

            if (!spec.Options.Any(option => string.Equals(option.Name, name, StringComparison.Ordinal)))
            {
                throw CliErrors.Usage($"命令 {spec.Path} 不支持选项 --{name}",
                    spec.Options.Count == 0 ? "该命令没有专属选项" : $"可用选项: {string.Join(", ", spec.Options.Select(option => "--" + option.Name))}");
            }
        }
    }

    private static Dictionary<string, bool> BuildOptionArity()
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var option in CommandCatalog.Commands.SelectMany(spec => spec.Options))
        {
            if (map.TryGetValue(option.Name, out var existing) && existing != option.TakesValue)
            {
                throw new InvalidOperationException($"选项 --{option.Name} 在命令表中的取值语义不一致");
            }
            map[option.Name] = option.TakesValue;
        }
        return map;
    }

    private static bool NeedsSubVerb(string verb) => CommandCatalog.Commands.Any(spec => spec.Verb == verb && spec.SubVerb is not null);

    private static IEnumerable<string> SubVerbs(string verb)
        => CommandCatalog.Commands.Where(spec => spec.Verb == verb && spec.SubVerb is not null).Select(spec => spec.SubVerb!);
}
