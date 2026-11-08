using BashCommandManager.Core.Services;
using Xunit;

namespace BashCommandManager.Tests.Core;

public class StartupArgumentParserTests
{
    [Fact]
    public void Parses_command_id()
    {
        Assert.True(StartupArgumentParser.TryGetCommandId(new[] { "--command", "7" }, out var id));
        Assert.Equal(7, id);
    }

    [Fact]
    public void Ignores_case_and_position()
    {
        Assert.True(StartupArgumentParser.TryGetCommandId(new[] { "--other", "--COMMAND", "12" }, out var id));
        Assert.Equal(12, id);
    }

    // 偏差记录（T24）：计划用 [InlineData(new[] { ... })] 传 string[]，C# 特性实参不允许这种
    // 隐式数组创建（CS0182），改用 MemberData 表达同一组用例。
    public static IEnumerable<object[]> InvalidArguments => new[]
    {
        new object[] { Array.Empty<string>() },
        new object[] { new[] { "--command" } },
        new object[] { new[] { "--command", "abc" } },
        new object[] { new[] { "--command", "0" } },
        new object[] { new[] { "--command", "-3" } },
        new object[] { new[] { "--noargs" } }
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Returns_false_for_invalid_input(string[] args)
    {
        Assert.False(StartupArgumentParser.TryGetCommandId(args, out var id));
        Assert.Equal(0, id);
    }
}
