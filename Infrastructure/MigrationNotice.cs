namespace BashCommandManager.Infrastructure;

/// <summary>
/// 数据库迁移异常的用户提示文案。抽成纯函数，便于单测，也保证 GUI（Growl）与日志用同一段话。
/// </summary>
public static class MigrationNotice
{
    public static string Build(string warning, string databasePath)
        => $"{warning} 当前使用数据库：{databasePath}。" +
           $"如需改用其他数据库，可设置环境变量 {DatabasePathResolver.EnvironmentVariableName}=<数据库路径> " +
           "后重启应用（回滚到旧库或指向保留的副本均可）；修复问题后重启会重新尝试迁移。";
}
