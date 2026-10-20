@echo off
chcp 65001 >nul
rem 停止并删除 ddcswitch 服务，删除安装目录（需要管理员权限）
net session >nul 2>&1
if errorlevel 1 (
    echo 请用管理员身份运行
    exit /b 1
)

sc stop ddcswitch >nul 2>&1
ping -n 4 127.0.0.1 >nul
taskkill /f /im ddcswitch.exe >nul 2>&1
sc delete ddcswitch
rmdir /s /q "C:\Program Files\ddcswitch"
echo 已卸载
