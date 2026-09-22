@echo off
setlocal
chcp 65001 >nul
title SelfSwitch 小体积版编译（框架依赖，约 210 KB）
cd /d "%~dp0"

echo ============================================================
echo   SelfSwitch 编译 -【小体积版】框架依赖单文件 EXE
echo ============================================================
echo.
echo 产物约 210 KB（自包含版是 66 MB），下载/传输快 300 倍。
echo.
echo 【前提】运行本 EXE 的机器必须已安装 .NET 8 **桌面**运行时：
echo        - 装过 .NET 8 SDK 的机器自带，无需额外安装
echo        - 只装了 Runtime 的机器需要 Windows Desktop Runtime（x64）
echo        检查命令：dotnet --list-runtimes
echo        输出里要有：Microsoft.WindowsDesktop.App 8.0.x
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未找到 dotnet 命令，请先安装 .NET 8 SDK。
    pause
    exit /b 1
)

dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=false
if errorlevel 1 (
    echo.
    echo [失败] 编译出错。
    pause
    exit /b 1
)

set "OUTDIR=%~dp0bin\Release\net8.0-windows\win-x64\publish"
echo.
echo ============================================================
echo   编译成功！
echo   小体积 EXE: %OUTDIR%\SelfSwitch.exe
echo   （同目录的 config.json / app.log 行为与自包含版完全一致）
echo ============================================================
echo.
pause
endlocal
