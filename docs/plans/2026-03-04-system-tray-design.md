# 系统托盘功能设计文档

## 概述
在 WPF 应用程序中添加系统托盘支持，当窗体最小化时隐藏到系统托盘（状态栏），双击托盘图标恢复窗体显示，右键菜单提供"显示窗口"和"退出"选项。

## 需求确认

### 功能需求
1. 窗体最小化时，不放在任务栏，而是放到 Windows 状态栏（系统托盘）
2. 双击状态栏图标可以恢复窗体显示
3. 右键托盘图标显示菜单：显示窗口、退出
4. 点击关闭按钮时最小化到托盘，而非退出程序

### 技术选型
使用 **HandyControl 内置的 NotifyIcon 控件**（hc:NotifyIcon）实现，原因：
- 纯 WPF 方案，无需引入 WinForms 依赖
- 与现有 HandyControl UI 框架一致
- 版本 3.5.1 已支持该组件

## 架构设计

### 组件位置
- `MainWindow.xaml` - 添加 NotifyIcon 控件声明
- `MainWindow.xaml.cs` - 处理窗体状态变化和托盘交互事件

### 核心行为
1. 窗体最小化 → 隐藏窗体，显示托盘图标
2. 双击托盘图标 → 恢复窗体显示
3. 右键托盘图标 → 显示上下文菜单（显示窗口、退出）
4. 点击关闭按钮 → 取消关闭，最小化到托盘

## UI 设计

### XAML 声明
```xml
<hc:NotifyIcon x:Name="TrayIcon"
               Icon="Assets/app.ico"
               Text="批处理命令管理器"
               MouseDoubleClick="TrayIcon_MouseDoubleClick">
    <hc:NotifyIcon.ContextMenu>
        <ContextMenu>
            <MenuItem Header="显示窗口" Click="ShowWindow_Click"/>
            <Separator/>
            <MenuItem Header="退出" Click="Exit_Click"/>
        </ContextMenu>
    </hc:NotifyIcon.ContextMenu>
</hc:NotifyIcon>
```

## 数据流与交互

### 窗体最小化流程
```
用户点击最小化按钮 / 点击关闭按钮
    ↓
触发 Window_StateChanged / OnClosing 事件
    ↓
隐藏窗体 (Hide())
托盘图标保持显示
```

### 恢复窗体流程
```
双击托盘图标 / 点击右键菜单"显示窗口"
    ↓
恢复窗体 (Show())
设置窗体状态 (WindowState = Normal)
激活窗体 (Activate())
```

### 退出流程
```
点击右键菜单"退出"
    ↓
清理托盘图标 (TrayIcon.Dispose())
关闭应用程序 (Application.Current.Shutdown())
```

## 关键代码逻辑

### 1. 窗体状态变化处理
```csharp
private void Window_StateChanged(object sender, EventArgs e)
{
    if (WindowState == WindowState.Minimized)
    {
        Hide();  // 隐藏窗体（从任务栏消失）
    }
}
```

### 2. 关闭按钮处理（最小化到托盘）
```csharp
protected override void OnClosing(CancelEventArgs e)
{
    e.Cancel = true;  // 取消关闭
    WindowState = WindowState.Minimized;  // 触发最小化
    Hide();  // 隐藏窗体
    base.OnClosing(e);
}
```

### 3. 双击托盘恢复
```csharp
private void TrayIcon_MouseDoubleClick(object sender, MouseButtonEventArgs e)
{
    ShowWindow();
}
```

### 4. 显示窗口方法
```csharp
private void ShowWindow()
{
    Show();
    WindowState = WindowState.Normal;
    Activate();  // 激活窗体到前台
}
```

### 5. 菜单项处理
```csharp
// 显示窗口
private void ShowWindow_Click(object sender, RoutedEventArgs e)
{
    ShowWindow();
}

// 退出程序
private void Exit_Click(object sender, RoutedEventArgs e)
{
    TrayIcon.Dispose();  // 清理托盘图标资源
    Application.Current.Shutdown();
}
```

## 资源管理

### 窗体关闭时的处理
- 重载 `OnClosing` 方法，用户点击关闭按钮时最小化到托盘而非退出
- 提供真正的退出选项（右键菜单中的"退出"）
- 确保托盘图标在程序退出时被正确清理（调用 Dispose）

## 错误处理

### 边界情况
1. **重复最小化** - 检查当前窗体状态，避免重复操作
2. **托盘图标残留** - 确保程序退出时调用 `NotifyIcon.Dispose()`
3. **窗体已关闭** - 防止在窗体关闭后尝试操作窗体
4. **多次点击退出** - 防止用户快速多次点击退出导致异常

## 设计批准

- 设计方案已确认，符合用户需求
- 用户要求：使用 HandyControl NotifyIcon，保留双击打开功能，右键菜单包含"显示窗口"和"退出"
