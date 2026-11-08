using BashCommandManager.Core.Models;
using System.IO;
using System.Text;

namespace BashCommandManager.Core.Services;

/// <summary>终端参数模板非法时抛出，消息面向用户可直接展示。</summary>
public class TerminalConfigurationException : Exception
{
    public TerminalConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>把 ExecutionSettings + Command 映射为 TerminalLaunchPlan 的纯函数集合。</summary>
public static class TerminalCommandBuilder
{
    private static readonly string[] AllPlaceholders =
    {
        "{file}", "{dir}", "{filePs}", "{dirPs}", "{fileRaw}", "{dirRaw}"
    };

    private static readonly string[] FilePlaceholders = { "{file}", "{filePs}", "{fileRaw}" };

    public static IReadOnlyList<string> AllowedPlaceholders => AllPlaceholders;

    public static string GetDefaultTemplate(TerminalKind kind, bool keepWindowOpen) => kind switch
    {
        TerminalKind.Cmd => keepWindowOpen ? "/k {file}" : "/c {file}",
        TerminalKind.WindowsPowerShell => keepWindowOpen
            ? "-NoExit -Command \"& '{filePs}'\""
            : "-Command \"& '{filePs}'\"",
        TerminalKind.PowerShell7 => keepWindowOpen
            ? "-NoExit -Command \"& '{filePs}'\""
            : "-Command \"& '{filePs}'\"",
        TerminalKind.WindowsTerminal => keepWindowOpen
            ? "-d {dir} cmd.exe /k {file}"
            : "-d {dir} cmd.exe /c {file}",
        TerminalKind.Custom => "{file}",
        _ => keepWindowOpen ? "/k {file}" : "/c {file}"
    };

    public static TerminalLaunchPlan Build(ExecutionSettings settings, Command command, string? resolvedExecutable = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(command);

        var kind = Enum.IsDefined(settings.Terminal) ? settings.Terminal : TerminalKind.Cmd;
        var keepWindowOpen = settings.KeepWindowOpen;
        var template = string.IsNullOrWhiteSpace(settings.ArgumentsTemplate)
            ? GetDefaultTemplate(kind, keepWindowOpen)
            : settings.ArgumentsTemplate;

        if (!ContainsFilePlaceholder(template))
        {
            throw new TerminalConfigurationException(
                $"参数模板必须包含 {string.Join("、", FilePlaceholders)} 之一，当前模板：{template}");
        }

        var filePath = command.FilePath ?? string.Empty;
        if (filePath.Contains('"'))
        {
            throw new TerminalConfigurationException($"脚本路径包含非法字符 \"：{filePath}");
        }

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var arguments = Expand(template, filePath, directory);

        string fileName;
        if (kind == TerminalKind.Custom)
        {
            var customPath = resolvedExecutable ?? settings.CustomExecutablePath;
            if (string.IsNullOrWhiteSpace(customPath))
            {
                throw new TerminalConfigurationException("未配置自定义终端程序路径");
            }

            fileName = customPath.Trim();
        }
        else
        {
            fileName = resolvedExecutable ?? GetLogicalFileName(kind);
        }

        return new TerminalLaunchPlan(
            fileName,
            arguments,
            string.IsNullOrEmpty(directory) ? null : directory,
            UseShellExecute: true,
            Verb: settings.RunAsAdministrator ? TerminalLaunchPlan.RunAsVerb : null);
    }

    public static IReadOnlyList<string> ValidateTemplate(string template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            return errors;
        }

        var hasFilePlaceholder = false;
        var index = 0;
        while (index < template.Length)
        {
            var current = template[index];

            if (current == '}')
            {
                errors.Add("参数模板中的花括号不配对：存在多余的 }");
                return errors;
            }

            if (current != '{')
            {
                index++;
                continue;
            }

            var closeIndex = template.IndexOf('}', index + 1);
            if (closeIndex < 0)
            {
                errors.Add("参数模板中的花括号不配对：缺少 }");
                return errors;
            }

            var token = template.Substring(index, closeIndex - index + 1);
            if (Array.IndexOf(AllPlaceholders, token) < 0)
            {
                errors.Add($"未知占位符 {token}，允许的占位符：{string.Join(" ", AllPlaceholders)}");
            }
            else if (Array.IndexOf(FilePlaceholders, token) >= 0)
            {
                hasFilePlaceholder = true;
            }

            index = closeIndex + 1;
        }

        if (!hasFilePlaceholder)
        {
            errors.Add($"参数模板必须包含 {string.Join("、", FilePlaceholders)} 之一");
        }

        return errors;
    }

    private static string GetLogicalFileName(TerminalKind kind) => kind switch
    {
        TerminalKind.Cmd => "cmd.exe",
        TerminalKind.WindowsPowerShell => "powershell.exe",
        TerminalKind.PowerShell7 => "pwsh.exe",
        TerminalKind.WindowsTerminal => "wt.exe",
        _ => "cmd.exe"
    };

    private static bool ContainsFilePlaceholder(string template)
    {
        foreach (var placeholder in FilePlaceholders)
        {
            if (template.Contains(placeholder, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Expand(string template, string filePath, string directory)
    {
        var builder = new StringBuilder(template.Length + filePath.Length + 8);
        var index = 0;
        while (index < template.Length)
        {
            var current = template[index];
            if (current != '{')
            {
                builder.Append(current);
                index++;
                continue;
            }

            var closeIndex = template.IndexOf('}', index + 1);
            if (closeIndex < 0)
            {
                throw new TerminalConfigurationException("参数模板中的花括号不配对：缺少 }");
            }

            var token = template.Substring(index, closeIndex - index + 1);
            builder.Append(ExpandToken(token, filePath, directory));
            index = closeIndex + 1;
        }

        return builder.ToString();
    }

    private static string ExpandToken(string token, string filePath, string directory) => token switch
    {
        "{file}" => Quote(filePath),
        "{dir}" => Quote(directory),
        "{filePs}" => EscapePowerShellLiteral(filePath),
        "{dirPs}" => EscapePowerShellLiteral(directory),
        "{fileRaw}" => filePath,
        "{dirRaw}" => directory,
        _ => throw new TerminalConfigurationException(
            $"未知占位符 {token}，允许的占位符：{string.Join(" ", AllPlaceholders)}")
    };

    private static string Quote(string value) => $"\"{value}\"";

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");
}
