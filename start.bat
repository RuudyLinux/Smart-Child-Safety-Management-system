@echo off
setlocal
set ROOT=%~dp0

echo ============================================
echo  ChildGuard - Starting all services
echo ============================================
echo.

REM --- Check MySQL is reachable on 3306 (informational only; assumes the
REM     MySQL service itself is already running as a Windows service) ---
powershell -NoProfile -Command "if (Test-NetConnection -ComputerName localhost -Port 3306 -InformationLevel Quiet -WarningAction SilentlyContinue) { exit 0 } else { exit 1 }" >nul 2>&1
if errorlevel 1 (
    echo [WARNING] MySQL does not appear to be listening on port 3306.
    echo           Start the MySQL service before using the app.
    echo.
)

echo Starting ChildGuard.Api  (http://localhost:5275) ...
start "ChildGuard-API" cmd /k "cd /d "%ROOT%src\ChildGuard.Api" && dotnet run"

timeout /t 4 /nobreak >nul

echo Starting ChildGuard.AdminWeb  (http://localhost:5080) ...
start "ChildGuard-AdminWeb" cmd /k "cd /d "%ROOT%src\ChildGuard.AdminWeb" && dotnet run --urls http://localhost:5080"

set "FACE_PY=%ROOT%face-service\.venv\Scripts\python.exe"
if not exist "%FACE_PY%" goto :skipface

echo Starting Face Service  (http://localhost:8001) ...
start "ChildGuard-FaceService" cmd /k "cd /d "%ROOT%face-service" && .venv\Scripts\python.exe -m uvicorn app.main:app --host 0.0.0.0 --port 8001"
goto :afterface

:skipface
echo [INFO] face-service\.venv not found - skipping Face Service.
echo        Run: cd face-service ^&^& python -m venv .venv ^&^& .venv\Scripts\pip install -r requirements.txt

:afterface

echo.
echo ============================================
echo  All services are starting in separate windows:
echo    API         - http://localhost:5275
echo    Admin Web   - http://localhost:5080
echo    Face Service- http://localhost:8001
echo ============================================
echo.
echo Run stop.bat to stop everything, or close the windows manually.
pause
