using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using BashCommandManager.ViewModels;
using System.Windows.Input;
using Xunit;

namespace BashCommandManager.Tests;

public class SettingsViewModelTests
{
    private static SettingsViewModel CreateViewModel(
        FakeSettingsService? settings = null,
        FakeGlobalHotkeyService? hotkey = null)
        => new(settings ?? new FakeSettingsService(), hotkey ?? new FakeGlobalHotkeyService());

    [Fact]
    public void 构造时载入执行设置的编辑副本()
    {
        var settings = new FakeSettingsService
        {
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.WindowsTerminal,
                CustomExecutablePath = @"D:\Apps\term.exe",
                ArgumentsTemplate = "-d {dir} cmd.exe /c {file}",
                KeepWindowOpen = false,
                RunAsAdministrator = true
            }
        };

        var viewModel = CreateViewModel(settings);

        Assert.Equal(TerminalKind.WindowsTerminal, viewModel.Terminal);
        Assert.Equal(@"D:\Apps\term.exe", viewModel.CustomExecutablePath);
        Assert.Equal("-d {dir} cmd.exe /c {file}", viewModel.ArgumentsTemplate);
        Assert.False(viewModel.KeepWindowOpen);
        Assert.True(viewModel.RunAsAdministrator);
        Assert.False(viewModel.IsCustomTerminal);
    }

    [Fact]
    public void 终端选项为五个且顺序固定()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(5, viewModel.TerminalOptions.Count);
        Assert.Equal(TerminalKind.Cmd, viewModel.TerminalOptions[0].Kind);
        Assert.Equal(TerminalKind.WindowsPowerShell, viewModel.TerminalOptions[1].Kind);
        Assert.Equal(TerminalKind.PowerShell7, viewModel.TerminalOptions[2].Kind);
        Assert.Equal(TerminalKind.WindowsTerminal, viewModel.TerminalOptions[3].Kind);
        Assert.Equal(TerminalKind.Custom, viewModel.TerminalOptions[4].Kind);
        Assert.All(viewModel.TerminalOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
    }

    [Fact]
    public void 切换终端_模板仍为默认时自动重写()
    {
        var viewModel = CreateViewModel();
        Assert.Equal("/k {file}", viewModel.ArgumentsTemplate);

        viewModel.Terminal = TerminalKind.WindowsTerminal;
        Assert.Equal("-d {dir} cmd.exe /k {file}", viewModel.ArgumentsTemplate);

        viewModel.KeepWindowOpen = false;
        Assert.Equal("-d {dir} cmd.exe /c {file}", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 手改模板后切换终端不覆盖用户输入()
    {
        var viewModel = CreateViewModel();
        viewModel.ArgumentsTemplate = "/k \"{file}\" /wait";

        viewModel.Terminal = TerminalKind.WindowsTerminal;

        Assert.Equal("/k \"{file}\" /wait", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 恢复默认模板后重新随终端联动()
    {
        var viewModel = CreateViewModel();
        viewModel.ArgumentsTemplate = "/c {file} /custom";
        viewModel.Terminal = TerminalKind.Cmd;
        Assert.Equal("/c {file} /custom", viewModel.ArgumentsTemplate);

        viewModel.ResetTemplateCommand.Execute(null);
        Assert.Equal("/k {file}", viewModel.ArgumentsTemplate);

        viewModel.KeepWindowOpen = false;
        Assert.Equal("/c {file}", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 切到自定义终端时显示路径输入区()
    {
        var viewModel = CreateViewModel();
        Assert.False(viewModel.IsCustomTerminal);

        viewModel.Terminal = TerminalKind.Custom;

        Assert.True(viewModel.IsCustomTerminal);
        Assert.Equal("{file}", viewModel.ArgumentsTemplate);
    }

    [Fact]
    public void 保存_自定义路径为空_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.Terminal = TerminalKind.Custom;
        viewModel.CustomExecutablePath = "   ";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("自定义终端程序路径不能为空", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveExecutionCalls);
        Assert.Equal(0, settings.SaveHotkeyCalls);
    }

    [Fact]
    public void 保存_自定义路径不存在_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.Terminal = TerminalKind.Custom;
        viewModel.CustomExecutablePath = @"C:\nope\nope.exe";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("自定义程序不存在", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveExecutionCalls);
    }

    [Fact]
    public void 保存_模板缺少文件占位符_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.ArgumentsTemplate = "-Command echo hi";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("参数模板必须包含", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveExecutionCalls);
    }

    [Fact]
    public void 保存_模板花括号不配对_内联报错()
    {
        var viewModel = CreateViewModel();
        viewModel.ArgumentsTemplate = "-Command \"{file\"";

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("不配对", viewModel.ErrorMessage);
    }

    [Fact]
    public void 保存_快捷键无效_内联报错且不写盘()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.HotkeySettings.Modifiers = ModifierKeys.None;

        viewModel.SaveCommand.Execute(null);

        Assert.True(viewModel.HasError);
        Assert.Equal("快捷键设置无效", viewModel.ErrorMessage);
        Assert.Equal(0, settings.SaveHotkeyCalls);
    }
}
