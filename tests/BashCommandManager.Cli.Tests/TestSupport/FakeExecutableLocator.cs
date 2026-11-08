using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>终端程序定位的内存替身：默认返回逻辑名 cmd.exe，可按用例替换解析函数。</summary>
internal sealed class FakeExecutableLocator : IExecutableLocator
{
    // 偏差记录（VD-14）：计划给的是 C:\Windows\System32\cmd.exe，但 T20 的
    // Default_run_starts_process_records_execution_and_reports_pid 断言 request.FileName == "cmd.exe"，
    // 而实现使用的是 locator 的解析结果；默认值取逻辑名才能两者一致。
    public Func<TerminalKind, string, string?> Resolver { get; set; }
        = (_, _) => "cmd.exe";

    public string? Resolve(TerminalKind kind, string customExecutablePath) => Resolver(kind, customExecutablePath);
}
