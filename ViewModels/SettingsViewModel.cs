using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace BashCommandManager.ViewModels;

/// <summary>终端下拉项的展示模型。</summary>
public sealed record TerminalOption(TerminalKind Kind, string DisplayName);

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private bool _templateIsDefault;

    [ObservableProperty]
    private HotkeySettings _hotkeySettings = new();

    [ObservableProperty]
    private string _hotkeySettingsDisplay = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private TerminalKind _terminal;

    [ObservableProperty]
    private string _customExecutablePath = string.Empty;

    [ObservableProperty]
    private string _argumentsTemplate = string.Empty;

    [ObservableProperty]
    private bool _keepWindowOpen = true;

    [ObservableProperty]
    private bool _runAsAdministrator;

    public IReadOnlyList<TerminalOption> TerminalOptions { get; } = new List<TerminalOption>
    {
        new(TerminalKind.Cmd, TerminalKind.Cmd.GetDisplayName()),
        new(TerminalKind.WindowsPowerShell, TerminalKind.WindowsPowerShell.GetDisplayName()),
        new(TerminalKind.PowerShell7, TerminalKind.PowerShell7.GetDisplayName()),
        new(TerminalKind.WindowsTerminal, TerminalKind.WindowsTerminal.GetDisplayName()),
        new(TerminalKind.Custom, TerminalKind.Custom.GetDisplayName())
    };

    public bool IsCustomTerminal => Terminal == TerminalKind.Custom;

    public SettingsViewModel(ISettingsService settingsService, IGlobalHotkeyService hotkeyService)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;

        HotkeySettings = _settingsService.GetHotkeySettings().Clone();

        var execution = _settingsService.GetExecutionSettings().Clone();
        _terminal = Enum.IsDefined(execution.Terminal) ? execution.Terminal : TerminalKind.Cmd;
        _customExecutablePath = execution.CustomExecutablePath ?? string.Empty;
        _keepWindowOpen = execution.KeepWindowOpen;
        _runAsAdministrator = execution.RunAsAdministrator;
        _argumentsTemplate = string.IsNullOrWhiteSpace(execution.ArgumentsTemplate)
            ? TerminalCommandBuilder.GetDefaultTemplate(_terminal, _keepWindowOpen)
            : execution.ArgumentsTemplate.Trim();
        _templateIsDefault = string.Equals(
            _argumentsTemplate,
            TerminalCommandBuilder.GetDefaultTemplate(_terminal, _keepWindowOpen),
            StringComparison.Ordinal);

        UpdateDisplayString();
    }

    partial void OnHotkeySettingsChanged(HotkeySettings value) => UpdateDisplayString();

    partial void OnTerminalChanged(TerminalKind value)
    {
        OnPropertyChanged(nameof(IsCustomTerminal));

        if (_templateIsDefault)
        {
            ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(value, KeepWindowOpen);
        }
    }

    partial void OnKeepWindowOpenChanged(bool value)
    {
        if (_templateIsDefault)
        {
            ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(Terminal, value);
        }
    }

    partial void OnArgumentsTemplateChanged(string value)
    {
        _templateIsDefault = string.Equals(
            value,
            TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen),
            StringComparison.Ordinal);
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
    private void BrowseCustomExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择终端程序",
            Filter = "可执行文件|*.exe;*.bat;*.cmd|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            CustomExecutablePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void ResetTemplate()
    {
        _templateIsDefault = true;
        ArgumentsTemplate = TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen);
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

        if (Terminal == TerminalKind.Custom)
        {
            if (string.IsNullOrWhiteSpace(CustomExecutablePath))
            {
                ErrorMessage = "自定义终端程序路径不能为空";
                HasError = true;
                return;
            }

            if (!File.Exists(CustomExecutablePath))
            {
                ErrorMessage = $"自定义程序不存在：{CustomExecutablePath}";
                HasError = true;
                return;
            }
        }

        var templateErrors = TerminalCommandBuilder.ValidateTemplate(ArgumentsTemplate);
        if (templateErrors.Count > 0)
        {
            ErrorMessage = string.Join("；", templateErrors);
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

        var execution = new ExecutionSettings
        {
            Terminal = Terminal,
            CustomExecutablePath = (CustomExecutablePath ?? string.Empty).Trim(),
            ArgumentsTemplate = (ArgumentsTemplate ?? string.Empty).Trim(),
            KeepWindowOpen = KeepWindowOpen,
            RunAsAdministrator = RunAsAdministrator
        };

        try
        {
            // 传克隆体，避免服务内的内存配置与界面编辑副本互为别名（#8 Task 9 的语义）。
            _settingsService.SaveHotkeySettings(HotkeySettings.Clone());
            _settingsService.SaveExecutionSettings(execution);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"设置保存失败：{ex.Message}";
            HasError = true;
            Growl.Error(new GrowlInfo
            {
                Message = $"设置保存失败：{ex.Message}",
                WaitTime = 5
            });
            return;
        }

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

    private void UpdateDisplayString() => HotkeySettingsDisplay = HotkeySettings.ToDisplayString();
}
