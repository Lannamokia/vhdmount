@echo off
setlocal
set "ROOT_DIR=%~dp0.."
set "PUBLISH_ROOT=%ROOT_DIR%\artifacts\local-publish"
set "BASE_PUBLISH_DIR=%PUBLISH_ROOT%\VHDMounter"
set "MAIMOLLER_PUBLISH_DIR=%PUBLISH_ROOT%\VHDMounter_Maimoller"
set "UPDATER_PUBLISH_DIR=%PUBLISH_ROOT%\Updater"
set "TOOLS_PUBLISH_DIR=%PUBLISH_ROOT%\VHDMountAdminTools"
set "FLUTTER_DIR=%ROOT_DIR%\vhd_mount_admin_flutter"
set "FLUTTER_BUILD_DIR=%FLUTTER_DIR%\build\windows\x64\runner\Release"
set "SINGLE_DIR=%ROOT_DIR%\single"
pushd "%ROOT_DIR%"

echo ============================================
echo    VHD Mounter Interactive Build Script
echo ============================================
echo.
echo Select components to build:
echo   [1] VHDMounter (Base)
echo   [2] VHDMounter (Maimoller)
echo   [3] Updater
echo   [4] VHDMountAdminTools
echo   [5] Flutter Windows Client
echo   [6] Docker Image (vhd-select-server)
echo   [0] All components
echo.
echo Enter numbers separated by space or comma.
echo Example: 1,3,5  or  1 3 5
echo.

set /p SELECTION="Your choice: "
set SELECTION=%SELECTION:,= %

echo.

set BUILD_BASE=0
set BUILD_MAIMOLLER=0
set BUILD_UPDATER=0
set BUILD_TOOLS=0
set BUILD_FLUTTER=0
set BUILD_DOCKER=0
set "DOCKER_IMAGE=vhd-select-server"

for %%n in (%SELECTION%) do (
    if "%%n"=="0" (
        set BUILD_BASE=1
        set BUILD_MAIMOLLER=1
        set BUILD_UPDATER=1
        set BUILD_TOOLS=1
        set BUILD_FLUTTER=1
        set BUILD_DOCKER=1
    )
    if "%%n"=="1" set BUILD_BASE=1
    if "%%n"=="2" set BUILD_MAIMOLLER=1
    if "%%n"=="3" set BUILD_UPDATER=1
    if "%%n"=="4" set BUILD_TOOLS=1
    if "%%n"=="5" set BUILD_FLUTTER=1
    if "%%n"=="6" set BUILD_DOCKER=1
)

echo Selected:
if %BUILD_BASE%==1       echo   [x] VHDMounter (Base)
if %BUILD_MAIMOLLER%==1  echo   [x] VHDMounter (Maimoller)
if %BUILD_UPDATER%==1    echo   [x] Updater
if %BUILD_TOOLS%==1     echo   [x] VHDMountAdminTools
if %BUILD_FLUTTER%==1   echo   [x] Flutter Windows Client
if %BUILD_DOCKER%==1    echo   [x] Docker Image
echo.

set /p CONFIRM="Proceed? [Y/n] "
if /I "%CONFIRM%"=="n" goto :cancelled
if /I "%CONFIRM%"=="no" goto :cancelled

echo.
echo ============================================
echo              Starting Build
echo ============================================
echo.

if not exist "%SINGLE_DIR%" mkdir "%SINGLE_DIR%"

set OK_BASE=0
set OK_MAIMOLLER=0
set OK_UPDATER=0
set OK_TOOLS=0
set OK_FLUTTER=0
set OK_DOCKER=0
set "GIT_SHA="
set "DOCKER_TAG="

call :build_base
call :build_maimoller
call :build_updater
call :build_tools
call :build_flutter
call :build_docker

