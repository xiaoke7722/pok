@echo off
chcp 65001 >nul
setlocal
title 卸载管理员权限静默自启（任务计划程序）

net session >nul 2>&1
if errorlevel 1 (
    echo 需要管理员权限，正在请求提权 ...
    powershell -NoProfile -Command "Start-Process -FilePath $env:ComSpec -ArgumentList '/c','\"%~f0\"' -Verb RunAs"
    exit /b
)

echo 正在删除计划任务 SelfSwitch ...
powershell -NoProfile -Command "Unregister-ScheduledTask -TaskName 'SelfSwitch' -Confirm:$false -ErrorAction SilentlyContinue; Write-Host '[OK] 已删除（若原本不存在则忽略）'"

echo.
echo 提示：注册表 Run 自启项由程序自己管理，如需关闭请在配置界面取消勾选「开机自启」。
echo.
pause
endlocal
