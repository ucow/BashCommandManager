# MSIX 打包脚本
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$Version = "1.0.0.0"
)

Write-Host "=== BashCommandManager MSIX 打包工具 ===" -ForegroundColor Cyan

# 设置路径
$ProjectDir = $PSScriptRoot
$PublishDir = "$ProjectDir\bin\Publish\MSIX"
$PackageDir = "$ProjectDir\bin\Publish\Package"
$OutputDir = "$ProjectDir\bin\Publish\Output"

# 清理旧文件
Write-Host "清理旧文件..." -ForegroundColor Yellow
Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $PackageDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# 发布应用
Write-Host "发布应用..." -ForegroundColor Yellow
dotnet publish "$ProjectDir\BashCommandManager.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "发布失败！" -ForegroundColor Red
    exit 1
}

# 复制打包文件
Write-Host "复制打包文件..." -ForegroundColor Yellow
$AppFilesDir = "$PackageDir\AppFiles"
New-Item -ItemType Directory -Path $AppFilesDir -Force | Out-Null

# 复制发布文件到AppFiles
Copy-Item -Path "$PublishDir\*" -Destination $AppFilesDir -Recurse -Force

# 创建MSIX布局目录
$MSIXLayoutDir = "$PackageDir\MSIXLayout"
New-Item -ItemType Directory -Path $MSIXLayoutDir -Force | Out-Null

# 复制应用文件到布局目录
Copy-Item -Path "$AppFilesDir\*" -Destination $MSIXLayoutDir -Recurse -Force

# 创建或更新 Package.appxmanifest
$ManifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <Identity
    Name="BashCommandManager"
    Publisher="CN=BashCommandManager"
    Version="$Version"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>BashCommandManager</DisplayName>
    <PublisherDisplayName>BashCommandManager</PublisherDisplayName>
    <Logo>Assets\app.ico</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>

  <Resources>
    <Resource Language="zh-cn" />
  </Resources>

  <Applications>
    <Application Id="App"
      Executable="BashCommandManager.exe"
      EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="BashCommandManager"
        Description="批处理命令管理工具"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\app.ico"
        Square44x44Logo="Assets\app.ico">
        <uap:DefaultTile Wide310x150Logo="Assets\app.ico" />
      </uap:VisualElements>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

$ManifestContent | Out-File -FilePath "$MSIXLayoutDir\AppxManifest.xml" -Encoding UTF8

# 创建 Assets 目录
$AssetsDir = "$MSIXLayoutDir\Assets"
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null
Copy-Item -Path "$ProjectDir\Assets\app.ico" -Destination $AssetsDir -Force

Write-Host "打包完成！布局目录: $MSIXLayoutDir" -ForegroundColor Green

# 尝试使用 MakeAppx 打包
$MakeAppx = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $MakeAppx) {
    $MakeAppx = Get-ChildItem -Path "${env:ProgramFiles}\Windows Kits\10\bin" -Filter "makeappx.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}

if ($MakeAppx) {
    Write-Host "找到 MakeAppx 工具，开始打包..." -ForegroundColor Yellow
    $MSIXPath = "$OutputDir\BashCommandManager_$Version`_x64.msix"

    & $MakeAppx.FullName pack /d "$MSIXLayoutDir" /p "$MSIXPath" /o

    if ($LASTEXITCODE -eq 0) {
        Write-Host "MSIX 包创建成功！" -ForegroundColor Green
        Write-Host "输出路径: $MSIXPath" -ForegroundColor Cyan

        # 尝试签名（如果有证书）
        Write-Host "注意：MSIX 包需要签名才能安装。你可以使用以下命令签名：" -ForegroundColor Yellow
        Write-Host "  signtool sign /fd SHA256 /a /f your_certificate.pfx /p your_password `"$MSIXPath`"" -ForegroundColor Gray
        Write-Host " 或者使用自签名证书测试安装：" -ForegroundColor Yellow
        Write-Host "  Add-AppxPackage -Path `"$MSIXPath`"" -ForegroundColor Gray
    } else {
        Write-Host "MSIX 打包失败，但布局文件已准备好，可以手动打包。" -ForegroundColor Red
    }
} else {
    Write-Host "未找到 MakeAppx 工具。MSIX 布局文件已准备好，位置：" -ForegroundColor Yellow
    Write-Host "  $MSIXLayoutDir" -ForegroundColor Cyan
    Write-Host "你可以安装 Windows SDK 后使用以下命令打包：" -ForegroundColor Yellow
    Write-Host "  makeappx pack /d `"$MSIXLayoutDir`" /p `"BashCommandManager.msix`"" -ForegroundColor Gray
}

Write-Host "`n打包流程完成！" -ForegroundColor Green
