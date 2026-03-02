using System.Data;
using System.Data.SQLite;

namespace BashCommandManager.Core.Services;

public interface IDatabaseService
{
    IDbConnection CreateConnection();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        return new SQLiteConnection(_connectionString);
    }
}
