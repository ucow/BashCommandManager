using BashCommandManager.Core.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace BashCommandManager.Views;

public partial class HotkeyCaptureWindow : Window, INotifyPropertyChanged
{
    private Key _capturedKey;
    private ModifierKeys _capturedModifiers;
    private bool _canConfirm;

    public (Key Key, ModifierKeys Modifiers) CapturedHotkey => (_capturedKey, _capturedModifiers);

    public string CapturedHotkeyText { get; private set; } = "等待输入...";

    public bool CanConfirm
    {
        get => _canConfirm;
        private set
        {
            _canConfirm = value;
            OnPropertyChanged();
        }
    }

    public HotkeyCaptureWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt 组合下 e.Key 是 Key.System，真实键在 e.SystemKey：先归一化再判断与保存。
        var key = HotkeyKeyNormalizer.Normalize(e.Key, e.SystemKey);

        // 忽略单独的修饰键（Alt 组合里 e.SystemKey 才是 Key.LeftAlt/Key.RightAlt）
        if (key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin)
        {
            UpdateDisplayText();
            return;
        }

        _capturedKey = key;
        _capturedModifiers = Keyboard.Modifiers;

        UpdateDisplayText();
        CanConfirm = _capturedModifiers != ModifierKeys.None;
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        e.Handled = true;
    }

    private void UpdateDisplayText()
    {
        var modifiers = Keyboard.Modifiers;
        var key = _capturedKey;

        if (modifiers == ModifierKeys.None && key == Key.None)
        {
            CapturedHotkeyText = "等待输入...";
        }
        else
        {
            var modString = modifiers switch
            {
                ModifierKeys.Control => "Ctrl+",
                ModifierKeys.Alt => "Alt+",
                ModifierKeys.Shift => "Shift+",
                ModifierKeys.Windows => "Win+",
                ModifierKeys.Control | ModifierKeys.Alt => "Ctrl+Alt+",
                ModifierKeys.Control | ModifierKeys.Shift => "Ctrl+Shift+",
                ModifierKeys.Control | ModifierKeys.Windows => "Ctrl+Win+",
                ModifierKeys.Alt | ModifierKeys.Shift => "Alt+Shift+",
                ModifierKeys.Alt | ModifierKeys.Windows => "Alt+Win+",
                ModifierKeys.Shift | ModifierKeys.Windows => "Shift+Win+",
                ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift => "Ctrl+Alt+Shift+",
                _ => string.Empty
            };

            var keyString = key != Key.None ? key.ToString() : "...";
            CapturedHotkeyText = modString + keyString;
        }

        OnPropertyChanged(nameof(CapturedHotkeyText));
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null!)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
