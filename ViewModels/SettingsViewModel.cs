using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;
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

    [ObservableProperty]
    private bool _isCapturingHotkey;

    private Key _capturedKey;
    private ModifierKeys _capturedModifiers;

    public SettingsViewModel(ISettingsService settingsService, IGlobalHotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;

        HotkeySettings = _settingsService.GetHotkeySettings();
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
        // 显示捕获快捷键的对话框
        var captureWindow = new Views.HotkeyCaptureWindow();
        if (captureWindow.ShowDialog() == true)
        {
            var (key, modifiers) = captureWindow.CapturedHotkey;

            if (modifiers == ModifierKeys.None)
            {
                ErrorMessage = "快捷键必须包含至少一个修饰键 (Ctrl/Alt/Shift/Win)";
                HasError = true;
                return;
            }

            HotkeySettings.Key = key;
            HotkeySettings.Modifiers = modifiers;
            UpdateDisplayString();
            HasError = false;
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

        _settingsService.SaveHotkeySettings(HotkeySettings);

        // 如果启用了快捷键，更新注册
        if (HotkeySettings.Enabled)
        {
            var registered = _hotkeyService.UpdateHotkey(HotkeySettings.Key, HotkeySettings.Modifiers);
            if (!registered)
            {
                Growl.Warning(new GrowlInfo
                {
                    Message = "快捷键更新失败，可能已被其他程序占用",
                    WaitTime = 5
                });
            }
        }
        else
        {
            _hotkeyService.Unregister();
        }

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
