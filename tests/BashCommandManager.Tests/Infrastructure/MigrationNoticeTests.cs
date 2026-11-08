using BashCommandManager.Infrastructure;
using Xunit;

namespace BashCommandManager.Tests.Infrastructure;

public class MigrationNoticeTests
{
    [Fact]
    public void Notice_contains_failure_reason_current_database_and_rollback_hint()
    {
        var message = MigrationNotice.Build(
            "数据库迁移失败，已回退使用旧库: 磁盘已满",
            @"C:\Apps\BashCommandManager\data\app.db");

        Assert.Contains("磁盘已满", message);
        Assert.Contains(@"C:\Apps\BashCommandManager\data\app.db", message);
        Assert.Contains(DatabasePathResolver.EnvironmentVariableName, message);
        Assert.Contains("回滚", message);
    }

    [Fact]
    public void Notice_for_existing_target_mentions_pending_recovery()
    {
        var message = MigrationNotice.Build(
            "数据库迁移未完成：目标库已存在（C:\\data\\app.db），旧库副本已保留在 C:\\data\\app.db.pending-abc",
            @"C:\data\app.db");

        Assert.Contains("目标库已存在", message);
        Assert.Contains(@"C:\data\app.db", message);
        Assert.Contains(DatabasePathResolver.EnvironmentVariableName, message);
    }
}
