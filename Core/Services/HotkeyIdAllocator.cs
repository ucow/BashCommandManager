namespace BashCommandManager.Core.Services;

/// <summary>
/// 在 Win32 应用可用的 0x9000–0x90FF 区间内分配热键 id；用尽后回绕。
/// 每次改键都用新 id，从而可以先注册新组合、成功后再注销旧 id。
/// </summary>
public sealed class HotkeyIdAllocator
{
    public const int FirstId = 0x9000;
    public const int LastId = 0x90FF;

    private int _next = FirstId;

    public int Next()
    {
        var id = _next;
        _next = id == LastId ? FirstId : id + 1;
        return id;
    }
}
