using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;
using System.Data;
using System.Data.SQLite;

namespace BashCommandManager.Cli.Application;

/// <summary>CLI 的数据访问层：复用仓库，补齐 CLI 需要的查询、子树统计与 Busy 重试。</summary>
public sealed class CliCommandStore
{
    private static readonly int[] RetryDelays = { 200, 500, 1000 };

    private readonly IGroupRepository _groups;
    private readonly ICommandRepository _commands;

    public CliCommandStore(IDbConnection db)
    {
        _groups = new GroupRepository(db);
        _commands = new CommandRepository(db);
    }

    public Task<IReadOnlyList<Group>> GetGroupsAsync()
        => Retry(async () => (IReadOnlyList<Group>)(await _groups.GetAllAsync()).ToList());

    public Task<Group?> GetGroupAsync(int id)
        => Retry(() => _groups.GetByIdAsync(id));

    public async Task<Group?> FindChildGroupByNameAsync(int? parentId, string name)
    {
        var all = await GetGroupsAsync();
        return all.FirstOrDefault(group =>
            group.ParentId == parentId
            && string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> CreateGroupAsync(string name, int? parentId)
    {
        var all = await GetGroupsAsync();
        var sortOrder = all.Count(group => group.ParentId == parentId);
        return await Retry(() => _groups.CreateAsync(new Group
        {
            Name = name,
            ParentId = parentId,
            SortOrder = sortOrder
        }));
    }

    public async Task RenameGroupAsync(int id, string name)
    {
        var group = await Retry(() => _groups.GetByIdAsync(id))
                    ?? throw CliErrors.NotFound($"未找到分组: {id}");
        group.Name = name;
        await Retry(() => _groups.UpdateAsync(group));
    }

    public Task DeleteGroupAsync(int id) => Retry(() => _groups.DeleteAsync(id));

    public async Task<IReadOnlyList<int>> GetGroupSubtreeIdsAsync(int rootGroupId)
    {
        var all = await GetGroupsAsync();
        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootGroupId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            foreach (var child in all.Where(group => group.ParentId == current))
            {
                queue.Enqueue(child.Id);
            }
        }
        return result.OrderBy(id => id).ToList();
    }

    public async Task<IReadOnlyList<Command>> GetCommandsAsync(int? groupId, SortOption sort, SortDirection direction, int limit)
    {
        var items = groupId is int scope
            ? await Retry(async () => (IReadOnlyList<Command>)(await _commands.GetByGroupIdWithSortAsync(scope, sort, direction)).ToList())
            : await Retry(async () => (IReadOnlyList<Command>)(await _commands.GetAllWithSortAsync(sort, direction)).ToList());
        return limit > 0 ? items.Take(limit).ToList() : items;
    }

    public async Task<IReadOnlyList<Command>> SearchCommandsAsync(string keyword, int? groupId, SortOption sort, SortDirection direction, int limit)
    {
        var items = groupId is int scope
            ? await Retry(async () => (IReadOnlyList<Command>)(await _commands.SearchInGroupAsync(keyword, scope, sort, direction)).ToList())
            : await Retry(async () => (IReadOnlyList<Command>)(await _commands.SearchWithSortAsync(keyword, sort, direction)).ToList());
        return limit > 0 ? items.Take(limit).ToList() : items;
    }

    public Task<Command?> GetCommandAsync(int id) => Retry(() => _commands.GetByIdAsync(id));

    public Task<IReadOnlyList<Command>> GetAllCommandsAsync()
        => Retry(async () => (IReadOnlyList<Command>)(await _commands.GetAllAsync()).ToList());

    public async Task<IReadOnlyList<Command>> FindCommandsByNameAsync(string name, int? groupId)
    {
        var all = await GetAllCommandsAsync();
        return all
            .Where(command => string.Equals(command.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(command => groupId is null || command.GroupId == groupId)
            .ToList();
    }

    public Task<int> CreateCommandAsync(Command command) => Retry(() => _commands.CreateAsync(command));

    public Task UpdateCommandAsync(Command command) => Retry(() => _commands.UpdateAsync(command));

    public async Task DeleteCommandsAsync(IReadOnlyList<int> ids)
    {
        foreach (var id in ids)
        {
            await Retry(() => _commands.DeleteAsync(id));
        }
    }

    public async Task MoveCommandsAsync(IReadOnlyList<int> ids, int targetGroupId)
    {
        foreach (var id in ids)
        {
            await Retry(() => _commands.MoveToGroupAsync(id, targetGroupId));
        }
    }

    public Task<IReadOnlyList<Command>> GetFrequentlyUsedAsync(int limit)
        => Retry(async () => (IReadOnlyList<Command>)(await _commands.GetFrequentlyUsedAsync(limit)).ToList());

    public async Task RecordExecutionAsync(int id)
    {
        var command = await Retry(() => _commands.GetByIdAsync(id));
        if (command is null)
        {
            return;
        }

        command.ExecutionCount += 1;
        command.LastExecutedAt = DateTime.Now;
        await Retry(() => _commands.UpdateAsync(command));
    }

    public async Task<int> CountCommandsInGroupAsync(int groupId)
        => (await Retry(async () => (await _commands.GetByGroupIdAsync(groupId)).ToList())).Count;

    public async Task<IReadOnlyDictionary<int, int>> GetCommandCountsAsync()
    {
        var all = await GetAllCommandsAsync();
        return all.GroupBy(command => command.GroupId).ToDictionary(group => group.Key, group => group.Count());
    }

    public async Task<IReadOnlyList<int>> GetCommandIdsInGroupsAsync(IReadOnlyList<int> groupIds)
    {
        var scope = groupIds.ToHashSet();
        var all = await GetAllCommandsAsync();
        return all.Where(command => scope.Contains(command.GroupId)).Select(command => command.Id).OrderBy(id => id).ToList();
    }

    public async Task<IReadOnlyList<Command>> FillGroupNamesAsync(IReadOnlyList<Command> commands)
    {
        var names = (await GetGroupsAsync()).ToDictionary(group => group.Id, group => group.Name);
        foreach (var command in commands)
        {
            command.GroupName = names.TryGetValue(command.GroupId, out var name) ? name : string.Empty;
        }
        return commands;
    }

    private static async Task<T> Retry<T>(Func<Task<T>> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SQLiteException ex) when (IsBusy(ex) && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt]);
            }
            catch (SQLiteException ex) when (IsBusy(ex))
            {
                throw CliErrors.DbBusy(ex.Message);
            }
            catch (SQLiteException ex)
            {
                throw CliErrors.DbError(ex.Message);
            }
        }
    }

    private static async Task Retry(Func<Task> operation)
    {
        await Retry(async () =>
        {
            await operation();
            return true;
        });
    }

    private static bool IsBusy(SQLiteException ex)
        => ex.ResultCode is SQLiteErrorCode.Busy or SQLiteErrorCode.Locked;
}
