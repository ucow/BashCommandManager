<#
.SYNOPSIS
    BashCommandManager 全局快捷键 / 托盘隐藏行为的自动化冒烟脚本（Issue #8 回归）。

.DESCRIPTION
    启动 Release 版应用，模拟真实用户操作，验证：
      1) 窗口可见时按热键 -> 隐藏到托盘
      2) 托盘隐藏时按热键 -> 显示并恢复
      3) 点击关闭按钮（WM_CLOSE）-> 隐藏到托盘（不退出进程）
      4) 关闭到托盘后按热键 -> 仍能显示（Issue #8 的核心回归）

    本脚本只操作 BashCommandManager 进程与临时数据库（应用目录 data/app.db），不修改用户配置；
    热键组合从 %LOCALAPPDATA%\BashCommandManager\settings.json 读取（缺省 Ctrl+Alt+B）。

.PARAMETER Exe
    BashCommandManager.exe 路径（默认 Release 输出）。

.PARAMETER StepDelayMs
    每次按键/消息后的等待毫秒数。

.EXAMPLE
    pwsh -NoProfile -File scripts\verify-hotkey-tray.ps1
#>
[CmdletBinding()]
param(
    [string]$Exe = "$PSScriptRoot\..\bin\Release\net8.0-windows10.0.19041.0\win-x64\BashCommandManager.exe",
    [int]$StepDelayMs = 800
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationFramework -ErrorAction SilentlyContinue

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeProbe
{
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public const uint WM_CLOSE = 0x0010;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public static void KeyDown(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); }
    public static void KeyUp(byte vk) { keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }
}
"@

function Write-Step([string]$Text) { Write-Host "==> $Text" -ForegroundColor Cyan }
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "断言失败: $Message" }
    Write-Host "    OK  $Message" -ForegroundColor Green
}

function Get-HotkeySpec {
    $settingsPath = Join-Path $env:LOCALAPPDATA 'BashCommandManager\settings.json'
    $key = 45      # WPF Key.B
    $mods = 3      # Alt | Control
    if (Test-Path $settingsPath) {
        try {
            $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
            $node = if ($json.PSObject.Properties.Name -contains 'Hotkey') { $json.Hotkey } else { $json }
            if ($null -ne $node.Key) { $key = [int]$node.Key }
            if ($null -ne $node.Modifiers) { $mods = [int]$node.Modifiers }
        } catch {
            Write-Warning "settings.json 解析失败，使用默认 Ctrl+Alt+B：$($_.Exception.Message)"
        }
    }
    return [pscustomobject]@{ Key = $key; Modifiers = $mods }
}

function Convert-KeyToVirtualKey([int]$wpfKey) {
    # 优先用 WPF 的 KeyInterop，保证与 GUI 注册时使用的换算完全一致
    if ($null -ne ('System.Windows.Input.KeyInterop' -as [type])) {
        $vk = [System.Windows.Input.KeyInterop]::VirtualKeyFromKey([System.Windows.Input.Key]$wpfKey)
        if ($vk -ne 0) { return [int]$vk }
    }
    if ($wpfKey -ge 44 -and $wpfKey -le 69) { return 0x41 + ($wpfKey - 44) }   # A..Z
    if ($wpfKey -ge 34 -and $wpfKey -le 43) { return 0x30 + ($wpfKey - 34) }   # D0..D9
    if ($wpfKey -ge 90 -and $wpfKey -le 101) { return 0x70 + ($wpfKey - 90) }  # F1..F12
    throw "无法把 WPF Key=$wpfKey 换算为虚拟键码"
}

