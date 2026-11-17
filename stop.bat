@echo off
title ChildGuard - System Stopper
echo ============================================================
echo   ChildGuard - Stopping all services
echo ============================================================
echo.

echo Stopping application windows...
REM Match the service consoles by command line: window titles are unreliable under
REM Windows Terminal, and start.bat renames them with "title" anyway.
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='cmd.exe'\" | Where-Object { $_.CommandLine -match 'title ChildGuard - (Face Service|Web API|Admin Web|Guardian App)' } | ForEach-Object { taskkill /PID $_.ProcessId /T /F *> $null }"

REM Only shut down MySQL if start.bat launched it (XAMPP mysqld with --console),
REM not a MySQL started from the XAMPP control panel or a Windows service.
powershell -NoProfile -Command "if (Get-CimInstance Win32_Process -Filter \"Name='mysqld.exe'\" | Where-Object { $_.CommandLine -match 'xampp' -and $_.CommandLine -match '--console' }) { exit 0 } else { exit 1 }" >nul 2>&1
if not errorlevel 1 (
    echo Stopping MySQL started by ChildGuard...
    "C:\xampp\mysql\bin\mysqladmin.exe" -u root -h 127.0.0.1 -P 3306 shutdown >nul 2>&1
)

REM Anything still owning a ChildGuard port (e.g. started outside start.bat).
REM Any state, not just Listen: the TCP table does not always report a listening
REM socket as LISTENING (netstat then omits it entirely), which left the process
REM running. A local port in this range belongs to the server, never a client.
echo Freeing ports (5275, 5080, 8001, 5090)...
powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 5275,5080,8001,5090 -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -gt 4 } | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object { taskkill /PID $_ /T /F *> $null }"
ping -n 2 127.0.0.1 >nul

echo.
REM Verify with a real connection attempt on IPv4 and IPv6.
powershell -NoProfile -Command "foreach ($p in 5275,5080,8001,5090) { foreach ($a in '127.0.0.1','::1') { $ip = [Net.IPAddress]::Parse($a); $c = New-Object Net.Sockets.TcpClient($ip.AddressFamily); try { if ($c.ConnectAsync($ip, $p).Wait(500) -and $c.Connected) { exit 1 } } catch { } finally { $c.Close() } } }; exit 0" >nul 2>&1
if errorlevel 1 (
    echo [WARN] A ChildGuard port is still in use. Close the remaining window manually.
) else (
    echo [OK] All ChildGuard services and ports have been stopped.
)
echo.
ping -n 2 127.0.0.1 >nul
