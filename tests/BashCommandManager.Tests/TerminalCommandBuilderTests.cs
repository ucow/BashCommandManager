using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests;

public class TerminalCommandBuilderTests
{
    private static Command MakeCommand(string filePath) => new() { Id = 1, Name = "demo", FilePath = filePath };

    private static TerminalLaunchPlan Build(ExecutionSettings settings, string filePath)
        => TerminalCommandBuilder.Build(settings, MakeCommand(filePath));

    [Fact]
    public void B1_Cmd_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal(@"/k ""C:\work\demo.bat""", plan.Arguments);
        Assert.Equal(@"C:\work", plan.WorkingDirectory);
        Assert.True(plan.UseShellExecute);
        Assert.Null(plan.Verb);
    }

    [Fact]
    public void B2_Cmd_中文与空格路径()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\工作 目录\我的 脚本.bat");

        Assert.Equal(@"/k ""C:\工作 目录\我的 脚本.bat""", plan.Arguments);
        Assert.Equal(@"C:\工作 目录", plan.WorkingDirectory);
    }

    [Fact]
    public void B3_Cmd_不保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, KeepWindowOpen = false };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal(@"/c ""C:\work\demo.bat""", plan.Arguments);
    }

    [Fact]
    public void B4_Cmd_管理员运行()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd, RunAsAdministrator = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("runas", plan.Verb);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void B5_WindowsPowerShell_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsPowerShell, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("powershell.exe", plan.FileName);
        Assert.Equal(@"-NoExit -Command ""& 'C:\work\demo.bat'""", plan.Arguments);
        Assert.Equal(@"C:\work", plan.WorkingDirectory);
    }

    [Fact]
    public void B6_PowerShell7_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.PowerShell7, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("pwsh.exe", plan.FileName);
        Assert.Equal(@"-NoExit -Command ""& 'C:\work\demo.bat'""", plan.Arguments);
    }

    [Fact]
    public void B7_PowerShell_路径含单引号时翻倍()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsPowerShell, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work\it's.bat");

        Assert.Equal(@"-NoExit -Command ""& 'C:\work\it''s.bat'""", plan.Arguments);
    }

    [Fact]
    public void B8_WindowsTerminal_保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\work 目录\demo.bat");

        Assert.Equal("wt.exe", plan.FileName);
        Assert.Equal(@"-d ""C:\work 目录"" cmd.exe /k ""C:\work 目录\demo.bat""", plan.Arguments);
        Assert.Equal(@"C:\work 目录", plan.WorkingDirectory);
    }

    [Fact]
    public void B9_WindowsTerminal_不保持窗口打开()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, KeepWindowOpen = false };

        var plan = Build(settings, @"C:\work 目录\demo.bat");

        Assert.Equal(@"-d ""C:\work 目录"" cmd.exe /c ""C:\work 目录\demo.bat""", plan.Arguments);
    }

    [Fact]
    public void B10_WindowsTerminal_管理员运行()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, RunAsAdministrator = true };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal("runas", plan.Verb);
    }

    [Fact]
    public void B11_WindowsTerminal_分号路径被引号包住()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal, KeepWindowOpen = true };

        var plan = Build(settings, @"C:\a;b\demo.bat");

        Assert.Equal(@"-d ""C:\a;b"" cmd.exe /k ""C:\a;b\demo.bat""", plan.Arguments);
        Assert.Equal(@"C:\a;b", plan.WorkingDirectory);
    }

    [Fact]
    public void B12_自定义程序_使用自定义路径与模板()
    {
        var settings = new ExecutionSettings
        {
            Terminal = TerminalKind.Custom,
            CustomExecutablePath = @"D:\Apps\ConEmu\ConEmu64.exe",
            ArgumentsTemplate = "-run {dir} {file}"
        };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal(@"D:\Apps\ConEmu\ConEmu64.exe", plan.FileName);
        Assert.Equal(@"-run ""C:\work"" ""C:\work\demo.bat""", plan.Arguments);
    }

    [Fact]
    public void B13_自定义程序_fileRaw_原样输出()
    {
        var settings = new ExecutionSettings
        {
            Terminal = TerminalKind.Custom,
            CustomExecutablePath = @"D:\Apps\ConEmu\ConEmu64.exe",
            ArgumentsTemplate = "-run {fileRaw}"
        };

        var plan = Build(settings, @"C:\work\demo.bat");

        Assert.Equal(@"-run C:\work\demo.bat", plan.Arguments);
    }

    [Fact]
    public void B14_传入已探测的可执行文件时优先使用()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.WindowsTerminal };
        var command = MakeCommand(@"C:\work\demo.bat");

        var plan = TerminalCommandBuilder.Build(settings, command, @"C:\Fake\wt.exe");

        Assert.Equal(@"C:\Fake\wt.exe", plan.FileName);
    }

    [Fact]
    public void B15_非法模板抛配置异常()
    {
        var unknownPlaceholder = new ExecutionSettings
        {
            Terminal = TerminalKind.Cmd,
            ArgumentsTemplate = "-Command \"& '{foo}'\""
        };
        var withoutFilePlaceholder = new ExecutionSettings
        {
            Terminal = TerminalKind.Cmd,
            ArgumentsTemplate = "-Command echo hi"
        };
        var unknownBesideFile = new ExecutionSettings
        {
            Terminal = TerminalKind.Cmd,
            ArgumentsTemplate = "{file} {foo}"
        };

        var first = Assert.Throws<TerminalConfigurationException>(() => Build(unknownPlaceholder, @"C:\work\demo.bat"));
        Assert.Contains("参数模板", first.Message);

        var second = Assert.Throws<TerminalConfigurationException>(() => Build(withoutFilePlaceholder, @"C:\work\demo.bat"));
        Assert.Contains("参数模板必须包含", second.Message);

        var third = Assert.Throws<TerminalConfigurationException>(() => Build(unknownBesideFile, @"C:\work\demo.bat"));
        Assert.Contains("{foo}", third.Message);
    }

    [Fact]
    public void B16_脚本路径含双引号时抛配置异常()
    {
        var command = MakeCommand("C:\\wo\"rk\\demo.bat");

        Assert.Throws<TerminalConfigurationException>(
            () => TerminalCommandBuilder.Build(new ExecutionSettings(), command));
    }

    [Fact]
    public void B17_磁盘不存在的路径仍返回计划()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd };

        var plan = Build(settings, @"Z:\nope\x.bat");

        Assert.Equal(@"Z:\nope", plan.WorkingDirectory);
        Assert.Equal(@"/k ""Z:\nope\x.bat""", plan.Arguments);
    }

    [Fact]
    public void B18_根目录文件的工作目录为盘符根()
    {
        var settings = new ExecutionSettings { Terminal = TerminalKind.Cmd };

        var plan = Build(settings, @"C:\demo.bat");

        Assert.Equal(@"C:\", plan.WorkingDirectory);
    }

    [Fact]
    public void 相同输入两次调用返回相等结果()
    {
        var settings = new ExecutionSettings
        {
            Terminal = TerminalKind.WindowsTerminal,
            KeepWindowOpen = false,
            RunAsAdministrator = true
        };

        var first = Build(settings, @"C:\工作 目录\demo.bat");
        var second = Build(settings, @"C:\工作 目录\demo.bat");

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void ValidateTemplate_默认模板无错误()
    {
        foreach (var kind in new[]
                 {
                     TerminalKind.Cmd, TerminalKind.WindowsPowerShell, TerminalKind.PowerShell7,
                     TerminalKind.WindowsTerminal, TerminalKind.Custom
                 })
        {
            Assert.Empty(TerminalCommandBuilder.ValidateTemplate(
                TerminalCommandBuilder.GetDefaultTemplate(kind, true)));
            Assert.Empty(TerminalCommandBuilder.ValidateTemplate(
                TerminalCommandBuilder.GetDefaultTemplate(kind, false)));
        }
    }

    [Fact]
    public void ValidateTemplate_空白模板视为合法()
    {
        Assert.Empty(TerminalCommandBuilder.ValidateTemplate(string.Empty));
        Assert.Empty(TerminalCommandBuilder.ValidateTemplate("   "));
    }

    [Fact]
    public void ValidateTemplate_未知占位符报错()
    {
        // 模板必须同时带 {file}：否则实现循环结束后还会追加「必须包含 {file}…」，错误数会变成 2 条。
        var errors = TerminalCommandBuilder.ValidateTemplate("{file} {foo}");

        Assert.Single(errors);
        Assert.Contains("{foo}", errors[0]);
    }

    [Fact]
    public void ValidateTemplate_缺少文件类占位符报错()
    {
        var errors = TerminalCommandBuilder.ValidateTemplate("-Command echo hi");

        Assert.Single(errors);
        Assert.Contains("{file}", errors[0]);
    }

    [Fact]
    public void ValidateTemplate_花括号不配对报错()
    {
        var missingClose = TerminalCommandBuilder.ValidateTemplate("-Command \"{file\"");
        Assert.Contains("不配对", Assert.Single(missingClose));

        var extraClose = TerminalCommandBuilder.ValidateTemplate("-Command \"} {file}\"");
        Assert.Contains("不配对", Assert.Single(extraClose));
    }
}
