using BashCommandManager.Infrastructure;

namespace BashCommandManager.Cli.Infrastructure;

/// <summary>CLI 使用的目录：默认与 GUI 相同的 settings 目录，可用环境变量覆盖以便测试/便携。</summary>
public static class CliPaths
{
    public const string SettingsDirectoryVariable = "BASHCOMMANDMANAGER_SETTINGS_DIR";

    public static string SettingsDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(SettingsDirectoryVariable);
            return string.IsNullOrWhiteSpace(overridden)
                ? DatabasePathResolver.SettingsDirectory
                : Path.GetFullPath(overridden);
        }
    }
}
