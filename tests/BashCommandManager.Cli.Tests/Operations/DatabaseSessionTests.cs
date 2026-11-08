using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Tests.TestSupport;
using BashCommandManager.Infrastructure;
using Dapper;
using System.Data.SQLite;

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

    // B-1：CLI 位于 {app}\cli 时，自动命中并迁移 GUI 的 {app}\data\app.db，Source 仍是 canonical。
    [Fact]
    public void Cli_base_directory_migrates_gui_legacy_database()
    {
        using var temp = new TempDatabase();
        var cliBase = temp.CreateDirectory("cli");
        var settingsDirectory = temp.CreateDirectory("settings");
        var legacyPath = CreateLegacyDatabase(temp.Root);

        WithIsolatedEnvironment(settingsDirectory, () =>
        {
            using var session = new DatabaseSession(null, baseDirectory: cliBase);

            Assert.Equal(Path.Combine(settingsDirectory, "data", "app.db"), session.DatabasePath);
            Assert.True(File.Exists(session.DatabasePath));
            Assert.False(session.Created);
            Assert.Equal(DatabasePathSource.LocalAppData, session.Source);
            Assert.Null(session.MigrationWarning);
            Assert.Equal(1L, session.Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM LegacyProbe"));
            Assert.True(File.Exists(legacyPath));
        });
    }

    // I-4：迁移失败必须是 MigrationNotice 的统一文案（含当前数据库与回滚提示），Source=Legacy。
    [Fact]
    public void Migration_failure_warning_is_formatted_and_source_is_legacy()
    {
        using var temp = new TempDatabase();
        var cliBase = temp.CreateDirectory("cli");
        var settingsDirectory = temp.CreateDirectory("settings");
        var legacyPath = CreateLegacyDatabase(temp.Root);
        // 让 canonical 路径被目录占用：File.Move 失败会回退旧库。
        Directory.CreateDirectory(Path.Combine(settingsDirectory, "data", "app.db"));

        WithIsolatedEnvironment(settingsDirectory, () =>
        {
            using var session = new DatabaseSession(null, baseDirectory: cliBase);

            Assert.Equal(Path.GetFullPath(legacyPath), session.DatabasePath);
            Assert.Equal(DatabasePathSource.LegacyApplicationDirectory, session.Source);
            Assert.NotNull(session.MigrationWarning);
            Assert.Contains("数据库迁移失败", session.MigrationWarning!);
            Assert.Contains("当前使用数据库", session.MigrationWarning!);
            Assert.Contains(DatabasePathResolver.EnvironmentVariableName, session.MigrationWarning!);
        });
    }

    // I-4：目标库已存在（并发/另一实例先建库）时 warning 非空，但实际使用的是 canonical，Source 不能是 Legacy。
    [Fact]
    public void Target_exists_warning_is_exposed_and_source_is_not_legacy()
    {
        using var temp = new TempDatabase();
        var cliBase = temp.CreateDirectory("cli");
        var settingsDirectory = temp.CreateDirectory("settings");
        CreateLegacyDatabase(temp.Root);
        var canonicalPath = Path.Combine(settingsDirectory, "data", "app.db");

        WithIsolatedEnvironment(settingsDirectory, () =>
        {
            var initializer = new DatabaseInitializer(
                settingsDirectory: settingsDirectory,
                baseDirectory: cliBase,
                prepareDatabase: resolution =>
                {
                    // 模拟迁移期间目标库被另一个实例创建。
                    Directory.CreateDirectory(Path.GetDirectoryName(resolution.DatabasePath)!);
                    using (var target = new SQLiteConnection($"Data Source={resolution.DatabasePath};Version=3;"))
                    {
                        target.Open();
                    }
                    return DatabaseMigrator.Prepare(resolution);
                });

            using var session = DatabaseSession.Create(initializer);

            Assert.Equal(canonicalPath, session.DatabasePath);
            Assert.NotNull(session.MigrationWarning);
            Assert.Contains("目标库已存在", session.MigrationWarning!);
            Assert.Contains("当前使用数据库", session.MigrationWarning!);
            Assert.NotEqual(DatabasePathSource.LegacyApplicationDirectory, session.Source);
            Assert.Equal(DatabasePathSource.LocalAppData, session.Source);
        });
    }

    private static string CreateLegacyDatabase(string root)
    {
        var legacyPath = Path.Combine(root, "data", "app.db");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        using var connection = new SQLiteConnection($"Data Source={legacyPath};Version=3;");
        connection.Open();
        using var command = new SQLiteCommand(
            "CREATE TABLE LegacyProbe (Id INTEGER PRIMARY KEY, Name TEXT);" +
            "INSERT INTO LegacyProbe (Id, Name) VALUES (1, '旧数据');", connection);
        command.ExecuteNonQuery();
        return legacyPath;
    }

    private static void WithIsolatedEnvironment(string settingsDirectory, Action action)
    {
        var previousSettings = Environment.GetEnvironmentVariable(CliPaths.SettingsDirectoryVariable);
        var previousDatabase = Environment.GetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName);
        Environment.SetEnvironmentVariable(CliPaths.SettingsDirectoryVariable, settingsDirectory);
        Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, null);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliPaths.SettingsDirectoryVariable, previousSettings);
            Environment.SetEnvironmentVariable(DatabasePathResolver.EnvironmentVariableName, previousDatabase);
        }
    }
}
