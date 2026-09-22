@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title SelfSwitch 单文件编译
cd /d "%~dp0"

echo ============================================================
echo   SelfSwitch 窗口切换小工具 - 一键编译（单文件 EXE）
echo ============================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未找到 dotnet 命令。
    echo        请先安装 .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo [1/3] 当前 SDK 版本:
dotnet --version
echo.

echo [2/3] 开始发布 (self-contained + single-file + win-x64) ...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
if errorlevel 1 (
    echo.
    echo [失败] 编译出错，请检查上面的错误信息。
    pause
    exit /b 1
)

set "OUTDIR=%~dp0bin\Release\net8.0-windows\win-x64\publish"

echo.
echo [3/3] 复制默认 config.json 到输出目录 ...
if not exist "%OUTDIR%\config.json" (
    if exist "%~dp0config.json" (
        copy /y "%~dp0config.json" "%OUTDIR%\config.json" >nul
    ) else (
        echo {> "%OUTDIR%\config.json"
        echo   "ChromePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",>> "%OUTDIR%\config.json"
        echo   "TargetExePath": "C:\\Program Files\\FleetView\\FleetView.exe",>> "%OUTDIR%\config.json"
        echo   "WindowTitleKeyword": "",>> "%OUTDIR%\config.json"
        echo   "ButtonTextWhenChrome": "切到目标程序",>> "%OUTDIR%\config.json"
        echo   "ButtonTextWhenTarget": "切到浏览器",>> "%OUTDIR%\config.json"
        echo   "ButtonTextOther": "—",>> "%OUTDIR%\config.json"
        echo   "ButtonOpacity": 0.75,>> "%OUTDIR%\config.json"
        echo   "ButtonFontSize": 15,>> "%OUTDIR%\config.json"
        echo   "Anchor": "BottomRight",>> "%OUTDIR%\config.json"
        echo   "OffsetX": 12,>> "%OUTDIR%\config.json"
        echo   "OffsetY": 12,>> "%OUTDIR%\config.json"
        echo   "FreePosition": false,>> "%OUTDIR%\config.json"
        echo   "ChromeMode": "SendF11",>> "%OUTDIR%\config.json"
        echo   "TargetMode": "Maximize",>> "%OUTDIR%\config.json"
        echo   "ChromeRect": "0,0,50%%,100%%",>> "%OUTDIR%\config.json"
        echo   "TargetRect": "50%%,0,50%%,100%%",>> "%OUTDIR%\config.json"
        echo   "StrictForegroundCheck": true,>> "%OUTDIR%\config.json"
        echo   "IgnoreShellWindows": true,>> "%OUTDIR%\config.json"
        echo   "HotkeyEnabled": true,>> "%OUTDIR%\config.json"
        echo   "HotkeyText": "Ctrl+Alt+S",>> "%OUTDIR%\config.json"
        echo   "HideButtonWhenOther": true,>> "%OUTDIR%\config.json"
        echo   "Previous": "Minimize",>> "%OUTDIR%\config.json"
        echo   "AutoStart": true>> "%OUTDIR%\config.json"
        echo }>> "%OUTDIR%\config.json"
    )
)
if exist "%~dp0自启-任务计划程序-安装.bat" copy /y "%~dp0自启-任务计划程序-安装.bat" "%OUTDIR%\" >nul
if exist "%~dp0自启-任务计划程序-卸载.bat" copy /y "%~dp0自启-任务计划程序-卸载.bat" "%OUTDIR%\" >nul
if exist "%~dp0README.md" copy /y "%~dp0README.md" "%OUTDIR%\" >nul

echo.
echo ============================================================
echo   编译成功！
echo   单文件 EXE: %OUTDIR%\SelfSwitch.exe
echo   配置/日志 与 EXE 同目录: config.json / app.log
echo ============================================================
echo.
if exist "%OUTDIR%" start "" "%OUTDIR%"
pause
endlocal
