using BashCommandManager.Cli.Operations;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>每个测试一个临时库目录；Dispose 时清理。</summary>
internal sealed class TempDatabase : IDisposable
{
    public TempDatabase()
    {
        Root = Path.Combine(Path.GetTempPath(), "bcm-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatabasePath = Path.Combine(Root, "app.db");
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public DatabaseSession Open() => new(DatabasePath);

    public string CreateScript(string fileName, string content = "@echo off\r\nexit /b 0\r\n")
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public string CreateDirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
