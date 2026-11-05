@echo off
setlocal
title ChildGuard - System Starter
set ROOT=%~dp0

echo ============================================================
echo   ChildGuard - Smart Child Safety Management System
echo ============================================================
echo.

REM ------------------------------------------------------------
REM  1. Check and Start MySQL (port 3306)
REM ------------------------------------------------------------
echo [1/5] Checking Database on port 3306...
powershell -NoProfile -Command "if (Test-NetConnection -ComputerName localhost -Port 3306 -InformationLevel Quiet -WarningAction SilentlyContinue) { exit 0 } else { exit 1 }" >nul 2>&1
if not errorlevel 1 (
    echo       [OK] MySQL is already listening on port 3306.
    goto :db_ready
)

echo       Port 3306 not responding. Attempting to start MySQL service...
net start MySQL80 >nul 2>&1
powershell -NoProfile -Command "if (Test-NetConnection -ComputerName localhost -Port 3306 -InformationLevel Quiet -WarningAction SilentlyContinue) { exit 0 } else { exit 1 }" >nul 2>&1
if not errorlevel 1 (
    echo       [OK] Windows MySQL80 service started successfully.
    goto :db_ready
)

REM Fallback to XAMPP MySQL if installed
if exist "C:\xampp\mysql\bin\mysqld.exe" (
    echo       Starting portable MySQL via XAMPP on port 3306...
    start "ChildGuard-MySQL" /min "C:\xampp\mysql\bin\mysqld.exe" --defaults-file="C:\xampp\mysql\bin\my.ini" --port=3306 --console
    
    REM Wait up to 10 seconds for MySQL to be ready
    for /L %%i in (1,1,10) do (
        ping -n 2 127.0.0.1 >nul
        powershell -NoProfile -Command "if (Test-NetConnection -ComputerName localhost -Port 3306 -InformationLevel Quiet -WarningAction SilentlyContinue) { exit 0 } else { exit 1 }" >nul 2>&1
        if not errorlevel 1 (
            echo       [OK] MySQL ready on port 3306.
            goto :db_init
        )
    )
)

echo.
echo [WARNING] Could not start MySQL automatically on port 3306.
echo           Please ensure MySQL is running before continuing.
echo.
pause

:db_init
:db_ready
REM Database, app user, admin and JWT secrets are created on first run by the setup
REM script and kept in .NET user-secrets only - never hardcoded here. Idempotent.
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\dev-setup-secrets.ps1"
if errorlevel 1 echo       [WARN] Credential setup failed - check MySQL is running and reachable as root.
echo.

REM ------------------------------------------------------------
REM  2. Apply Pending Database Migrations
REM ------------------------------------------------------------
echo [2/5] Applying database migrations...
where dotnet-ef >nul 2>&1
if errorlevel 1 (
    dotnet tool run dotnet-ef database update --project "%ROOT%src\ChildGuard.Infrastructure" --startup-project "%ROOT%src\ChildGuard.Api" >nul 2>&1
) else (
    dotnet ef database update --project "%ROOT%src\ChildGuard.Infrastructure" --startup-project "%ROOT%src\ChildGuard.Api" >nul 2>&1
)
if errorlevel 1 (
    echo       [WARN] Migrations could not be applied automatically. The API will attempt self-seeding.
) else (
    echo       [OK] Database schema up to date.
)
echo.

REM ------------------------------------------------------------
REM  3. Start Face Service (Python FastAPI, port 8001)
REM ------------------------------------------------------------
echo [3/5] Starting Face Recognition Service (Port 8001)...
set "FACE_PY=%ROOT%face-service\.venv\Scripts\python.exe"
if exist "%FACE_PY%" (
    start "ChildGuard-FaceService" cmd /k "title ChildGuard - Face Service [Port 8001] && cd /d "%ROOT%face-service" && .venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8001"
    echo       [OK] Face service started at http://127.0.0.1:8001
) else (
    echo       [SKIP] face-service\.venv not found. Run: cd face-service ^&^& python -m venv .venv ^&^& .venv\Scripts\pip install -r requirements.txt
)
echo.

REM ------------------------------------------------------------
REM  4. Start ChildGuard.Api (ASP.NET Core Web API, port 5275)
REM ------------------------------------------------------------
echo [4/5] Starting ChildGuard API (Port 5275)...
start "ChildGuard-API" cmd /k "title ChildGuard - Web API [Port 5275] && cd /d "%ROOT%src\ChildGuard.Api" && dotnet run"
echo       [OK] API started at http://localhost:5275
echo.

REM Brief pause to let API begin listening before launching Admin Web
ping -n 4 127.0.0.1 >nul

REM ------------------------------------------------------------
REM  5. Start ChildGuard.AdminWeb (ASP.NET Core MVC, port 5080)
REM ------------------------------------------------------------
echo [5/5] Starting ChildGuard Admin Web Portal (Port 5080)...
start "ChildGuard-AdminWeb" cmd /k "title ChildGuard - Admin Web [Port 5080] && cd /d "%ROOT%src\ChildGuard.AdminWeb" && dotnet run --urls http://localhost:5080"
echo       [OK] Admin Web started at http://localhost:5080
echo.

echo ============================================================
echo   ChildGuard Services Active:
echo   - Admin Portal:   http://localhost:5080
echo   - API Backend:    http://localhost:5275
echo   - Face Service:   http://127.0.0.1:8001
echo   - Diagnostics:    http://localhost:5080/Diagnostics
echo.
echo   Admin sign-in: SeedAdmin:Email / SeedAdmin:Password in user-secrets
echo     dotnet user-secrets list --project src\ChildGuard.Api
echo ============================================================
echo.

REM Optional launch of Guardian Flutter App
set "LAUNCH_APP="
if /i "%~1"=="--with-app" set "LAUNCH_APP=y"
if /i "%~1"=="-app" set "LAUNCH_APP=y"

if defined LAUNCH_APP goto :do_launch_app

echo Would you like to start the Guardian App [Flutter Web] as well?
set /p "LAUNCH_APP=Press Y and Enter to launch, or just press Enter to skip [y/N]: "

:do_launch_app
if /i "%LAUNCH_APP%"=="y" (
    echo.
    echo Starting Guardian App on Chrome [Port 5090]...
    start "ChildGuard-GuardianApp" cmd /k "title ChildGuard - Guardian App [Web] && cd /d "%ROOT%guardian_app" && flutter run -d chrome --web-port 5090 --dart-define=API_BASE_URL=http://localhost:5275"
)

echo.
echo Opening Admin Web Portal in your default browser...
ping -n 3 127.0.0.1 >nul
start http://localhost:5080

echo.
echo [INFO] To stop all services anytime, run stop.bat or close their windows.
echo Press any key to close this launcher window.
pause >nul
