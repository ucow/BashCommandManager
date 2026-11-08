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
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CanonicalPath)!, "*.pending-*"));
    }

    [Fact]
    public void Existing_target_keeps_legacy_copy_as_pending_and_warns()
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

        // 目标已存在时不能声称迁移成功：旧库数据并未被采用。
        Assert.False(result.Migrated);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        Assert.NotNull(result.Warning);
        Assert.Contains("目标库已存在", result.Warning!);
        Assert.Contains(DatabasePathResolver.EnvironmentVariableName, result.Warning!);

        var pendingFiles = Directory.GetFiles(
            Path.GetDirectoryName(CanonicalPath)!,
            "app.db.pending-*");
        var pendingPath = Assert.Single(pendingFiles);
        Assert.Contains(pendingPath, result.Warning!);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CanonicalPath)!, "*.migrating-*"));

        // 目标库保持原样，旧库快照完整保留在 pending 副本里。
        using (var connection = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;"))
        {
            connection.Open();
            using var marker = new SQLiteCommand("SELECT COUNT(*) FROM Marker", connection);
            Assert.Equal(1L, Convert.ToInt64(marker.ExecuteScalar()));
        }

        using (var pending = new SQLiteConnection($"Data Source={pendingPath};Version=3;"))
        {
            pending.Open();
            using var commands = new SQLiteCommand("SELECT COUNT(*) FROM Commands", pending);
            Assert.Equal(1L, Convert.ToInt64(commands.ExecuteScalar()));
        }
    }

    [Fact]
    public void Target_created_during_copy_also_keeps_pending_copy()
    {
        CreateLegacyDatabase("Commands");

        var result = DatabaseMigrator.Prepare(
            Resolution(),
            copyDatabase: (source, target) =>
            {
                File.Copy(source, target);
                Directory.CreateDirectory(Path.GetDirectoryName(CanonicalPath)!);
                using var seed = new SQLiteConnection($"Data Source={CanonicalPath};Version=3;");
                seed.Open();
                using var seedCommand = new SQLiteCommand("CREATE TABLE Marker (Id INTEGER); INSERT INTO Marker (Id) VALUES (7);", seed);
                seedCommand.ExecuteNonQuery();
            });

        Assert.False(result.Migrated);
        Assert.Equal(CanonicalPath, result.DatabasePath);
        Assert.NotNull(result.Warning);
        var pendingPath = Assert.Single(
            Directory.GetFiles(Path.GetDirectoryName(CanonicalPath)!, "app.db.pending-*"));
        Assert.Contains(pendingPath, result.Warning!);
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
