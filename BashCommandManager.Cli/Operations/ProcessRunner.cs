using System.Diagnostics;

namespace BashCommandManager.Cli.Operations;

/// <summary>一次进程启动请求（与 TerminalLaunchPlan 的字段一一对应）。</summary>
public sealed record ProcessRunRequest(
    string FileName,
    string Arguments,
    string? WorkingDirectory,
    bool UseShellExecute,
    string? Verb,
    bool RedirectOutput,
    int? TimeoutSeconds);

/// <summary>进程启动结果：是否启动、pid、退出码、是否超时。</summary>
public sealed record ProcessRunResult(bool Started, int? ProcessId, int? ExitCode, bool TimedOut);

/// <summary>进程启动端口，便于在单测中断言参数而不真的起进程。</summary>
public interface IProcessRunner
{
    ProcessRunResult Start(ProcessRunRequest request);

    ProcessRunResult RunWithResult(ProcessRunRequest request);
}

/// <summary>真实实现：ShellExecute 启动或重定向等待；子进程输出逐字节转发到本进程 stderr。</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly Stream? _stderrBytes;

    public ProcessRunner(Stream? stderrBytes = null) => _stderrBytes = stderrBytes;

    public ProcessRunResult Start(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = true,
            Verb = request.Verb ?? string.Empty
        };

        var process = Process.Start(startInfo);
        return new ProcessRunResult(true, process?.Id, null, false);
    }

    public ProcessRunResult RunWithResult(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!string.IsNullOrEmpty(request.Verb))
        {
            startInfo.Verb = request.Verb;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new ProcessRunResult(false, null, null, false);
        }

        var processId = process.Id;
        var stdoutTask = Task.Run(() => Forward(process.StandardOutput.BaseStream));
        var stderrTask = Task.Run(() => Forward(process.StandardError.BaseStream));

        bool exited;
        if (request.TimeoutSeconds is int seconds)
        {
            exited = process.WaitForExit(seconds * 1000);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                Task.WaitAll(new[] { stdoutTask, stderrTask }, 5000);
                return new ProcessRunResult(true, processId, null, true);
            }
        }
        else
        {
            process.WaitForExit();
        }

        Task.WaitAll(new[] { stdoutTask, stderrTask }, 5000);
        return new ProcessRunResult(true, processId, process.ExitCode, false);
    }

    private void Forward(Stream source)
    {
        try
        {
            if (_stderrBytes is null)
            {
                source.CopyTo(Stream.Null);
                return;
            }

            source.CopyTo(_stderrBytes);
            _stderrBytes.Flush();
        }
        catch (IOException)
        {
            // 子进程被 Kill 后管道关闭，属预期
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
