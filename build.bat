@echo off
echo VHD Mounter Build Script
echo ==================

echo Building project...
dotnet build ./VHDMounter.csproj --configuration Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build succeeded!
    echo.
    echo Publishing self-contained single-file binaries...
    dotnet publish ./VHDMounter.csproj -c Release -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -r win10-x64
    dotnet publish ./Updater/Updater.csproj -c Release -r win10-x64
    dotnet publish ./UpdatePackagerGUI/UpdatePackagerGUI.csproj -c Release -r win10-x64
    
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo Publish succeeded!
        if not exist .\single mkdir .\single
        copy /Y .\bin\Release\net6.0-windows\win10-x64\publish\VHDMounter.exe .\single\VHDMounter.exe >nul
        copy /Y .\bin\Release\net6.0-windows\win10-x64\publish\vhdmonter_config.ini .\single\vhdmonter_config.ini >nul
        copy /Y .\Updater\bin\Release\net6.0-windows\win10-x64\publish\Updater.exe .\single\Updater.exe >nul
        copy /Y .\UpdatePackagerGUI\bin\Release\net6.0-windows\win10-x64\publish\UpdatePackagerGUI.exe .\single\UpdatePackagerGUI.exe >nul
        echo Output to single directory:
        echo   single\VHDMounter.exe
        echo   single\vhdmonter_config.ini
        echo   single\Updater.exe
        echo   single\UpdatePackagerGUI.exe
        echo.
        echo Note: Run VHDMounter.exe as Administrator
    ) else (
        echo Publish failed!
    )
) else (
    echo Build failed!
)

pause
