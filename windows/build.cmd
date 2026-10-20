@echo off
chcp 65001 >nul
rem 用系统自带的 .NET Framework 4.x 编译器编译，不需要装任何东西
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo 找不到 %CSC%
    exit /b 1
)
cd /d "%~dp0"
if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /target:winexe /r:System.ServiceProcess.dll /out:bin\ddcswitch.exe src\Ddc.cs src\Agent.cs src\Service.cs
if errorlevel 1 (
    echo 编译失败
    exit /b 1
)
echo 编译完成：%~dp0bin\ddcswitch.exe
