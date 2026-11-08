using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Infrastructure;
using Dapper;

namespace BashCommandManager.Cli.Tests.Operations;

[Collection(EnvironmentCollection.Name)] // 会改进程级 BASHCOMMANDMANAGER_DB / BASHCOMMANDMANAGER_SETTINGS_DIR，禁止并行
public class DatabaseSessionTests
{
    [Fact]
    public void Creates_missing_database_and_reports_created_true()
    {
        using var temp = new TempDatabase();

        using var session = new DatabaseSession(temp.DatabasePath);

        Assert.True(session.Created);
        Assert.True(File.Exists(temp.DatabasePath));
        Assert.Equal(DatabasePathSource.Argument, session.Source);
        Assert.Null(session.MigrationWarning);
    }

    [Fact]
    public void Existing_database_reports_created_false()
    {
        using var temp = new TempDatabase();
        using (var first = new DatabaseSession(temp.DatabasePath))
        {
            Assert.True(first.Created);
        }

        using var second = new DatabaseSession(temp.DatabasePath);

        Assert.False(second.Created);
    }

    [Fact]
    public void Schema_and_root_group_are_ready()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);

        var rootGroups = session.Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Groups WHERE Id = 1");

        Assert.Equal(1L, rootGroups);
    }

    [Fact]
    public void Busy_timeout_is_configured()
    {
        using var temp = new TempDatabase();
        using var session = new DatabaseSession(temp.DatabasePath);

        var timeout = session.Connection.ExecuteScalar<long>("PRAGMA busy_timeout;");

        Assert.Equal(3000L, timeout);
    }

    [Fact]
    public void Session_uses_resolver_when_no_explicit_path()
    {
        using var temp = new TempDatabase();
        var previous = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, temp.DatabasePath);
        try
        {
            using var session = new DatabaseSession(null);

            Assert.Equal(Path.GetFullPath(temp.DatabasePath), session.DatabasePath);
            Assert.Equal(DatabasePathSource.Environment, session.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, previous);
        }
    }

    [Fact]
    public void Settings_directory_override_isolates_database_when_no_explicit_path()
    {
        using var temp = new TempDatabase();
        var settingsDirectory = temp.CreateDirectory("settings");
        var previousSettings = Environment.GetEnvironmentVariable(CliPaths.SettingsDirectoryVariable);
        var previousDatabase = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(CliPaths.SettingsDirectoryVariable, settingsDirectory);
        Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, null);
        try
        {
            using var session = new DatabaseSession(null);

            // BASHCOMMANDMANAGER_SETTINGS_DIR 同时隔离 settings.json 与数据库（VD-6 / B-8）
            Assert.Equal(Path.Combine(settingsDirectory, "data", "app.db"), session.DatabasePath);
            Assert.True(File.Exists(session.DatabasePath));
            Assert.Equal(DatabasePathSource.LocalAppData, session.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliPaths.SettingsDirectoryVariable, previousSettings);
            Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, previousDatabase);
        }
    }
}
