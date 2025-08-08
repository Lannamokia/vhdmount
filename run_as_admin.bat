@echo off
echo VHD Mounter 管理员运行脚本
echo ============================

REM 检查是否以管理员身份运行
net session >nul 2>&1
if %errorLevel% == 0 (
    echo 已以管理员身份运行
    echo.
) else (
    echo 请求管理员权限...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

REM 检查可执行文件是否存在
if exist "./publish/VHDMounter.exe" (
    echo 启动 VHD Mounter...
    cd publish
    VHDMounter.exe
) else if exist "./bin/Release/net6.0-windows/VHDMounter.exe" (
    echo 启动 VHD Mounter (Debug版本)...
    cd bin/Release/net6.0-windows
    VHDMounter.exe
) else (
    echo 错误：找不到可执行文件！
    echo 请先运行 build.bat 编译项目
    pause
    exit /b 1
)

echo.
echo 程序已退出
pause