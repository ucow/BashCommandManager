# WPF 单文件 EXE 打包指南

本文档说明如何将 BashCommandManager WPF 应用打包成单个独立的 exe 文件。

---

## 目录

1. [前置要求](#前置要求)
2. [项目配置](#项目配置)
3. [关键注意事项](#关键注意事项)
4. [打包命令](#打包命令)
5. [发布到 GitHub](#发布到-github)
6. [故障排除](#故障排除)

---

## 前置要求

- .NET 8 SDK 或更高版本
- Windows 系统（WPF 仅支持 Windows）

---

## 项目配置

### 1. 修改 `.csproj` 文件

在 `.csproj` 文件中添加以下配置：

```xml
<PropertyGroup>
  <!-- 其他配置保持不变 -->

  <!-- 关键：支持单文件发布时提取所有内容 -->
  <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
</PropertyGroup>
```

### 2. 资源文件配置

对于需要嵌入的图标等资源，确保在 `.csproj` 中同时标记为 `Content` 和 `Resource`：

```xml
<ItemGroup>
  <!-- Content：复制到输出目录 -->
  <Content Include="Assets\app.ico">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>

  <!-- Resource：嵌入到程序集中（单文件必需） -->
  <Resource Include="Assets\app.ico" />
</ItemGroup>
```

### 3. XAML 中移除外部资源引用

**问题**：在 XAML 中直接引用外部资源文件（如 `Icon="Assets/app.ico"`）在单文件发布时会导致运行时错误。

**解决方案**：

1. **从 XAML 中移除资源引用**：

```xml
<!-- 修改前 -->
<hc:Window x:Class="BashCommandManager.MainWindow"
           Title="批处理命令管理器"
           Icon="Assets/app.ico"  <!-- 移除这一行 -->
           Height="600" Width="900">

<!-- 修改后 -->
<hc:Window x:Class="BashCommandManager.MainWindow"
           Title="批处理命令管理器"
           Height="600" Width="900">
```

2. **在代码后台动态加载资源**（可选）：

```csharp
public MainWindow(MainViewModel viewModel)
{
    InitializeComponent();
    DataContext = viewModel;

    // 动态加载图标
    LoadIcon();
}

private void LoadIcon()
{
    try
    {
        // 使用 Pack URI 加载图标（支持单文件发布）
        var iconUri = new Uri("pack://application:,,,/Assets/app.ico");
        Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
    }
    catch
    {
        // 图标加载失败不影响应用运行
    }
}
```

---

## 关键注意事项

### 1. 单文件发布的限制

- **SQLite 问题**：SQLite 等包含原生 DLL 的 NuGet 包在单文件发布时需要特殊处理
- **外部资源**：XAML 中引用的外部资源路径在单文件中可能失效
- **临时文件**：单文件发布会在运行时解压到临时目录

### 2. 两个版本的区别

| 版本 | 参数 | 大小 | 说明 |
|------|------|------|------|
| **Full** | `--self-contained true` | ~77MB | 包含 .NET 运行时，无需预装 |
| **Lite** | `--self-contained false` | ~29MB | 不包含运行时，需预装 .NET 8 |

### 3. 不支持压缩的情况

当 `--self-contained false` 时，不能启用 `EnableCompressionInSingleFile`，否则报错：

```
NETSDK1176: 仅在发布独立应用程序时才支持在单个文件捆绑包中进行压缩
```

---

## 打包命令

### 完整打包流程

```bash
# 1. 清理旧发布文件
rm -rf bin/Publish/Release

# 2. 发布 Full 版本（包含 .NET 8 运行时）
dotnet publish -c Release -r win-x64 \
  -p:SelfContained=true \
  -p:PublishSingleFile=true \
  -o bin/Publish/Release/Full

# 3. 发布 Lite 版本（不包含运行时）
dotnet publish -c Release -r win-x64 \
  -p:SelfContained=false \
  -p:PublishSingleFile=true \
  -o bin/Publish/Release/Lite

# 4. 复制并重命名
cp bin/Publish/Release/Full/BashCommandManager.exe \
   bin/Publish/Release/BashCommandManager-v{VERSION}-Full.exe

cp bin/Publish/Release/Lite/BashCommandManager.exe \
   bin/Publish/Release/BashCommandManager-v{VERSION}-Lite.exe
```

### 参数说明

| 参数 | 说明 |
|------|------|
| `-c Release` | Release 配置，启用优化 |
| `-r win-x64` | 目标运行时：Windows x64 |
| `-p:SelfContained=true/false` | 是否包含 .NET 运行时 |
| `-p:PublishSingleFile=true` | 打包成单个文件 |
| `-o <path>` | 输出目录 |

---

## 发布到 GitHub

### 1. 提交代码

```bash
git add -A
git commit -m "release: v{VERSION}"
git push origin master
```

### 2. 创建 Tag

```bash
git tag v{VERSION}
git push origin v{VERSION}
```

### 3. 创建 Release

使用 GitHub CLI：

```bash
gh release create v{VERSION} \
  bin/Publish/Release/BashCommandManager-v{VERSION}-Full.exe \
  bin/Publish/Release/BashCommandManager-v{VERSION}-Lite.exe \
  --title "v{VERSION} - {发布标题}" \
  --notes "## 更新内容

### 新增功能
- xxx

### 修复
- xxx

## 下载说明

### Full 版本 ({FullSize}MB)
包含 .NET 8 运行时，适合没有安装 .NET 8 的系统

### Lite 版本 ({LiteSize}MB)
不包含 .NET 8 运行时，需要系统已安装 .NET 8 桌面运行时
下载地址：https://dotnet.microsoft.com/download/dotnet/8.0"
```

---

## 故障排除

### 问题1：运行时提示缺少 DLL

**原因**：某些 NuGet 包包含原生 DLL，单文件发布时可能丢失

**解决**：
- 检查 `.csproj` 中的 `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>`
- 确保 `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`

### 问题2：资源文件加载失败（如图标）

**错误信息**：
```
System.Windows.Markup.XamlParseException: 在类型 System.Windows.Baml2006.TypeConverterMarkupExtension 上提供值时引发了异常
```

**原因**：XAML 中直接引用的外部资源在单文件中路径失效

**解决**：
1. 从 XAML 中移除 `Icon` 等外部资源引用
2. 在代码中使用 Pack URI 动态加载
3. 将资源标记为 `<Resource Include="..." />`

### 问题3：应用启动后立即退出

**原因**：可能是单文件解压问题或依赖缺失

**解决**：
- 检查事件查看器中的 .NET Runtime 错误
- 尝试发布非单文件版本测试是否正常
- 确保所有依赖包支持单文件发布

---

## 参考文档

- [.NET 单文件部署](https://docs.microsoft.com/zh-cn/dotnet/core/deploying/single-file)
- [WPF 应用程序打包](https://docs.microsoft.com/zh-cn/dotnet/desktop/wpf/app-development/packaging-wpf-applications)
- [GitHub CLI 发布管理](https://cli.github.com/manual/gh_release_create)

---

## 快速检查清单

发布前确认以下事项：

- [ ] `.csproj` 包含 `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>`
- [ ] 资源文件同时标记为 `Content` 和 `Resource`
- [ ] XAML 中没有直接引用外部资源路径
- [ ] 代码中使用 Pack URI 加载资源
- [ ] 测试 Full 版本在无 .NET 环境的机器上运行
- [ ] 测试 Lite 版本在有 .NET 环境的机器上运行
- [ ] 单例启动模式正常工作（如果有此功能）
