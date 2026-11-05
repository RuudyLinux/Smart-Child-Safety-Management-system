@echo off
title ChildGuard - System Stopper
echo ============================================================
echo   ChildGuard - Stopping all services
echo ============================================================
echo.

echo Stopping application windows...
taskkill /FI "WINDOWTITLE eq ChildGuard-GuardianApp*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq ChildGuard-AdminWeb*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq ChildGuard-API*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq ChildGuard-FaceService*" /T /F >nul 2>&1

REM If ChildGuard started XAMPP MySQL, shut it down cleanly
if exist "C:\xampp\mysql\bin\mysqladmin.exe" (
    "C:\xampp\mysql\bin\mysqladmin.exe" -u root -P 3306 shutdown >nul 2>&1
)
taskkill /FI "WINDOWTITLE eq ChildGuard-MySQL*" /T /F >nul 2>&1

echo Freeing ports (5275, 5080, 8001, 5090)...
for %%P in (5275 5080 8001 5090) do (
    for /f "tokens=5" %%A in ('netstat -ano ^| findstr ":%%P " ^| findstr "LISTENING"') do (
        taskkill /PID %%A /F >nul 2>&1
    )
)

echo.
echo [OK] All ChildGuard services and ports have been stopped.
echo.
ping -n 2 127.0.0.1 >nul