REM ========== Final Report ==========
echo(
echo ============================================
echo              Build Report
echo ============================================

if %BUILD_BASE%==0 if %BUILD_MAIMOLLER%==0 if %BUILD_UPDATER%==0 if %BUILD_TOOLS%==0 if %BUILD_FLUTTER%==0 if %BUILD_DOCKER%==0 (
    echo No valid selection made. Nothing to build.
    goto :end
)

set HAS_FAIL=0

call :report_base
call :report_maimoller
call :report_updater
call :report_tools
call :report_flutter
call :report_docker

echo(
if %HAS_FAIL%==0 (
    echo All selected components built successfully.
) else (
    echo Some components failed. Check output above for details.
)
echo(

goto :end

:cancelled
echo Build cancelled by user.

:end
popd
endlocal
pause
goto :eof

REM ============================================
REM              Build Subroutines
REM ============================================

:build_base
if %BUILD_BASE%==0 exit /b
echo [1/6] Building VHDMounter (Base)...
dotnet build ./VHDMounter.csproj --configuration Release
if not %ERRORLEVEL%==0 (echo   [x] VHDMounter ^(Base^) build FAILED. & exit /b)
dotnet publish ./VHDMounter.csproj -c Release -r win-x64 -p:EnableHidMenuFeatures=false -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o "%BASE_PUBLISH_DIR%"
if not %ERRORLEVEL%==0 (echo   [x] VHDMounter ^(Base^) publish FAILED. & exit /b)
copy /Y "%BASE_PUBLISH_DIR%\VHDMounter.exe" "%SINGLE_DIR%\VHDMounter.exe" >nul
if exist "%BASE_PUBLISH_DIR%\vhdmonter_config.ini" copy /Y "%BASE_PUBLISH_DIR%\vhdmonter_config.ini" "%SINGLE_DIR%\vhdmonter_config.ini" >nul
set OK_BASE=1
echo   [ok] VHDMounter (Base) done.
echo(
exit /b

:build_maimoller
if %BUILD_MAIMOLLER%==0 exit /b
echo [2/6] Building VHDMounter (Maimoller)...
dotnet build ./VHDMounter.csproj --configuration Release -p:EnableHidMenuFeatures=true
if not %ERRORLEVEL%==0 (echo   [x] VHDMounter ^(Maimoller^) build FAILED. & exit /b)
dotnet publish ./VHDMounter.csproj -c Release -r win-x64 -p:EnableHidMenuFeatures=true -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o "%MAIMOLLER_PUBLISH_DIR%"
if not %ERRORLEVEL%==0 (echo   [x] VHDMounter ^(Maimoller^) publish FAILED. & exit /b)
copy /Y "%MAIMOLLER_PUBLISH_DIR%\VHDMounter_Maimoller.exe" "%SINGLE_DIR%\VHDMounter_Maimoller.exe" >nul
set OK_MAIMOLLER=1
echo   [ok] VHDMounter (Maimoller) done.
echo(
exit /b

:build_updater
if %BUILD_UPDATER%==0 exit /b
echo [3/6] Building Updater...
dotnet publish ./Updater/Updater.csproj -c Release -r win-x64 -o "%UPDATER_PUBLISH_DIR%"
if not %ERRORLEVEL%==0 (echo   [x] Updater publish FAILED. & exit /b)
copy /Y "%UPDATER_PUBLISH_DIR%\Updater.exe" "%SINGLE_DIR%\Updater.exe" >nul
set OK_UPDATER=1
echo   [ok] Updater done.
echo(
exit /b

:build_tools
if %BUILD_TOOLS%==0 exit /b
echo [4/6] Building VHDMountAdminTools...
dotnet publish ./VHDMountAdminTools/VHDMountAdminTools.csproj -c Release -r win-x64 -o "%TOOLS_PUBLISH_DIR%"
if not %ERRORLEVEL%==0 (echo   [x] VHDMountAdminTools publish FAILED. & exit /b)
copy /Y "%TOOLS_PUBLISH_DIR%\VHDMountAdminTools.exe" "%SINGLE_DIR%\VHDMountAdminTools.exe" >nul
set OK_TOOLS=1
echo   [ok] VHDMountAdminTools done.
echo(
exit /b

:build_flutter
if %BUILD_FLUTTER%==0 exit /b
echo [5/6] Building Flutter Windows Client...
pushd "%FLUTTER_DIR%"
call flutter build windows
if not %ERRORLEVEL%==0 (popd & echo   [x] Flutter build FAILED. & exit /b)
popd

set "FLUTTER_TEMP_BUILD=%TEMP%\vhd_flutter_build_%RANDOM%"
mkdir "%FLUTTER_TEMP_BUILD%"
xcopy /E /I /Y /Q "%FLUTTER_BUILD_DIR%\*" "%FLUTTER_TEMP_BUILD%\"
powershell -NoProfile -Command "Compress-Archive -Path '%FLUTTER_TEMP_BUILD%\*' -DestinationPath '%SINGLE_DIR%\vhd_mount_admin_windows.zip' -Force"
if not %ERRORLEVEL%==0 (
    echo   [x] Flutter zip packaging FAILED.
    rmdir /S /Q "%FLUTTER_TEMP_BUILD%" 2>nul
    exit /b
)

rmdir /S /Q "%FLUTTER_TEMP_BUILD%"
rmdir /S /Q "%FLUTTER_DIR%\build"
set OK_FLUTTER=1
echo   [ok] Flutter Windows Client done.
echo(
exit /b

:build_docker
if %BUILD_DOCKER%==0 exit /b
echo [6/6] Building Docker Image...
for /f "tokens=*" %%a in ('git -C "%~dp0.." rev-parse --short HEAD 2^>nul') do set "GIT_SHA=%%a"
if not defined GIT_SHA (
    for /f "tokens=*" %%a in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "GIT_SHA=local-%%a"
)
set "DOCKER_TAG=%DOCKER_IMAGE%:ci-%GIT_SHA%"
docker build -t %DOCKER_TAG% "%~dp0..\VHDSelectServer"
if not %ERRORLEVEL%==0 (echo   [x] Docker build FAILED. & exit /b)
docker save %DOCKER_TAG% | gzip > "%SINGLE_DIR%\%DOCKER_IMAGE%-ci-%GIT_SHA%.tgz"
if not %ERRORLEVEL%==0 (echo   [x] Docker export FAILED. Image available locally as %DOCKER_TAG% & set OK_DOCKER=1 & exit /b)
set OK_DOCKER=1
echo   [ok] Docker Image done.
echo(
exit /b

REM ============================================
REM            Report Subroutines
REM ============================================

:report_base
if %BUILD_BASE%==0 exit /b
if %OK_BASE%==0 (echo [FAIL] VHDMounter (Base)       & set HAS_FAIL=1 & exit /b)
echo [OK]   VHDMounter (Base)       ^> single\VHDMounter.exe
exit /b

:report_maimoller
if %BUILD_MAIMOLLER%==0 exit /b
if %OK_MAIMOLLER%==0 (echo [FAIL] VHDMounter (Maimoller)  & set HAS_FAIL=1 & exit /b)
echo [OK]   VHDMounter (Maimoller)  ^> single\VHDMounter_Maimoller.exe
exit /b

:report_updater
if %BUILD_UPDATER%==0 exit /b
if %OK_UPDATER%==0 (echo [FAIL] Updater                 & set HAS_FAIL=1 & exit /b)
echo [OK]   Updater                 ^> single\Updater.exe
exit /b

:report_tools
if %BUILD_TOOLS%==0 exit /b
if %OK_TOOLS%==0 (echo [FAIL] VHDMountAdminTools      & set HAS_FAIL=1 & exit /b)
echo [OK]   VHDMountAdminTools      ^> single\VHDMountAdminTools.exe
exit /b

:report_flutter
if %BUILD_FLUTTER%==0 exit /b
if %OK_FLUTTER%==0 (echo [FAIL] Flutter Windows Client  & set HAS_FAIL=1 & exit /b)
echo [OK]   Flutter Windows Client  ^> single\vhd_mount_admin_windows.zip
exit /b

:report_docker
if %BUILD_DOCKER%==0 exit /b
if %OK_DOCKER%==0 (echo [FAIL] Docker Image            & set HAS_FAIL=1 & exit /b)
echo [OK]   Docker Image            ^> single\%DOCKER_IMAGE%-ci-%GIT_SHA%.tgz
echo        Tag: %DOCKER_TAG%
exit /b
