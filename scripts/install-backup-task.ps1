<#
  Registers a daily Windows scheduled task that backs up the live database with backup-db.ps1.
  One-time setup, then run this script (no administrator rights needed; runs as you):
      1. Get the DIRECT owner connection string and store it as a User environment variable (never in the repo):
         [Environment]::SetEnvironmentVariable('CHILDGUARD_BACKUP_CONNECTION',
             (neon connection-string --project-id sweet-bird-96372858 --branch br-icy-rice-bev38l0y --role-name neondb_owner --database-name neondb), 'User')
         (If you rotate the owner password later, repeat this step.)
      2. powershell -ExecutionPolicy Bypass -File .\scripts\install-backup-task.ps1
  Backups go to %USERPROFILE%\ChildGuardBackups (newest 14 kept). Test a restore any time with verify-restore.ps1.
  Remove with: Unregister-ScheduledTask -TaskName 'ChildGuard Database Backup' -Confirm:$false
#>
param([string]$At = '02:30')
$ErrorActionPreference = 'Stop'
$taskName = 'ChildGuard Database Backup'
$script = Join-Path $PSScriptRoot 'backup-db.ps1'

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$script`""
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1) -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Daily pg_dump of the ChildGuard database (Neon) to a local folder.' -Force | Out-Null
Write-Host "Registered '$taskName' daily at $At. Run it now with: Start-ScheduledTask -TaskName '$taskName'"
