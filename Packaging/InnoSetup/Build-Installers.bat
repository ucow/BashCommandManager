@echo off
chcp 65001 >nul
echo ==========================================
echo BashCommandManager 安装包生成工具
echo ==========================================
echo.

REM 检查 Inno Setup 是否安装
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
) else if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"
) else (
    echo [错误] 未找到 Inno Setup。
    echo.
    echo 请先下载并安装 Inno Setup：
    echo https://jrsoftware.org/isdl.php#stable
    echo.
    pause
    exit /b 1
)

echo [1/4] 发布完整版（带 .NET 运行时）...
dotnet publish ..\..\BashCommandManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o Full\bin
if errorlevel 1 goto :error

echo [2/4] 发布精简版（不带 .NET 运行时）...
dotnet publish ..\..\BashCommandManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o Lite\bin
if errorlevel 1 goto :error

echo [3/4] 构建完整版安装包...
%ISCC% BashCommandManager-Full.iss
if errorlevel 1 goto :error

echo [4/4] 构建精简版安装包...
%ISCC% BashCommandManager-Lite.iss
if errorlevel 1 goto :error

echo.
echo ==========================================
echo [成功] 安装包生成完成！
echo ==========================================
echo.
echo 输出目录：..\..\bin\Publish\Installer\
echo.
echo 生成的文件：
dir /b "..\..\bin\Publish\Installer\*.exe"
echo.
pause
exit /b 0

:error
echo.
echo [错误] 构建过程中出现错误！
pause
exit /b 1
