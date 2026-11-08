using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>处理器返回值：data 必填、meta 追加字段可选、HumanText 覆盖人类可读渲染。</summary>
public sealed record CliResult(JsonNode? Data, JsonObject? Meta = null, string? HumanText = null)
{
    public static CliResult Object(JsonObject data, string? humanText = null) => new(data, null, humanText);

    public static CliResult Items(JsonArray items, string? humanText = null) => new(items, null, humanText);
}
