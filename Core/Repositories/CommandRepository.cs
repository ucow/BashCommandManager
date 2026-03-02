using BashCommandManager.Core.Models;
using Dapper;
using System.Data;

namespace BashCommandManager.Core.Repositories;

public interface ICommandRepository
{
    Task<IEnumerable<Command>> GetByGroupIdAsync(int groupId);
    Task<Command?> GetByIdAsync(int id);
    Task<int> CreateAsync(Command command);
    Task UpdateAsync(Command command);
    Task DeleteAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
}

public class CommandRepository : ICommandRepository
{
    private readonly IDbConnection _db;

    public CommandRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Command>> GetByGroupIdAsync(int groupId)
    {
        var sql = "SELECT * FROM Commands WHERE GroupId = @GroupId ORDER BY SortOrder";
        return await _db.QueryAsync<Command>(sql, new { GroupId = groupId });
    }

    public async Task<Command?> GetByIdAsync(int id)
    {
        var sql = "SELECT * FROM Commands WHERE Id = @Id";
        return await _db.QueryFirstOrDefaultAsync<Command>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(Command command)
    {
        var sql = @"
            INSERT INTO Commands (Name, Description, FilePath, GroupId, SortOrder)
            VALUES (@Name, @Description, @FilePath, @GroupId, @SortOrder);
            SELECT last_insert_rowid();";
        return await _db.ExecuteScalarAsync<int>(sql, command);
    }

    public async Task UpdateAsync(Command command)
    {
        var sql = @"
            UPDATE Commands
            SET Name = @Name, Description = @Description,
                FilePath = @FilePath, GroupId = @GroupId, SortOrder = @SortOrder
            WHERE Id = @Id";
        await _db.ExecuteAsync(sql, command);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = "DELETE FROM Commands WHERE Id = @Id";
        await _db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IEnumerable<Command>> SearchAsync(string keyword)
    {
        var sql = @"
            SELECT * FROM Commands
            WHERE Name LIKE @Keyword OR Description LIKE @Keyword
            ORDER BY Name";
        return await _db.QueryAsync<Command>(sql, new { Keyword = $"%{keyword}%" });
    }
}
