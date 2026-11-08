using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>groups 子命令：list / create（后续任务追加 rename / delete）。</summary>
public static class GroupCommands
{
    public static async Task<CliResult> ListAsync(ParsedCommand command, CliCommandStore store)
    {
        var groups = await store.GetGroupsAsync();
        var counts = await store.GetCommandCountsAsync();
        int CountOf(int id) => counts.TryGetValue(id, out var count) ? count : 0;

        if (command.HasFlag("tree"))
        {
            var roots = new JsonArray();
            foreach (var group in groups.Where(item => item.ParentId is null).OrderBy(item => item.SortOrder))
            {
                roots.Add(BuildTree(group, groups, CountOf));
            }
            return new CliResult(roots, null, RenderTree(groups, CountOf));
        }

        var items = new JsonArray();
        foreach (var group in groups)
        {
            items.Add(JsonShapes.Group(group, CountOf(group.Id)));
        }
        return CliResult.Items(items);
    }

    public static async Task<CliResult> CreateAsync(ParsedCommand command, CliCommandStore store, CommandResolver resolver)
    {
        var name = (command.GetValue("name") ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw CliErrors.InvalidValue("分组名称不能为空");
        }

        var parentToken = command.GetValue("parent");
        int? parentId;
        if (parentToken is null)
        {
            parentId = 1;
        }
        else if (string.Equals(parentToken.Trim(), "none", StringComparison.OrdinalIgnoreCase))
        {
            parentId = null;
        }
        else
        {
            parentId = (await resolver.ResolveGroupAsync(parentToken)).Id;
        }

        var duplicate = await store.FindChildGroupByNameAsync(parentId, name);
        if (duplicate is not null)
        {
            throw CliErrors.InvalidValue($"同级分组已存在同名分组: {name}", "换一个名称，或用 bcm groups list --json 查看现有分组");
        }

        var id = await store.CreateGroupAsync(name, parentId);
        var created = await store.GetGroupAsync(id)
                      ?? throw CliErrors.DbError($"创建分组后无法读回: {id}");

        var data = new JsonObject
        {
            ["id"] = created.Id,
            ["name"] = created.Name,
            ["parentId"] = created.ParentId.HasValue ? JsonValue.Create(created.ParentId.Value) : null,
            ["sortOrder"] = created.SortOrder
        };
        return CliResult.Object(data, $"id: {created.Id}{Environment.NewLine}name: {created.Name}");
    }

    private static JsonObject BuildTree(Group group, IReadOnlyList<Group> all, Func<int, int> countOf)
    {
        var children = new JsonArray();
        foreach (var child in all.Where(item => item.ParentId == group.Id).OrderBy(item => item.SortOrder))
        {
            children.Add(BuildTree(child, all, countOf));
        }

        return new JsonObject
        {
            ["id"] = group.Id,
            ["name"] = group.Name,
            ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null,
            ["sortOrder"] = group.SortOrder,
            ["commandCount"] = countOf(group.Id),
            ["children"] = children
        };
    }

    private static string RenderTree(IReadOnlyList<Group> groups, Func<int, int> countOf)
    {
        var lines = new List<string>();

        void Visit(int? parentId, int depth)
        {
            foreach (var group in groups.Where(item => item.ParentId == parentId).OrderBy(item => item.SortOrder))
            {
                lines.Add($"{new string(' ', depth * 2)}{group.Name} (id={group.Id}, 命令 {countOf(group.Id)})");
                Visit(group.Id, depth + 1);
            }
        }

        Visit(null, 0);
        return string.Join(Environment.NewLine, lines);
    }
}
