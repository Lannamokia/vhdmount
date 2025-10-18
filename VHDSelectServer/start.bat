@echo off
chcp 65001 >nul
echo ================================================
echo           VHD选择服务器启动脚本
echo ================================================
echo.

:: 检查Node.js是否已安装
node --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ❌ 未检测到Node.js，请先安装Node.js
    echo.
    echo 📥 请访问以下网址下载并安装Node.js:
    echo    https://nodejs.org/
    echo.
    echo 💡 建议下载LTS版本（长期支持版本）
    echo.
    pause
    exit /b 1
)

echo ✅ Node.js已安装
node --version
echo.

:: 检查依赖是否已安装
if not exist "node_modules" (
    echo 📦 正在安装依赖包...
    npm install
    if %errorlevel% neq 0 (
        echo ❌ 依赖安装失败
        pause
        exit /b 1
    )
    echo ✅ 依赖安装完成
    echo.
)

echo 🚀 正在启动VHD选择服务器...
echo.
node server.js

if %errorlevel% neq 0 (
    echo.
    echo ❌ 服务器启动失败
    pause
)