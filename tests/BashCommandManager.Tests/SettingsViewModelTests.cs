using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using BashCommandManager.Tests.Fakes;
using BashCommandManager.ViewModels;
using System.IO;
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

    [Fact]
    public void Reload_丢弃未保存编辑并恢复服务值()
    {
        var settings = new FakeSettingsService
        {
            Hotkey = new HotkeySettings
            {
                Enabled = true,
                Key = Key.T,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift
            },
            Execution = new ExecutionSettings
            {
                Terminal = TerminalKind.Cmd,
                ArgumentsTemplate = "/k {file}",
                KeepWindowOpen = true
            }
        };
        var viewModel = CreateViewModel(settings);

        // 模拟取消前未保存的编辑。
        viewModel.HotkeySettings.Key = Key.F9;
        viewModel.Terminal = TerminalKind.WindowsTerminal;
        viewModel.CustomExecutablePath = @"D:\tmp\term.exe";
        viewModel.KeepWindowOpen = false;
        viewModel.ArgumentsTemplate = "-d {dir} cmd.exe /c {file}";
        viewModel.HasError = true;
        viewModel.ErrorMessage = "上次编辑留下的错误";

        viewModel.Reload();

        Assert.Equal(Key.T, viewModel.HotkeySettings.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, viewModel.HotkeySettings.Modifiers);
        Assert.Equal("Ctrl+Shift+T", viewModel.HotkeySettingsDisplay);
        Assert.Equal(TerminalKind.Cmd, viewModel.Terminal);
        Assert.Equal(string.Empty, viewModel.CustomExecutablePath);
        Assert.True(viewModel.KeepWindowOpen);
        Assert.False(viewModel.RunAsAdministrator);
        Assert.Equal("/k {file}", viewModel.ArgumentsTemplate);
        Assert.False(viewModel.IsCustomTerminal);
        Assert.False(viewModel.HasError);
        Assert.Equal(string.Empty, viewModel.ErrorMessage);
    }

    [Fact]
    public void 保存_快捷键冲突_执行设置仍保存且提示未更改()
    {
        var settings = new FakeSettingsService();
        var hotkey = new FakeGlobalHotkeyService
        {
            UpdateResult = new HotkeyRegistrationResult(
                HotkeyRegistrationStatus.Conflict,
                Key.B,
                ModifierKeys.Control | ModifierKeys.Alt)
        };
        var viewModel = CreateViewModel(settings, hotkey);
        viewModel.Terminal = TerminalKind.WindowsTerminal;

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(1, settings.SaveExecutionCalls);
        Assert.Equal(TerminalKind.WindowsTerminal, settings.Execution.Terminal);
        Assert.Equal(0, settings.SaveHotkeyCalls);
        Assert.True(viewModel.HasError);
        Assert.Contains("执行设置已保存，快捷键未更改", viewModel.ErrorMessage);
    }

    [Fact]
    public void 保存_关闭快捷键_先落盘再注销()
    {
        var settings = new FakeSettingsService();
        var hotkey = new FakeGlobalHotkeyService();
        var viewModel = CreateViewModel(settings, hotkey);
        viewModel.HotkeySettings.Enabled = false;

        var hotkeyPersisted = false;
        settings.OnSaveHotkey = saved =>
        {
            hotkeyPersisted = true;
            Assert.False(saved.Enabled);
        };
        hotkey.OnUnregister = () =>
            Assert.True(hotkeyPersisted, "必须先保存 Enabled=false 再注销热键");

        viewModel.SaveCommand.Execute(null);

        Assert.True(hotkeyPersisted);
        Assert.True(hotkey.UnregisterCalled);
        Assert.Equal(1, settings.SaveHotkeyCalls);
        Assert.Equal(1, settings.SaveExecutionCalls);
        Assert.False(settings.Hotkey.Enabled);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public void 保存_关闭快捷键_热键落盘失败时保持注册()
    {
        var settings = new FakeSettingsService
        {
            SaveHotkeyError = new IOException("磁盘已满")
        };
        var hotkey = new FakeGlobalHotkeyService();
        var viewModel = CreateViewModel(settings, hotkey);
        viewModel.HotkeySettings.Enabled = false;

        viewModel.SaveCommand.Execute(null);

        Assert.False(hotkey.UnregisterCalled);
        Assert.True(viewModel.HasError);
        Assert.Contains("执行设置已保存，快捷键设置未保存", viewModel.ErrorMessage);
        Assert.Contains("磁盘已满", viewModel.ErrorMessage);
    }

    [Fact]
    public void 保存_执行设置落盘失败时不注册也不注销热键()
    {
        var settings = new FakeSettingsService
        {
            SaveExecutionError = new IOException("磁盘已满")
        };
        var hotkey = new FakeGlobalHotkeyService();
        var viewModel = CreateViewModel(settings, hotkey);
        viewModel.HotkeySettings.Enabled = false;

        viewModel.SaveCommand.Execute(null);

        Assert.False(hotkey.UnregisterCalled);
        Assert.Equal(0, settings.SaveHotkeyCalls);
        Assert.True(viewModel.HasError);
        Assert.Contains("执行设置保存失败", viewModel.ErrorMessage);
    }
}
