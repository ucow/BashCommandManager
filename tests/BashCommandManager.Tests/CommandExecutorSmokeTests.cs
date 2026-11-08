using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests;

public class CommandExecutorSmokeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BashCommandManager.Tests",
        Guid.NewGuid().ToString("N"));

    public CommandExecutorSmokeTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void 真实启动_cmd_并在脚本退出后置为_Completed()
    {
        if (Environment.GetEnvironmentVariable("BCM_EXECUTOR_SMOKE") != "1")
        {
            return;
        }

        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Cmd,
                KeepWindowOpen = false
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;

        var executor = new CommandExecutor(settings, locator);
        var script = Path.Combine(_directory, "smoke.bat");
        File.WriteAllText(script, "@echo off\r\nexit /b 0\r\n");
        var command = new Command { Id = 99, Name = "smoke", FilePath = script };

        using var completed = new ManualResetEventSlim();
        command.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Command.Status) && command.Status == CommandStatus.Completed)
            {
                completed.Set();
            }
        };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), $"实际状态: {command.Status}");
    }
}
