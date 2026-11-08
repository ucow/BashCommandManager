namespace BashCommandManager.Cli.CommandLine;

/// <summary>解析结果：命令路径、选项值、布尔开关、位置参数与全局选项。</summary>
public sealed class ParsedCommand
{
    public ParsedCommand(
        string commandPath,
        string verb,
        IReadOnlyDictionary<string, string> values,
        IReadOnlySet<string> flags,
        IReadOnlyList<string> arguments,
        bool json,
        string? databasePath)
    {
        CommandPath = commandPath;
        Verb = verb;
        Values = values;
        Flags = flags;
        Arguments = arguments;
        Json = json;
        DatabasePath = databasePath;
    }

    public string CommandPath { get; }
    public string Verb { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public IReadOnlySet<string> Flags { get; }
    public IReadOnlyList<string> Arguments { get; }
    public bool Json { get; }
    public string? DatabasePath { get; }

    public bool HasFlag(string name) => Flags.Contains(name);

    public string? GetValue(string name) => Values.TryGetValue(name, out var value) ? value : null;

    public bool HasValue(string name) => Values.ContainsKey(name);
}
