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

    public static async Task<CliResult> EditAsync(ParsedCommand command, CliServices services, CliCommandStore store, CommandResolver resolver)
    {
        // --group 语义（VD-13）：edit 的 --group 是“移动到该分组”，不是查找范围；
        // 所以这里不传 groupToken（<id|name> 按全局名称解析），--group 只在下面按“目标分组”处理。
        var entity = await resolver.ResolveCommandAsync(command.Arguments[0], groupToken: null);

        if (command.HasValue("name"))
        {
            var name = command.GetValue("name")!.Trim();
            if (name.Length == 0)
            {
                throw CliErrors.InvalidValue("命令名称不能为空");
            }
            entity.Name = name;
        }

        if (command.HasValue("description"))
        {
            entity.Description = command.GetValue("description")!;
        }

        if (command.HasValue("file"))
        {
            var filePath = command.GetValue("file")!.Trim();
            if (!services.FileExists(filePath))
            {
                throw CliErrors.FileNotFound($"文件不存在: {filePath}");
            }
            entity.FilePath = Path.GetFullPath(filePath);
        }

        if (command.GetValue("group") is string groupToken)
        {
            entity.GroupId = (await resolver.ResolveGroupAsync(groupToken)).Id;
        }

        await store.UpdateCommandAsync(entity);

        var group = await store.GetGroupAsync(entity.GroupId);
        var data = JsonShapes.Command(entity, group?.Name, includeFileExists: true, services.FileExists);
        return CliResult.Object(data);
    }

    public static async Task<CliResult> DeleteAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var entities = await resolver.ResolveCommandsAsync(command.Arguments[0], command.GetValue("group"));
        var ids = entities.Select(entity => entity.Id).ToArray();
        await store.DeleteCommandsAsync(ids);

        var data = new JsonObject
        {
            ["deleted"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray())
        };
        return CliResult.Object(data, $"deleted: {string.Join(",", ids)}");
    }

    public static async Task<CliResult> MoveAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var entities = await resolver.ResolveCommandsAsync(command.Arguments[0], groupToken: null);
        var target = await resolver.ResolveGroupAsync(command.GetValue("to")!);
        var ids = entities.Select(entity => entity.Id).ToArray();
        await store.MoveCommandsAsync(ids, target.Id);

        var data = new JsonObject
        {
            ["moved"] = new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
            ["targetGroupId"] = target.Id
        };
        return CliResult.Object(data, $"moved: {string.Join(",", ids)}{Environment.NewLine}targetGroup: {target.Name} ({target.Id})");
    }

    public static async Task<CliResult> FrequentAsync(ParsedCommand command, CliCommandStore store)
    {
        var limit = command.GetValue("limit") is string text ? int.Parse(text, CultureInfo.InvariantCulture) : 10;
        var commands = await store.GetFrequentlyUsedAsync(limit);
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
