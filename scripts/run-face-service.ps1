<#
  Runs the face service on this machine, for use behind a tunnel (see docs/FACE_SERVICE_ON_THIS_PC.md).

  * Listens on 127.0.0.1 only: the tunnel is the single way in from outside.
  * Refuses to start without FACE_SERVICE_KEY (User environment variable, at least 32 characters):
    the service itself enforces this because FACE_SERVICE_REQUIRE_KEY is set here.
  * Restarts the service if it stops, waiting a few seconds between attempts.
  * Logs go to logs/face-service.log in the repository folder (git-ignored). Face images are never logged.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Join-Path $root 'face-service'
$python = Join-Path $service '.venv\Scripts\python.exe'
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'face-service.log'

if (-not (Test-Path $python)) { throw "Python environment not found at $python. See face-service/README.md." }

# The key lives in the User environment, not in this file or the repository.
$key = [Environment]::GetEnvironmentVariable('FACE_SERVICE_KEY', 'User')
if ([string]::IsNullOrWhiteSpace($key) -or $key.Length -lt 32) {
    throw 'FACE_SERVICE_KEY (User environment variable, 32+ characters) is not set.'
}
$env:FACE_SERVICE_KEY = $key
$env:FACE_SERVICE_REQUIRE_KEY = 'true'
$env:TF_CPP_MIN_LOG_LEVEL = '3'
$port = if ($env:FACE_SERVICE_PORT) { $env:FACE_SERVICE_PORT } else { '8001' }

Set-Location $service
# uvicorn logs to stderr; in Windows PowerShell 5.1 that would be a terminating error under 'Stop'.
$ErrorActionPreference = 'Continue'

function Write-Log([string]$message) { "$(Get-Date -Format o) [watchdog $PID] $message" | Add-Content $log }

# Something already listening on the port: either another copy of the service (healthy, or still
# loading its models) or an unrelated program. Starting a second copy then only crash-loops with
# "address already in use", so the watchdog waits instead.
function Get-PortState {
    if (-not (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)) { return 'free' }
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200 -and $r.Content -match '"status"\s*:\s*"ok"') { return 'healthy' }
    } catch { }
    return 'busy'
}

$parent = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction SilentlyContinue
$parentName = if ($parent) { (Get-Process -Id $parent.ParentProcessId -ErrorAction SilentlyContinue).ProcessName } else { '?' }
Write-Log "watchdog started (parent: $parentName)"
$lastState = $null
try {
    while ($true) {
        $state = Get-PortState
        if ($state -ne 'free') {
            if ($state -ne $lastState) {
                Write-Log $(if ($state -eq 'healthy') { "another face service already answers on 127.0.0.1:$port; watching instead of starting a second copy" }
                            else { "port $port is in use by something that is not a healthy face service; waiting" })
            }
            $lastState = $state
            Start-Sleep -Seconds 15
            continue
        }
        $lastState = $null
        Write-Log "starting face service on 127.0.0.1:$port"
        & $python -m uvicorn app.main:app --host 127.0.0.1 --port $port --workers 1 2>&1 |
            ForEach-Object { "$_" } | Out-File -FilePath $log -Append -Encoding utf8
        Write-Log "face service exited with code $LASTEXITCODE; restarting in 5 s"
        Start-Sleep -Seconds 5
    }
}
finally {
    # Runs when the watchdog itself is stopped (Ctrl+C, task stopped, sign-out), so the log says when and
    # how it ended instead of simply going quiet.
    Write-Log 'watchdog stopping'
}
