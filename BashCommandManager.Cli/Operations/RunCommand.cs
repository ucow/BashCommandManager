using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Output;
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BashCommandManager.Cli.Operations;

/// <summary>run 子命令：自实现启动逻辑（D2.2），命令构造复用 TerminalCommandBuilder。</summary>
public static class RunCommand
{
    private const int ErrorCancelled = 1223;

    public static async Task<CliResult> ExecuteAsync(
        ParsedCommand command,
        CliServices services,
        CliCommandStore store,
        CommandResolver resolver)
    {
        var entity = await resolver.ResolveCommandAsync(command.Arguments[0], command.GetValue("group"));
        var settings = services.Settings.GetExecutionSettings().Clone();
        var wait = command.HasFlag("wait");
        var admin = command.HasFlag("admin");

        if (admin)
        {
            settings = settings.Clone();
            settings.RunAsAdministrator = true;
        }

        if (wait)
        {
            settings = WithoutKeepWindowOpen(settings);
        }

        if (command.HasFlag("dry-run"))
        {
            var preview = TerminalCommandBuilder.Build(settings, entity);
            var previewData = new JsonObject
            {
                ["dryRun"] = true,
                ["fileName"] = preview.FileName,
                ["arguments"] = preview.Arguments,
                ["workingDirectory"] = preview.WorkingDirectory
            };
            return CliResult.Object(previewData,
                $"fileName: {preview.FileName}{Environment.NewLine}arguments: {preview.Arguments}{Environment.NewLine}workingDirectory: {preview.WorkingDirectory}");
        }

        if (!services.FileExists(entity.FilePath))
        {
            throw CliErrors.FileNotFound($"脚本文件不存在: {entity.FilePath}", "先用 bcm commands show <id> --json 核对 filePath", exitCode: 3);
        }

        var extraMeta = new JsonObject();
        var resolved = services.Locator.Resolve(settings.Terminal, settings.CustomExecutablePath);
        if (resolved is null && settings.Terminal is TerminalKind.WindowsTerminal or TerminalKind.PowerShell7)
        {
            var requested = settings.Terminal;
            settings = settings.WithTerminal(TerminalKind.Cmd);
            resolved = services.Locator.Resolve(TerminalKind.Cmd, string.Empty);
            extraMeta["warning"] = $"未检测到 {requested}，已改用 cmd 启动";
        }

        if (resolved is null)
        {
            throw CliErrors.FileNotFound($"自定义程序不存在: {settings.CustomExecutablePath}",
                "在设置窗口修正「执行」页的自定义程序路径", exitCode: 3);
        }

        var plan = TerminalCommandBuilder.Build(settings, entity, resolved);
        var stopwatch = Stopwatch.StartNew();

        if (wait)
        {
            var timeout = command.GetValue("timeout") is string timeoutText
                ? int.Parse(timeoutText, CultureInfo.InvariantCulture)
                : (int?)null;
            var request = new ProcessRunRequest(
                plan.FileName,
                plan.Arguments,
                plan.WorkingDirectory,
                UseShellExecute: false,
                Verb: plan.Verb,
                RedirectOutput: true,
                TimeoutSeconds: timeout);

            ProcessRunResult runResult;
            try
            {
                runResult = services.Runner.RunWithResult(request);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                throw CliErrors.ElevationCancelled();
            }

            if (runResult.TimedOut)
            {
                throw CliErrors.Timeout(timeout ?? 0);
            }

            await store.RecordExecutionAsync(entity.Id);
            if (runResult.ExitCode is not 0)
            {
                throw CliErrors.CommandFailed(runResult.ExitCode ?? -1);
            }

            var waitData = new JsonObject
            {
                ["id"] = entity.Id,
                ["name"] = entity.Name,
                ["started"] = true,
                ["waited"] = true,
                ["exitCode"] = runResult.ExitCode ?? 0,
                ["durationMs"] = (int)stopwatch.ElapsedMilliseconds
            };
            return new CliResult(waitData, extraMeta.Count > 0 ? extraMeta : null,
                $"exitCode: 0{Environment.NewLine}durationMs: {stopwatch.ElapsedMilliseconds}");
        }

        try
        {
            var startResult = services.Runner.Start(new ProcessRunRequest(
                plan.FileName,
                plan.Arguments,
                plan.WorkingDirectory,
                UseShellExecute: true,
                Verb: plan.Verb,
                RedirectOutput: false,
                TimeoutSeconds: null));
            await store.RecordExecutionAsync(entity.Id);

            var data = new JsonObject
            {
                ["id"] = entity.Id,
                ["name"] = entity.Name,
                ["started"] = true,
                ["pid"] = startResult.ProcessId.HasValue ? JsonValue.Create(startResult.ProcessId.Value) : null,
                ["waited"] = false
            };
            return new CliResult(data, extraMeta.Count > 0 ? extraMeta : null,
                $"started: true{Environment.NewLine}pid: {(startResult.ProcessId.HasValue ? startResult.ProcessId.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw CliErrors.ElevationCancelled();
        }
    }

    // --wait 语义：把「保持窗口打开」用的模板换成关闭模板（仅当模板仍是默认模板）
    private static ExecutionSettings WithoutKeepWindowOpen(ExecutionSettings settings)
    {
        var clone = settings.Clone();
        var defaultTemplate = TerminalCommandBuilder.GetDefaultTemplate(settings.Terminal, settings.KeepWindowOpen);
        if (string.IsNullOrWhiteSpace(settings.ArgumentsTemplate)
            || string.Equals(settings.ArgumentsTemplate, defaultTemplate, StringComparison.Ordinal))
        {
            clone.KeepWindowOpen = false;
            clone.ArgumentsTemplate = string.Empty;
        }
        return clone;
    }
}
