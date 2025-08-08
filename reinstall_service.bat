@echo off
echo ========================================
echo VHD Mounter 服务重新安装脚本
echo ========================================
echo.

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% == 0 (
    echo 检测到管理员权限，继续执行...
) else (
    echo 此脚本需要管理员权限才能重新安装Windows服务
    echo 请右键点击此批处理文件，选择"以管理员身份运行"
    pause
    exit /b 1
)

echo.
echo 正在停止并卸载现有服务...
sc stop VHDMounterService
sc delete VHDMounterService

echo.
echo 等待服务完全卸载...
timeout /t 3 /nobreak >nul

echo.
echo 正在重新安装VHD Mounter服务（支持桌面交互）...
sc create VHDMounterService binPath= "%~dp0VHDMounter.exe --service" start= auto DisplayName= "VHD Mounter Service" type= own

if %errorLevel% == 0 (
    echo 服务安装成功！
    sc description VHDMounterService "VHD文件自动挂载服务（支持自动启动桌面应用程序窗口）"
    sc failure VHDMounterService reset= 86400 actions= restart/60000/restart/60000/restart/60000
    
    echo.
    echo 配置服务权限以启动桌面应用程序...
    
    echo 服务配置完成！现在可以自动启动桌面应用程序了。
    echo.
    set /p startNow=是否立即启动服务？ (Y/N): 
    if /i "%startNow%"=="Y" (
        sc start VHDMounterService
        if %errorLevel% == 0 (
            echo 服务启动成功！
            echo 请等待10-15秒，WPF应用程序窗口应该会自动出现。
        ) else (
            echo 服务启动失败，请检查事件查看器中的错误信息。
        )
    )
) else (
    echo 服务安装失败！
    echo 请检查以下可能的原因：
    echo 1. 确保VHDMounter.exe文件存在于当前目录
    echo 2. 确保以管理员权限运行此脚本
    echo 3. 检查Windows事件查看器中的错误信息
)

echo.
echo 操作完成！
echo.
echo 故障排除提示：
echo - 如果WPF应用程序仍未启动，请检查Windows事件查看器
echo - 确保用户已登录到桌面
echo - 检查应用程序是否被防病毒软件阻止
echo.
pause
exit /b 0