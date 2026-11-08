using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Output;

public class OutputWriterTests
{
    private static (OutputWriter Writer, StringWriter Out, StringWriter Err) Create(bool json)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        return (new OutputWriter(stdout, stderr, json), stdout, stderr);
    }

    [Fact]
    public void Json_success_goes_to_stdout_only()
    {
        var (writer, stdout, stderr) = Create(json: true);
        var data = new JsonArray(new JsonObject { ["id"] = 1, ["name"] = "备份" });

        writer.WriteResult(CliResult.Items(data!), new JsonObject { ["dbPath"] = "x", ["created"] = false, ["count"] = 1 });

        Assert.Empty(stderr.ToString());
        var root = JsonNode.Parse(stdout.ToString())!.AsObject();
        Assert.True(root["ok"]!.GetValue<bool>());
        Assert.Equal(1, root["meta"]!["count"]!.GetValue<int>());
        Assert.Contains("备份", stdout.ToString());
    }

    [Fact]
    public void Json_error_goes_to_stderr_with_empty_stdout()
    {
        var (writer, stdout, stderr) = Create(json: true);

        writer.WriteError(CliErrors.NotFound("未找到命令: 备份", "用 bcm commands list --json 查看可用 id").Error);

        Assert.Empty(stdout.ToString());
        var root = JsonNode.Parse(stderr.ToString())!.AsObject();
        Assert.False(root["ok"]!.GetValue<bool>());
        Assert.Equal("E_NOT_FOUND", root["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Human_error_line_matches_spec_format()
    {
        var (writer, _, stderr) = Create(json: false);

        writer.WriteError(CliErrors.NotFound("未找到命令: 备份", "用 bcm commands list --json 查看可用 id").Error);

        var lines = stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("错误 [E_NOT_FOUND] 未找到命令: 备份", lines[0]);
        Assert.Equal("提示: 用 bcm commands list --json 查看可用 id", lines[1]);
    }

    [Fact]
    public void Human_object_renders_key_value_lines()
    {
        var (writer, stdout, _) = Create(json: false);
        var data = new JsonObject { ["id"] = 7, ["name"] = "备份", ["lastExecutedAt"] = null };

        writer.WriteResult(CliResult.Object(data), null);

        var text = stdout.ToString();
        Assert.Contains("id: 7", text);
        Assert.Contains("name: 备份", text);
        Assert.Contains("lastExecutedAt: ", text);
    }

    [Fact]
    public void Human_array_renders_table_with_mapped_headers()
    {
        var (writer, stdout, _) = Create(json: false);
        var data = new JsonArray
        {
            new JsonObject
            {
                ["id"] = 7,
                ["name"] = "备份",
                ["groupName"] = "运维",
                ["filePath"] = @"D:\bat\备份.bat"
            }
        };

        writer.WriteResult(CliResult.Items(data), null);

        // TableWriter 只用 '\n' 作行分隔（见 T4 步骤 4 的说明），不能按 Windows 的 '\r\n' 切分
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id  Name  Group  FilePath", lines[0]);
        Assert.Contains("备份", lines[1]);
        Assert.Contains(@"D:\bat\备份.bat", lines[1]);
    }

    [Fact]
    public void Human_array_uses_explicit_human_text_when_provided()
    {
        var (writer, stdout, _) = Create(json: false);

        writer.WriteResult(CliResult.Object(new JsonObject { ["id"] = 7 }, "自定义输出"), null);

        Assert.Equal("自定义输出", stdout.ToString().Trim());
    }

    [Fact]
    public void Empty_human_array_prints_placeholder()
    {
        var (writer, stdout, _) = Create(json: false);

        writer.WriteResult(CliResult.Items(new JsonArray()), null);

        Assert.Equal("(空)", stdout.ToString().Trim());
    }
}
