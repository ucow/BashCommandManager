namespace BashCommandManager.Cli.CommandLine;

/// <summary>19 条命令的唯一元数据表：解析器、帮助、处理器分发都从这里取。</summary>
public static class CommandCatalog
{
    private static readonly OptionSpec ForceOption = new("force", false, "显式确认删除（缺省时退出 1）", Required: true);

    public static IReadOnlyList<CommandSpec> Commands { get; } = new List<CommandSpec>
    {
        new("groups.list", "groups", "list",
            "bcm groups list [--tree] [--db <path>] [--json]",
            "列出分组",
            new[] { new OptionSpec("tree", false, "以树形结构输出（children 嵌套）") },
            0, 0,
            new[] { "bcm groups list --json", "bcm groups list --tree" }),

        new("groups.create", "groups", "create",
            "bcm groups create --name <名称> [--parent <id|路径>] [--db <path>] [--json]",
            "新建分组",
            new[]
            {
                new OptionSpec("name", true, "分组名称", Required: true),
                new OptionSpec("parent", true, "父分组 id 或路径；none 表示顶级（默认 1 根分组）")
            },
            0, 0,
            new[] { "bcm groups create --name 运维 --parent 1", "bcm groups create --name 临时 --parent none" }),

        new("groups.rename", "groups", "rename",
            "bcm groups rename <id|路径> --name <新名称> [--db <path>] [--json]",
            "重命名分组",
            new[] { new OptionSpec("name", true, "新名称", Required: true) },
            1, 1,
            new[] { "bcm groups rename 2 --name 生产运维" }),

        new("groups.delete", "groups", "delete",
            "bcm groups delete <id|路径> --force [--db <path>] [--json]",
            "删除分组（级联删除子分组与命令，需 --force）",
            new[] { ForceOption },
            1, 1,
            new[] { "bcm groups delete 运维 --force" }),

        new("commands.list", "commands", "list",
            "bcm commands list [--group <id|路径>] [--sort name|last|count] [--order asc|desc] [--limit <n>] [--db <path>] [--json]",
            "列出命令",
            new[]
            {
                new OptionSpec("group", true, "只列出该分组的直属命令（不含子分组）"),
                new OptionSpec("sort", true, "排序字段，默认 name", AllowedValues: new[] { "name", "last", "count" }),
                new OptionSpec("order", true, "排序方向，默认 asc", AllowedValues: new[] { "asc", "desc" }),
                new OptionSpec("limit", true, "最多返回条数，0 表示全部（默认 0）")
            },
            0, 0,
            new[] { "bcm commands list --json", "bcm commands list --group 运维 --sort count --order desc" }),

        new("commands.search", "commands", "search",
            "bcm commands search <关键字> [--group <id|路径>] [--sort name|last|count] [--order asc|desc] [--limit <n>] [--db <path>] [--json]",
            "搜索命令（名称或描述包含关键字）",
            new[]
            {
                new OptionSpec("group", true, "只在该分组内搜索"),
                new OptionSpec("sort", true, "排序字段，默认 name", AllowedValues: new[] { "name", "last", "count" }),
                new OptionSpec("order", true, "排序方向，默认 asc", AllowedValues: new[] { "asc", "desc" }),
                new OptionSpec("limit", true, "最多返回条数，0 表示全部（默认 0）")
            },
            1, 1,
            new[] { "bcm commands search 备份 --json" }),

        new("commands.show", "commands", "show",
            "bcm commands show <id|名称> [--group <id|路径>] [--db <path>] [--json]",
            "显示单条命令",
            new[] { new OptionSpec("group", true, "在该分组内查找") },
            1, 1,
            new[] { "bcm commands show 7 --json" }),

        new("commands.add", "commands", "add",
            "bcm commands add --name <名称> --file <路径> [--description <描述>] [--group <id|路径>] [--db <path>] [--json]",
            "添加命令",
            new[]
            {
                new OptionSpec("name", true, "显示名称", Required: true),
                new OptionSpec("file", true, "批处理文件路径（必须是已存在的 .bat/.cmd）", Required: true),
                new OptionSpec("description", true, "描述，默认空"),
                new OptionSpec("group", true, "所属分组，默认根分组 1")
            },
            0, 0,
            new[] { "bcm commands add --name 备份 --file D:\\bat\\备份.bat --group 运维" }),

        new("commands.edit", "commands", "edit",
            "bcm commands edit <id|名称> [--name <新名称>] [--description <描述>] [--file <路径>] [--group <id|路径>] [--db <path>] [--json]",
            "编辑命令（至少指定一项）",
            new[]
            {
                new OptionSpec("name", true, "新名称"),
                new OptionSpec("description", true, "新描述（空串表示清空）"),
                new OptionSpec("file", true, "新文件路径（必须是已存在的 .bat/.cmd）"),
                new OptionSpec("group", true, "移动到该分组")
            },
            1, 1,
            new[] { "bcm commands edit 7 --description \"每日 2:00\"" }),

        new("commands.delete", "commands", "delete",
            "bcm commands delete <id|名称[,..]> --force [--db <path>] [--json]",
            "删除命令（需 --force）",
            new[] { ForceOption },
            1, 1,
            new[] { "bcm commands delete 7,8 --force" }),

        new("commands.move", "commands", "move",
            "bcm commands move <id|名称[,..]> --to <id|路径> [--db <path>] [--json]",
            "移动命令到其他分组",
            new[] { new OptionSpec("to", true, "目标分组 id 或路径", Required: true) },
            1, 1,
            new[] { "bcm commands move 7,8 --to 生产运维" }),

        new("commands.import", "commands", "import",
            "bcm commands import --group <id|路径> <路径...> [--recursive] [--allow-duplicates] [--dry-run] [--db <path>] [--json]",
            "导入 .bat/.cmd 文件",
            new[]
            {
                new OptionSpec("group", true, "导入目标分组", Required: true),
                new OptionSpec("recursive", false, "目录递归展开"),
                new OptionSpec("allow-duplicates", false, "允许同一 FilePath 重复导入"),
                new OptionSpec("dry-run", false, "只报告将导入的内容，不写库")
            },
            1, int.MaxValue,
            new[] { "bcm commands import --group 运维 D:\\bat --recursive" }),

        new("commands.export", "commands", "export",
            "bcm commands export [--group <id|路径>] [--recursive] [--format json|csv] [--output <文件>] [--db <path>] [--json]",
            "导出命令为 JSON/CSV",
            new[]
            {
                new OptionSpec("group", true, "只导出该分组"),
                new OptionSpec("recursive", false, "包含子分组的命令"),
                new OptionSpec("format", true, "导出格式，默认 json", AllowedValues: new[] { "json", "csv" }),
                new OptionSpec("output", true, "写入文件；缺省时输出到 stdout 的 data")
            },
            0, 0,
            new[] { "bcm commands export --format json --output backup.json", "bcm commands export --format csv --output backup.csv" }),

        new("commands.frequent", "commands", "frequent",
            "bcm commands frequent [--limit <n>] [--db <path>] [--json]",
            "列出常用命令（按执行次数降序）",
            new[] { new OptionSpec("limit", true, "返回条数，1-1000，默认 10") },
            0, 0,
            new[] { "bcm commands frequent --limit 5 --json" }),

        new("run", "run", null,
            "bcm run <id|名称> [--group <id|路径>] [--admin] [--wait] [--timeout <秒>] [--dry-run] [--db <path>] [--json]",
            "运行命令",
            new[]
            {
                new OptionSpec("group", true, "在该分组内查找命令"),
                new OptionSpec("admin", false, "以管理员身份启动（UAC）"),
                new OptionSpec("wait", false, "等待结束并返回退出码（用 /c）"),
                new OptionSpec("timeout", true, "等待超时秒数，仅与 --wait 同用"),
                new OptionSpec("dry-run", false, "只打印将启动的命令行，不执行、不写库")
            },
            1, 1,
            new[] { "bcm run 备份 --wait", "bcm run 7 --dry-run --json" }),

        new("config.get", "config", "get",
            "bcm config get [key] [--db <path>] [--json]",
            "读取配置",
            Array.Empty<OptionSpec>(),
            0, 1,
            new[] { "bcm config get --json", "bcm config get db.path --json" }),

        new("config.set", "config", "set",
            "bcm config set <hotkey|hotkey.enabled> <值> [--db <path>] [--json]",
            "写入配置",
            Array.Empty<OptionSpec>(),
            2, 2,
            new[] { "bcm config set hotkey \"Ctrl+Shift+N\"", "bcm config set hotkey.enabled false" }),

        new("open", "open", null,
            "bcm open [--command <id>] [--gui <路径>] [--db <path>] [--json]",
            "打开 GUI（可定位命令）",
            new[]
            {
                new OptionSpec("command", true, "打开后定位到该命令 id"),
                new OptionSpec("gui", true, "指定 BashCommandManager.exe 路径")
            },
            0, 0,
            new[] { "bcm open --command 7" }),

        new("version", "version", null,
            "bcm --version [--json]",
            "显示版本",
            Array.Empty<OptionSpec>(),
            0, 0,
            new[] { "bcm --version", "bcm --version --json" })
    };

    public static CommandSpec? Find(string path)
        => Commands.FirstOrDefault(spec => string.Equals(spec.Path, path, StringComparison.Ordinal));

    public static CommandSpec? Find(string verb, string? subVerb)
        => Commands.FirstOrDefault(spec =>
            string.Equals(spec.Verb, verb, StringComparison.Ordinal)
            && string.Equals(spec.SubVerb, subVerb, StringComparison.Ordinal));
}
