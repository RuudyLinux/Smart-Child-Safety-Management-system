@echo off
echo ============================================
echo  ChildGuard - Stopping all services
echo ============================================
echo.

taskkill /FI "WINDOWTITLE eq ChildGuard-API*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq ChildGuard-AdminWeb*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq ChildGuard-FaceService*" /T /F >nul 2>&1

REM --- Fallback: also free the known ports directly, in case a window was
REM     closed by hand and left an orphaned process behind. ---
for %%P in (5275 5080 8001) do (
    for /f "tokens=5" %%A in ('netstat -ano ^| findstr ":%%P " ^| findstr "LISTENING"') do (
        taskkill /PID %%A /F >nul 2>&1
    )
)

echo Done. All ChildGuard services stopped.
pause
