using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests;

public class CommandExecutorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "BashCommandManager.Tests",
        Guid.NewGuid().ToString("N"));

    public CommandExecutorTests()
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

    private string CreateScript(string name = "demo.bat")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "@echo off\r\nexit /b 0\r\n");
        return path;
    }

    [Theory]
    [InlineData(TerminalKind.PowerShell7, "PowerShell 7")]
    [InlineData(TerminalKind.WindowsTerminal, "Windows Terminal")]
    public void 终端缺失时降级_cmd_并给出提示(TerminalKind missing, string displayPart)
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = missing }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[missing] = null;
        locator.Results[TerminalKind.Cmd] = @"C:\Windows\System32\cmd.exe";

        var executor = new NotStartingCommandExecutor(settings, locator);
        var command = new Command { Id = 7, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal(TerminalKind.Cmd, result.EffectiveTerminal);
        Assert.True(result.FallbackApplied);
        Assert.NotNull(result.Notice);
        Assert.Contains(displayPart, result.Notice!);
        Assert.Contains("cmd", result.Notice!);
        Assert.Equal(new[] { missing, TerminalKind.Cmd }, locator.Calls);
        Assert.Equal(CommandStatus.Running, command.Status);
        Assert.True(executor.IsRunning(command.Id));
    }

    [Fact]
    public void 降级_cmd_时重置为_cmd_默认模板()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.WindowsTerminal,
                ArgumentsTemplate = "-d {dir} cmd.exe /k {file}"
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.WindowsTerminal] = null;
        locator.Results[TerminalKind.Cmd] = "cmd.exe";

        var executor = new CapturingCommandExecutor(settings, locator);
        var script = CreateScript();
        var command = new Command { Id = 19, Name = "demo", FilePath = script };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal(TerminalKind.Cmd, result.EffectiveTerminal);
        Assert.True(result.FallbackApplied);
        Assert.Equal("cmd.exe", executor.StartInfo!.FileName);
        Assert.Equal(@"/k """ + script + @"""", executor.StartInfo.Arguments);
        Assert.Contains("已改用 cmd 并使用 cmd 默认参数", result.Notice);
        // 降级只作用于本次启动，不能污染服务里的用户配置。
        Assert.Equal("-d {dir} cmd.exe /k {file}", settings.Execution.ArgumentsTemplate);
    }

    [Fact]
    public void 终端存在时不降级()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.PowerShell7 }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.PowerShell7] = @"C:\Fake\pwsh.exe";

        var executor = new CapturingCommandExecutor(settings, locator);
        var command = new Command { Id = 8, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal(TerminalKind.PowerShell7, result.EffectiveTerminal);
        Assert.False(result.FallbackApplied);
        Assert.Null(result.Notice);
        Assert.Equal(@"C:\Fake\pwsh.exe", executor.StartInfo!.FileName);
    }

    [Fact]
    public void Cmd_探测为_null_时直接使用逻辑名且不降级()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;

        var executor = new CapturingCommandExecutor(settings, locator);
        var command = new Command { Id = 9, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.False(result.FallbackApplied);
        Assert.Null(result.Notice);
        Assert.Equal("cmd.exe", executor.StartInfo!.FileName);
    }

    [Fact]
    public void 自定义程序不存在时返回错误且不启动()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Custom,
                CustomExecutablePath = @"C:\nope\term.exe"
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Custom] = null;

        var executor = new NotStartingCommandExecutor(settings, locator);
        var command = new Command { Id = 10, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.False(result.Started);
        var error = Assert.IsType<FileNotFoundException>(result.Error);
        Assert.Contains(@"C:\nope\term.exe", error.Message);
        Assert.False(result.FallbackApplied);
        Assert.Equal(CommandStatus.Failed, command.Status);
        Assert.False(executor.IsRunning(command.Id));
    }

    [Fact]
    public void 取消_UAC_时状态回到_Idle_并返回取消提示()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var executor = new ThrowingCommandExecutor(settings, locator, new Win32Exception(1223));
        var command = new Command { Id = 11, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions(RunAsAdmin: true));

        Assert.False(result.Started);
        Assert.Null(result.Error);
        Assert.Equal("已取消管理员授权", result.Notice);
        Assert.Equal(CommandStatus.Idle, command.Status);
        Assert.False(executor.IsRunning(command.Id));
    }

    [Fact]
    public void 其它启动失败时状态为_Failed()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var executor = new ThrowingCommandExecutor(settings, locator, new Win32Exception(2, "系统找不到指定的文件。"));
        var command = new Command { Id = 12, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.False(result.Started);
        Assert.NotNull(result.Error);
        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void 脚本不存在时抛_FileNotFoundException_且状态为_Failed()
    {
        var settings = new FakeSettingsService();
        var executor = new NotStartingCommandExecutor(settings, new FakeExecutableLocator());
        var command = new Command
        {
            Id = 13,
            Name = "missing",
            FilePath = Path.Combine(_directory, "missing.bat")
        };

        Assert.Throws<FileNotFoundException>(
            () => executor.ExecuteWithResult(command, new ExecutionOptions()));
        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void 模板非法时返回配置异常且不启动()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Cmd,
                ArgumentsTemplate = "{file} {foo}"
            }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var executor = new NotStartingCommandExecutor(settings, locator);
        var command = new Command { Id = 14, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.False(result.Started);
        Assert.IsType<TerminalConfigurationException>(result.Error);
        Assert.Equal(CommandStatus.Failed, command.Status);
    }

    [Fact]
    public void 本次管理员运行只作用于本次且不改动全局配置()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd, RunAsAdministrator = false }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;
        var executor = new CapturingCommandExecutor(settings, locator);
        var script = CreateScript();
        var command = new Command { Id = 15, Name = "demo", FilePath = script };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions(RunAsAdmin: true));

        Assert.True(result.Started);
        Assert.Equal("runas", executor.StartInfo!.Verb);
        Assert.True(executor.StartInfo.UseShellExecute);
        Assert.Equal("cmd.exe", executor.StartInfo.FileName);
        Assert.Equal(@"/k """ + script + @"""", executor.StartInfo.Arguments);
        Assert.Equal(_directory, executor.StartInfo.WorkingDirectory);
        Assert.False(settings.Execution.RunAsAdministrator);
    }

    [Fact]
    public void 全局管理员运行配置直接作用于启动计划()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd, RunAsAdministrator = true }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = null;
        var executor = new CapturingCommandExecutor(settings, locator);
        var command = new Command { Id = 16, Name = "demo", FilePath = CreateScript() };

        var result = executor.ExecuteWithResult(command, new ExecutionOptions());

        Assert.True(result.Started);
        Assert.Equal("runas", executor.StartInfo!.Verb);
    }

    [Fact]
    public void Execute_成功时不抛异常_取消时抛_OperationCanceledException()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings { Terminal = TerminalKind.Cmd }
        };
        var locator = new FakeExecutableLocator();
        locator.Results[TerminalKind.Cmd] = "cmd.exe";
        var command = new Command { Id = 17, Name = "demo", FilePath = CreateScript() };

        var success = new NotStartingCommandExecutor(settings, locator);
        success.Execute(command, runAsAdmin: false);
        Assert.Equal(CommandStatus.Running, command.Status);

        var cancelledCommand = new Command { Id = 18, Name = "demo", FilePath = CreateScript("demo2.bat") };
        var cancelled = new ThrowingCommandExecutor(settings, locator, new Win32Exception(1223));
        Assert.Throws<OperationCanceledException>(() => cancelled.Execute(cancelledCommand, runAsAdmin: true));
        Assert.Equal(CommandStatus.Idle, cancelledCommand.Status);
    }

    private sealed class NotStartingCommandExecutor : CommandExecutor
    {
        public NotStartingCommandExecutor(ISettingsService settingsService, IExecutableLocator locator)
            : base(settingsService, locator)
        {
        }

        protected override void StartProcess(Process process)
        {
        }
    }

    private sealed class CapturingCommandExecutor : CommandExecutor
    {
        public CapturingCommandExecutor(ISettingsService settingsService, IExecutableLocator locator)
            : base(settingsService, locator)
        {
        }

        public ProcessStartInfo? StartInfo { get; private set; }

        protected override void StartProcess(Process process)
        {
            StartInfo = process.StartInfo;
        }
    }

    private sealed class ThrowingCommandExecutor : CommandExecutor
    {
        private readonly Exception _error;

        public ThrowingCommandExecutor(
            ISettingsService settingsService,
            IExecutableLocator locator,
            Exception error)
            : base(settingsService, locator)
        {
            _error = error;
        }

        protected override void StartProcess(Process process) => throw _error;
    }
}
