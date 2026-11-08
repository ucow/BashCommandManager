using BashCommandManager.Cli.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>输出契约：成功写 stdout、失败写 stderr；JSON 或人类可读两种模式。</summary>
public interface IOutputWriter
{
    void WriteResult(CliResult result, JsonObject? meta);

    void WriteError(CliError error);
}

/// <summary>IOutputWriter 的默认实现。</summary>
public sealed class OutputWriter : IOutputWriter
{
    private static readonly Dictionary<string, string> HeaderLabels = new(StringComparer.Ordinal)
    {
        ["id"] = "Id",
        ["name"] = "Name",
        ["parentId"] = "ParentId",
        ["sortOrder"] = "SortOrder",
        ["commandCount"] = "命令数",
        ["groupId"] = "GroupId",
        ["groupName"] = "Group",
        ["description"] = "Description",
        ["filePath"] = "FilePath",
        ["executionCount"] = "执行次数",
        ["lastExecutedAt"] = "上次执行",
        ["fileExists"] = "文件存在",
        ["enabled"] = "Enabled",
        ["key"] = "Key",
        ["keyName"] = "KeyName",
        ["modifiers"] = "Modifiers",
        ["display"] = "Display",
        ["path"] = "Path",
        ["format"] = "Format",
        ["count"] = "Count"
    };

    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _json;

    public OutputWriter(TextWriter stdout, TextWriter stderr, bool json)
    {
        _stdout = stdout;
        _stderr = stderr;
        _json = json;
    }

    public void WriteResult(CliResult result, JsonObject? meta)
    {
        if (_json)
        {
            _stdout.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Success(result.Data, meta)));
            return;
        }

        var text = result.HumanText ?? RenderHuman(result.Data);
        if (text.Length > 0)
        {
            _stdout.WriteLine(text);
        }
    }

    public void WriteError(CliError error)
    {
        if (_json)
        {
            _stderr.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Failure(error)));
            return;
        }

        _stderr.WriteLine($"错误 [{error.Code}] {error.Message}");
        if (error.Hint is not null)
        {
            _stderr.WriteLine($"提示: {error.Hint}");
        }
    }

    private static string RenderHuman(JsonNode? data) => data switch
    {
        null => string.Empty,
        JsonObject node => RenderObject(node, string.Empty),
        JsonArray array => RenderArray(array),
        JsonNode node => node.ToJsonString(JsonEnvelope.SerializerOptions)
    };

    private static string RenderObject(JsonObject node, string prefix)
    {
        var lines = new List<string>();
        foreach (var pair in node)
        {
            var label = prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key;
            if (pair.Value is JsonObject nested)
            {
                lines.AddRange(RenderObject(nested, label).Split(Environment.NewLine));
                continue;
            }

            lines.Add($"{label}: {ScalarText(pair.Value)}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderArray(JsonArray array)
    {
        if (array.Count == 0)
        {
            return "(空)";
        }

        if (array.All(item => item is JsonObject))
        {
            var headers = ((JsonObject)array[0]!).Select(pair => HeaderLabel(pair.Key)).ToList();
            var rows = array
                .Select(item => (IReadOnlyList<string>)((JsonObject)item!)
                    .Select(pair => ScalarText(pair.Value))
                    .ToList())
                .ToList();
            return TableWriter.Render(headers, rows);
        }

        return string.Join(Environment.NewLine, array.Select(item => item?.ToJsonString(JsonEnvelope.SerializerOptions) ?? "null"));
    }

    private static string HeaderLabel(string key) => HeaderLabels.TryGetValue(key, out var label) ? label : key;

    private static string ScalarText(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToJsonString(JsonEnvelope.SerializerOptions);
    }
}
