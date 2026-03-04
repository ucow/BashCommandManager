# BashCommandManager 安装包制作说明

## 两种安装包

### 1. Full 完整版 (`BashCommandManager-v1.0.0-Full-Setup.exe`)
- **大小**：约 150 MB
- **包含内容**：应用程序 + .NET 8 运行时
- **适用场景**：
  - 不确定用户是否已安装 .NET 8
  - 需要在无网络环境下安装
  - 追求开箱即用的体验

### 2. Lite 精简版 (`BashCommandManager-v1.0.0-Lite-Setup.exe`)
- **大小**：约 5 MB
- **包含内容**：仅应用程序（需用户已安装 .NET 8）
- **适用场景**：
  - 用户已安装 .NET 8
  - 追求小体积安装包
  - 网络环境良好，必要时可下载运行时

## 前置要求

1. **.NET 8 SDK** - 用于构建项目
2. **Inno Setup 6** - 用于制作安装包
   - 下载地址：https://jrsoftware.org/isdl.php#stable

## 制作安装包

### 方法一：使用批处理脚本（推荐）

1. 确保已安装 Inno Setup 6
2. 双击运行 `Build-Installers.bat`
3. 等待构建完成
4. 安装包将生成在 `bin\Publish\Installer\` 目录

### 方法二：手动构建

#### 完整版
```bash
# 1. 发布完整版
dotnet publish BashCommandManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o Packaging/InnoSetup/Full/bin

# 2. 使用 Inno Setup 编译
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Packaging/InnoSetup/BashCommandManager-Full.iss
```

#### 精简版
```bash
# 1. 发布精简版
dotnet publish BashCommandManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o Packaging/InnoSetup/Lite/bin

# 2. 使用 Inno Setup 编译
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Packaging/InnoSetup/BashCommandManager-Lite.iss
```

## 安装包功能

- ✅ 自动创建开始菜单快捷方式
- ✅ 可选创建桌面快捷方式
- ✅ 集成卸载程序
- ✅ 显示许可协议
- ✅ 安装完成后可选择立即运行
- ✅ 精简版自动检测 .NET 8 运行时

## 上传到 GitHub Release

构建完成后，将以下文件上传到 Release：

```
bin/Publish/Installer/
├── BashCommandManager-v1.0.0-Full-Setup.exe  (完整版)
└── BashCommandManager-v1.0.0-Lite-Setup.exe  (精简版)
```

## 版本更新

更新版本时，修改以下文件中的版本号：

1. `BashCommandManager-Full.iss` - 第2行的 `#define MyAppVersion`
2. `BashCommandManager-Lite.iss` - 第2行的 `#define MyAppVersion`
