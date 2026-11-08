using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Data.SQLite;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>open 子命令：定位并启动 GUI（可选定位到某条命令）。</summary>
public static class OpenCommand
{
    public static Task<CliResult> ExecuteAsync(ParsedCommand command, CliServices services)
    {
        int? commandId = command.GetValue("command") is string text
            ? int.Parse(text, CultureInfo.InvariantCulture)
            : null;

        // --command 必须指向真实存在的命令：先校验再启动 GUI，避免代理误判“定位成功”（验收 9）。
        // 只读打开数据库；库文件不存在时直接 E_NOT_FOUND，绝不创建空库。
        if (commandId is int id && !CommandExists(services.DatabaseResolution.DatabasePath, id))
        {
            throw CliErrors.NotFound($"未找到命令: {id}",
                "用 bcm commands list --json 查看现有命令 id");
        }

        var explicitGui = command.GetValue("gui");
        var guiPath = services.Launcher.ResolveGuiPath(explicitGui);
        if (guiPath is null)
        {
            throw explicitGui is null
                ? CliErrors.GuiNotFound("未找到 BashCommandManager.exe",
                    $"用 --gui <路径> 指定，或设置环境变量 {GuiLauncher.EnvironmentVariableName}")
                : CliErrors.GuiNotFound($"指定的 GUI 不存在: {explicitGui}", "核对 --gui 路径");
        }

        var processId = services.Launcher.Launch(guiPath, commandId);

        var data = new JsonObject
        {
            ["opened"] = true,
            ["guiPath"] = guiPath,
            ["commandId"] = commandId.HasValue ? JsonValue.Create(commandId.Value) : null
        };
        var human = $"guiPath: {guiPath}{Environment.NewLine}commandId: {(commandId?.ToString(CultureInfo.InvariantCulture) ?? "null")}";
        return Task.FromResult(new CliResult(data,
            processId > 0 ? new JsonObject { ["pid"] = processId } : null,
            human));
    }

    private static bool CommandExists(string databasePath, int commandId)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        var builder = new SQLiteConnectionStringBuilder
        {
            DataSource = databasePath,
            ReadOnly = true,
            FailIfMissing = true
        };
        using var connection = new SQLiteConnection(builder.ConnectionString);
        connection.Open();
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT COUNT(1) FROM Commands WHERE Id = @id";
        query.Parameters.AddWithValue("@id", commandId);
        return Convert.ToInt32(query.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }
}
