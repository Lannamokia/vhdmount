@echo off
echo ========================================
echo VHD Mounter Windows服务安装工具
echo ========================================
echo.

:: 检查管理员权限
net session >nul 2>&1
if %errorLevel% == 0 (
    echo 检测到管理员权限，继续执行...
) else (
    echo 错误：需要管理员权限才能安装Windows服务
    echo 请右键点击此批处理文件，选择"以管理员身份运行"
    pause
    exit /b 1
)

echo.
echo 选择操作：
echo 1. 安装VHD Mounter服务
echo 2. 卸载VHD Mounter服务
echo 3. 启动VHD Mounter服务
echo 4. 停止VHD Mounter服务
echo 5. 查看服务状态
echo 6. 退出
echo.
set /p choice=请输入选择 (1-6): 

if "%choice%"=="1" goto install
if "%choice%"=="2" goto uninstall
if "%choice%"=="3" goto start
if "%choice%"=="4" goto stop
if "%choice%"=="5" goto status
if "%choice%"=="6" goto exit
echo 无效选择，请重新运行脚本
pause
exit /b 1

:install
echo.
echo 正在安装VHD Mounter服务（开机自动启动）...
sc create VHDMounterService binPath= "%~dp0VHDMounter.exe --service" start= auto DisplayName= "VHD Mounter Service"
if %errorLevel% == 0 (
    echo 服务安装成功！
    sc description VHDMounterService "VHD文件自动挂载服务（开机自动启动，服务拉起窗口）"
    sc failure VHDMounterService reset= 86400 actions= restart/60000/restart/60000/restart/60000
    echo 服务配置完成！已设置开机自动启动服务，服务将自动拉起窗口！
    echo.
    set /p startNow=是否立即启动服务？ (Y/N): 
    if /i "%startNow%"=="Y" (
        sc start VHDMounterService
        echo 服务启动完成！
    )
) else (
    echo 服务安装失败！
)
goto end

:uninstall
echo.
echo 正在卸载VHD Mounter服务...
sc stop VHDMounterService
sc delete VHDMounterService
if %errorLevel% == 0 (
    echo 服务卸载成功！
) else (
    echo 服务卸载完成！
)
goto end

:start
echo.
echo 正在启动VHD Mounter服务...
sc start VHDMounterService
if %errorLevel% == 0 (
    echo 服务启动成功！
) else (
    echo 服务启动失败！
)
goto end

:stop
echo.
echo 正在停止VHD Mounter服务...
sc stop VHDMounterService
if %errorLevel% == 0 (
    echo 服务停止成功！
) else (
    echo 服务停止失败！
)
goto end

:status
echo.
echo VHD Mounter服务状态：
sc query VHDMounterService
goto end

:exit
echo 退出安装工具
exit /b 0

:end
echo.
echo 操作完成！
pause
exit /b 0