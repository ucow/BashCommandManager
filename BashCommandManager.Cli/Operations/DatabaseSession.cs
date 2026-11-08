using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Infrastructure;
using Dapper;
using System.Data;
using System.Data.SQLite;

namespace BashCommandManager.Cli.Operations;

/// <summary>CLI 的数据库会话：初始化 schema、记录 created/source/迁移告警，并保持单一连接。</summary>
public sealed class DatabaseSession : IDisposable
{
    private readonly SQLiteConnection _connection;

    public DatabaseSession(string? explicitDatabasePath, string? baseDirectory = null)
        : this(new DatabaseInitializer(explicitDatabasePath, CliPaths.SettingsDirectory, baseDirectory))
    {
    }

    private DatabaseSession(DatabaseInitializer initializer)
    {
        // settings 目录覆盖（BASHCOMMANDMANAGER_SETTINGS_DIR）同样作用于数据库解析（VD-6 / B-8）；
        // baseDirectory 默认 AppContext.BaseDirectory，CLI 在 {app}\cli 时会自动命中 {app}\data\app.db。
        Resolution = initializer.Resolution;
        DatabasePath = initializer.DatabasePath;
        // I-4：迁移失败/目标已存在统一用 MigrationNotice 文案，既进 meta.warning 也写 stderr。
        MigrationWarning = initializer.MigrationWarning is null
            ? null
            : MigrationNotice.Build(initializer.MigrationWarning, initializer.DatabasePath);
        // Source 必须按「实际使用的库」判定：只有真回退旧库才是 Legacy（由 DatabaseInitializer 比较路径得出）。
        Source = initializer.Source;
        Created = !File.Exists(DatabasePath);

        initializer.Initialize();

        ConnectionString = initializer.ConnectionString;
        _connection = new SQLiteConnection(ConnectionString);
        _connection.Open();
        _connection.Execute("PRAGMA busy_timeout = 3000;");
    }

    /// <summary>用外部构造好的 DatabaseInitializer 开会话（测试/特殊宿主注入迁移实现时使用）。</summary>
    public static DatabaseSession Create(DatabaseInitializer initializer) => new(initializer);

    public DatabasePathResolution Resolution { get; }

    public string DatabasePath { get; }

    public string? MigrationWarning { get; }

    public DatabasePathSource Source { get; }

    public bool Created { get; }

    public string ConnectionString { get; }

    public IDbConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
