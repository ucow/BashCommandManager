# 批次3：全局快捷键功能实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** 实现系统级全局快捷键，一键打开/最小化应用窗口

**Architecture:** 使用 Win32 API RegisterHotKey 注册全局快捷键，监听 WM_HOTKEY 消息，切换窗口显示状态

**Tech Stack:** WPF, Win32 API (user32.dll), CommunityToolkit.Mvvm

---

## 前置条件

确保系统托盘功能正常工作：
- `MainWindow.xaml` 中已配置 `hc:NotifyIcon`
- 应用已支持最小化到托盘

需阅读：
- `docs/plans/2026-03-07-batch3-global-hotkey-design.md` - 本批次设计文档
- `MainWindow.xaml` - 主窗口定义
- `MainWindow.xaml.cs` - 主窗口代码

---

## 创建全局快捷键服务

### Task 1: 创建 GlobalHotkeyService

**Files:**
- Create: `Core/Services/GlobalHotkeyService.cs`

**Step 1: 编写 Win32 API 调用和封装**

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace BashCommandManager.Core.Services;

public interface IGlobalHotkeyService
{
    bool Register(Window window, Key key, ModifierKeys modifiers);
    void Unregister();
    bool UpdateHotkey(Key key, ModifierKeys modifiers);
    event EventHandler? HotkeyPressed;
}

public class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    // Win32 API 定义
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    // Modifier keys
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private Window? _window;
    private HwndSource? _source;
    private Key _currentKey;
    private ModifierKeys _currentModifiers;
    private bool _isRegistered;

    public event EventHandler? HotkeyPressed;

    public bool Register(Window window, Key key, ModifierKeys modifiers)
    {
        _window = window;
        _currentKey = key;
        _currentModifiers = modifiers;

        var helper = new WindowInteropHelper(window);
        var handle = helper.Handle;

        // 确保窗口句柄已创建
        if (handle == IntPtr.Zero)
        {
            // 等待窗口加载完成
            window.SourceInitialized += Window_SourceInitialized;
            return false;
        }

        return RegisterHotkeyInternal(handle);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        if (_window != null)
        {
            _window.SourceInitialized -= Window_SourceInitialized;
            var helper = new WindowInteropHelper(_window);
            RegisterHotkeyInternal(helper.Handle);
        }
    }

    private bool RegisterHotkeyInternal(IntPtr handle)
    {
        if (_isRegistered)
        {
            UnregisterHotKey(handle, HOTKEY_ID);
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(_currentKey);
        var mod = ConvertModifiers(_currentModifiers);

        _isRegistered = RegisterHotKey(handle, HOTKEY_ID, mod, vk);

        if (_isRegistered)
        {
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(HwndHook);
        }

        return _isRegistered;
    }

    public void Unregister()
    {
        if (_window != null && _isRegistered)
        {
            var helper = new WindowInteropHelper(_window);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            _isRegistered = false;
        }

        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }
    }

    public bool UpdateHotkey(Key key, ModifierKeys modifiers)
    {
        if (_window == null) return false;

        Unregister();

        _currentKey = key;
        _currentModifiers = modifiers;

        var helper = new WindowInteropHelper(_window);
        return RegisterHotkeyInternal(helper.Handle);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint result = MOD_NONE;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            result |= MOD_ALT;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            result |= MOD_CONTROL;
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            result |= MOD_SHIFT;
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            result |= MOD_WIN;
        return result;
    }

    public void Dispose()
    {
        Unregister();
    }
}
```

**Step 2: 提交**

```bash
git add Core/Services/GlobalHotkeyService.cs
git commit -m "feat: 创建全局快捷键服务

- 封装 Win32 RegisterHotKey/UnregisterHotKey API
- 支持修改快捷键
- 支持监听 WM_HOTKEY 消息"
```

---

## 创建设置存储

### Task 2: 创建快捷键设置模型

**Files:**
- Create: `Core/Models/HotkeySettings.cs`

**Step 1: 编写设置模型**

```csharp
using System.Windows.Input;

namespace BashCommandManager.Core.Models;

