using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;

namespace BashCommandManager.Tests.Fakes;

public sealed class FakeExecutableLocator : IExecutableLocator
{
    public Dictionary<TerminalKind, string?> Results { get; } = new();

    public List<TerminalKind> Calls { get; } = new();

    public string? Resolve(TerminalKind kind, string customExecutablePath)
    {
        Calls.Add(kind);
        return Results.TryGetValue(kind, out var value) ? value : null;
    }
}
