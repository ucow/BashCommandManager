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

    public static string LegacyDatabasePath => Path.Combine(AppContext.BaseDirectory, "data", DatabaseFileName);

    public static DatabasePathResolution Resolve(
        string? explicitPath,
        string? environmentValue,
        Func<string, bool> fileExists,
        string? settingsDirectory = null,
        string? legacyDatabasePath = null)
    {
        var settingsDir = settingsDirectory ?? SettingsDirectory;
        var canonical = Path.Combine(settingsDir, "data", DatabaseFileName);
        var legacy = legacyDatabasePath ?? LegacyDatabasePath;

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

        if (fileExists(legacy))
        {
            return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, true);
        }

        return new DatabasePathResolution(canonical, DatabasePathSource.LocalAppData, legacy, false);
    }

    // settingsDirectory 由 CLI 传 CliPaths.SettingsDirectory；为 null 时回落到 %LOCALAPPDATA%
    public static DatabasePathResolution ResolveForCurrentProcess(string? explicitPath = null, string? settingsDirectory = null)
        => Resolve(explicitPath, Environment.GetEnvironmentVariable(EnvironmentVariableName), File.Exists, settingsDirectory);
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
                TryDelete(tempPath);
                return new DatabasePreparationResult(targetPath, true, null);
            }

            try
            {
                File.Move(tempPath, targetPath);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                TryDelete(tempPath);
                return new DatabasePreparationResult(targetPath, true, null);
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
