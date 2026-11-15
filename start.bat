@echo off
setlocal
title ChildGuard - System Starter
set "ROOT=%~dp0"
if not exist "%ROOT%logs" mkdir "%ROOT%logs"

echo ============================================================
echo   ChildGuard - Smart Child Safety Management System
echo ============================================================
echo.

REM Services already running are left alone. Starting a second copy only crashes
REM with "address already in use" in its own window.
set "API_RUNNING="
set "WEB_RUNNING="
set "FACE_RUNNING="
set "FACE_TASK="
call :is_listening 5275
if not errorlevel 1 set "API_RUNNING=1"
call :is_listening 5080
if not errorlevel 1 set "WEB_RUNNING=1"
call :is_listening 8001
if not errorlevel 1 set "FACE_RUNNING=1"

REM ------------------------------------------------------------
REM  1. Check and Start MySQL (port 3306)
REM ------------------------------------------------------------
echo [1/6] Checking Database on port 3306...
call :is_listening 3306
if not errorlevel 1 (
    echo       [OK] MySQL is already listening on port 3306.
    goto :db_ready
)

echo       Port 3306 not responding. Attempting to start MySQL service...
net start MySQL80 >nul 2>&1
call :is_listening 3306
if not errorlevel 1 (
    echo       [OK] Windows MySQL80 service started successfully.
    goto :db_ready
)

REM Fallback to XAMPP MySQL if installed
if exist "C:\xampp\mysql\bin\mysqld.exe" (
    echo       Starting portable MySQL via XAMPP on port 3306...
    start "ChildGuard-MySQL" /min "C:\xampp\mysql\bin\mysqld.exe" --defaults-file="C:\xampp\mysql\bin\my.ini" --port=3306 --console

    REM Wait up to 15 seconds. An open port is not enough: mysqld can open the socket
    REM and then abort (e.g. crashed system tables), so require a real ping.
    for /L %%i in (1,1,15) do (
        ping -n 2 127.0.0.1 >nul
        "C:\xampp\mysql\bin\mysqladmin.exe" -u root -h 127.0.0.1 -P 3306 --connect-timeout=2 ping >nul 2>&1
        if not errorlevel 1 (
            echo       [OK] MySQL ready on port 3306.
            goto :db_ready
        )
    )
    echo       [ERROR] MySQL did not become ready. To see why, run:
    echo               C:\xampp\mysql\bin\mysqld.exe --defaults-file=C:\xampp\mysql\bin\my.ini --console
)

echo.
echo [WARNING] Could not start MySQL automatically on port 3306.
echo           Please ensure MySQL is running before continuing.
echo.
pause

:db_ready
REM Database, app user, admin and JWT secrets are created on first run by the setup
REM script and kept in .NET user-secrets only - never hardcoded here. Idempotent.
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\dev-setup-secrets.ps1"
if errorlevel 1 echo       [WARN] Credential setup failed - check MySQL is running and reachable as root.
echo.

REM ------------------------------------------------------------
REM  2. Build (one at a time, before anything runs)
REM ------------------------------------------------------------
REM API and Admin Web share the Application project. Building each with its own
REM "dotnet run" at the same moment makes them fight over the same output files,
REM so build here, in order, and start them with --no-build. A running service
REM locks its own binaries, so it is skipped.
echo [2/6] Building...
if defined API_RUNNING (
    echo       [SKIP] API is already running - using its current build.
) else (
    dotnet build "%ROOT%src\ChildGuard.Api" -v q -nologo > "%ROOT%logs\build-api.log" 2>&1
    if errorlevel 1 (
        set "BUILD_LOG=%ROOT%logs\build-api.log"
        goto :build_failed
    )
    echo       [OK] API built.
)
if defined WEB_RUNNING (
    echo       [SKIP] Admin Web is already running - using its current build.
) else (
    dotnet build "%ROOT%src\ChildGuard.AdminWeb" -v q -nologo > "%ROOT%logs\build-adminweb.log" 2>&1
    if errorlevel 1 (
        set "BUILD_LOG=%ROOT%logs\build-adminweb.log"
        goto :build_failed
    )
    echo       [OK] Admin Web built.
)
echo.

REM ------------------------------------------------------------
REM  3. Apply Pending Database Migrations
REM ------------------------------------------------------------
echo [3/6] Applying database migrations...
where dotnet-ef >nul 2>&1
if errorlevel 1 (
    echo       [WARN] dotnet-ef is not installed, so migrations were not applied. Install it with:
    echo              dotnet tool install --global dotnet-ef
    goto :migrations_done
)
REM --no-build: the API was just built, and if it is running its files are locked.
dotnet ef database update --no-build --project "%ROOT%src\ChildGuard.Infrastructure" --startup-project "%ROOT%src\ChildGuard.Api" > "%ROOT%logs\migrate.log" 2>&1
if errorlevel 1 (
    echo       [WARN] Migrations could not be applied. Details: logs\migrate.log
) else (
    echo       [OK] Database schema up to date.
)
:migrations_done
echo.

