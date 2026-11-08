using System.Data.SQLite;
using System.IO;

namespace BashCommandManager.Infrastructure;

/// <summary>数据库路径来源。</summary>
public enum DatabasePathSource
{
    Argument,
    Environment,
    LocalAppData,
    LegacyApplicationDirectory
}

/// <summary>一次解析结果：最终路径、来源、旧库路径、是否需要迁移。</summary>
public sealed record DatabasePathResolution(
    string DatabasePath,
    DatabasePathSource Source,
    string? LegacyPath,
    bool RequiresMigration);

/// <summary>迁移结果：最终使用的路径、是否发生迁移、失败告警。</summary>
public sealed record DatabasePreparationResult(string DatabasePath, bool Migrated, string? Warning);

/// <summary>GUI 与 CLI 共用的数据库位置解析（D4.1）。</summary>
public static class DatabasePathResolver
{
    public const string EnvironmentVariableName = "BASHCOMMANDMANAGER_DB";
    public const string DatabaseFileName = "app.db";

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BashCommandManager");

    public static string CanonicalDatabasePath => Path.Combine(SettingsDirectory, "data", DatabaseFileName);

    /// <summary>GUI 安装目录下的旧库（{base}\data\app.db）；仅为兼容旧调用方，实际解析会遍历候选列表。</summary>
    public static string LegacyDatabasePath => Path.Combine(AppContext.BaseDirectory, "data", DatabaseFileName);

    /// <summary>当前进程基目录下的全部旧库候选（按优先级排序）。</summary>
    public static IReadOnlyList<string> LegacyDatabaseCandidates => GetLegacyCandidatePaths(AppContext.BaseDirectory);

    /// <summary>
    /// 旧库候选路径，按顺序取第一个存在的：
    /// 1) {base}\data\app.db（GUI 安装目录）；
    /// 2) {base}\..\data\app.db（CLI 位于 {app}\cli 时的 GUI 安装目录）；
    /// 3) 若 base 的目录名为 cli，显式再声明一次父目录候选（便于阅读与测试）。
    /// </summary>
    public static IReadOnlyList<string> GetLegacyCandidatePaths(string baseDirectory)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);

        var trimmedBase = normalizedBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmedBase.EndsWith(':'))
        {
            // 盘符根（如 C:\）TrimEnd 后变成 "C:"，Path.Combine 会生成盘符相对路径，补回分隔符。
            trimmedBase += Path.DirectorySeparatorChar;
        }

        var candidates = new List<string>(3);
        AddCandidate(candidates, Path.Combine(trimmedBase, "data", DatabaseFileName));

        var parentCandidate = Path.GetFullPath(
            Path.Combine(trimmedBase, "..", "data", DatabaseFileName));
        AddCandidate(candidates, parentCandidate);

        if (string.Equals(Path.GetFileName(trimmedBase), "cli", StringComparison.OrdinalIgnoreCase))
        {
            // CLI 安装在 {app}\cli：父目录即 GUI 安装目录。与上一条同值，去重后不会重复。
            AddCandidate(candidates, parentCandidate);
        }

        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }

    public static DatabasePathResolution Resolve(
        string? explicitPath,
        string? environmentValue,
        Func<string, bool> fileExists,
        string? settingsDirectory = null,
        string? legacyDatabasePath = null,
        string? baseDirectory = null)
    {
        var settingsDir = settingsDirectory ?? SettingsDirectory;
        var canonical = Path.Combine(settingsDir, "data", DatabaseFileName);

        // legacyDatabasePath 是显式单路径入口（测试/特殊部署）；未提供时按基目录展开候选列表。
        var candidates = legacyDatabasePath is not null
            ? new[] { legacyDatabasePath }
            : GetLegacyCandidatePaths(baseDirectory ?? AppContext.BaseDirectory);
        var legacy = candidates.FirstOrDefault(fileExists) ?? candidates[0];

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return new DatabasePathResolution(Path.GetFullPath(explicitPath), DatabasePathSource.Argument, null, false);
        }

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return new DatabasePathResolution(Path.GetFullPath(environmentValue), DatabasePathSource.Environment, null, false);
        }

        if (fileExists(canonical))
        {
            return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, false);
        }

        if (candidates.Any(fileExists))
        {
            return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, true);
        }

        return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, false);
    }

    // settingsDirectory 由 CLI 传 CliPaths.SettingsDirectory；为 null 时回落到 %LOCALAPPDATA%
    // baseDirectory 仅供测试/特殊宿主注入；为 null 时回落到 AppContext.BaseDirectory。
    public static DatabasePathResolution ResolveForCurrentProcess(
        string? explicitPath = null,
        string? settingsDirectory = null,
        string? baseDirectory = null)
        => Resolve(
            explicitPath,
            Environment.GetEnvironmentVariable(EnvironmentVariableName),
            File.Exists,
            settingsDirectory,
            baseDirectory: baseDirectory);
}

