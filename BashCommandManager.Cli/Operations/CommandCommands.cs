using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
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
