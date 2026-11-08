using BashCommandManager.Infrastructure;
using BashCommandManager.Tests.TestSupport;
using System.Data.SQLite;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests.Infrastructure;

[Collection(EnvironmentCollection.Name)] // 会改进程级 BASHCOMMANDMANAGER_DB，禁止并行
public class DatabaseInitializerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bcm-initializer-" + Guid.NewGuid().ToString("N"));

    public DatabaseInitializerTests() => Directory.CreateDirectory(_root);

    private string DatabasePath => Path.Combine(_root, "nested", "data", "app.db");

    [Fact]
    public void Uses_explicit_path_and_creates_directories()
    {
        var initializer = new DatabaseInitializer(DatabasePath);

        Assert.Equal(DatabasePath, initializer.DatabasePath);
        Assert.Null(initializer.MigrationWarning);
        Assert.Equal(DatabasePathSource.Argument, initializer.Source);

        initializer.Initialize();

        Assert.True(File.Exists(DatabasePath));
        Assert.True(Directory.Exists(Path.GetDirectoryName(DatabasePath)));
    }

    [Fact]
    public void Connection_string_enables_foreign_keys()
    {
        var initializer = new DatabaseInitializer(DatabasePath);

        Assert.Contains("Foreign Keys=True", initializer.ConnectionString);

        initializer.Initialize();
        using var connection = new SQLiteConnection(initializer.ConnectionString);
        connection.Open();
        using var command = new SQLiteCommand("PRAGMA foreign_keys;", connection);
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Initialize_creates_schema_and_root_group_idempotently()
    {
        var initializer = new DatabaseInitializer(DatabasePath);

        initializer.Initialize();
        initializer.Initialize();

        using var connection = new SQLiteConnection(initializer.ConnectionString);
        connection.Open();
        using var groups = new SQLiteCommand("SELECT COUNT(*) FROM Groups WHERE Id = 1 AND Name = '根分组'", connection);
        Assert.Equal(1L, Convert.ToInt64(groups.ExecuteScalar()));
        using var tables = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Groups','Commands')", connection);
        Assert.Equal(2L, Convert.ToInt64(tables.ExecuteScalar()));
    }

    [Fact]
    public void Deleting_group_cascades_to_commands()
    {
        var initializer = new DatabaseInitializer(DatabasePath);
        initializer.Initialize();

        using var connection = new SQLiteConnection(initializer.ConnectionString);
        connection.Open();
        using (var seed = new SQLiteCommand(@"
            INSERT INTO Groups (Id, Name, ParentId, SortOrder) VALUES (5, '运维', 1, 0);
            INSERT INTO Commands (Name, FilePath, GroupId, SortOrder) VALUES ('备份', 'D:\bat\a.bat', 5, 0);", connection))
        {
            seed.ExecuteNonQuery();
        }

        using (var delete = new SQLiteCommand("DELETE FROM Groups WHERE Id = 5", connection))
        {
            delete.ExecuteNonQuery();
        }

        using var count = new SQLiteCommand("SELECT COUNT(*) FROM Commands WHERE GroupId = 5", connection);
        Assert.Equal(0L, Convert.ToInt64(count.ExecuteScalar()));
    }

    [Fact]
    public void Settings_directory_redirects_database_when_no_explicit_path()
    {
        var settingsDirectory = Path.Combine(_root, "settings");
        var previous = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, null);
        try
        {
            var initializer = new DatabaseInitializer(settingsDirectory: settingsDirectory);

            Assert.Equal(Path.Combine(settingsDirectory, "data", "app.db"), initializer.DatabasePath);
            Assert.Equal(DatabasePathSource.LocalAppData, initializer.Source);

            initializer.Initialize();

            Assert.True(File.Exists(Path.Combine(settingsDirectory, "data", "app.db")));
            Assert.Null(initializer.MigrationWarning);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, previous);
        }
    }

    [Fact]
    public void Cli_base_directory_finds_and_migrates_gui_legacy_database()
    {
        var root = Path.Combine(_root, "install");
        var cliBase = Path.Combine(root, "cli");
        var settingsDirectory = Path.Combine(_root, "settings");
        var legacyPath = Path.Combine(root, "data", "app.db");
        var canonicalPath = Path.Combine(settingsDirectory, "data", "app.db");

        Directory.CreateDirectory(cliBase);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        using (var legacy = new SQLiteConnection($"Data Source={legacyPath};Version=3;"))
        {
            legacy.Open();
            using var seed = new SQLiteCommand(
                "CREATE TABLE LegacyProbe (Id INTEGER PRIMARY KEY, Name TEXT);" +
                "INSERT INTO LegacyProbe (Id, Name) VALUES (1, '旧数据');",
                legacy);
            seed.ExecuteNonQuery();
        }

        var previous = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, null);
        try
        {
            var initializer = new DatabaseInitializer(
                settingsDirectory: settingsDirectory,
                baseDirectory: cliBase);

            Assert.Equal(canonicalPath, initializer.DatabasePath);
            Assert.Null(initializer.MigrationWarning);
            Assert.Equal(DatabasePathSource.LocalAppData, initializer.Source);
            Assert.True(initializer.Resolution.RequiresMigration);
            Assert.Equal(Path.GetFullPath(legacyPath), initializer.Resolution.LegacyPath);

            initializer.Initialize();

            using var connection = new SQLiteConnection(initializer.ConnectionString);
            connection.Open();
            using var probe = new SQLiteCommand("SELECT COUNT(*) FROM LegacyProbe", connection);
            Assert.Equal(1L, Convert.ToInt64(probe.ExecuteScalar()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, previous);
        }
    }

    public void Dispose()
    {
        SQLiteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
