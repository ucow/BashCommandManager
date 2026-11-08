using BashCommandManager.Cli;
using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>处理器单测的 CliServices 构造器：所有端口默认用内存替身。</summary>
internal static class CliServicesFactory
{
    public static CliServices Create(
        DatabaseSession session,
        ICliSettingsStore? settings = null,
        IProcessRunner? runner = null,
        IGuiLauncher? launcher = null,
        IExecutableLocator? locator = null,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        string cliDirectory = @"C:\fake\cli")
        => new(
            new DatabasePathResolution(session.DatabasePath, DatabasePathSource.Argument, null, false),
            session,
            settings ?? new FakeSettingsStore(),
            runner ?? new FakeProcessRunner(),
            launcher ?? new FakeGuiLauncher(),
            locator ?? new FakeExecutableLocator(),
            fileExists ?? File.Exists,
            directoryExists ?? Directory.Exists,
            cliDirectory);
}
