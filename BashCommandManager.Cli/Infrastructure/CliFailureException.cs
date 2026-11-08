using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Infrastructure;

/// <summary>CLI 的统一错误对象：错误码、消息、提示、明细与退出码。</summary>
public sealed class CliError
{
    public CliError(string code, string message, int exitCode, string? hint = null, JsonObject? details = null)
    {
        Code = code;
        Message = message;
        ExitCode = exitCode;
        Hint = hint;
        Details = details;
    }

    public string Code { get; }
    public string Message { get; }
    public int ExitCode { get; }
    public string? Hint { get; }
    public JsonObject? Details { get; }
}

/// <summary>携带 CliError 的异常；CliHost 是唯一捕获点。</summary>
public sealed class CliFailureException : Exception
{
    public CliFailureException(CliError error) : base(error.Message) => Error = error;

    public CliError Error { get; }
}

/// <summary>固定错误码集合与默认退出码映射。</summary>
public static class CliErrorCodes
{
    public const string Usage = "E_USAGE";
    public const string InvalidValue = "E_INVALID_VALUE";
    public const string Ambiguous = "E_AMBIGUOUS";
    public const string ConfirmRequired = "E_CONFIRM_REQUIRED";
    public const string RootGroup = "E_ROOT_GROUP";
    public const string NotFound = "E_NOT_FOUND";
    public const string FileNotFound = "E_FILE_NOT_FOUND";
    public const string GuiNotFound = "E_GUI_NOT_FOUND";
    public const string ConfigKey = "E_CONFIG_KEY";
    public const string DbError = "E_DB_ERROR";
    public const string DbBusy = "E_DB_BUSY";
    public const string CommandFailed = "E_COMMAND_FAILED";
    public const string Timeout = "E_TIMEOUT";
    public const string ElevationCancelled = "E_ELEVATION_CANCELLED";
    public const string Internal = "E_INTERNAL";

    public static int ExitCodeFor(string code) => code switch
    {
        Usage or InvalidValue or Ambiguous or ConfirmRequired or RootGroup or ConfigKey or FileNotFound => 1,
        NotFound or GuiNotFound => 2,
        _ => 3
    };
}

/// <summary>错误工厂：所有消息文案集中在此，便于测试逐字断言。</summary>
public static class CliErrors
{
    public static CliFailureException Usage(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.Usage, message, 1, hint));

    public static CliFailureException InvalidValue(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.InvalidValue, message, 1, hint));

    public static CliFailureException Ambiguous(string what, JsonArray candidates)
        => new(new CliError(CliErrorCodes.Ambiguous, $"{what}不唯一，请改用 id 或 --group",
            CliErrorCodes.ExitCodeFor(CliErrorCodes.Ambiguous),
            "用 bcm commands list --json 或 bcm groups list --json 查看 id",
            new JsonObject { ["candidates"] = candidates }));

    public static CliFailureException ConfirmRequired(string message)
        => new(new CliError(CliErrorCodes.ConfirmRequired, message, 1,
            "确认后加 --force 重新执行；代理必须先向人类确认"));

    public static CliFailureException RootGroup()
        => new(new CliError(CliErrorCodes.RootGroup, "根分组不允许删除（Id=1）", 1,
            "如需清空，请删除根分组的子分组"));

    public static CliFailureException NotFound(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.NotFound, message, 2, hint));

    public static CliFailureException FileNotFound(string message, string? hint = null, int exitCode = 1)
        => new(new CliError(CliErrorCodes.FileNotFound, message, exitCode, hint));

    public static CliFailureException GuiNotFound(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.GuiNotFound, message, 2, hint));

    public static CliFailureException ConfigKey(string message, string? hint = null)
        => new(new CliError(CliErrorCodes.ConfigKey, message, 1, hint));

    public static CliFailureException DbError(string message)
        => new(new CliError(CliErrorCodes.DbError, $"数据库错误: {message}", 3));

    public static CliFailureException DbBusy(string message)
        => new(new CliError(CliErrorCodes.DbBusy, $"数据库被占用，请稍后重试: {message}", 3));

    public static CliFailureException CommandFailed(int childExitCode)
        => new(new CliError(CliErrorCodes.CommandFailed, $"命令退出码 {childExitCode}", 3, null,
            new JsonObject { ["exitCode"] = childExitCode }));

    public static CliFailureException Timeout(int seconds)
        => new(new CliError(CliErrorCodes.Timeout, $"命令执行超时（{seconds} 秒），已终止进程树", 3,
            null, new JsonObject { ["timeoutSeconds"] = seconds }));

    public static CliFailureException ElevationCancelled()
        => new(new CliError(CliErrorCodes.ElevationCancelled, "已取消管理员授权", 3));

    public static CliFailureException Internal(string message)
        => new(new CliError(CliErrorCodes.Internal, $"内部错误: {message}", 3));
}
