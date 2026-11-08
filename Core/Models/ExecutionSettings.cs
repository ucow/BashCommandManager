namespace BashCommandManager.Core.Models;

/// <summary>全局「执行/终端」配置，持久化在 settings.json 的 Execution 段。</summary>
public class ExecutionSettings
{
    public TerminalKind Terminal { get; set; } = TerminalKind.Cmd;

    public string CustomExecutablePath { get; set; } = string.Empty;

    /// <summary>参数模板；空白表示使用 TerminalCommandBuilder 的默认模板。</summary>
    public string ArgumentsTemplate { get; set; } = string.Empty;

    public bool KeepWindowOpen { get; set; } = true;

    public bool RunAsAdministrator { get; set; }

    public ExecutionSettings Clone() => (ExecutionSettings)MemberwiseClone();

    public ExecutionSettings WithTerminal(TerminalKind kind)
    {
        var clone = Clone();
        clone.Terminal = kind;
        return clone;
    }
}
