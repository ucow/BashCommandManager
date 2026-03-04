# 系统托盘功能实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 添加系统托盘支持，窗体最小化时隐藏到系统托盘，双击恢复，右键菜单包含"显示窗口"和"退出"

**Architecture:** 使用 HandyControl 内置的 hc:NotifyIcon 控件，在 MainWindow.xaml 中声明托盘图标，在 MainWindow.xaml.cs 中处理窗体最小化/恢复逻辑和托盘交互事件。

**Tech Stack:** .NET 8 WPF, HandyControl 3.5.1, NotifyIcon

---

## 前置条件

- 项目已安装 HandyControl 3.5.1（已确认）
- 已有 Assets/app.ico 图标文件（已存在）

---

## Task 1: 在 MainWindow.xaml 中添加 NotifyIcon 控件

**Files:**
- Modify: `MainWindow.xaml:11`（在 Window 标签内）

**Step 1: 在 Window 标签内添加 hc:NotifyIcon 控件**

在 `MainWindow.xaml` 中，找到 Window 标签的结束处（在 `</hc:Window>` 之前），添加以下代码：

```xml
    <!-- 系统托盘图标 -->
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

**Step 2: 验证 XAML 语法**

确保添加的位置在 `</hc:Window>` 结束标签之前。

**Step 3: 编译检查**

Run: `dotnet build`
Expected: 编译成功（此时事件处理器尚未实现，警告可忽略）

**Step 4: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: 添加系统托盘图标到 XAML"
```

---

## Task 2: 在 MainWindow.xaml.cs 中实现窗体最小化逻辑

**Files:**
- Modify: `MainWindow.xaml.cs`（在构造函数和现有方法之后添加新代码）

**Step 1: 在构造函数中订阅 StateChanged 事件**

在构造函数末尾（在 `Loaded += ...` 代码块之后）添加：

```csharp
        // 订阅窗体状态变化事件
        StateChanged += MainWindow_StateChanged;
```

**Step 2: 添加窗体状态变化处理方法**

在类末尾（`TreeViewItem_ContextMenuOpening` 方法之后）添加：

```csharp
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide(); // 隐藏窗体（从任务栏消失）
        }
    }
```

**Step 3: 添加关闭按钮处理（重载 OnClosing）**

在 `MainWindow_StateChanged` 方法之后添加：

```csharp
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true; // 取消关闭操作
        WindowState = WindowState.Minimized; // 触发最小化
        Hide(); // 隐藏窗体
        base.OnClosing(e);
    }
```

**Step 4: 添加必要的命名空间（如需要）**

确保文件顶部已有以下 using：
```csharp
using System.ComponentModel; // 用于 CancelEventArgs
```

**Step 5: 编译检查**

Run: `dotnet build`
Expected: 编译成功

**Step 6: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: 实现窗体最小化时隐藏到托盘"
```

---

## Task 3: 实现托盘图标双击恢复功能

**Files:**
- Modify: `MainWindow.xaml.cs`

**Step 1: 添加双击事件处理器**

在 `MainWindow.xaml.cs` 中添加：

```csharp
    private void TrayIcon_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ShowWindow();
    }
```

**Step 2: 添加 ShowWindow 辅助方法**

```csharp
    private void ShowWindow()
    {
        Show(); // 显示窗体
        WindowState = WindowState.Normal; // 恢复窗体状态
        Activate(); // 激活窗体到前台
    }
```

**Step 3: 添加必要的命名空间（如需要）**

确保文件顶部已有：
```csharp
using System.Windows.Input; // 用于 MouseButtonEventArgs
```

**Step 4: 编译检查**

Run: `dotnet build`
Expected: 编译成功

**Step 5: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: 实现双击托盘图标恢复窗体"
```

---

## Task 4: 实现右键菜单功能

**Files:**
- Modify: `MainWindow.xaml.cs`

**Step 1: 添加"显示窗口"菜单项处理器**

```csharp
    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }
```

**Step 2: 添加"退出"菜单项处理器**

```csharp
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose(); // 清理托盘图标资源
        Application.Current.Shutdown();
    }
```

**Step 3: 修改关闭行为（移除 Cancel，直接退出）**

修改 `OnClosing` 方法，添加一个标志位来控制是真正退出还是最小化到托盘：

首先添加字段：
```csharp
public partial class MainWindow : HandyControl.Controls.Window
{
    private bool _isExiting = false; // 标记是否真正退出
```

然后修改 OnClosing：
```csharp
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            TrayIcon.Dispose(); // 真正退出时清理托盘图标
            base.OnClosing(e);
            return;
        }

        e.Cancel = true; // 取消关闭操作
        WindowState = WindowState.Minimized; // 触发最小化
        Hide(); // 隐藏窗体
        base.OnClosing(e);
    }
```

修改 Exit_Click：
```csharp
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true; // 设置退出标志
        Close(); // 调用关闭方法
    }
```

**Step 4: 编译检查**

Run: `dotnet build`
Expected: 编译成功

**Step 5: Commit**

```bash
git add MainWindow.xaml.cs
git commit -m "feat: 实现托盘右键菜单（显示窗口、退出）"
```

---

## Task 5: 功能测试

**Files:**
- 无需修改代码，手动测试

**Step 1: 运行应用程序**

Run: `dotnet run` 或启动调试

**Step 2: 测试窗体最小化**

- 点击窗体最小化按钮
- Expected: 窗体从任务栏消失，系统托盘显示图标

**Step 3: 测试双击恢复**

- 双击系统托盘图标
- Expected: 窗体恢复显示，出现在任务栏

**Step 4: 测试右键菜单**

- 右键点击托盘图标
- Expected: 显示菜单（显示窗口、退出）
- 点击"显示窗口" → 窗体恢复

**Step 5: 测试关闭按钮**

- 点击窗体关闭按钮（X）
- Expected: 窗体最小化到托盘，程序未退出

**Step 6: 测试退出功能**

- 右键托盘图标 → 点击"退出"
- Expected: 程序正常退出，托盘图标消失

**Step 7: Commit（如测试通过）**

```bash
git commit --allow-empty -m "test: 系统托盘功能测试通过"
```

---

## 完成检查清单

- [ ] MainWindow.xaml 中添加了 hc:NotifyIcon 控件
- [ ] MainWindow.xaml.cs 中实现了 StateChanged 事件处理
- [ ] MainWindow.xaml.cs 中重载了 OnClosing 方法
- [ ] 实现了 TrayIcon_MouseDoubleClick 事件处理器
- [ ] 实现了 ShowWindow 辅助方法
- [ ] 实现了 ShowWindow_Click 和 Exit_Click 菜单处理器
- [ ] 添加了 _isExiting 标志位控制退出行为
- [ ] 所有功能手动测试通过
- [ ] 代码已提交到 git

---

## 可能的后续优化（YAGNI - 暂不需要）

- 托盘图标气泡提示功能
- 最小化时显示气泡通知"程序已最小化到托盘"
- 托盘图标动画效果
- 配置选项：是否启用托盘功能