public class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    public Key Key { get; set; } = Key.B;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;

    public string ToDisplayString()
    {
        var modifierString = Modifiers switch
        {
            ModifierKeys.Control => "Ctrl",
            ModifierKeys.Alt => "Alt",
            ModifierKeys.Shift => "Shift",
            ModifierKeys.Windows => "Win",
            ModifierKeys.Control | ModifierKeys.Alt => "Ctrl+Alt",
            ModifierKeys.Control | ModifierKeys.Shift => "Ctrl+Shift",
            ModifierKeys.Control | ModifierKeys.Windows => "Ctrl+Win",
            ModifierKeys.Alt | ModifierKeys.Shift => "Alt+Shift",
            ModifierKeys.Alt | ModifierKeys.Windows => "Alt+Win",
            ModifierKeys.Shift | ModifierKeys.Windows => "Shift+Win",
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift => "Ctrl+Alt+Shift",
            _ => Modifiers.ToString()
        };
        return $"{modifierString}+{Key}";
    }

    public static bool IsValid(HotkeySettings settings)
    {
        // 至少需要有一个修饰键
        return settings.Modifiers != ModifierKeys.None;
    }
}
```

**Step 2: 提交**

```bash
git add Core/Models/HotkeySettings.cs
git commit -m "feat: 添加快捷键设置模型"
```

### Task 3: 创建设置服务

**Files:**
- Create: `Core/Services/SettingsService.cs`

**Step 1: 编写设置服务**

```csharp
using BashCommandManager.Core.Models;
using System.IO;
using System.Text.Json;

namespace BashCommandManager.Core.Services;

public interface ISettingsService
{
    HotkeySettings GetHotkeySettings();
    void SaveHotkeySettings(HotkeySettings settings);
    event EventHandler? HotkeySettingsChanged;
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private HotkeySettings _hotkeySettings;

    public event EventHandler? HotkeySettingsChanged;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BashCommandManager");

        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        _settingsPath = Path.Combine(appDataPath, "settings.json");
        _hotkeySettings = LoadHotkeySettings();
    }

    public HotkeySettings GetHotkeySettings()
    {
        return _hotkeySettings;
    }

    public void SaveHotkeySettings(HotkeySettings settings)
    {
        _hotkeySettings = settings;
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
        HotkeySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private HotkeySettings LoadHotkeySettings()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<HotkeySettings>(json);
                if (settings != null && HotkeySettings.IsValid(settings))
                {
                    return settings;
                }
            }
            catch
            {
                // 读取失败时使用默认设置
            }
        }

        return new HotkeySettings();
    }
}
```

**Step 2: 提交**

```bash
git add Core/Services/SettingsService.cs
git commit -m "feat: 创建设置服务用于保存快捷键配置"
```

---

## 注册服务并集成

### Task 4: 在 DI 中注册服务

**Files:**
- Modify: `App.xaml.cs`

**Step 1: 添加服务注册**

找到 `ConfigureServices` 方法，添加：

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // ... 现有服务 ...

    // 新增：设置服务（单例）
    services.AddSingleton<ISettingsService, SettingsService>();

    // 新增：全局快捷键服务（单例）
    services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

    // ... 其他服务 ...
}
```

**Step 2: 提交**

```bash
git add App.xaml.cs
git commit -m "feat: DI 注册设置服务和全局快捷键服务"
```

### Task 5: 在 MainWindow 中集成快捷键

**Files:**
- Modify: `MainWindow.xaml.cs`

**Step 1: 注入服务并注册快捷键**

```csharp
public partial class MainWindow : Window
{
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    public MainWindow()
    {
        InitializeComponent();

        // 从服务提供者获取服务
        _hotkeyService = ((App)Application.Current).Services.GetRequiredService<IGlobalHotkeyService>();
        _settingsService = ((App)Application.Current).Services.GetRequiredService<ISettingsService>();

        // 订阅快捷键事件
        _hotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;

        // 窗口加载时注册快捷键
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.GetHotkeySettings();
        if (settings.Enabled)
        {
            var registered = _hotkeyService.Register(this, settings.Key, settings.Modifiers);
            if (!registered)
            {
                Growl.Warning(new GrowlInfo
                {
                    Message = "全局快捷键注册失败，可能已被其他程序占用",
                    WaitTime = 5
                });
            }
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyService.Unregister();
        _hotkeyService.HotkeyPressed -= HotkeyService_HotkeyPressed;
    }

    private void HotkeyService_HotkeyPressed(object? sender, EventArgs e)
    {
        // 在 UI 线程上执行
        Dispatcher.Invoke(() =>
        {
            ToggleWindowVisibility();
        });
    }

    private void ToggleWindowVisibility()
    {
        if (WindowState == WindowState.Minimized || !IsVisible)
        {
            // 显示窗口
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        }
        else if (IsActive)
        {
            // 当前是活动窗口，最小化到托盘
            WindowState = WindowState.Minimized;
            Hide();
        }
        else
        {
            // 窗口可见但不是活动窗口，激活它
            Activate();
            Focus();
        }
    }
}
```

**Step 2: 提交**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: MainWindow 集成全局快捷键

