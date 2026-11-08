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

    [Fact]
    public void Legacy_candidates_put_base_data_directory_before_parent()
    {
        var candidates = DatabasePathResolver.GetLegacyCandidatePaths(LegacyDir);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(Path.Combine(LegacyDir, "data", "app.db"), candidates[0]);
        Assert.Equal(Path.GetFullPath(Path.Combine(LegacyDir, "..", "data", "app.db")), candidates[1]);
    }

    [Fact]
    public void Cli_base_resolves_gui_legacy_database_in_parent()
    {
        var cliBase = Path.Combine(LegacyDir, "cli");
        var guiLegacy = Path.Combine(LegacyDir, "data", "app.db");

        var resolution = DatabasePathResolver.Resolve(
            null,
            null,
            path => path == guiLegacy,
            SettingsDir,
            baseDirectory: cliBase);

        Assert.Equal(Canonical, resolution.DatabasePath);
        Assert.Equal(guiLegacy, resolution.LegacyPath);
        Assert.True(resolution.RequiresMigration);
    }

    [Fact]
    public void Gui_base_resolves_its_own_legacy_database()
    {
        var guiLegacy = Path.Combine(LegacyDir, "data", "app.db");

        var resolution = DatabasePathResolver.Resolve(
            null,
            null,
            path => path == guiLegacy,
            SettingsDir,
            baseDirectory: LegacyDir);

        Assert.Equal(guiLegacy, resolution.LegacyPath);
        Assert.True(resolution.RequiresMigration);
    }

    [Fact]
    public void Base_candidate_wins_when_both_base_and_parent_legacy_exist()
    {
        var guiLegacy = Path.Combine(LegacyDir, "data", "app.db");
        var parentLegacy = Path.GetFullPath(Path.Combine(LegacyDir, "..", "data", "app.db"));

        var resolution = DatabasePathResolver.Resolve(
            null,
            null,
            path => path == guiLegacy || path == parentLegacy,
            SettingsDir,
            baseDirectory: LegacyDir);

        Assert.Equal(guiLegacy, resolution.LegacyPath);
    }

    [Fact]
    public void Cli_in_subdirectory_migrates_gui_legacy_database_to_canonical()
    {
        var root = Path.Combine(Path.GetTempPath(), "bcm-resolver-" + Guid.NewGuid().ToString("N"));
        var cliBase = Path.Combine(root, "cli");
        var legacyPath = Path.Combine(root, "data", "app.db");
        var settingsDir = Path.Combine(root, "settings");
        var canonicalPath = Path.Combine(settingsDir, "data", "app.db");

        Directory.CreateDirectory(cliBase);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "legacy-data");

        try
        {
            var resolution = DatabasePathResolver.Resolve(
                null,
                null,
                File.Exists,
                settingsDir,
                baseDirectory: cliBase);

            Assert.Equal(canonicalPath, resolution.DatabasePath);
            Assert.Equal(Path.GetFullPath(legacyPath), resolution.LegacyPath);
            Assert.True(resolution.RequiresMigration);

            var prepared = DatabaseMigrator.Prepare(resolution, (source, target) => File.Copy(source, target));

            Assert.True(prepared.Migrated);
            Assert.Null(prepared.Warning);
            Assert.Equal(canonicalPath, prepared.DatabasePath);
            Assert.Equal("legacy-data", File.ReadAllText(canonicalPath));
            Assert.True(File.Exists(legacyPath));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
