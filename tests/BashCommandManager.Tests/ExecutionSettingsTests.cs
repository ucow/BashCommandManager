using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class ExecutionSettingsTests
{
    [Fact]
    public void 默认执行设置_与现有行为等价()
    {
        var settings = new ExecutionSettings();

        Assert.Equal(TerminalKind.Cmd, settings.Terminal);
        Assert.Equal(string.Empty, settings.CustomExecutablePath);
        Assert.Equal(string.Empty, settings.ArgumentsTemplate);
        Assert.True(settings.KeepWindowOpen);
        Assert.False(settings.RunAsAdministrator);
    }

    [Fact]
    public void Clone_修改副本不影响原对象()
    {
        var original = new ExecutionSettings
        {
            Terminal = TerminalKind.WindowsTerminal,
            KeepWindowOpen = false
        };

        var clone = original.Clone();
        clone.Terminal = TerminalKind.Custom;
        clone.ArgumentsTemplate = "{fileRaw}";
        clone.KeepWindowOpen = true;
        clone.RunAsAdministrator = true;

        Assert.Equal(TerminalKind.WindowsTerminal, original.Terminal);
        Assert.Equal(string.Empty, original.ArgumentsTemplate);
        Assert.False(original.KeepWindowOpen);
        Assert.False(original.RunAsAdministrator);
    }

    [Fact]
    public void WithTerminal_返回新实例且原对象不变()
    {
        var original = new ExecutionSettings { Terminal = TerminalKind.Cmd };

        var switched = original.WithTerminal(TerminalKind.PowerShell7);

        Assert.Equal(TerminalKind.PowerShell7, switched.Terminal);
        Assert.Equal(TerminalKind.Cmd, original.Terminal);
        Assert.NotSame(original, switched);
    }

    [Fact]
    public void AppSettings_默认包含快捷键与执行两段()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.Hotkey);
        Assert.NotNull(settings.Execution);
        Assert.Equal(TerminalKind.Cmd, settings.Execution.Terminal);
        Assert.Equal(Key.B, settings.Hotkey.Key);
    }

    [Theory]
    [InlineData(TerminalKind.Cmd, "cmd")]
    [InlineData(TerminalKind.WindowsPowerShell, "PowerShell")]
    [InlineData(TerminalKind.PowerShell7, "pwsh")]
    [InlineData(TerminalKind.WindowsTerminal, "wt")]
    [InlineData(TerminalKind.Custom, "自定义")]
    public void GetDisplayName_返回中文名称(TerminalKind kind, string expectedPart)
    {
        var display = kind.GetDisplayName();

        Assert.False(string.IsNullOrWhiteSpace(display));
        Assert.Contains(expectedPart, display);
    }

    [Fact]
    public void ExecutionResult_记录值相等()
    {
        var error = new InvalidOperationException("boom");
        var first = new ExecutionResult(false, TerminalKind.WindowsTerminal, true, "已降级", error);
        var second = new ExecutionResult(false, TerminalKind.WindowsTerminal, true, "已降级", error);

        Assert.Equal(first, second);
        Assert.Equal(TerminalKind.WindowsTerminal, first.EffectiveTerminal);
        Assert.True(first.FallbackApplied);
    }

    [Fact]
    public void TerminalLaunchPlan_运行身份常量固定为_runas()
    {
        Assert.Equal("runas", TerminalLaunchPlan.RunAsVerb);
    }
}
