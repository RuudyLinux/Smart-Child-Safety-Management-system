<#
  Registers a Windows scheduled task that starts the face service when you sign in and keeps it running.
  Runs as the current user. Administrator rights are needed only if the task was first created from an
  elevated PowerShell (Windows then refuses to change it otherwise: "Access is denied"). Remove it with:
      Unregister-ScheduledTask -TaskName 'ChildGuard Face Service' -Confirm:$false
#>
$ErrorActionPreference = 'Stop'
$taskName = 'ChildGuard Face Service'
$script = Join-Path $PSScriptRoot 'run-face-service.ps1'

# Hosted by a headless conhost: on Windows 11 a plain console program is otherwise hosted by Windows Terminal
# (even with -WindowStyle Hidden), lands as a tab in a Terminal window, and is killed with exit 0xC000013A
# whenever that window is closed. That is how the watchdog kept disappearing.
$action = New-ScheduledTaskAction -Execute 'conhost.exe' `
    -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$script`""
$atLogOn = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
# Self-healing: the watchdog has been seen to end on its own (exit 0xC000013A, "closed as if by Ctrl+C").
# "Restart on failure" does not cover that, so the task is also fired every 5 minutes. While a watchdog runs,
# IgnoreNew drops the extra start; if it has died, a new one starts and adopts a still-running service
# (run-face-service.ps1 watches a healthy service on the port instead of starting a second copy).
$every5Min = New-ScheduledTaskTrigger -Once -At (Get-Date).Date -RepetitionInterval (New-TimeSpan -Minutes 5)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($atLogOn, $every5Min) -Settings $settings `
    -Description 'ChildGuard face service (127.0.0.1:8001), exposed only through the tunnel.' -Force -ErrorAction Stop | Out-Null
Write-Host "Registered '$taskName'. Start it now with: Start-ScheduledTask -TaskName '$taskName'"
