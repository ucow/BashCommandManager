using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Reflection;

namespace BashCommandManager.Infrastructure;

public class DatabaseInitializer
{
    private readonly string _dbPath;

    public DatabaseInitializer()
    {
        // 使用 AppContext.BaseDirectory 替代 Assembly.Location，支持单文件发布
        var appDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "app.db");
    }

    public string ConnectionString => $"Data Source={_dbPath};Version=3;";

    public void Initialize()
    {
        using var connection = new SQLiteConnection(ConnectionString);
        connection.Open();

        var sql = @"
            CREATE TABLE IF NOT EXISTS Groups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ParentId INTEGER NULL,
                SortOrder INTEGER DEFAULT 0,
                FOREIGN KEY (ParentId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Commands (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                FilePath TEXT NOT NULL,
                GroupId INTEGER NOT NULL,
                SortOrder INTEGER DEFAULT 0,
                ExecutionCount INTEGER DEFAULT 0,
                LastExecutedAt DATETIME,
                FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_groups_parent ON Groups(ParentId);
            CREATE INDEX IF NOT EXISTS idx_commands_group ON Commands(GroupId);

            -- 插入根分组
            INSERT OR IGNORE INTO Groups (Id, Name, ParentId, SortOrder)
            VALUES (1, '根分组', NULL, 0);
        ";

        using var command = new SQLiteCommand(sql, connection);
        command.ExecuteNonQuery();

        // 迁移：添加新列（如果表已存在但缺少新列）
        AddColumnIfNotExists(connection, "Commands", "ExecutionCount", "INTEGER DEFAULT 0");
        AddColumnIfNotExists(connection, "Commands", "LastExecutedAt", "DATETIME");
    }

    private void AddColumnIfNotExists(SQLiteConnection connection, string table, string column, string type)
    {
        var checkSql = $@"
            SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        using var checkCommand = new SQLiteCommand(checkSql, connection);
        var exists = Convert.ToInt32(checkCommand.ExecuteScalar());
        if (exists == 0)
        {
            var alterSql = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            using var alterCommand = new SQLiteCommand(alterSql, connection);
            alterCommand.ExecuteNonQuery();
        }
    }
}