- 窗口加载时注册快捷键
- 快捷键触发时切换窗口显示/隐藏
- 窗口关闭时注销快捷键"
```

---

## 创建设置窗口

### Task 6: 创建快捷键设置窗口

**Files:**
- Create: `Views/SettingsWindow.xaml`
- Create: `Views/SettingsWindow.xaml.cs`

**Step 1: 创建 XAML**

```xml
<Window x:Class="BashCommandManager.Views.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:hc="https://handyorg.github.io/handycontrol"
        Title="设置"
        Height="400"
        Width="500"
        WindowStartupLocation="CenterOwner">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   Text="设置"
                   FontSize="20"
                   FontWeight="Bold"
                   Margin="0,0,0,20"/>

        <TabControl Grid.Row="1">
            <TabItem Header="快捷键">
                <StackPanel Margin="10">
                    <CheckBox Content="启用全局快捷键"
                              IsChecked="{Binding HotkeySettings.Enabled}"
                              Margin="0,0,0,20"/>

                    <GroupBox Header="当前快捷键"
                              IsEnabled="{Binding HotkeySettings.Enabled}">
                        <StackPanel Margin="10">
                            <TextBlock Text="{Binding HotkeySettingsDisplay}"
                                      FontSize="24"
                                      FontWeight="Bold"
                                      HorizontalAlignment="Center"
                                      Margin="0,10"/>

                            <Button Content="设置新快捷键"
                                    Command="{Binding SetHotkeyCommand}"
                                    HorizontalAlignment="Center"
                                    Margin="0,10"/>

                            <TextBlock Text="按下快捷键可以显示或隐藏应用窗口"
                                      Foreground="Gray"
                                      TextWrapping="Wrap"
                                      Margin="0,10,0,0"/>

                            <TextBlock Text="{Binding ErrorMessage}"
                                      Foreground="Red"
                                      TextWrapping="Wrap"
                                      Visibility="{Binding HasError, Converter={StaticResource BoolToVisibility}}"
                                      Margin="0,10,0,0"/>
                        </StackPanel>
                    </GroupBox>
                </StackPanel>
            </TabItem>
        </TabControl>

        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,20,0,0">
            <Button Content="保存"
                    Command="{Binding SaveCommand}"
                    Margin="0,0,10,0"/>
            <Button Content="取消"
                    Command="{Binding CancelCommand}"
                    Style="{StaticResource ButtonDefault}"/>
        </StackPanel>
    </Grid>
</Window>
```

**Step 2: 创建 Code-Behind**

```csharp
using BashCommandManager.ViewModels;
using System.Windows;

namespace BashCommandManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = ((App)Application.Current).Services.GetRequiredService<SettingsViewModel>();
    }
}
```

**Step 3: 提交**

```bash
git add Views/SettingsWindow.xaml Views/SettingsWindow.xaml.cs
git commit -m "feat: 创建设置窗口"
```

### Task 7: 创建设置视图模型

**Files:**
- Create: `ViewModels/SettingsViewModel.cs`

**Step 1: 编写视图模型**

```csharp
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
        var captureWindow = new HotkeyCaptureWindow();
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
    private void Save(Window? window)
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
    private void Cancel(Window? window)
    {
        window?.Close();
    }
}
```

**Step 2: 提交**

```bash
git add ViewModels/SettingsViewModel.cs
git commit -m "feat: 创建设置视图模型"
```

### Task 8: 创建快捷键捕获窗口

**Files:**
- Create: `Views/HotkeyCaptureWindow.xaml`
- Create: `Views/HotkeyCaptureWindow.xaml.cs`

**Step 1: 创建 XAML**

```xml
<Window x:Class="BashCommandManager.Views.HotkeyCaptureWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:hc="https://handyorg.github.io/handycontrol"
        Title="设置快捷键"
        Height="250"
        Width="400"
        WindowStartupLocation="CenterOwner"
        KeyDown="Window_KeyDown"
        KeyUp="Window_KeyUp">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   Text="请按下快捷键组合..."
                   FontSize="16"
                   HorizontalAlignment="Center"/>

        <Border Grid.Row="1"
                Background="{DynamicResource SecondaryRegionBrush}"
                CornerRadius="4"
                Margin="0,20">
            <TextBlock Text="{Binding CapturedHotkeyText}"
                      FontSize="28"
                      FontWeight="Bold"
                      HorizontalAlignment="Center"
                      VerticalAlignment="Center"/>
        </Border>

        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Center">
            <Button Content="确认"
                    Click="ConfirmButton_Click"
                    IsEnabled="{Binding CanConfirm}"
                    Margin="0,0,10,0"/>
            <Button Content="取消"
                    Click="CancelButton_Click"
                    Style="{StaticResource ButtonDefault}"/>
        </StackPanel>
    </Grid>
