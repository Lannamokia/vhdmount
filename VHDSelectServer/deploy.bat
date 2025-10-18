@echo off
echo VHD Select Server Docker 部署脚本
echo ================================

echo 检查Docker是否运行...
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo 错误: Docker未安装或未运行
    echo 请先安装并启动Docker Desktop
    pause
    exit /b 1
)

echo 检查Docker Compose...
docker-compose --version >nul 2>&1
if %errorlevel% neq 0 (
    echo 错误: Docker Compose未安装
    pause
    exit /b 1
)

echo 创建配置目录...
if not exist "config" mkdir config

echo 拉取并启动服务...
docker-compose up -d

if %errorlevel% equ 0 (
    echo.
    echo ✅ 部署成功!
    echo 🌐 Web界面: http://localhost:8080
    echo 📁 配置文件: ./config/vhd-config.json
    echo.
    echo 常用命令:
    echo   查看日志: docker-compose logs -f
    echo   停止服务: docker-compose down
    echo   重启服务: docker-compose restart
) else (
    echo.
    echo ❌ 部署失败，请检查错误信息
    echo 如果是网络问题，请尝试:
    echo   1. 检查网络连接
    echo   2. 配置Docker镜像源
    echo   3. 使用本地Node.js部署: npm start
)

echo.
echo 按任意键退出...
pause >nul