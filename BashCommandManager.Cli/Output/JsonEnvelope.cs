using BashCommandManager.Cli.Infrastructure;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Output;

/// <summary>统一 JSON 信封：{ok:true,data,meta} / {ok:false,error}。</summary>
public static class JsonEnvelope
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonObject Success(JsonNode? data, JsonObject? meta)
    {
        var root = new JsonObject { ["ok"] = true };
        root["data"] = data?.DeepClone();
        if (meta is not null)
        {
            root["meta"] = meta;
        }
        return root;
    }

    public static JsonObject Failure(CliError error)
    {
        var node = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message
        };
        if (error.Hint is not null)
        {
            node["hint"] = error.Hint;
        }
        if (error.Details is not null)
        {
            node["details"] = error.Details.DeepClone();
        }
        return new JsonObject { ["ok"] = false, ["error"] = node };
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(SerializerOptions);
}
