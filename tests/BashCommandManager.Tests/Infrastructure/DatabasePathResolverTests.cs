using BashCommandManager.Infrastructure;
using System.IO;
using Xunit;

namespace BashCommandManager.Tests.Infrastructure;

public class DatabasePathResolverTests
{
    private const string SettingsDir = @"C:\Users\tester\AppData\Local\BashCommandManager";
    private const string LegacyDir = @"C:\Apps\BashCommandManager";

    private static string Canonical => Path.Combine(SettingsDir, "data", "app.db");
    private static string Legacy => Path.Combine(LegacyDir, "data", "app.db");

    [Fact]
    public void Explicit_path_wins_and_is_absolutized()
    {
        var resolution = DatabasePathResolver.Resolve(@"relative\my.db", null, _ => true, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.Argument, resolution.Source);
        Assert.Equal(Path.GetFullPath(@"relative\my.db"), resolution.DatabasePath);
        Assert.False(resolution.RequiresMigration);
        Assert.Null(resolution.LegacyPath);
    }

    [Fact]
    public void Environment_variable_used_when_no_explicit_path()
    {
        var resolution = DatabasePathResolver.Resolve(null, @"D:\portable\data.db", _ => true, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.Environment, resolution.Source);
        Assert.Equal(Path.GetFullPath(@"D:\portable\data.db"), resolution.DatabasePath);
        Assert.False(resolution.RequiresMigration);
    }

    [Fact]
    public void Canonical_existing_database_is_used_without_migration()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, path => path == Canonical, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.LocalAppData, resolution.Source);
        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.Equal(Legacy, resolution.LegacyPath);
        Assert.False(resolution.RequiresMigration);
    }

    [Fact]
    public void Legacy_only_requires_migration_to_canonical()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, path => path == Legacy, SettingsDir, Legacy);

        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.Equal(Legacy, resolution.LegacyPath);
        Assert.True(resolution.RequiresMigration);
    }

    [Fact]
    public void Neither_exists_uses_canonical_without_migration()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, _ => false, SettingsDir, Legacy);

        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.False(resolution.RequiresMigration);
        Assert.Equal(DatabasePathSource.LocalAppData, resolution.Source);
    }

    [Fact]
    public void Blank_environment_value_is_ignored()
    {
        var resolution = DatabasePathResolver.Resolve(null, "   ", path => path == Canonical, SettingsDir, Legacy);

        Assert.Equal(DatabasePathSource.LocalAppData, resolution.Source);
    }

    [Fact]
    public void Canonical_path_matches_decision_document()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BashCommandManager",
            "data",
            "app.db");

        Assert.Equal(expected, DatabasePathResolver.CanonicalDatabasePath);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data", "app.db"), DatabasePathResolver.LegacyDatabasePath);
        Assert.Equal("BASHCOMMANDMANAGER_DB", DatabasePathResolver.EnvironmentVariableName);
    }

    [Fact]
    public void Never_points_to_the_foreign_legacy_file_name()
    {
        var resolution = DatabasePathResolver.Resolve(null, null, _ => false, SettingsDir, Legacy);

        Assert.DoesNotContain("BashCommandManager.db", resolution.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"data\app.db", resolution.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }
}