REM ------------------------------------------------------------
REM  4. Start Face Service (Python FastAPI, port 8001)
REM ------------------------------------------------------------
echo [4/6] Starting Face Recognition Service (Port 8001)...
set "FACE_PY=%ROOT%face-service\.venv\Scripts\python.exe"
REM On a PC with the "ChildGuard Face Service" scheduled task (the tunnel setup), that task owns the
REM service. Its copy needs 10-15 s to load models before port 8001 opens, so a second copy started
REM here would race it for the port. Start the task instead; it ignores the request if already running.
schtasks /query /tn "ChildGuard Face Service" >nul 2>&1
if not errorlevel 1 set "FACE_TASK=1"
if defined FACE_RUNNING (
    echo       [SKIP] Already running on port 8001.
) else if defined FACE_TASK (
    schtasks /run /tn "ChildGuard Face Service" >nul 2>&1
    echo       Started by the "ChildGuard Face Service" scheduled task.
) else if exist "%FACE_PY%" (
    start "ChildGuard-FaceService" cmd /k "title ChildGuard - Face Service [Port 8001] && cd /d "%ROOT%face-service" && .venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8001"
    echo       Launched.
) else (
    echo       [SKIP] face-service\.venv not found. Run: cd face-service ^&^& python -m venv .venv ^&^& .venv\Scripts\pip install -r requirements.txt
)
echo.

REM ------------------------------------------------------------
REM  5. Start ChildGuard.Api (ASP.NET Core Web API, port 5275)
REM ------------------------------------------------------------
echo [5/6] Starting ChildGuard API (Port 5275)...
if defined API_RUNNING (
    echo       [SKIP] Already running on port 5275.
) else (
    start "ChildGuard-API" cmd /k "title ChildGuard - Web API [Port 5275] && cd /d "%ROOT%src\ChildGuard.Api" && dotnet run --no-build"
    echo       Launched.
)
echo.

REM ------------------------------------------------------------
REM  6. Start ChildGuard.AdminWeb (ASP.NET Core MVC, port 5080)
REM ------------------------------------------------------------
echo [6/6] Starting ChildGuard Admin Web Portal (Port 5080)...
if defined WEB_RUNNING (
    echo       [SKIP] Already running on port 5080.
) else (
    start "ChildGuard-AdminWeb" cmd /k "title ChildGuard - Admin Web [Port 5080] && cd /d "%ROOT%src\ChildGuard.AdminWeb" && dotnet run --no-build --urls http://localhost:5080"
    echo       Launched.
)
echo.

REM ------------------------------------------------------------
REM  Confirm each service actually answers, not just that it was launched.
REM ------------------------------------------------------------
echo Waiting for services to answer...
set "ALL_UP=1"
call :wait_http "http://localhost:5275/api/health" 90
if errorlevel 1 (
    echo   [FAIL] API        http://localhost:5275  - see the "ChildGuard - Web API" window
    set "ALL_UP="
) else (
    echo   [ OK ] API        http://localhost:5275
)
call :wait_http "http://localhost:5080/Account/Login" 60
if errorlevel 1 (
    echo   [FAIL] Admin Web  http://localhost:5080  - see the "ChildGuard - Admin Web" window
    set "ALL_UP="
) else (
    echo   [ OK ] Admin Web  http://localhost:5080
)
if exist "%FACE_PY%" (
    call :wait_http "http://127.0.0.1:8001/health" 120
    if errorlevel 1 (
        echo   [FAIL] Face       http://127.0.0.1:8001  - see the "ChildGuard - Face Service" window
        set "ALL_UP="
    ) else (
        echo   [ OK ] Face       http://127.0.0.1:8001
    )
)
echo.

echo ============================================================
if defined ALL_UP (
    echo   ChildGuard Services Active:
) else (
    echo   Some services did not start - check the windows marked FAIL above.
)
echo   - Admin Portal:   http://localhost:5080
echo   - API Backend:    http://localhost:5275
echo   - Face Service:   http://127.0.0.1:8001
echo   - Diagnostics:    http://localhost:5080/Diagnostics
echo.
echo   First admin sign-in: SeedAdmin:Email / SeedAdmin:Password in user-secrets
echo     dotnet user-secrets list --project src\ChildGuard.Api
echo   If you changed the password on the Profile page, use the new one.
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
if defined ALL_UP (
    echo Opening Admin Web Portal in your default browser...
    start http://localhost:5080
)

echo.
echo [INFO] To stop all services anytime, run stop.bat or close their windows.
echo Press any key to close this launcher window.
pause >nul
exit /b 0

REM ============================================================
REM  Helpers
REM ============================================================

:build_failed
echo       [ERROR] Build failed. Errors:
findstr /R /C:": error " "%BUILD_LOG%"
echo       Full log: %BUILD_LOG%
echo.
echo Nothing was started. Fix the errors above and run start.bat again.
pause
exit /b 1

REM errorlevel 0 when something accepts connections on localhost TCP port %1.
REM A real connect on IPv4 and IPv6: the TCP table (netstat, Get-NetTCPConnection)
REM does not always report a listening socket as LISTENING, and Kestrel's
REM "localhost" may bind only ::1.
:is_listening
powershell -NoProfile -Command "foreach ($a in '127.0.0.1','::1') { $ip = [Net.IPAddress]::Parse($a); $c = New-Object Net.Sockets.TcpClient($ip.AddressFamily); try { if ($c.ConnectAsync($ip, %1).Wait(1000) -and $c.Connected) { exit 0 } } catch { } finally { $c.Close() } }; exit 1" >nul 2>&1
exit /b %errorlevel%

REM errorlevel 0 once URL %1 answers HTTP 200 within %2 seconds.
:wait_http
powershell -NoProfile -Command "$end = (Get-Date).AddSeconds(%2); do { try { if ((Invoke-WebRequest -Uri '%~1' -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) { exit 0 } } catch { }; Start-Sleep -Seconds 2 } while ((Get-Date) -lt $end); exit 1" >nul 2>&1
exit /b %errorlevel%
