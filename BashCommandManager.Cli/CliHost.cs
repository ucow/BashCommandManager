using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;
using System.Data.SQLite;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli;

/// <summary>CLI 的唯一入口编排与异常边界。</summary>
public static class CliHost
{
    public static int Run(string[] args) => Run(args, Console.Out, Console.Error, Console.OpenStandardError());

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Stream? stderrBytes = null)
    {
        var wantsJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.Ordinal));
        var stopwatch = Stopwatch.StartNew();
        DatabaseSession? session = null;

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
                output.WriteResult(new CliResult(new JsonObject { ["version"] = CliVersion.Value }, null, CliVersion.Value), null);
                return 0;
            }

            DatabasePathResolution resolution;
            if (NeedsDatabase(parsed.Verb))
            {
                session = new DatabaseSession(parsed.DatabasePath);
                resolution = session.Resolution;
            }
            else
            {
                resolution = DatabasePathResolver.ResolveForCurrentProcess(parsed.DatabasePath, CliPaths.SettingsDirectory);
            }

            using var services = CreateServices(resolution, session, stderrBytes);
            var store = session is null ? null : new CliCommandStore(session.Connection);
            var resolver = store is null ? null : new CommandResolver(store);

            var result = CliDispatcher.ExecuteAsync(parsed, services, store, resolver).GetAwaiter().GetResult();
            output.WriteResult(result, BuildMeta(result, session, resolution, stopwatch.ElapsedMilliseconds));
            if (session?.MigrationWarning is string migrationWarning)
            {
                // I-4：迁移失败/目标已存在除 meta.warning 外，也在 stderr 给人类可读告警；退出码不变。
                stderr.WriteLine($"警告: {migrationWarning}");
            }
            return 0;
        }
        catch (CliFailureException ex)
        {
            new OutputWriter(stdout, stderr, wantsJson).WriteError(ex.Error);
            return ex.Error.ExitCode;
        }
        catch (SQLiteException ex)
        {
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
        finally
        {
            session?.Dispose();
        }
    }

    private static bool NeedsDatabase(string verb) => verb is "groups" or "commands" or "run";

    private static CliServices CreateServices(DatabasePathResolution resolution, DatabaseSession? session, Stream? stderrBytes)
        => new(
            resolution,
            session,
            new CliSettingsStore(CliPaths.SettingsDirectory),
            new ProcessRunner(stderrBytes),
            new GuiLauncher(),
            new ExecutableLocator(),
            File.Exists,
            Directory.Exists,
            AppContext.BaseDirectory);

    private static JsonObject BuildMeta(CliResult result, DatabaseSession? session, DatabasePathResolution resolution, long durationMs)
    {
        var meta = new JsonObject
        {
            ["dbPath"] = session?.DatabasePath ?? resolution.DatabasePath,
            ["created"] = session?.Created ?? false
        };

        if (result.Data is JsonArray array)
        {
            meta["count"] = array.Count;
        }

        var warning = session?.MigrationWarning;
        if (warning is not null)
        {
            meta["warning"] = warning;
        }

        if (result.Meta is not null)
        {
            foreach (var pair in result.Meta)
            {
                meta[pair.Key] = pair.Value?.DeepClone();
            }
        }

        meta["durationMs"] = durationMs;
        return meta;
    }
}
