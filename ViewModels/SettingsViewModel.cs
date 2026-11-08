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

        Reload();
    }

    /// <summary>
    /// 从设置服务重新读取一份编辑快照。本 VM 由根容器解析、实际是单例，
    /// 设置窗口每次 Loaded 都调用一次，避免「取消后重开仍显示上次未保存的编辑值」。
    /// </summary>
    public void Reload()
    {
        HotkeySettings = _settingsService.GetHotkeySettings().Clone();
        ApplyExecutionSettings(_settingsService.GetExecutionSettings().Clone());

        HasError = false;
        ErrorMessage = string.Empty;
    }

    private void ApplyExecutionSettings(ExecutionSettings execution)
    {
        // 载入期间关闭模板联动，避免 Terminal/KeepWindowOpen 的 setter 把读回来的模板改成别的默认值。
        _templateIsDefault = false;

        Terminal = Enum.IsDefined(execution.Terminal) ? execution.Terminal : TerminalKind.Cmd;
        CustomExecutablePath = execution.CustomExecutablePath ?? string.Empty;
        KeepWindowOpen = execution.KeepWindowOpen;
        RunAsAdministrator = execution.RunAsAdministrator;
        ArgumentsTemplate = string.IsNullOrWhiteSpace(execution.ArgumentsTemplate)
            ? TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen)
            : execution.ArgumentsTemplate.Trim();
        _templateIsDefault = string.Equals(
            ArgumentsTemplate,
            TerminalCommandBuilder.GetDefaultTemplate(Terminal, KeepWindowOpen),
            StringComparison.Ordinal);

        // 属性 setter 只在值变化时通知；这里统一补一次，保证 Reload 后 UI 一定刷新。
        OnPropertyChanged(nameof(IsCustomTerminal));
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
                    ShowWarning(ErrorMessage);
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

        var execution = new ExecutionSettings
        {
            Terminal = Terminal,
            CustomExecutablePath = (CustomExecutablePath ?? string.Empty).Trim(),
            ArgumentsTemplate = (ArgumentsTemplate ?? string.Empty).Trim(),
            KeepWindowOpen = KeepWindowOpen,
            RunAsAdministrator = RunAsAdministrator
        };

        // 执行设置与热键是两个独立失败域：先落盘执行设置，快捷键注册失败不能阻断它。
        try
        {
            _settingsService.SaveExecutionSettings(execution);
        }
        catch (Exception ex)
        {
            ShowSaveFailure($"执行设置保存失败：{ex.Message}");
            return;
        }

        if (HotkeySettings.Enabled)
        {
            // 先注册成功，再落盘：冲突时旧组合继续生效，settings.json 的热键段不被改写。
            var result = _hotkeyService.UpdateHotkey(HotkeySettings.Key, HotkeySettings.Modifiers);
            if (!result.IsSuccess)
            {
                var reason = result.Status switch
                {
                    HotkeyRegistrationStatus.Conflict => "可能已被其他程序占用",
                    HotkeyRegistrationStatus.InvalidCombination => "组合无效或系统不可用",
                    HotkeyRegistrationStatus.HostUnavailable => "组合无效或系统不可用",
                    _ => "注册失败"
                };

                ShowSaveFailure($"执行设置已保存，快捷键未更改（{reason}）");
                return;
            }
        }

        try
        {
            _settingsService.SaveHotkeySettings(HotkeySettings.Clone());
        }
        catch (Exception ex)
        {
            ShowSaveFailure($"执行设置已保存，快捷键设置未保存：{ex.Message}");
            return;
        }

        if (!HotkeySettings.Enabled)
        {
            // 用户主动关闭启用开关：先成功落盘再注销；写盘失败时保持原热键，避免内存/磁盘不一致。
            _hotkeyService.Unregister();
        }

        // 成功路径必须复位：本 VM 由根容器解析、实际是单例，否则下次打开设置页还挂着旧红字。
        HasError = false;
        ErrorMessage = string.Empty;

        window?.Close();

        ShowSuccess("设置已保存");
    }

    private void ShowSaveFailure(string message)
    {
        ErrorMessage = message;
        HasError = true;
        ShowWarning(message);
    }

    private static void ShowWarning(string message)
    {
        try
        {
            Growl.Warning(new GrowlInfo
            {
                Message = message,
                WaitTime = 5
            });
        }
        catch (Exception ex)
        {
            // 无 UI 容器（单测/宿主未初始化）时通知失败不能影响保存逻辑。
            Debug.WriteLine($"显示设置告警失败: {ex.Message}");
        }
    }

    private static void ShowSuccess(string message)
    {
        try
        {
            Growl.Success(new GrowlInfo
            {
                Message = message,
                WaitTime = 3
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示设置成功提示失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Cancel(System.Windows.Window? window)
    {
        window?.Close();
    }

    private void UpdateDisplayString() => HotkeySettingsDisplay = HotkeySettings.ToDisplayString();
}
