# 应用程序图标设计方案

## 概述
为 BashCommandManager WPF 应用程序设置统一图标，覆盖所有显示位置：窗口标题栏、任务栏和 .exe 文件图标。

## 图标来源
- 源文件: `D:\EdgeDownload\1772627923356-019cb8db-583c-785c-9b09-40014716a5c4.png`
- 目标格式: ICO (多尺寸)

## 实现方案

### 1. 图标转换
将 PNG 图片转换为 ICO 格式，包含以下尺寸：
- 16x16 - 用于窗口标题栏、任务栏小图标
- 32x32 - 用于任务栏、资源管理器中等图标
- 48x48 - 用于资源管理器大图标
- 256x256 - 用于高 DPI 显示、资源管理器超大图标

输出位置: `Assets/app.ico`

### 2. 项目配置
在 `BashCommandManager.csproj` 中添加：
```xml
<ApplicationIcon>Assets/app.ico</ApplicationIcon>
```

### 3. 窗口图标
在 `MainWindow.xaml` 中设置：
```xml
Icon="Assets/app.ico"
```

### 4. 资源打包
确保 ICO 文件：
- 设置为 `Resource` 或 `Content` 生成操作
- 复制到输出目录

## 预期效果
- [x] 窗口标题栏显示图标
- [x] 任务栏显示图标
- [x] .exe 文件在资源管理器显示图标

## 设计日期
2026-03-04
