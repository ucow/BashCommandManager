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

    public DatabaseSession(string? explicitDatabasePath)
    {
        // settings 目录覆盖（BASHCOMMANDMANAGER_SETTINGS_DIR）同样作用于数据库解析（VD-6 / B-8）
        var initializer = new DatabaseInitializer(explicitDatabasePath, CliPaths.SettingsDirectory);
        Resolution = initializer.Resolution;
        DatabasePath = initializer.DatabasePath;
        MigrationWarning = initializer.MigrationWarning;
        Source = initializer.MigrationWarning is null
            ? initializer.Source
            : DatabasePathSource.LegacyApplicationDirectory;
        Created = !File.Exists(DatabasePath);

        initializer.Initialize();

        ConnectionString = initializer.ConnectionString;
        _connection = new SQLiteConnection(ConnectionString);
        _connection.Open();
        _connection.Execute("PRAGMA busy_timeout = 3000;");
    }

    public DatabasePathResolution Resolution { get; }

    public string DatabasePath { get; }

    public string? MigrationWarning { get; }

    public DatabasePathSource Source { get; }

    public bool Created { get; }

    public string ConnectionString { get; }

    public IDbConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}
