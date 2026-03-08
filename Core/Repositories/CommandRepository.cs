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
    Task<IEnumerable<Command>> GetAllAsync();

    // 新增：支持排序的查询
    Task<IEnumerable<Command>> GetByGroupIdWithSortAsync(int groupId, SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> GetAllWithSortAsync(SortOption sortBy, SortDirection direction);
    Task<IEnumerable<Command>> SearchWithSortAsync(string keyword, SortOption sortBy, SortDirection direction);

    // 新增：搜索时分组筛选
    Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction);

    // 新增：移动命令到指定分组
    Task MoveToGroupAsync(int commandId, int targetGroupId);

    // 新增：获取常用命令
    Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10);
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
                FilePath = @FilePath, GroupId = @GroupId, SortOrder = @SortOrder,
                ExecutionCount = @ExecutionCount, LastExecutedAt = @LastExecutedAt
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

    public async Task<IEnumerable<Command>> GetAllAsync()
    {
        var sql = "SELECT * FROM Commands ORDER BY Name";
        return await _db.QueryAsync<Command>(sql);
    }

    public async Task<IEnumerable<Command>> GetByGroupIdWithSortAsync(int groupId, SortOption sortBy, SortDirection direction)
    {
        var orderBy = GetOrderByClause(sortBy, direction);
        var sql = $"SELECT * FROM Commands WHERE GroupId = @GroupId {orderBy}";
        return await _db.QueryAsync<Command>(sql, new { GroupId = groupId });
    }

    public async Task<IEnumerable<Command>> GetAllWithSortAsync(SortOption sortBy, SortDirection direction)
    {
        var orderBy = GetOrderByClause(sortBy, direction);
        var sql = $"SELECT * FROM Commands {orderBy}";
        return await _db.QueryAsync<Command>(sql);
    }

    public async Task<IEnumerable<Command>> SearchWithSortAsync(string keyword, SortOption sortBy, SortDirection direction)
    {
        var orderBy = GetOrderByClause(sortBy, direction);
        var sql = $@"
            SELECT * FROM Commands
            WHERE Name LIKE @Keyword OR Description LIKE @Keyword
            {orderBy}";
        return await _db.QueryAsync<Command>(sql, new { Keyword = $"%{keyword}%" });
    }

    public async Task<IEnumerable<Command>> SearchInGroupAsync(string keyword, int groupId, SortOption sortBy, SortDirection direction)
    {
        var orderBy = GetOrderByClause(sortBy, direction);
        var sql = $@"
            SELECT * FROM Commands
            WHERE (Name LIKE @Keyword OR Description LIKE @Keyword)
            AND GroupId = @GroupId
            {orderBy}";
        return await _db.QueryAsync<Command>(sql, new { Keyword = $"%{keyword}%", GroupId = groupId });
    }

    private string GetOrderByClause(SortOption sortBy, SortDirection direction)
    {
        var column = sortBy switch
        {
            SortOption.Name => "Name",
            SortOption.LastExecutedAt => "LastExecutedAt",
            SortOption.ExecutionCount => "ExecutionCount",
            _ => "Name"
        };
        var dir = direction == SortDirection.Ascending ? "ASC" : "DESC";
        return $"ORDER BY {column} {dir}";
    }

    public async Task MoveToGroupAsync(int commandId, int targetGroupId)
    {
        var sql = "UPDATE Commands SET GroupId = @TargetGroupId WHERE Id = @CommandId";
        await _db.ExecuteAsync(sql, new { CommandId = commandId, TargetGroupId = targetGroupId });
    }

    public async Task<IEnumerable<Command>> GetFrequentlyUsedAsync(int limit = 10)
    {
        // 获取有执行记录的命令，按执行次数和最近执行时间排序
        var sql = @"
            SELECT c.*, g.Name as GroupName
            FROM Commands c
            LEFT JOIN Groups g ON c.GroupId = g.Id
            WHERE c.ExecutionCount > 0 OR c.LastExecutedAt IS NOT NULL
            ORDER BY c.ExecutionCount DESC, c.LastExecutedAt DESC
            LIMIT @Limit";
        return await _db.QueryAsync<Command>(sql, new { Limit = limit });
    }
}
