using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests;

public class ExecutableLocatorTests
{
    private static ExecutableLocator CreateLocator(
        Dictionary<string, string?> environment,
        Func<string, bool> fileExists)
        => new(name => environment.TryGetValue(name, out var value) ? value : null, fileExists);

    [Fact]
    public void FindOnPath_命中第一个存在的文件()
    {
        var found = ExecutableLocator.FindOnPath(
            "tool.exe",
            @"C:\empty;C:\tools;C:\other",
            candidate => string.Equals(candidate, @"C:\tools\tool.exe", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"C:\tools\tool.exe", found);
    }

    [Fact]
    public void FindOnPath_未命中返回_null()
    {
        var found = ExecutableLocator.FindOnPath("tool.exe", @"C:\a;C:\b", _ => false);

        Assert.Null(found);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindOnPath_PATH_为空返回_null(string? pathVariable)
    {
        Assert.Null(ExecutableLocator.FindOnPath("tool.exe", pathVariable, _ => true));
    }

    [Fact]
    public void FindOnPath_忽略带引号的目录与空条目()
    {
        var probed = new List<string>();

        var found = ExecutableLocator.FindOnPath(
            "tool.exe",
            "\"C:\\quoted\";;C:\\good",
            candidate =>
            {
                probed.Add(candidate);
                return string.Equals(candidate, @"C:\good\tool.exe", StringComparison.OrdinalIgnoreCase);
            });

        Assert.Equal(@"C:\good\tool.exe", found);
        Assert.DoesNotContain(probed, candidate => candidate.StartsWith(@"C:\quoted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cmd_优先使用_COMSPEC()
    {
        var environment = new Dictionary<string, string?> { ["COMSPEC"] = @"C:\Fake\cmd.exe" };
        var locator = CreateLocator(environment, _ => false);

        var resolved = locator.Resolve(TerminalKind.Cmd, string.Empty);

        Assert.Equal(@"C:\Fake\cmd.exe", resolved);
    }

    [Fact]
    public void Cmd_没有_COMSPEC_时回退_System32()
    {
        var environment = new Dictionary<string, string?>
        {
            ["COMSPEC"] = null,
            ["SystemRoot"] = @"C:\Windows"
        };
        var locator = CreateLocator(environment, candidate => candidate == @"C:\Windows\System32\cmd.exe");

        var resolved = locator.Resolve(TerminalKind.Cmd, string.Empty);

        Assert.Equal(@"C:\Windows\System32\cmd.exe", resolved);
    }

    [Fact]
    public void Cmd_与_WindowsPowerShell_永不返回_null()
    {
        var locator = CreateLocator(new Dictionary<string, string?>(), _ => false);

        Assert.Equal("cmd.exe", locator.Resolve(TerminalKind.Cmd, string.Empty));
        Assert.Equal("powershell.exe", locator.Resolve(TerminalKind.WindowsPowerShell, string.Empty));
    }

    [Fact]
    public void WindowsPowerShell_优先使用_System32_全路径()
    {
        var environment = new Dictionary<string, string?> { ["SystemRoot"] = @"C:\Windows" };
        var locator = CreateLocator(
            environment,
            candidate => candidate == @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        var resolved = locator.Resolve(TerminalKind.WindowsPowerShell, string.Empty);

        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", resolved);
    }

    [Fact]
    public void PowerShell7_按_ProgramFiles_到_PATH_顺序探测()
    {
        var environment = new Dictionary<string, string?>
        {
            ["ProgramFiles"] = @"C:\PF",
            ["ProgramFiles(x86)"] = @"C:\PF86",
            ["LOCALAPPDATA"] = @"C:\LAD",
            ["PATH"] = @"C:\bin"
        };

        var locator = CreateLocator(environment, candidate => candidate == @"C:\PF\PowerShell\7\pwsh.exe");
        Assert.Equal(@"C:\PF\PowerShell\7\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\PF86\PowerShell\7\pwsh.exe");
        Assert.Equal(@"C:\PF86\PowerShell\7\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\LAD\Microsoft\WindowsApps\pwsh.exe");
        Assert.Equal(@"C:\LAD\Microsoft\WindowsApps\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\bin\pwsh.exe");
        Assert.Equal(@"C:\bin\pwsh.exe", locator.Resolve(TerminalKind.PowerShell7, string.Empty));

        locator = CreateLocator(environment, _ => false);
        Assert.Null(locator.Resolve(TerminalKind.PowerShell7, string.Empty));
    }

    [Fact]
    public void WindowsTerminal_先查_WindowsApps_再查_PATH()
    {
        var environment = new Dictionary<string, string?>
        {
            ["LOCALAPPDATA"] = @"C:\LAD",
            ["PATH"] = @"C:\bin"
        };

        var locator = CreateLocator(environment, candidate => candidate == @"C:\LAD\Microsoft\WindowsApps\wt.exe");
        Assert.Equal(@"C:\LAD\Microsoft\WindowsApps\wt.exe", locator.Resolve(TerminalKind.WindowsTerminal, string.Empty));

        locator = CreateLocator(environment, candidate => candidate == @"C:\bin\wt.exe");
        Assert.Equal(@"C:\bin\wt.exe", locator.Resolve(TerminalKind.WindowsTerminal, string.Empty));

        locator = CreateLocator(environment, _ => false);
        Assert.Null(locator.Resolve(TerminalKind.WindowsTerminal, string.Empty));
    }

    [Fact]
    public void Custom_文件存在返回路径_不存在返回_null()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BashCommandManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var existing = Path.Combine(directory, "term.exe");
        File.WriteAllText(existing, string.Empty);

        try
        {
            var locator = new ExecutableLocator();

            Assert.Equal(existing, locator.Resolve(TerminalKind.Custom, existing));
            Assert.Null(locator.Resolve(TerminalKind.Custom, Path.Combine(directory, "missing.exe")));
            Assert.Null(locator.Resolve(TerminalKind.Custom, "   "));
            Assert.Null(locator.Resolve(TerminalKind.Custom, string.Empty));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