</Window>
```

**Step 2: 创建 Code-Behind**

```csharp
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

        // 忽略单独的修饰键
        if (e.Key == Key.LeftShift || e.Key == Key.RightShift ||
            e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
            e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
            e.Key == Key.LWin || e.Key == Key.RWin)
        {
            UpdateDisplayText();
            return;
        }

        _capturedKey = e.Key;
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
```

**Step 3: 提交**

```bash
git add Views/HotkeyCaptureWindow.xaml Views/HotkeyCaptureWindow.xaml.cs
git commit -m "feat: 创建快捷键捕获窗口"
```

### Task 9: 在 DI 中注册视图模型

**Files:**
- Modify: `App.xaml.cs`

**Step 1: 注册视图模型**

```csharp
private void ConfigureServices(IServiceCollection services)
{
    // ... 现有服务 ...

    // 视图模型
    services.AddSingleton<MainViewModel>();
    services.AddSingleton<GroupTreeViewModel>();
    services.AddSingleton<CommandListViewModel>();
    services.AddSingleton<SettingsViewModel>();  // 新增
}
```

**Step 2: 提交**

```bash
git add App.xaml.cs
git commit -m "feat: DI 注册 SettingsViewModel"
```

### Task 10: 在主窗口添加设置菜单

**Files:**
- Modify: `MainWindow.xaml`

**Step 1: 在工具栏添加设置按钮**

```xml
<ToolBar Grid.Row="0" Margin="5">
    <hc:ButtonGroup>
        <Button Content="导入命令"
                Command="{Binding ImportCommandCommand}"/>
    </hc:ButtonGroup>

    <Separator/>

    <!-- 批量操作按钮... -->

    <Separator/>

    <!-- 排序控件... -->

    <Separator/>

    <!-- 新增：设置按钮 -->
    <Button Content="设置"
            Command="{Binding OpenSettingsCommand}"/>

    <Separator/>

    <!-- 搜索框... -->
</ToolBar>
```

**Step 2: 提交**

```bash
git add MainWindow.xaml
git commit -m "feat: 工具栏添加设置按钮"
```

### Task 11: 在 MainViewModel 添加打开设置命令

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

**Step 1: 添加打开设置命令**

```csharp
[RelayCommand]
private void OpenSettings()
{
    var settingsWindow = new Views.SettingsWindow
    {
        Owner = Application.Current.MainWindow
    };
    settingsWindow.ShowDialog();
}
```

**Step 2: 提交**

```bash
git add ViewModels/MainViewModel.cs
git commit -m "feat: 添加打开设置窗口命令"
```

---

## 验证和测试

### Task 12: 构建和测试

**Step 1: 构建项目**

```bash
dotnet build
```

**Step 2: 运行并测试**

```bash
dotnet run
```

**手动测试清单：**

- [ ] 应用启动时注册默认快捷键 Ctrl+Alt+B
- [ ] 按下 Ctrl+Alt+B 显示应用窗口
- [ ] 再次按下 Ctrl+Alt+B 最小化应用
- [ ] 打开设置窗口可以看到当前快捷键
- [ ] 可以修改快捷键组合
- [ ] 修改后立即生效
- [ ] 禁用快捷键后不再响应
- [ ] 设置重启后保持

**Step 3: 提交最终代码**

```bash
git add .
git commit -m "feat: 批次3 - 全局快捷键功能完成

- 实现全局快捷键服务（Win32 API）
- 实现设置存储和加载
- 创建设置窗口和快捷键捕获功能
- 默认快捷键 Ctrl+Alt+B 一键显示/隐藏窗口
- 所有功能测试通过"
```

---

## 批次3完成总结

完成后应有以下修改：

1. **服务层**：
   - `GlobalHotkeyService.cs` - Win32 API 封装
   - `SettingsService.cs` - 设置存储

2. **模型**：
   - `HotkeySettings.cs` - 快捷键设置模型

3. **UI**：
   - `SettingsWindow.xaml/cs` - 设置窗口
   - `HotkeyCaptureWindow.xaml/cs` - 快捷键捕获窗口

4. **视图模型**：
   - `SettingsViewModel.cs` - 设置视图模型
   - `MainViewModel.cs` - 添加 OpenSettingsCommand

5. **集成**：
   - `MainWindow.xaml.cs` - 注册快捷键，处理 WM_HOTKEY
   - `MainWindow.xaml` - 添加设置按钮
   - `App.xaml.cs` - DI 注册新服务
