---
name: packaging-release
description: Use when user mentions 打包, 生成安装包, release installer, 或需要构建 BashCommandManager 的 Windows 安装程序
---

# BashCommandManager 打包

## Overview

指导如何打包 BashCommandManager 为 Windows 安装程序。包含两个版本：

| 版本 | 大小 | 说明 |
|------|------|------|
| **Full** | ~54MB | 包含 .NET 8 运行时，安装后即可运行 |
| **Lite** | ~7MB | 不包含 .NET 8 运行时，需系统预装 .NET 8 |

## When to Use

- 用户说"打包"、"生成安装包"
- 构建安装程序 (installer/setup.exe)

## Quick Reference

### 环境要求

- .NET 8 SDK
- Inno Setup 6
- Windows 系统

### 关键文件

```
Packaging/InnoSetup/
├── BashCommandManager-Full.iss    # Full 版本安装脚本
├── BashCommandManager-Lite.iss    # Lite 版本安装脚本
├── Full/bin/                      # Full 版本发布输出
└── Lite/bin/                      # Lite 版本发布输出
```

## 打包步骤

### 1. 更新版本号

编辑两个 `.iss` 文件中的版本号：

```pascal
#define MyAppVersion "x.x.x"
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

### 4. 检查生成的安装包

```
bin/Publish/Installer/
├── BashCommandManager-v{版本号}-Full-Setup.exe
└── BashCommandManager-v{版本号}-Lite-Setup.exe
```

## Common Mistakes

### Lite 版本和 Full 版本一样大？

**原因**：`MSIX.pubxml` 设置了 `SelfContained=true`，会覆盖命令行参数。

**解决**：使用 `-p:SelfContained=false` 显式指定。

```bash
# 正确
dotnet publish -c Release -r win-x64 -p:SelfContained=false -o Packaging/InnoSetup/Lite/bin

# 错误（会被 pubxml 覆盖）
dotnet publish -c Release -r win-x64 --self-contained false -o Packaging/InnoSetup/Lite/bin
```

### 如何验证 Lite 版本不包含运行时？

检查发布目录大小：

```bash
du -sh Packaging/InnoSetup/Full/bin/   # 应该 ~190MB
du -sh Packaging/InnoSetup/Lite/bin/   # 应该 ~29MB
```

## 打包检查清单

- [ ] 更新了 `.iss` 脚本中的版本号
- [ ] Full 版本使用 `-p:SelfContained=true`
- [ ] Lite 版本使用 `-p:SelfContained=false`
- [ ] 验证 Lite 版本目录大小约 29MB
- [ ] 验证 Full 版本目录大小约 190MB
- [ ] 两个安装包都生成成功
