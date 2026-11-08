using BashCommandManager.Cli.Operations;

namespace BashCommandManager.Cli.Tests.Operations;

public class ProcessRunnerTests
{
    private static ProcessRunRequest Cmd(string arguments, bool redirect = false, int? timeout = null)
        => new("cmd.exe", arguments, Path.GetTempPath(), UseShellExecute: false, Verb: null, RedirectOutput: redirect, TimeoutSeconds: timeout);

    [Fact]
    public void RunWithResult_returns_child_exit_code_and_pid()
    {
        var runner = new ProcessRunner();

        var result = runner.RunWithResult(Cmd("/c exit 5"));

        Assert.True(result.Started);
        Assert.False(result.TimedOut);
        Assert.NotNull(result.ProcessId);
        Assert.True(result.ProcessId > 0);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public void RunWithResult_forwards_child_stderr_bytes_to_provided_stream()
    {
        using var buffer = new MemoryStream();
        var runner = new ProcessRunner(buffer);

        var result = runner.RunWithResult(Cmd("/c echo marker 1>&2"));

        Assert.Equal(0, result.ExitCode);
        var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        Assert.Contains("marker", text);
    }

    [Fact]
    public void RunWithResult_reports_timeout_and_kills_process()
    {
        var runner = new ProcessRunner();

        var result = runner.RunWithResult(Cmd("/c ping -n 6 127.0.0.1 > nul", timeout: 1));

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void Start_with_shell_execute_returns_started()
    {
        var runner = new ProcessRunner();

        var result = runner.Start(Cmd("/c exit 0"));

        Assert.True(result.Started);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void Start_with_missing_file_throws_win32_exception()
    {
        var runner = new ProcessRunner();

        var request = new ProcessRunRequest(
            @"Z:\definitely-missing\bcm-missing.exe", string.Empty, null, UseShellExecute: true, Verb: null, RedirectOutput: false, TimeoutSeconds: null);

        Assert.ThrowsAny<System.ComponentModel.Win32Exception>(() => runner.Start(request));
    }
}
