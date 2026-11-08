using BashCommandManager.Infrastructure;
using System.Data.SQLite;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests.Infrastructure;

public class DatabaseMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bcm-migrator-" + Guid.NewGuid().ToString("N"));

    public DatabaseMigratorTests() => Directory.CreateDirectory(_root);

    private string LegacyPath => Path.Combine(_root, "legacy", "data", "app.db");
    private string CanonicalPath => Path.Combine(_root, "canonical", "data", "app.db");

    private DatabasePathResolution Resolution(bool requiresMigration = true)
        => new(CanonicalPath, DatabasePathSource.LocalAppData, LegacyPath, requiresMigration);

    private void CreateLegacyDatabase(string table)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        using var connection = new SQLiteConnection($"Data Source={LegacyPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand($"CREATE TABLE {table} (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO {table} (Id, Name) VALUES (1, '备份');", connection);
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Migrates_legacy_database_and_keeps_it()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(Resolution());

        Assert.True(result.Migrated);
        Assert.Null(result.Warning);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        Assert.True(File.Exists(CanonicalPath));
        Assert.True(File.Exists(LegacyPath));
        using var connection = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand("SELECT COUNT(*) FROM Commands", connection);
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Leaves_no_migration_temp_files()
    {
        CreateLegacyDatabase("Commands");

        DatabaseMigrator.Prepare(Resolution());

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CanonicalPath)!, "*.migrating-*"));
    }

    [Fact]
    public void Existing_target_is_treated_as_concurrent_success()
    {
        CreateLegacyDatabase("Commands");
        Directory.CreateDirectory(Path.GetDirectoryName(CanonicalPath)!);
        using (var seed = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;"))
        {
            seed.Open();
            using var seedCommand = new SQLiteCommand("CREATE TABLE Marker (Id INTEGER); INSERT INTO Marker (Id) VALUES (42);", seed);
            seedCommand.ExecuteNonQuery();
        }

        var result = DatabaseMigrator.Prepare(Resolution());

        Assert.True(result.Migrated);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        using var connection = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand("SELECT COUNT(*) FROM Marker", connection);
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Copy_failure_falls_back_to_legacy_with_warning()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(
            Resolution(),
            copyDatabase: (_, _) => throw new IOException("磁盘已满"),
            createDirectory: _ => { });

        Assert.False(result.Migrated);
        Assert.Equal(LegacyPath, result.DatabasePath);
        Assert.NotNull(result.Warning);
        Assert.Contains("磁盘已满", result.Warning!);
        Assert.False(File.Exists(CanonicalPath));
    }

    [Fact]
    public void Directory_creation_failure_falls_back_to_legacy()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(
            Resolution(),
            copyDatabase: (_, _) => { },
            createDirectory: _ => throw new UnauthorizedAccessException("拒绝访问"));

        Assert.Equal(LegacyPath, result.DatabasePath);
        Assert.Contains("拒绝访问", result.Warning!);
    }

    [Fact]
    public void No_migration_when_not_required()
    {
        var copied = false;

        var result = DatabaseMigrator.Prepare(Resolution(requiresMigration: false), (_, _) => copied = true);

        Assert.False(copied);
        Assert.False(result.Migrated);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        Assert.Null(result.Warning);
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
