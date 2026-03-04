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
    }
}