/// <summary>把旧库一次性复制到规范位置；失败回退旧库并给告警。</summary>
public static class DatabaseMigrator
{
    public static DatabasePreparationResult Prepare(
        DatabasePathResolution resolution,
        Action<string, string>? copyDatabase = null,
        Action<string>? createDirectory = null)
    {
        if (!resolution.RequiresMigration || string.IsNullOrWhiteSpace(resolution.LegacyPath))
        {
            return new DatabasePreparationResult(resolution.DatabasePath, false, null);
        }

        var legacyPath = resolution.LegacyPath!;
        var targetPath = resolution.DatabasePath;
        var targetDirectory = Path.GetDirectoryName(targetPath);
        var tempPath = targetPath + ".migrating-" + Guid.NewGuid().ToString("N");
        var copy = copyDatabase ?? BackupCopy;
        var ensureDirectory = createDirectory ?? (directory => Directory.CreateDirectory(directory));

        try
        {
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                ensureDirectory(targetDirectory);
            }

            copy(legacyPath, tempPath);

            if (File.Exists(targetPath))
            {
                var preserved = PreserveTempCopy(tempPath, targetPath);
                return new DatabasePreparationResult(
                    targetPath,
                    false,
                    BuildTargetExistsWarning(targetPath, preserved));
            }

            try
            {
                File.Move(tempPath, targetPath);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                var preserved = PreserveTempCopy(tempPath, targetPath);
                return new DatabasePreparationResult(
                    targetPath,
                    false,
                    BuildTargetExistsWarning(targetPath, preserved));
            }

            return new DatabasePreparationResult(targetPath, true, null);
        }
        catch (Exception ex)
        {
            // 清理只做兜底：TryDeleteQuietly 不会再抛，保证一定返回「回退旧库」结果
            TryDeleteQuietly(tempPath);
            return new DatabasePreparationResult(legacyPath, false, $"数据库迁移失败，已回退使用旧库: {ex.Message}");
        }
    }

    /// <summary>
    /// 目标库在复制期间已存在（并发初始化/迁移）：不能把刚复制出来的旧库快照静默丢弃，
    /// 改名为 *.pending-{guid} 保留，返回实际保留路径供告警文案使用。
    /// </summary>
    private static string PreserveTempCopy(string tempPath, string targetPath)
    {
        var pendingPath = targetPath + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            if (File.Exists(tempPath))
            {
                File.Move(tempPath, pendingPath);
                return pendingPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保留改名失败时退回临时副本路径：文件仍在磁盘上，至少告警能指引用户找到它。
            System.Diagnostics.Debug.WriteLine($"[Database] 保留旧库副本失败: {ex.Message}");
        }

        return tempPath;
    }

    private static string BuildTargetExistsWarning(string targetPath, string preservedPath)
        => $"数据库迁移未完成：目标库已存在（{targetPath}），旧库副本已保留在 {preservedPath}。" +
           $"如需使用旧数据，请关闭程序后把该副本复制到 {targetPath}，或设置环境变量 " +
           $"{DatabasePathResolver.EnvironmentVariableName}={preservedPath} 后重启。";

    // 一致性快照复制：不用 File.Copy，避免热库撕裂
    private static void BackupCopy(string sourcePath, string targetPath)
    {
        using var source = new SQLiteConnection($"Data Source={sourcePath};Version=3;Read Only=True;");
        using var target = new SQLiteConnection($"Data Source={targetPath};Version=3;");
        source.Open();
        target.Open();
        source.BackupDatabase(target, "main", "main", -1, null, 0);
    }

    /// <summary>
    /// 尽力删除迁移临时文件，只用于清理，不参与成功/失败判定。
    /// 吞掉：IOException（含 FileNotFound/DirectoryNotFound/PathTooLong 与文件被占用）、
    /// UnauthorizedAccessException（只读/权限不足）。
    /// 上抛：ArgumentException、NotSupportedException 等路径编程错误 —— 这类错误说明调用方拼错了路径，应立即暴露。
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Prepare 的 catch 分支专用：清理异常一律吞掉，绝不覆盖原始迁移错误（保证能返回回退结果）。</summary>
    private static void TryDeleteQuietly(string path)
    {
        try
        {
            TryDelete(path);
        }
        catch (Exception)
        {
        }
    }
}
