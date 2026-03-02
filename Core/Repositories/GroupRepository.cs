using BashCommandManager.Core.Models;
using Dapper;
using System.Data;

namespace BashCommandManager.Core.Repositories;

public interface IGroupRepository
{
    Task<IEnumerable<Group>> GetAllAsync();
    Task<Group?> GetByIdAsync(int id);
    Task<int> CreateAsync(Group group);
    Task UpdateAsync(Group group);
    Task DeleteAsync(int id);
    Task<IEnumerable<Group>> GetChildrenAsync(int? parentId);
}

public class GroupRepository : IGroupRepository
{
    private readonly IDbConnection _db;

    public GroupRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Group>> GetAllAsync()
    {
        var sql = "SELECT * FROM Groups ORDER BY SortOrder, Name";
        return await _db.QueryAsync<Group>(sql);
    }

    public async Task<Group?> GetByIdAsync(int id)
    {
        var sql = "SELECT * FROM Groups WHERE Id = @Id";
        return await _db.QueryFirstOrDefaultAsync<Group>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(Group group)
    {
        var sql = @"
            INSERT INTO Groups (Name, ParentId, SortOrder)
            VALUES (@Name, @ParentId, @SortOrder);
            SELECT last_insert_rowid();";
        return await _db.ExecuteScalarAsync<int>(sql, group);
    }

    public async Task UpdateAsync(Group group)
    {
        var sql = @"
            UPDATE Groups
            SET Name = @Name, ParentId = @ParentId, SortOrder = @SortOrder
            WHERE Id = @Id";
        await _db.ExecuteAsync(sql, group);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = "DELETE FROM Groups WHERE Id = @Id";
        await _db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IEnumerable<Group>> GetChildrenAsync(int? parentId)
    {
        var sql = "SELECT * FROM Groups WHERE ParentId = @ParentId ORDER BY SortOrder";
        return await _db.QueryAsync<Group>(sql, new { ParentId = parentId });
    }
}
