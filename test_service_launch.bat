@echo off
echo ========================================
echo VHD Mounter 服务启动测试脚本
echo ========================================
echo.

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% == 0 (
    echo 检测到管理员权限，继续执行...
) else (
    echo 此脚本需要管理员权限才能测试服务
    echo 请右键点击此批处理文件，选择"以管理员身份运行"
    pause
    exit /b 1
)

echo.
echo 检查服务状态...
sc query VHDMounterService
if %errorLevel% neq 0 (
    echo 服务未安装！请先运行 install_service.bat 或 reinstall_service.bat
    pause
    exit /b 1
)

echo.
echo 停止服务（如果正在运行）...
sc stop VHDMounterService
timeout /t 3 /nobreak >nul

echo.
echo 启动服务并监控日志...
sc start VHDMounterService

if %errorLevel% == 0 (
    echo 服务启动成功！
    echo.
    echo 等待15秒让服务尝试启动WPF应用程序...
    echo 请观察是否有WPF应用程序窗口出现。
    
    for /l %%i in (15,-1,1) do (
        echo 剩余等待时间: %%i 秒
        timeout /t 1 /nobreak >nul
    )
    
    echo.
    echo 检查是否有VHDMounter进程运行...
    tasklist /fi "imagename eq VHDMounter.exe" 2>nul | find /i "VHDMounter.exe" >nul
    if %errorLevel% == 0 (
        echo ✓ 发现VHDMounter.exe进程正在运行！
        echo WPF应用程序启动成功。
    ) else (
        echo ✗ 未发现VHDMounter.exe进程。
        echo WPF应用程序可能未成功启动。
    )
    
    echo.
    echo 查看最近的服务日志（来自Windows事件查看器）...
    echo 正在获取最近5分钟的相关日志...
    
    powershell -Command "Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-5)} | Where-Object {$_.ProviderName -eq 'VHDMounterService' -or $_.Message -like '*VHD*' -or $_.Message -like '*WPF*'} | Select-Object TimeCreated, LevelDisplayName, Message | Format-Table -Wrap"
    
) else (
    echo 服务启动失败！
    echo 请检查以下可能的原因：
    echo 1. 服务配置是否正确
    echo 2. VHDMounter.exe文件是否存在
    echo 3. 是否有足够的权限
)

echo.
echo 测试完成！
echo.
echo 如果WPF应用程序仍未启动，请：
echo 1. 检查Windows事件查看器中的详细错误信息
echo 2. 确保用户已登录到桌面
echo 3. 检查防病毒软件是否阻止了应用程序
echo 4. 尝试手动运行VHDMounter.exe（不带--service参数）
echo.
pause
exit /b 0