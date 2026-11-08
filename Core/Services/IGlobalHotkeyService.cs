using BashCommandManager.Core.Models;
using System.Windows.Input;

namespace BashCommandManager.Core.Services;

/// <summary>
/// 进程级全局热键。宿主与主窗口生命周期无关：注册在 App 启动、生命周期的注销只在 App 退出。
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    HotkeyRegistrationResult TryRegister(Key key, ModifierKeys modifiers);

    HotkeyRegistrationResult UpdateHotkey(Key key, ModifierKeys modifiers);

    /// <summary>临时解注册（捕获新组合期间），保留 Current。</summary>
    void Suspend();

    /// <summary>用 Current 重新注册；没有 Current 时返回 NotRegistered。</summary>
    HotkeyRegistrationResult Resume();

    /// <summary>注销并清空 Current。</summary>
    void Unregister();

    bool IsRegistered { get; }

    event EventHandler? HotkeyPressed;
}
