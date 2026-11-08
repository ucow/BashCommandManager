using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Output;

public class JsonEnvelopeTests
{
    [Fact]
    public void Success_envelope_has_ok_data_and_meta()
    {
        var data = new JsonObject { ["id"] = 1 };
        var meta = new JsonObject { ["count"] = 1 };

        var text = JsonEnvelope.Serialize(JsonEnvelope.Success(data, meta));
        var root = JsonNode.Parse(text)!.AsObject();

        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal(1, root["data"]!["id"]!.GetValue<int>());
        Assert.Equal(1, root["meta"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void Success_envelope_omits_meta_when_null()
    {
        var text = JsonEnvelope.Serialize(JsonEnvelope.Success(new JsonObject(), null));

        Assert.DoesNotContain("\"meta\"", text);
    }

    [Fact]
    public void Failure_envelope_carries_code_message_hint_and_details()
    {
        var error = CliErrors.Ambiguous("命令", new JsonArray { new JsonObject { ["id"] = 3 } }).Error;

        var text = JsonEnvelope.Serialize(JsonEnvelope.Failure(error));
        var root = JsonNode.Parse(text)!.AsObject();
        var node = root["error"]!.AsObject();

        Assert.False(root["ok"]!.GetValue<bool>());
        Assert.Equal("E_AMBIGUOUS", node["code"]!.GetValue<string>());
        Assert.Contains("不唯一", node["message"]!.GetValue<string>());
        Assert.NotNull(node["hint"]);
        Assert.Equal(3, node["details"]!["candidates"]![0]!["id"]!.GetValue<int>());
    }

    [Fact]
    public void Chinese_is_not_escaped()
    {
        var text = JsonEnvelope.Serialize(JsonEnvelope.Success(new JsonObject { ["name"] = "备份库" }, null));

        Assert.Contains("备份库", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void Command_shape_has_expected_fields_and_null_timestamp()
    {
        var command = new Command
        {
            Id = 7,
            Name = "备份",
            Description = null,
            FilePath = @"D:\bat\备份.bat",
            GroupId = 2,
            SortOrder = 0,
            ExecutionCount = 3,
            LastExecutedAt = null
        };

        var node = JsonShapes.Command(command, "运维", includeFileExists: true, _ => true);

        Assert.Equal(7, node["id"]!.GetValue<int>());
        Assert.Equal(string.Empty, node["description"]!.GetValue<string>());
        Assert.Equal("运维", node["groupName"]!.GetValue<string>());
        Assert.Null(node["lastExecutedAt"]);
        Assert.True(node["fileExists"]!.GetValue<bool>());
        Assert.Equal(new[] { "id", "name", "description", "filePath", "groupId", "groupName", "sortOrder", "executionCount", "lastExecutedAt", "fileExists" },
            node.Select(pair => pair.Key).ToArray());
    }

    [Fact]
    public void Timestamp_is_local_iso_without_fraction_or_offset()
    {
        var value = new DateTime(2026, 11, 8, 20, 47, 32, 123);

        Assert.Equal("2026-11-08T20:47:32", JsonShapes.FormatTimestamp(value));
        Assert.Null(JsonShapes.FormatTimestamp(null));
    }

    [Fact]
    public void Group_shape_has_command_count_and_null_parent()
    {
        var group = new Group { Id = 1, Name = "根分组", ParentId = null, SortOrder = 0 };

        var node = JsonShapes.Group(group, 2);

        Assert.Equal(1, node["id"]!.GetValue<int>());
        Assert.Null(node["parentId"]);
        Assert.Equal(2, node["commandCount"]!.GetValue<int>());
        Assert.Equal(new[] { "id", "name", "parentId", "sortOrder", "commandCount" }, node.Select(pair => pair.Key).ToArray());
    }
}
