@echo off
chcp 65001 >nul
echo.
echo ========================================
echo PostgreSQL Database Setup Script
echo ========================================
echo.

REM Check if PostgreSQL is installed
echo Checking PostgreSQL installation...
psql --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: PostgreSQL is not installed or not in PATH
    echo.
    echo Please install PostgreSQL first:
    echo    1. Download from: https://www.postgresql.org/download/windows/
    echo    2. Run the installer as administrator
    echo    3. Set superuser password to: password
    echo    4. Use default port: 5432
    echo    5. Add PostgreSQL bin directory to PATH
    echo.
    echo After installation, run this script again.
    pause
    exit /b 1
)

echo SUCCESS: PostgreSQL is installed
echo.

REM Prompt for PostgreSQL superuser password
echo Please enter PostgreSQL superuser password:
set /p "PGPASSWORD=Password: "
echo.

REM Set environment variable for password
set PGPASSWORD=%PGPASSWORD%

echo Setting up database...
echo.

REM Execute the SQL setup script
psql -U postgres -h localhost -f setup_postgresql.sql

if %errorlevel% equ 0 (
    echo.
    echo SUCCESS: Database setup completed successfully!
    echo.
    echo Database Information:
    echo    Host: localhost
    echo    Port: 5432
    echo    Database: vhd_select
    echo    User: postgres
    echo.
    echo You can now start the VHDSelectServer:
    echo    node server.js
    echo.
) else (
    echo.
    echo ERROR: Database setup failed!
    echo Please check the error messages above and try again.
    echo.
)

REM Clear password from environment
set PGPASSWORD=

echo Press any key to exit...
pause >nul