using BashCommandManager.Core.Models;

namespace BashCommandManager.Core.Services;

/// <summary>单次执行的附加选项。</summary>
public sealed record ExecutionOptions(bool RunAsAdmin = false);

/// <summary>执行结果；Started=false 时调用方按 Notice/Error 给用户提示。</summary>
public sealed record ExecutionResult(
    bool Started,
    TerminalKind EffectiveTerminal,
    bool FallbackApplied,
    string? Notice,
    Exception? Error);
