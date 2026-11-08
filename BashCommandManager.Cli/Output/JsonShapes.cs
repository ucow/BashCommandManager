using BashCommandManager.Core.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>分组/命令的 JSON 形状：字段名与时间格式的唯一来源。</summary>
public static class JsonShapes
{
    public static JsonObject Group(Group group, int commandCount) => new()
    {
        ["id"] = group.Id,
        ["name"] = group.Name,
        ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null,
        ["sortOrder"] = group.SortOrder,
        ["commandCount"] = commandCount
    };

    public static JsonObject GroupNode(Group group) => new()
    {
        ["id"] = group.Id,
        ["name"] = group.Name,
        ["parentId"] = group.ParentId.HasValue ? JsonValue.Create(group.ParentId.Value) : null,
        ["sortOrder"] = group.SortOrder
    };

    public static JsonObject Command(Command command, string? groupName, bool includeFileExists, Func<string, bool> fileExists)
    {
        var node = new JsonObject
        {
            ["id"] = command.Id,
            ["name"] = command.Name,
            ["description"] = command.Description ?? string.Empty,
            ["filePath"] = command.FilePath,
            ["groupId"] = command.GroupId,
            ["groupName"] = groupName ?? string.Empty,
            ["sortOrder"] = command.SortOrder,
            ["executionCount"] = command.ExecutionCount,
            ["lastExecutedAt"] = command.LastExecutedAt.HasValue
                ? JsonValue.Create(FormatTimestamp(command.LastExecutedAt))
                : null
        };

        if (includeFileExists)
        {
            node["fileExists"] = fileExists(command.FilePath);
        }

        return node;
    }

    public static string? FormatTimestamp(DateTime? value)
        => value?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
