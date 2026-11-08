namespace BashCommandManager.Cli.CommandLine;

/// <summary>一个命令行选项：是否取值、帮助描述、是否必填、可选值集合。</summary>
public sealed record OptionSpec(
    string Name,
    bool TakesValue,
    string Description,
    bool Required = false,
    IReadOnlyList<string>? AllowedValues = null)
{
    public string ValuePlaceholder => Name switch
    {
        "name" => "名称",
        "parent" => "id|路径",
        "group" => "id|路径",
        "to" => "id|路径",
        "file" => "路径",
        "description" => "描述",
        "sort" => "name|last|count",
        "order" => "asc|desc",
        "limit" => "n",
        "format" => "json|csv",
        "output" => "文件",
        "timeout" => "秒",
        "command" => "id",
        "gui" => "路径",
        _ => "值"
    };

    public string HelpLine => TakesValue ? $"--{Name} <{ValuePlaceholder}>" : $"--{Name}";
}

/// <summary>一条命令的完整契约：用法、摘要、选项、位置参数个数、示例。</summary>
public sealed record CommandSpec(
    string Path,
    string Verb,
    string? SubVerb,
    string Usage,
    string Summary,
    IReadOnlyList<OptionSpec> Options,
    int MinArguments,
    int MaxArguments,
    IReadOnlyList<string> Examples);
