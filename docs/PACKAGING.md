# BashCommandManager 打包指南

本文档说明如何打包 BashCommandManager 的发布版本。

---

## 目录

1. [打包概述](#打包概述)
2. [环境要求](#环境要求)
3. [打包步骤](#打包步骤)
4. [发布到 GitHub](#发布到-github)
5. [常见问题](#常见问题)

---

## 打包概述

每次发布包含两个版本：

| 版本 | 大小 | 说明 |
|------|------|------|
| **Full** | ~54MB | 包含 .NET 8 运行时，安装后即可运行 |
| **Lite** | ~7MB | 不包含 .NET 8 运行时，需系统预装 .NET 8 |

两个安装包都会把 `bcm.exe`（CLI）随附到 `{app}\cli`；安装时可选勾选「Add bcm CLI to PATH」（写 HKCU 的 Path，卸载不自动移除）。

---

## 环境要求

- .NET 8 SDK
- Inno Setup 6（用于创建安装包）
- Windows 系统

---

## 打包步骤

### 1. 更新版本号

编辑以下文件中的版本号：

```bash
# 更新 Full 版本安装脚本
Packaging/InnoSetup/BashCommandManager-Full.iss
# 修改: #define MyAppVersion "x.x.x"

# 更新 Lite 版本安装脚本
Packaging/InnoSetup/BashCommandManager-Lite.iss
# 修改: #define MyAppVersion "x.x.x"
```

### 2. 发布 Full 版本

```bash
# 清理旧文件
rm -rf Packaging/InnoSetup/Full/bin/*

# 发布（包含 .NET 8 运行时）
dotnet publish -c Release -r win-x64 -p:SelfContained=true -o Packaging/InnoSetup/Full/bin

# 编译安装包
"C:/Program Files (x86)/Inno Setup 6/ISCC.exe" Packaging/InnoSetup/BashCommandManager-Full.iss
```

### 3. 发布 Lite 版本

```bash
# 清理旧文件
rm -rf Packaging/InnoSetup/Lite/bin/*

# 发布（不包含 .NET 8 运行时）
dotnet publish -c Release -r win-x64 -p:SelfContained=false -o Packaging/InnoSetup/Lite/bin

# 编译安装包
"C:/Program Files (x86)/Inno Setup 6/ISCC.exe" Packaging/InnoSetup/BashCommandManager-Lite.iss
```

### 4. 发布 CLI（bcm.exe）

```bash
# Full（自包含单文件）
dotnet publish BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -o Packaging/InnoSetup/Full/bin/cli

# Lite（框架依赖单文件）
dotnet publish BashCommandManager.Cli/BashCommandManager.Cli.csproj -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o Packaging/InnoSetup/Lite/bin/cli
```

两个 `[Files]` 段已有 `recursesubdirs createallsubdirs`，`cli` 目录会随 `bin\*` 递归复制到 `{app}\cli`，无需改 `.iss`。

验证：

```bash
./Packaging/InnoSetup/Full/bin/cli/bcm.exe --version   # 1.3.0
```

可另发一个 zip 给无 GUI 的机器：

```powershell
Compress-Archive -Path Packaging\InnoSetup\Full\bin\cli\* -DestinationPath bin\Publish\Installer\BashCommandManager-Cli-v1.3.0-win-x64.zip -Force
```

### 5. 检查生成的安装包

安装包会生成在：
```
bin/Publish/Installer/
├── BashCommandManager-v{版本号}-Full-Setup.exe
└── BashCommandManager-v{版本号}-Lite-Setup.exe
```

---

## 发布到 GitHub

### 1. 提交代码

```bash
git add -A
git commit -m "release: v{版本号}"
git push origin master
```

### 2. 创建 GitHub Release

```bash
gh release create v{版本号} \
  bin/Publish/Installer/BashCommandManager-v{版本号}-Full-Setup.exe \
  bin/Publish/Installer/BashCommandManager-v{版本号}-Lite-Setup.exe \
  --title "v{版本号} - {发布标题}" \
  --notes "## 更新内容

### 新增功能
- xxx

### 修复
- xxx

## 下载

### BashCommandManager-v{版本号}-Full-Setup.exe (~54MB)
完整安装包，包含 .NET 8 运行时

### BashCommandManager-v{版本号}-Lite-Setup.exe (~7MB)
精简安装包，需要系统已安装 .NET 8
下载地址：https://dotnet.microsoft.com/download/dotnet/8.0

## 注意事项
- 数据库位于 `%LOCALAPPDATA%\BashCommandManager\data\app.db`；首次启动会把旧版安装目录下的 `data/app.db` **复制**过去（旧文件保留，可回滚）
- 可用环境变量 `BASHCOMMANDMANAGER_DB` 指向其他库（便携/回滚），或用 `bcm --db <path>` 单次覆盖
- 建议安装到非系统目录以避免权限问题"
```

---

## 常见问题

### Q: 为什么 Lite 版本和 Full 版本一样大？

**原因**：项目中的 `MSIX.pubxml` 设置了 `SelfContained=true`，会覆盖命令行参数。

**解决**：使用 `-p:SelfContained=false` 显式指定。

```bash
# 正确
dotnet publish -c Release -r win-x64 -p:SelfContained=false -o Packaging/InnoSetup/Lite/bin

# 错误（会被 pubxml 覆盖）
dotnet publish -c Release -r win-x64 --self-contained false -o Packaging/InnoSetup/Lite/bin
```

### Q: 如何验证 Lite 版本不包含运行时？

检查发布目录大小：
```bash
du -sh Packaging/InnoSetup/Full/bin/   # 应该 ~190MB
du -sh Packaging/InnoSetup/Lite/bin/   # 应该 ~29MB
```

### Q: 安装包生成在哪里？

默认输出目录：
```
bin/Publish/Installer/
```

可在 `.iss` 脚本中修改 `OutputDir` 配置。

---

## 快速检查清单

发布前确认：

- [ ] 更新了 `.iss` 脚本中的版本号
- [ ] Full 版本使用 `-p:SelfContained=true`
- [ ] Lite 版本使用 `-p:SelfContained=false`
- [ ] 验证 Lite 版本目录大小约 29MB
- [ ] 验证 Full 版本目录大小约 190MB
- [ ] 两个安装包都生成成功
- [ ] 提交代码并推送
- [ ] 创建 GitHub Release
