using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using System.Data.SQLite;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli;

/// <summary>CLI 的唯一入口编排与异常边界。</summary>
public static class CliHost
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error, Console.OpenStandardError());

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null)
    {
        var wantsJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.Ordinal));
        try
        {
            var parsed = CommandLineParser.Parse(args);
            var output = new OutputWriter(stdout, stderr, parsed.Json);

            if (parsed.CommandPath == "help")
            {
                stdout.Write(HelpCatalog.Global());
                return 0;
            }

            if (parsed.CommandPath.StartsWith("help.", StringComparison.Ordinal))
            {
                stdout.Write(HelpCatalog.For(parsed.CommandPath["help.".Length..]));
                return 0;
            }

            if (parsed.CommandPath == "version")
            {
                output.WriteResult(
                    new CliResult(new JsonObject { ["version"] = CliVersion.Value }, null, CliVersion.Value),
                    null);
                return 0;
            }

            throw CliErrors.Usage($"命令尚未接线: {parsed.CommandPath}", "T12 起由 CliDispatcher 处理");
        }
        catch (CliFailureException ex)
        {
            new OutputWriter(stdout, stderr, wantsJson).WriteError(ex.Error);
            return ex.Error.ExitCode;
        }
        catch (SQLiteException ex)
        {
            // 偏差记录（T4）：计划原文 var error = CliErrors.DbError(...) 得到的是异常对象，
            // WriteError/ExitCode 需要 CliError，故取 .Error。
            var error = CliErrors.DbError(ex.Message).Error;
            new OutputWriter(stdout, stderr, wantsJson).WriteError(error);
            return error.ExitCode;
        }
        catch (Exception ex)
        {
            var error = CliErrors.Internal(ex.Message).Error;
            new OutputWriter(stdout, stderr, wantsJson).WriteError(error);
            return error.ExitCode;
        }
    }
}
