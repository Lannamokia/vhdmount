@echo off
echo VHD Mounter 调试工具
echo ==================

echo 正在编译调试工具...
dotnet build VHDDebugger.csproj --configuration Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo 编译成功！正在启动调试工具...
    echo.
    dotnet run --project VHDDebugger.csproj --configuration Release
) else (
    echo 编译失败！
    pause
)

echo.
echo 调试完成
pause