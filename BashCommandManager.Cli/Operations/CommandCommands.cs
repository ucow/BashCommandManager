using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>commands 子命令。每个子命令一个方法，逐条实现。</summary>
public static class CommandCommands
{
    public static Task<CliResult> ListAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
        => ListCoreAsync(command, store, resolver, keyword: null);

    public static Task<CliResult> SearchAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
        => ListCoreAsync(command, store, resolver, keyword: command.Arguments[0]);

    private static async Task<CliResult> ListCoreAsync(
        ParsedCommand command,
        CliCommandStore store,
        CommandResolver resolver,
        string? keyword)
    {
        int? groupId = null;
        if (command.GetValue("group") is string groupToken)
        {
            groupId = (await resolver.ResolveGroupAsync(groupToken)).Id;
        }

        var (sort, direction) = ParseSort(command);
        var limit = ParseLimit(command);
        var commands = keyword is null
            ? await store.GetCommandsAsync(groupId, sort, direction, limit)
            : await store.SearchCommandsAsync(keyword, groupId, sort, direction, limit);
        await store.FillGroupNamesAsync(commands);

        var items = new JsonArray();
        foreach (var item in commands)
        {
            items.Add(JsonShapes.Command(item, item.GroupName, includeFileExists: false, _ => false));
        }
        return CliResult.Items(items);
    }

    public static async Task<CliResult> ShowAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        var item = await resolver.ResolveCommandAsync(command.Arguments[0], command.GetValue("group"));
        var group = await store.GetGroupAsync(item.GroupId);
        var data = JsonShapes.Command(item, group?.Name, includeFileExists: true, services.FileExists);
        return CliResult.Object(data);
    }

    public static async Task<CliResult> AddAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        var name = (command.GetValue("name") ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw CliErrors.InvalidValue("命令名称不能为空");
        }

        var filePath = (command.GetValue("file") ?? string.Empty).Trim();
        if (filePath.Length == 0)
        {
            throw CliErrors.InvalidValue("--file 不能为空");
        }
        if (!services.FileExists(filePath))
        {
            throw CliErrors.FileNotFound($"文件不存在: {filePath}");
        }

        int groupId = 1;
        if (command.GetValue("group") is string groupToken)
        {
            groupId = (await resolver.ResolveGroupAsync(groupToken)).Id;
        }

        var sortOrder = await store.CountCommandsInGroupAsync(groupId);
        var entity = new Command
        {
            Name = name,
            Description = command.GetValue("description") ?? string.Empty,
            FilePath = Path.GetFullPath(filePath),
            GroupId = groupId,
            SortOrder = sortOrder
        };
        var id = await store.CreateCommandAsync(entity);

        var data = new JsonObject
        {
            ["id"] = id,
            ["name"] = entity.Name,
            ["filePath"] = entity.FilePath,
            ["groupId"] = groupId
        };
        return CliResult.Object(data, $"id: {id}{Environment.NewLine}name: {entity.Name}{Environment.NewLine}filePath: {entity.FilePath}");
    }

    internal static (SortOption Sort, SortDirection Direction) ParseSort(ParsedCommand command)
    {
        var sort = (command.GetValue("sort") ?? "name").ToLowerInvariant() switch
        {
            "last" => SortOption.LastExecutedAt,
            "count" => SortOption.ExecutionCount,
            _ => SortOption.Name
        };
        var direction = string.Equals(command.GetValue("order"), "desc", StringComparison.OrdinalIgnoreCase)
            ? SortDirection.Descending
            : SortDirection.Ascending;
        return (sort, direction);
    }

    internal static int ParseLimit(ParsedCommand command)
        => command.GetValue("limit") is string text ? int.Parse(text, CultureInfo.InvariantCulture) : 0;
}
