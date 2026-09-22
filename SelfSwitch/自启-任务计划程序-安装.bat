@echo off
chcp 65001 >nul
setlocal
title 安装管理员权限静默自启（任务计划程序）

set "SW_EXE=%~dp0SelfSwitch.exe"
if not exist "%SW_EXE%" (
    echo [错误] 本批处理所在目录没有找到 SelfSwitch.exe
    echo        请把本文件放到 SelfSwitch.exe 同目录再运行。
    echo.
    pause
    exit /b 1
)

net session >nul 2>&1
if errorlevel 1 (
    echo 需要管理员权限，正在请求提权 ...
    powershell -NoProfile -Command "Start-Process -FilePath $env:ComSpec -ArgumentList '/c','\"%~f0\"' -Verb RunAs"
    exit /b
)

echo ============================================================
echo   安装「登录时以管理员权限静默启动」计划任务
echo   EXE: %SW_EXE%
echo ============================================================
echo.

powershell -NoProfile -Command ^
  "$exe = $env:SW_EXE; $id = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name;" ^
  "$a = New-ScheduledTaskAction -Execute $exe;" ^
  "$t = New-ScheduledTaskTrigger -AtLogOn -User $id;" ^
  "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero);" ^
  "$p = New-ScheduledTaskPrincipal -UserId $id -LogonType Interactive -RunLevel Highest;" ^
  "Register-ScheduledTask -TaskName 'SelfSwitch' -Action $a -Trigger $t -Settings $s -Principal $p -Force | Out-Null;" ^
  "if ($?) { Write-Host '[OK] 计划任务 SelfSwitch 已创建（最高权限，登录时静默启动，不弹 UAC）' }"

echo.
echo 提示：
echo   1) 计划任务的程序名固定指向 %SW_EXE%，请不要移动 EXE；移动后重新运行本脚本即可。
echo   2) 使用本方式自启后，请在 SelfSwitch 配置界面里【取消勾选「开机自启」】，
echo      避免「注册表 Run + 计划任务」同时启动（本工具有单实例保护，不会重复，但会多一次启动动作）。
echo   3) 卸载：运行「自启-任务计划程序-卸载.bat」。
echo.
pause
endlocal
