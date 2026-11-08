using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
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
                if (parsed.Json)
                {
                    var payload = new JsonObject { ["version"] = CliVersion.Value };
                    stdout.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Success(payload, null)));
                }
                else
                {
                    stdout.WriteLine(CliVersion.Value);
                }
                return 0;
            }

            throw CliErrors.Usage($"未知命令: {parsed.CommandPath}", "用 bcm --help 查看可用命令");
        }
        catch (CliFailureException ex)
        {
            WriteError(stderr, ex.Error, wantsJson);
            return ex.Error.ExitCode;
        }
    }

    private static void WriteError(TextWriter stderr, CliError error, bool json)
    {
        if (json)
        {
            stderr.WriteLine(JsonEnvelope.Serialize(JsonEnvelope.Failure(error)));
            return;
        }

        stderr.WriteLine($"错误 [{error.Code}] {error.Message}");
        if (error.Hint is not null)
        {
            stderr.WriteLine($"提示: {error.Hint}");
        }
    }
}
