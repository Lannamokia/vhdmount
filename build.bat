@echo off
echo VHD Mounter 编译脚本
echo ==================

echo 正在编译项目...
dotnet build ./VHDMounter.csproj --configuration Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo 编译成功！
    echo.
    echo 正在发布自包含版本...
    dotnet publish ./VHDMounter.csproj -c Release -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -r win10-x64
    
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo 发布成功！
        echo 可执行文件位置: .\bin\Release\net6.0-windows\win10-x64\publish\VHDMounter.exe
        echo.
        echo 注意：程序需要以管理员身份运行！
    ) else (
        echo 发布失败！
    )
) else (
    echo 编译失败！
)

pause