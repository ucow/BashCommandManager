using System.Windows.Input;

namespace BashCommandManager.Core.Models;

public enum HotkeyRegistrationStatus
{
    Success,
    Conflict,
    InvalidCombination,
    HostUnavailable,
    NotRegistered
}

/// <summary>
/// 一次注册/改键尝试的结果。失败时携带被尝试的组合，便于调用方提示。
/// </summary>
public readonly record struct HotkeyRegistrationResult(
    HotkeyRegistrationStatus Status,
    Key Key,
    ModifierKeys Modifiers)
{
    /// <summary>Win32 ERROR_HOTKEY_ALREADY_REGISTERED。</summary>
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    public bool IsSuccess => Status == HotkeyRegistrationStatus.Success;

    public static HotkeyRegistrationResult Success(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.Success, key, modifiers);

    public static HotkeyRegistrationResult InvalidCombination(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.InvalidCombination, key, modifiers);

    public static HotkeyRegistrationResult HostUnavailable(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.HostUnavailable, key, modifiers);

    public static HotkeyRegistrationResult NotRegistered(Key key, ModifierKeys modifiers) =>
        new(HotkeyRegistrationStatus.NotRegistered, key, modifiers);

    /// <summary>
    /// 把 RegisterHotKey 的 GetLastError 映射成状态：1409 表示组合已被占用（Conflict），其余一律视为宿主不可用。
    /// </summary>
    public static HotkeyRegistrationResult FromRegisterError(int win32Error, Key key, ModifierKeys modifiers) =>
        new(
            win32Error == ErrorHotkeyAlreadyRegistered
                ? HotkeyRegistrationStatus.Conflict
                : HotkeyRegistrationStatus.HostUnavailable,
            key,
            modifiers);
}
