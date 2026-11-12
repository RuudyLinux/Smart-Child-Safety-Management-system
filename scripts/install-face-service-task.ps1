<#
  Registers a Windows scheduled task that starts the face service when you sign in and keeps it running.
  Runs as the current user: no administrator rights needed. Remove it with:
      Unregister-ScheduledTask -TaskName 'ChildGuard Face Service' -Confirm:$false
#>
$ErrorActionPreference = 'Stop'
$taskName = 'ChildGuard Face Service'
$script = Join-Path $PSScriptRoot 'run-face-service.ps1'

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'ChildGuard face service (127.0.0.1:8001), exposed only through the tunnel.' -Force | Out-Null
Write-Host "Registered '$taskName'. Start it now with: Start-ScheduledTask -TaskName '$taskName'"
