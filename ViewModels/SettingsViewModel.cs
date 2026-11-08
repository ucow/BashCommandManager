using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace BashCommandManager.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IGlobalHotkeyService _hotkeyService;

    [ObservableProperty]
    private HotkeySettings _hotkeySettings;

    [ObservableProperty]
    private string _hotkeySettingsDisplay = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public SettingsViewModel(ISettingsService settingsService, IGlobalHotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;

        // 编辑副本：冲突时服务内的当前配置仍是旧值，界面上的改动不会污染它。
        HotkeySettings = _settingsService.GetHotkeySettings().Clone();
        UpdateDisplayString();
    }

    partial void OnHotkeySettingsChanged(HotkeySettings value)
    {
        UpdateDisplayString();
    }

    private void UpdateDisplayString()
    {
        HotkeySettingsDisplay = HotkeySettings.ToDisplayString();
    }

    [RelayCommand]
    private void SetHotkey()
    {
        var captureWindow = new Views.HotkeyCaptureWindow();

        // 捕获期间临时解注册，否则按当前组合会被系统吞掉、主窗口被藏到对话框后面。
        _hotkeyService.Suspend();
        try
        {
            if (captureWindow.ShowDialog() != true)
            {
                return;
            }

            var (key, modifiers) = captureWindow.CapturedHotkey;

            if (modifiers == ModifierKeys.None)
            {
                ErrorMessage = "快捷键必须包含至少一个修饰键 (Ctrl/Alt/Shift/Win)";
                HasError = true;
                return;
            }

            if (key == Key.None)
            {
                // 只按下修饰键就确认：Key.None 会被服务判为 InvalidCombination，这里先拦下。
                ErrorMessage = "快捷键无效，请同时按下一个非修饰键";
                HasError = true;
                return;
            }

            HotkeySettings.Key = key;
            HotkeySettings.Modifiers = modifiers;
            UpdateDisplayString();
            HasError = false;
            ErrorMessage = string.Empty;
        }
        finally
        {
            // Resume 失败不能静默：捕获期间原组合可能已被别的程序抢走，
            // 用户看到「已捕获新键」，实际旧键已经失效。
            var resume = _hotkeyService.Resume();
            if (!resume.IsSuccess)
            {
                Debug.WriteLine(
                    $"捕获结束后恢复原热键失败: status={resume.Status}, key={resume.Key}, modifiers={resume.Modifiers}");

                // NotRegistered 表示用户本来就没有启用热键（Current 为空），属正常状态，不打扰。
                if (resume.Status != HotkeyRegistrationStatus.NotRegistered)
                {
                    ErrorMessage = "原快捷键恢复失败，可能已被其他程序占用（保存时会重新注册）";
                    HasError = true;
                    Growl.Warning(new GrowlInfo
                    {
                        Message = "原快捷键恢复失败，可能已被其他程序占用（保存时会重新注册）",
                        WaitTime = 5
                    });
                }
            }
        }
    }

    [RelayCommand]
    private void Save(System.Windows.Window? window)
    {
        if (!HotkeySettings.IsValid(HotkeySettings))
        {
            ErrorMessage = "快捷键设置无效";
            HasError = true;
            return;
        }

        if (HotkeySettings.Enabled)
        {
            // 先注册成功，再落盘：冲突时旧组合继续生效，settings.json 不被改写。
            var result = _hotkeyService.UpdateHotkey(HotkeySettings.Key, HotkeySettings.Modifiers);
            if (!result.IsSuccess)
            {
                // 分档文案：只有 Conflict（1409）才值得让用户去关别的程序；
                // InvalidCombination / HostUnavailable 是组合本身无效或宿主不可用，
                // 统一说「已被占用」会让用户永远修不好。三条分支都不落盘。
                var message = result.Status switch
                {
                    HotkeyRegistrationStatus.Conflict =>
                        "快捷键更新失败，可能已被其他程序占用（已保留原快捷键）",
                    HotkeyRegistrationStatus.InvalidCombination =>
                        "快捷键无效或系统不可用（已保留原快捷键）",
                    HotkeyRegistrationStatus.HostUnavailable =>
                        "快捷键无效或系统不可用（已保留原快捷键）",
                    _ => "快捷键更新失败（已保留原快捷键）"
                };

                ErrorMessage = message;
                HasError = true;
                Growl.Warning(new GrowlInfo
                {
                    Message = message,
                    WaitTime = 5
                });
                return;
            }
        }
        else
        {
            // 用户主动关闭启用开关：这是唯一由用户意图触发的注销。
            _hotkeyService.Unregister();
        }

        // 传克隆体，避免服务内的内存配置与界面编辑副本互为别名。
        _settingsService.SaveHotkeySettings(HotkeySettings.Clone());

        // 成功路径必须复位：本 VM 由根容器解析、实际是单例，否则下次打开设置页还挂着旧红字。
        HasError = false;
        ErrorMessage = string.Empty;

        window?.Close();

        Growl.Success(new GrowlInfo
        {
            Message = "设置已保存",
            WaitTime = 3
        });
    }

    [RelayCommand]
    private void Cancel(System.Windows.Window? window)
    {
        window?.Close();
    }
}
