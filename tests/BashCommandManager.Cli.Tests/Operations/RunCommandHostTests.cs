using BashCommandManager.Cli;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Tests.TestSupport;
using System.Text;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Tests.Operations;

/// <summary>run 的 ConfigKey 映射需要真实 settings.json，故走 CliHost 全链路（临时 settings 目录）。</summary>
[Collection(EnvironmentCollection.Name)] // 会改进程级 BASHCOMMANDMANAGER_SETTINGS_DIR，禁止并行
public class RunCommandHostTests
{
    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliHost.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Invalid_template_settings_with_dry_run_exits_one_with_config_key()
    {
        using var temp = new TempDatabase();
        var settingsDirectory = temp.CreateDirectory("settings");
        var script = temp.CreateScript("a.cmd");
        var previous = Environment.GetEnvironmentVariable(CliPaths.SettingsDirectoryVariable);
        Environment.SetEnvironmentVariable(CliPaths.SettingsDirectoryVariable, settingsDirectory);
        try
        {
            var added = Run("commands", "add", "--name", "a", "--file", script, "--db", temp.DatabasePath, "--json");
            Assert.Equal(0, added.ExitCode);
            var commandId = JsonNode.Parse(added.Stdout)!["data"]!["id"]!.GetValue<int>();

            File.WriteAllText(
                Path.Combine(settingsDirectory, "settings.json"),
                "{\"Execution\":{\"Terminal\":0,\"ArgumentsTemplate\":\"/k no-placeholder\"}}",
                new UTF8Encoding(false));

            var result = Run("run", commandId.ToString(), "--dry-run", "--db", temp.DatabasePath, "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("", result.Stdout);
            var error = JsonNode.Parse(result.Stderr)!["error"]!;
            Assert.Equal("E_CONFIG_KEY", error["code"]!.GetValue<string>());
            Assert.Contains("参数模板必须包含", error["message"]!.GetValue<string>());
            Assert.Equal("检查 Execution 配置或运行 bcm config get", error["hint"]!.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliPaths.SettingsDirectoryVariable, previous);
        }
    }
}
