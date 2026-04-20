@echo off
setlocal
set "ROOT_DIR=%~dp0.."
set "PUBLISH_ROOT=%ROOT_DIR%\artifacts\local-publish"
set "BASE_PUBLISH_DIR=%PUBLISH_ROOT%\VHDMounter"
set "MAIMOLLER_PUBLISH_DIR=%PUBLISH_ROOT%\VHDMounter_Maimoller"
set "UPDATER_PUBLISH_DIR=%PUBLISH_ROOT%\Updater"
set "TOOLS_PUBLISH_DIR=%PUBLISH_ROOT%\VHDMountAdminTools"
set "SINGLE_DIR=%ROOT_DIR%\single"
pushd "%ROOT_DIR%"

echo VHD Mounter Build Script
echo ==================

if exist "%PUBLISH_ROOT%" rmdir /S /Q "%PUBLISH_ROOT%"
if not exist "%SINGLE_DIR%" mkdir "%SINGLE_DIR%"

echo Building base project...
dotnet build ./VHDMounter.csproj --configuration Release
if errorlevel 1 goto :build_failed

echo Building Maimoller feature project...
dotnet build ./VHDMounter.csproj --configuration Release -p:EnableHidMenuFeatures=true
if errorlevel 1 goto :build_failed

echo.
echo Build succeeded!
echo.
echo Publishing self-contained single-file binaries...
dotnet publish ./VHDMounter.csproj -c Release -r win-x64 -p:EnableHidMenuFeatures=false -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o "%BASE_PUBLISH_DIR%"
if errorlevel 1 goto :publish_failed

dotnet publish ./VHDMounter.csproj -c Release -r win-x64 -p:EnableHidMenuFeatures=true -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o "%MAIMOLLER_PUBLISH_DIR%"
if errorlevel 1 goto :publish_failed

dotnet publish ./Updater/Updater.csproj -c Release -r win-x64 -o "%UPDATER_PUBLISH_DIR%"
if errorlevel 1 goto :publish_failed

dotnet publish ./VHDMountAdminTools/VHDMountAdminTools.csproj -c Release -r win-x64 -o "%TOOLS_PUBLISH_DIR%"
if errorlevel 1 goto :publish_failed

echo.
echo Publish succeeded!
copy /Y "%BASE_PUBLISH_DIR%\VHDMounter.exe" "%SINGLE_DIR%\VHDMounter.exe" >nul
copy /Y "%MAIMOLLER_PUBLISH_DIR%\VHDMounter_Maimoller.exe" "%SINGLE_DIR%\VHDMounter_Maimoller.exe" >nul
copy /Y "%BASE_PUBLISH_DIR%\vhdmonter_config.ini" "%SINGLE_DIR%\vhdmonter_config.ini" >nul
copy /Y "%UPDATER_PUBLISH_DIR%\Updater.exe" "%SINGLE_DIR%\Updater.exe" >nul
copy /Y "%TOOLS_PUBLISH_DIR%\VHDMountAdminTools.exe" "%SINGLE_DIR%\VHDMountAdminTools.exe" >nul
echo Output to single directory:
echo   single\VHDMounter.exe
echo   single\VHDMounter_Maimoller.exe
echo   single\vhdmonter_config.ini
echo   single\Updater.exe
echo   single\VHDMountAdminTools.exe
echo.
echo Note: Run VHDMounter.exe or VHDMounter_Maimoller.exe as Administrator
goto :end

:build_failed
echo Build failed!
goto :end

:publish_failed
echo Publish failed!

:end
popd
endlocal
pause
