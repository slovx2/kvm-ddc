@echo off
chcp 65001 >nul
rem 编译并安装/更新 ddcswitch 服务（需要管理员权限）
setlocal
set "DEST=C:\Program Files\ddcswitch"

net session >nul 2>&1
if errorlevel 1 (
    echo 请用管理员身份运行
    exit /b 1
)

call "%~dp0build.cmd" || exit /b 1

sc query ddcswitch >nul 2>&1
if not errorlevel 1 (
    echo 停止旧服务...
    sc stop ddcswitch >nul 2>&1
    ping -n 4 127.0.0.1 >nul
)

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0bin\ddcswitch.exe" "%DEST%\ddcswitch.exe" >nul || exit /b 1

sc query ddcswitch >nul 2>&1
if errorlevel 1 (
    sc create ddcswitch binPath= "\"%DEST%\ddcswitch.exe\" service" start= auto DisplayName= "DDC Input Switch" >nul || exit /b 1
) else (
    sc config ddcswitch binPath= "\"%DEST%\ddcswitch.exe\" service" start= auto >nul || exit /b 1
)
sc description ddcswitch "Win+Ctrl+Shift+S switches the monitor to Mac via DDC/CI (works at login/lock screen)" >nul
sc failure ddcswitch reset= 86400 actions= restart/5000/restart/5000/restart/30000 >nul
sc start ddcswitch >nul || exit /b 1

echo 安装完成，日志：%DEST%\ddcswitch.log