function Send-Hotkey([pscustomobject]$Spec) {
    $vk = Convert-KeyToVirtualKey $Spec.Key
    $modifierVks = @()
    if ($Spec.Modifiers -band 1) { $modifierVks += 0x12 }  # Alt
    if ($Spec.Modifiers -band 2) { $modifierVks += 0x11 }  # Control
    if ($Spec.Modifiers -band 4) { $modifierVks += 0x10 }  # Shift
    if ($Spec.Modifiers -band 8) { $modifierVks += 0x5B }  # Left Win
    foreach ($m in $modifierVks) { [NativeProbe]::KeyDown([byte]$m) }
    [NativeProbe]::KeyDown([byte]$vk); Start-Sleep -Milliseconds 60; [NativeProbe]::KeyUp([byte]$vk)
    foreach ($m in ($modifierVks | Sort-Object -Descending)) { [NativeProbe]::KeyUp([byte]$m) }
    Start-Sleep -Milliseconds $StepDelayMs
}

function Wait-WindowHandle([System.Diagnostics.Process]$Process, [int]$TimeoutMs = 20000) {
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) { throw "进程在窗口出现前退出，ExitCode=$($Process.ExitCode)" }
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) { return $Process.MainWindowHandle }
        Start-Sleep -Milliseconds 200
    }
    throw "等待主窗口超时（${TimeoutMs}ms）"
}

function Force-Foreground([IntPtr]$Handle) {
    [void][NativeProbe]::ShowWindow($Handle, 9)   # SW_RESTORE
    [void][NativeProbe]::SetForegroundWindow($Handle)
    Start-Sleep -Milliseconds 400
}

if (-not (Test-Path $Exe)) { throw "找不到可执行文件: $Exe（请先 dotnet build -c Release）" }
$Exe = (Resolve-Path $Exe).Path

Write-Step "清理残留进程"
Get-Process -Name 'BashCommandManager' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$spec = Get-HotkeySpec
Write-Step ("热键: Key={0} Modifiers={1}（{2}）" -f $spec.Key, $spec.Modifiers, (Convert-KeyToVirtualKey $spec.Key))

Write-Step "启动应用"
$proc = Start-Process -FilePath $Exe -PassThru
try {
    $hwnd = Wait-WindowHandle $proc

    Write-Step "步骤 1：窗口可见时按热键，应隐藏"
    Assert-True ([NativeProbe]::IsWindowVisible($hwnd)) "启动后主窗口可见"
    Force-Foreground $hwnd
    Send-Hotkey $spec
    Assert-True (-not [NativeProbe]::IsWindowVisible($hwnd)) "按热键后主窗口隐藏"

    Write-Step "步骤 2：托盘隐藏时按热键，应重新显示"
    Send-Hotkey $spec
    Assert-True ([NativeProbe]::IsWindowVisible($hwnd)) "再次按键后主窗口重新显示"

    Write-Step "步骤 3：发送 WM_CLOSE（等价点击关闭按钮），应隐藏到托盘且进程存活"
    [void][NativeProbe]::PostMessage($hwnd, [NativeProbe]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds $StepDelayMs
    Assert-True (-not [NativeProbe]::IsWindowVisible($hwnd)) "关闭后主窗口隐藏到托盘"
    Assert-True (-not $proc.HasExited) "进程未退出"

    Write-Step "步骤 4（Issue #8 核心回归）：关闭到托盘后按热键，应显示并激活"
    Send-Hotkey $spec
    Assert-True ([NativeProbe]::IsWindowVisible($hwnd)) "关闭到托盘后热键仍能唤出窗口"

    Write-Step "步骤 5（参考）：再次按热键，前台时应收起"
    Force-Foreground $hwnd
    Send-Hotkey $spec
    if ([NativeProbe]::IsWindowVisible($hwnd)) {
        Write-Warning "窗口仍可见（可能是前台锁导致 SetForegroundWindow 失败），本条为参考项，不计入失败"
    } else {
        Write-Host "    OK  前台时按热键隐藏到托盘" -ForegroundColor Green
    }

    Write-Host "`n全部断言通过：热键在托盘隐藏/关闭到托盘场景下均有效。" -ForegroundColor Green
}
finally {
    Write-Step "清理：结束应用进程"
    if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
}
