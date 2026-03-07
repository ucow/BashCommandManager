# 批次3：全局快捷键功能设计文档

## 概述

本批次实现系统级全局快捷键功能，允许用户通过快捷键一键打开或最小化应用窗口。

## 关联Issue

| Issue | 标题 | 描述 |
|-------|------|------|
| #6 | 全局快捷键支持 | 为窗体添加全局快捷键，一键打开和最小化应用，快捷键可配置 |

## 设计决策

### 1. 快捷键功能

**默认快捷键**：
- 建议：`Ctrl + Alt + B`（BashCommandManager 的首字母）
- 或者：`Ctrl + Shift + Space`

**行为**：
- 应用隐藏/最小化时 → 显示并激活窗口
- 应用显示时 → 最小化到系统托盘
- 相当于"切换"功能

### 2. 快捷键配置

**设置界面**：
- 在设置窗口中添加"快捷键"选项卡
- 显示当前快捷键
- 点击设置新快捷键（捕获按键组合）
- 显示冲突检测（如与其他应用冲突）

**快捷键格式**：
```csharp
public class HotkeySettings
{
    public Key Key { get; set; }           // 主键，如 B
    public ModifierKeys Modifiers { get; set; }  // 修饰键，如 Ctrl+Alt
}
```

### 3. 技术实现

**Windows API 调用**：
需要使用 Win32 API 注册全局快捷键：
```csharp
[DllImport("user32.dll")]
static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll")]
static extern bool UnregisterHotKey(IntPtr hWnd, int id);
```

**实现类设计**：
```csharp
public class GlobalHotkeyService : IDisposable
{
    // 注册全局快捷键
    public bool Register(Window window, int hotkeyId, Key key, ModifierKeys modifiers);

    // 注销快捷键
    public bool Unregister(int hotkeyId);

    // 快捷键触发事件
    public event EventHandler HotkeyPressed;
}
```

**窗口消息处理**：
- 重写 `WndProc` 处理 `WM_HOTKEY` 消息
- 当收到快捷键消息时触发切换窗口状态

### 4. 启动行为

**应用启动时**：
1. 读取用户设置的快捷键
2. 如果快捷键已设置，注册全局快捷键
3. 如果注册失败（被占用），显示通知提示

**应用退出时**：
- 注销所有已注册的全局快捷键
- 清理资源

## 数据模型变更

### 新增：GlobalHotkeySettings
```csharp
public class GlobalHotkeySettings
{
    public bool Enabled { get; set; } = true;
    public Key Key { get; set; } = Key.B;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;

    public string ToDisplayString()
    {
        return $"{Modifiers} + {Key}";
    }
}
```

### 新增：GlobalHotkeyService
```csharp
public interface IGlobalHotkeyService
{
    bool Register(Window window);
    void Unregister();
    bool UpdateHotkey(Key key, ModifierKeys modifiers);
    event EventHandler HotkeyPressed;
}
```

## UI布局变更

### 设置窗口 - 快捷键选项卡
```xml
<TabItem Header="快捷键">
    <StackPanel>
        <CheckBox Content="启用全局快捷键"
                  IsChecked="{Binding HotkeySettings.Enabled}" />

        <GroupBox Header="当前快捷键">
            <StackPanel>
                <TextBlock Text="{Binding HotkeyDisplayString}"
                          FontSize="20"
                          FontWeight="Bold" />
                <Button Content="设置新快捷键"
                        Command="{Binding SetHotkeyCommand}" />
            </StackPanel>
        </GroupBox>

        <TextBlock Text="按下快捷键可以显示或隐藏应用窗口"
                  Foreground="Gray" />

        <TextBlock Text="{Binding ConflictMessage}"
                  Foreground="Red"
                  Visibility="{Binding HasConflict, Converter={StaticResource BoolToVisibility}}" />
    </StackPanel>
</TabItem>
```

### 快捷键捕获对话框
```xml
<hc:Dialog x:Name="HotkeyCaptureDialog">
    <StackPanel>
        <TextBlock Text="请按下快捷键组合..."
                  FontSize="16" />
        <TextBlock x:Name="CapturedHotkeyText"
                  FontSize="24"
                  FontWeight="Bold"
                  Margin="0,20" />
        <Button Content="确认"
                Command="{Binding ConfirmHotkeyCommand}" />
        <Button Content="取消"
                Command="{Binding CancelCaptureCommand}" />
    </StackPanel>
</hc:Dialog>
```

## 交互流程

### 注册快捷键
```
应用启动
    ↓
读取设置中的快捷键配置
    ↓
调用 RegisterHotKey Win32 API
    ↓
注册成功 → 开始监听 WM_HOTKEY 消息
注册失败 → 显示通知"快捷键已被占用"
```

### 快捷键触发
```
用户按下快捷键
    ↓
Windows 发送 WM_HOTKEY 消息
    ↓
WndProc 接收消息
    ↓
触发 HotkeyPressed 事件
    ↓
判断当前窗口状态
    ↓
[隐藏/最小化] → 显示并激活窗口
[显示] → 最小化到托盘
```

### 修改快捷键
```
用户打开设置 → 快捷键选项卡
    ↓
点击"设置新快捷键"
    ↓
显示快捷键捕获对话框
    ↓
用户按下新的组合键
    ↓
显示捕获的快捷键
    ↓
用户点击确认
    ↓
注销旧快捷键
    ↓
注册新快捷键
    ↓
保存到设置
```

## 测试要点

1. **快捷键注册**
   - 应用启动时正确注册快捷键
   - 快捷键被占用时显示提示
   - 应用退出时注销快捷键

2. **快捷键触发**
   - 按下快捷键切换窗口显示/隐藏
   - 多次按下正确切换状态
   - 窗口在前台时也能响应

3. **快捷键配置**
   - 可以修改快捷键组合
   - 支持多种修饰键组合
   - 修改后立即生效
   - 设置持久化

4. **边界情况**
   - 系统休眠/恢复后快捷键仍有效
   - 多显示器环境正确激活窗口
   - UAC 弹窗不影响快捷键

## 验收标准

- [ ] 默认快捷键 `Ctrl+Alt+B` 可以显示/隐藏应用窗口
- [ ] 可以在设置中修改快捷键
- [ ] 快捷键修改后立即生效
- [ ] 快捷键设置重启后保持
- [ ] 快捷键被占用时显示友好提示
- [ ] 所有功能在单文件发布模式下正常工作

## 依赖关系

- 依赖系统托盘功能（已实现）
- 依赖设置存储机制
- 需要使用 Windows Win32 API

## 风险评估

| 风险 | 等级 | 缓解措施 |
|------|------|----------|
| 快捷键冲突 | 中 | 冲突检测和用户提示 |
| Win32 API 兼容性 | 低 | 使用标准 Windows API |
| 管理员权限问题 | 低 | 不需要特殊权限 |
| 防病毒软件误报 | 低 | 正常软件行为 |

## 技术参考

- Win32 API: `RegisterHotKey`, `UnregisterHotKey`
- WPF: `WindowInteropHelper`, `HwndSource`
- Windows Messages: `WM_HOTKEY = 0x0312`
