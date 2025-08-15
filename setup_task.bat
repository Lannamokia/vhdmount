@echo off
REM VHD Mounter - Setup Windows Scheduled Task
REM This script creates a scheduled task to run VHDMounter.exe at user logon with highest privileges

echo Setting up VHD Mounter scheduled task...

REM Get current directory
set "CURRENT_DIR=%~dp0"
set "EXE_PATH=%CURRENT_DIR%VHDMounter.exe"

REM Check if VHDMounter.exe exists
if not exist "%EXE_PATH%" (
    echo ERROR: VHDMounter.exe not found in current directory
    echo Please make sure VHDMounter.exe is in the same folder as this script
    pause
    exit /b 1
)

echo Found VHDMounter.exe at: %EXE_PATH%

REM Delete existing task if it exists
echo Removing existing task if present...
schtasks /delete /tn "VHDMounter" /f >nul 2>&1

REM Create new scheduled task
echo Creating new scheduled task...
schtasks /create /tn "VHDMounter" /tr "\"%EXE_PATH%\"" /sc onlogon /rl highest /f

if %errorlevel% equ 0 (
    echo SUCCESS: VHD Mounter scheduled task created successfully
    echo The task will run VHDMounter.exe at user logon with highest privileges
    echo Task Name: VHDMounter
    echo Trigger: At logon
    echo Run Level: Highest
    echo Executable: %EXE_PATH%
) else (
    echo ERROR: Failed to create scheduled task
    echo Please make sure you are running this script as Administrator
)

echo.
echo Press any key to exit...
pause >nul