@echo off
echo VHD Mounter 编译脚本
echo ==================

echo 正在编译项目...
dotnet build --configuration Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo 编译成功！
    echo.
    echo 正在发布自包含版本...
    dotnet publish --configuration Release --self-contained true --runtime win-x64 --output ./publish
    
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo 发布成功！
        echo 可执行文件位置: ./publish/VHDMounter.exe
        echo.
        echo 注意：程序需要以管理员身份运行！
    ) else (
        echo 发布失败！
    )
) else (
    echo 编译失败！
)

pause