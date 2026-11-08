using System.Reflection;

namespace BashCommandManager.Cli.CommandLine;

/// <summary>程序集版本（csproj 的 Version），--version 与帮助头部的唯一来源。</summary>
public static class CliVersion
{
    public static string Value { get; } = Read();

    private static string Read()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
