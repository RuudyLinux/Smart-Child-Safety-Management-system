<#
.SYNOPSIS
  Proves a backup can be restored: restores it into a throwaway database on a LOCAL scratch server and compares
  exact row counts with the counts recorded at backup time. Never touches Neon.

.DESCRIPTION
  -BackupFile      the .dump produced by backup-db.ps1 (its .sha256 and .counts.txt must sit beside it)
  Environment      CHILDGUARD_RESTORE_CONNECTION: postgres:// URI of a scratch server (superuser) used for the test.
                   It must be a local/throwaway server, never production; the script refuses a host ending in neon.tech.
  The scratch database is named childguard_restore_check_<n> and dropped at the end (only that database).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BackupFile,
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference = 'Stop'

$conn = $env:CHILDGUARD_RESTORE_CONNECTION
if ([string]::IsNullOrWhiteSpace($conn)) { throw 'Set CHILDGUARD_RESTORE_CONNECTION to a scratch PostgreSQL server (local), e.g. postgres://postgres:pw@127.0.0.1:5433/postgres' }
$uri = [Uri]$conn
if ($uri.Host -like '*neon.tech') { throw 'Refusing to restore into Neon. Use a local scratch server.' }
if (-not (Test-Path $BackupFile)) { throw "Backup file not found: $BackupFile" }

$expectedHash = (Get-Content "$BackupFile.sha256" -Raw).Trim()
if ((Get-FileHash $BackupFile -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Checksum mismatch: the backup file changed after it was written.' }
$expected = @{}
Get-Content "$BackupFile.counts.txt" | Where-Object { $_ } | ForEach-Object { $k, $v = $_.Split('=', 2); $expected[$k] = [long]$v }

$userInfo = $uri.UserInfo.Split(':', 2)
$env:PGPASSWORD = if ($userInfo.Count -gt 1) { [Uri]::UnescapeDataString($userInfo[1]) } else { '' }
$common = @('-h', $uri.Host, '-p', $(if ($uri.Port -gt 0) { $uri.Port } else { 5432 }), '-U', [Uri]::UnescapeDataString($userInfo[0]))
$scratch = 'childguard_restore_check_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$psql = Join-Path $PgBin 'psql.exe'

try {
    & $psql @common -d postgres -v ON_ERROR_STOP=1 -q -c "create database $scratch"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the scratch database.' }
    & $psql @common -d $scratch -v ON_ERROR_STOP=1 -q -c 'create extension if not exists citext'

    & (Join-Path $PgBin 'pg_restore.exe') @common -d $scratch --no-owner --no-privileges --exit-on-error $BackupFile
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed (exit $LASTEXITCODE)." }

    $countSql = @'
select table_name || '=' || (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text
from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1;
'@
    $actual = @{}
    & $psql @common -d $scratch -At -c $countSql | Where-Object { $_ } | ForEach-Object { $k, $v = $_.Split('=', 2); $actual[$k] = [long]$v }

    $problems = @()
    foreach ($t in $expected.Keys) {
        if (-not $actual.ContainsKey($t)) { $problems += "missing table $t" }
        elseif ($actual[$t] -ne $expected[$t]) { $problems += "$t has $($actual[$t]) rows, backup recorded $($expected[$t])" }
    }
    foreach ($t in $actual.Keys) { if (-not $expected.ContainsKey($t)) { $problems += "unexpected table $t" } }
    if ($problems) { throw ("Restore verification FAILED:`n  " + ($problems -join "`n  ")) }

    $rows = ($actual.Values | Measure-Object -Sum).Sum
    Write-Host "Restore verified: $($actual.Count) tables, $rows rows, all counts match, checksum OK."
}
finally {
    & $psql @common -d postgres -q -c "drop database if exists $scratch" 2>$null
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
