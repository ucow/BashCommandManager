using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>把「id / 名称 / 父/子 分组路径 / 逗号列表」解析成实体，并落实 0 条→2、多条→1 的规则。</summary>
public sealed class CommandResolver
{
    private const string GroupHint = "用 bcm groups list --json 查看可用分组";
    private const string CommandHint = "用 bcm commands list --json 查看可用 id";

    private readonly CliCommandStore _store;

    public CommandResolver(CliCommandStore store) => _store = store;

    public async Task<Group> ResolveGroupAsync(string token)
    {
        var matches = await FindGroupsAsync(token);
        return matches.Count switch
        {
            0 => throw CliErrors.NotFound($"未找到分组: {token}", GroupHint),
            1 => matches[0],
            _ => throw CliErrors.Ambiguous("分组", GroupCandidates(matches))
        };
    }

    public async Task<Group?> FindGroupAsync(string token)
    {
        var matches = await FindGroupsAsync(token);
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<Command> ResolveCommandAsync(string token, string? groupToken)
    {
        var text = token.Trim();
        int? scopeGroupId = null;
        if (!string.IsNullOrWhiteSpace(groupToken))
        {
            scopeGroupId = (await ResolveGroupAsync(groupToken)).Id;
        }

        if (int.TryParse(text, out var id))
        {
            var byId = await _store.GetCommandAsync(id)
                       ?? throw CliErrors.NotFound($"未找到命令: {text}", CommandHint);
            if (scopeGroupId is int scope && byId.GroupId != scope)
            {
                throw CliErrors.NotFound($"命令 {id} 不在指定分组内", CommandHint);
            }
            return byId;
        }

        var candidates = await _store.FindCommandsByNameAsync(text, scopeGroupId);
        return candidates.Count switch
        {
            0 => throw CliErrors.NotFound($"未找到命令: {text}", CommandHint),
            1 => candidates[0],
            _ => throw CliErrors.Ambiguous("命令", CommandCandidates(candidates))
        };
    }

    public async Task<IReadOnlyList<Command>> ResolveCommandsAsync(string token, string? groupToken)
    {
        var parts = token.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw CliErrors.InvalidValue("命令列表为空", "用逗号分隔 id 或名称，例如 7,8");
        }

        var result = new List<Command>();
        foreach (var part in parts)
        {
            result.Add(await ResolveCommandAsync(part, groupToken));
        }
        return result;
    }

    private async Task<IReadOnlyList<Group>> FindGroupsAsync(string token)
    {
        var groups = await _store.GetGroupsAsync();
        var text = token.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<Group>();
        }

        if (int.TryParse(text, out var id))
        {
            return groups.Where(group => group.Id == id).ToList();
        }

        var segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return Array.Empty<Group>();
        }

        var current = groups
            .Where(group => string.Equals(group.Name.Trim(), segments[0], StringComparison.OrdinalIgnoreCase))
            .ToList();
        for (var index = 1; index < segments.Length && current.Count > 0; index++)
        {
            var parents = current.Select(group => group.Id).ToHashSet();
            var segment = segments[index];
            current = groups
                .Where(group => group.ParentId is int parentId
                                && parents.Contains(parentId)
                                && string.Equals(group.Name.Trim(), segment, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        return current;
    }

    private static JsonArray GroupCandidates(IReadOnlyList<Group> groups)
    {
        var array = new JsonArray();
        foreach (var group in groups.OrderBy(group => group.Id))
        {
            array.Add(new JsonObject
            {
                ["id"] = group.Id,
                ["name"] = group.Name,
                ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null
            });
        }
        return array;
    }

    private static JsonArray CommandCandidates(IReadOnlyList<Command> commands)
    {
        var array = new JsonArray();
        foreach (var command in commands.OrderBy(command => command.Id))
        {
            array.Add(new JsonObject
            {
                ["id"] = command.Id,
                ["name"] = command.Name,
                ["groupId"] = command.GroupId
            });
        }
        return array;
    }
}
