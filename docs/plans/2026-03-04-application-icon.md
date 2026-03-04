# 应用程序图标实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 PNG 图标转换为 ICO 格式并配置 WPF 应用程序在所有位置显示统一图标

**Architecture:** 使用 ImageMagick 或在线工具将 PNG 转换为多尺寸 ICO 文件，配置 .csproj 设置应用程序图标，并在 MainWindow.xaml 中设置窗口图标

**Tech Stack:** WPF, .NET 8, HandyControl

---

### Task 1: 创建 Assets 目录并转换图标

**Files:**
- Create: `Assets/app.ico`
- Source: `D:\EdgeDownload\1772627923356-019cb8db-583c-785c-9b09-40014716a5c4.png`

**Step 1: 创建 Assets 目录**

Run:
```bash
mkdir -p Assets
```

**Step 2: 转换 PNG 为 ICO**

方式 A - 使用 ImageMagick (如果已安装):
```bash
magick convert "D:\EdgeDownload\1772627923356-019cb8db-583c-785c-9b09-40014716a5c4.png" -resize 256x256 -resize 48x48 -resize 32x32 -resize 16x16 Assets/app.ico
```

方式 B - 使用 PowerShell 和 .NET (推荐，无需额外安装):
```powershell
Add-Type -AssemblyName System.Drawing
$pngPath = "D:\EdgeDownload\1772627923356-019cb8db-583c-785c-9b09-40014716a5c4.png"
$icoPath = "Assets\app.ico"
$bitmap = [System.Drawing.Bitmap]::FromFile($pngPath)
$bitmap.SetResolution(96, 96)
$iconSizes = @(16, 32, 48, 256)
$memoryStream = New-Object System.IO.MemoryStream
$binaryWriter = New-Object System.IO.BinaryWriter($memoryStream)
# ICO Header
$binaryWriter.Write([byte]0)
$binaryWriter.Write([byte]0)
$binaryWriter.Write([short]1)
$binaryWriter.Write([short]$iconSizes.Length)
$imageOffset = 6 + ($iconSizes.Length * 16)
$imageStreams = @()
foreach ($size in $iconSizes) {
    $resized = New-Object System.Drawing.Bitmap($bitmap, $size, $size)
    $imageStream = New-Object System.IO.MemoryStream
    $resized.Save($imageStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageStreams += $imageStream
    # Directory entry
    $binaryWriter.Write([byte]$size)
    $binaryWriter.Write([byte]$size)
    $binaryWriter.Write([byte]0)
    $binaryWriter.Write([byte]0)
    $binaryWriter.Write([short]1)
    $binaryWriter.Write([short]32)
    $binaryWriter.Write([int]$imageStream.Length)
    $binaryWriter.Write([int]$imageOffset)
    $imageOffset += $imageStream.Length
}
foreach ($stream in $imageStreams) {
    $stream.Position = 0
    $stream.CopyTo($memoryStream)
    $stream.Dispose()
}
$binaryWriter.Flush()
[IO.File]::WriteAllBytes($icoPath, $memoryStream.ToArray())
$binaryWriter.Dispose()
$memoryStream.Dispose()
$bitmap.Dispose()
Write-Host "Created: $icoPath"
```

Expected: `Assets/app.ico` 文件被创建

**Step 3: 验证图标文件**

Run:
```bash
ls -la Assets/
```
Expected: 显示 `app.ico` 文件，大小 > 0 字节

**Step 4: Commit**

```bash
git add Assets/app.ico
git commit -m "assets: add application icon file"
```

---

### Task 2: 配置项目使用 ICO 作为应用程序图标

**Files:**
- Modify: `BashCommandManager.csproj`

**Step 1: 读取当前项目文件**

Read: `BashCommandManager.csproj`

**Step 2: 添加应用程序图标配置**

在 `<PropertyGroup>` 中添加：
```xml
<ApplicationIcon>Assets\app.ico</ApplicationIcon>
```

**Step 3: 添加图标资源到项目**

在 `<ItemGroup>` 中添加（如果不存在）：
```xml
<ItemGroup>
  <Resource Include="Assets\app.ico" />
</ItemGroup>
```

**Step 4: 验证修改后的项目文件**

完整示例：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HandyControl" Version="3.5.1" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageReference Include="Dapper" Version="2.1.35" />
    <PackageReference Include="System.Data.SQLite.Core" Version="1.0.115.5" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <Resource Include="Assets\app.ico" />
  </ItemGroup>
</Project>
```

**Step 5: Commit**

```bash
git add BashCommandManager.csproj
git commit -m "build: configure application icon in project file"
```

---

### Task 3: 设置主窗口图标

**Files:**
- Read: `MainWindow.xaml` (确定 Window 属性位置)
- Modify: `MainWindow.xaml`

**Step 1: 读取主窗口 XAML**

Read: `MainWindow.xaml`

**Step 2: 在 Window 元素上添加 Icon 属性**

找到 `<Window` 开始标签，添加：
```xml
Icon="Assets/app.ico"
```

例如：
```xml
<Window x:Class="BashCommandManager.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Icon="Assets/app.ico"
        ...>
```

**Step 3: Commit**

```bash
git add MainWindow.xaml
git commit -m "ui: set window icon"
```

---

### Task 4: 构建并验证

**Step 1: 清理并构建项目**

Run:
```bash
dotnet clean
dotnet build
```

Expected: 构建成功，无错误

**Step 2: 验证输出目录中的图标**

Run:
```bash
ls -la bin/Debug/net8.0-windows/Assets/
```

Expected: `app.ico` 存在于输出目录

**Step 3: 验证 .exe 文件图标**

Run:
```bash
ls -la bin/Debug/net8.0-windows/*.exe
```

Expected: `BashCommandManager.exe` 存在

**Step 4: 运行应用程序验证**

Run:
```bash
dotnet run
```

手动验证：
1. 窗口标题栏显示图标
2. 任务栏显示图标
3. Alt+Tab 切换时显示图标

**Step 5: 最终 Commit**

```bash
git status
git add -A
git commit -m "feat: complete application icon setup"
```

---

## 验证清单

- [ ] `Assets/app.ico` 文件存在
- [ ] `BashCommandManager.csproj` 包含 `<ApplicationIcon>`
- [ ] `MainWindow.xaml` 包含 `Icon` 属性
- [ ] 构建成功
- [ ] 窗口标题栏显示图标
- [ ] 任务栏显示图标
- [ ] .exe 文件图标正确
