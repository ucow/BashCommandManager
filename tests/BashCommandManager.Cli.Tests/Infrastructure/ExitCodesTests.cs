using BashCommandManager.Cli.Infrastructure;

namespace BashCommandManager.Cli.Tests.Infrastructure;

public class ExitCodesTests
{
    [Theory]
    [InlineData("E_USAGE", 1)]
    [InlineData("E_INVALID_VALUE", 1)]
    [InlineData("E_AMBIGUOUS", 1)]
    [InlineData("E_CONFIRM_REQUIRED", 1)]
    [InlineData("E_ROOT_GROUP", 1)]
    [InlineData("E_CONFIG_KEY", 1)]
    [InlineData("E_FILE_NOT_FOUND", 1)]
    [InlineData("E_NOT_FOUND", 2)]
    [InlineData("E_GUI_NOT_FOUND", 2)]
    [InlineData("E_DB_ERROR", 3)]
    [InlineData("E_DB_BUSY", 3)]
    [InlineData("E_COMMAND_FAILED", 3)]
    [InlineData("E_TIMEOUT", 3)]
    [InlineData("E_ELEVATION_CANCELLED", 3)]
    [InlineData("E_INTERNAL", 3)]
    public void Maps_error_code_to_exit_code(string code, int expected)
    {
        Assert.Equal(expected, CliErrorCodes.ExitCodeFor(code));
    }

    [Fact]
    public void FileNotFound_can_override_exit_code_to_three()
    {
        var error = CliErrors.FileNotFound("脚本文件不存在: D:\\x.bat", exitCode: 3).Error;
        Assert.Equal("E_FILE_NOT_FOUND", error.Code);
        Assert.Equal(3, error.ExitCode);
    }

    [Fact]
    public void CommandFailed_carries_child_exit_code_in_details()
    {
        var error = CliErrors.CommandFailed(7).Error;
        Assert.Equal("E_COMMAND_FAILED", error.Code);
        Assert.Equal(3, error.ExitCode);
        Assert.Equal(7, error.Details!["exitCode"]!.GetValue<int>());
        Assert.Equal("命令退出码 7", error.Message);
    }
}
