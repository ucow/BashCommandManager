using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli;

/// <summary>处理器依赖容器：一次命令处理所需的全部端口。</summary>
public sealed class CliServices : IDisposable
{
    public CliServices(
        DatabasePathResolution databaseResolution,
        DatabaseSession? session,
        ICliSettingsStore settings,
        IProcessRunner runner,
        IGuiLauncher launcher,
        IExecutableLocator locator,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        string cliDirectory)
    {
        DatabaseResolution = databaseResolution;
        Session = session;
        Settings = settings;
        Runner = runner;
        Launcher = launcher;
        Locator = locator;
        FileExists = fileExists;
        DirectoryExists = directoryExists;
        CliDirectory = cliDirectory;
    }

    public DatabasePathResolution DatabaseResolution { get; }

    public DatabaseSession? Session { get; }

    public ICliSettingsStore Settings { get; }

    public IProcessRunner Runner { get; }

    public IGuiLauncher Launcher { get; }

    public IExecutableLocator Locator { get; }

    public Func<string, bool> FileExists { get; }

    public Func<string, bool> DirectoryExists { get; }

    public string CliDirectory { get; }

    public string MetaDatabasePath => Session?.DatabasePath ?? DatabaseResolution.DatabasePath;

    public bool MetaCreated => Session?.Created ?? false;

    public DatabasePathSource MetaSource => Session?.Source ?? DatabaseResolution.Source;

    public string? MetaWarning => Session?.MigrationWarning;

    public void Dispose() => Session?.Dispose();
}
