using System.Diagnostics;
using System.Text;

namespace BashCommandManager.Cli.Tests.TestSupport;

internal sealed record CliProcessResult(int ExitCode, string Stdout, string Stderr);

/// <summary>起真实 bcm.exe；始终关闭 stdin，默认 30 秒超时。</summary>
internal static class CliProcess
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string CliExecutablePath
    {
        get
        {
            var baseDirectory = Path.Combine(RepoRoot, "BashCommandManager.Cli", "bin", Configuration);
            foreach (var candidate in new[]
                     {
                         Path.Combine(baseDirectory, "net8.0-windows", "bcm.exe"),
                         Path.Combine(baseDirectory, "net8.0-windows", "win-x64", "bcm.exe")
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(
                $"未找到 bcm.exe（{baseDirectory}）。先执行 dotnet build BashCommandManager.Cli\\BashCommandManager.Cli.csproj -c {Configuration}");
        }
    }

    public static CliProcessResult Run(string[] args, string? settingsDirectory = null, int timeoutMs = 30000)
    {
        // B-4：默认也走临时 settings 目录；绝不落到 %LOCALAPPDATA%\BashCommandManager\settings.json
        var ownsSettingsDirectory = settingsDirectory is null;
        settingsDirectory ??= Path.Combine(
            Path.GetTempPath(),
            "bcm-smoke-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settingsDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = CliExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = RepoRoot
        };

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // 先清除 ambient 变量，再按需显式设置，避免开发机环境泄漏进冒烟
        startInfo.Environment.Remove("BASHCOMMANDMANAGER_DB");
        startInfo.Environment.Remove("BASHCOMMANDMANAGER_GUI");
        startInfo.Environment.Remove("BASHCOMMANDMANAGER_SETTINGS_DIR");
        startInfo.Environment["BASHCOMMANDMANAGER_SETTINGS_DIR"] = settingsDirectory;

        try
        {
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("无法启动 bcm.exe");

            // 验收 7：stdin 立即关闭
            process.StandardInput.Close();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                throw new TimeoutException($"bcm 未在 {timeoutMs}ms 内退出: {string.Join(' ', args)}");
            }

            return new CliProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        finally
        {
            if (ownsSettingsDirectory)
            {
                try
                {
                    Directory.Delete(settingsDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BashCommandManager.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录（BashCommandManager.csproj）");
    }
}
