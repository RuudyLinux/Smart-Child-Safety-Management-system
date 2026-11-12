<#
.SYNOPSIS
  Takes a verified logical backup of the ChildGuard PostgreSQL database (Neon) to a local folder.

.DESCRIPTION
  Needs a DIRECT (non-pooled) connection string in the environment variable CHILDGUARD_BACKUP_CONNECTION
  (a postgres:// URI). Use a role that can read every table; never print or commit the value.
  Writes  childguard-<UTC timestamp>.dump  (pg_dump custom format), a .sha256 file and a .counts.txt file
  (exact row count per table, used by verify-restore.ps1). The archive is checked with pg_restore --list.
  Face embeddings and photos are already encrypted by the application (Biometrics key), but the rest of the
  dump is NOT encrypted: keep the folder private (default is under your user profile, outside the repo).
  Only files this script created (childguard-*.dump and its companions) are rotated, keeping the newest -Keep.

.EXAMPLE
  $env:CHILDGUARD_BACKUP_CONNECTION = '<direct connection string>'
  powershell -ExecutionPolicy Bypass -File .\scripts\backup-db.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $env:USERPROFILE 'ChildGuardBackups'),
    [int]$Keep = 14,
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference = 'Stop'

$conn = $env:CHILDGUARD_BACKUP_CONNECTION
if ([string]::IsNullOrWhiteSpace($conn)) { throw 'Set CHILDGUARD_BACKUP_CONNECTION to the direct (non-pooled) connection string.' }
if ($conn -match '-pooler') { throw 'Use the DIRECT connection string (host without "-pooler"); pg_dump needs a session connection.' }
foreach ($tool in 'pg_dump.exe', 'pg_restore.exe', 'psql.exe') {
    if (-not (Test-Path (Join-Path $PgBin $tool))) { throw "$tool not found in $PgBin (install PostgreSQL client tools 17 or newer, or pass -PgBin)." }
}

# Split the URI so the password travels in PGPASSWORD, not on a command line.
$uri = [Uri]$conn
$userInfo = $uri.UserInfo.Split(':', 2)
$env:PGPASSWORD = [Uri]::UnescapeDataString($userInfo[1])
$env:PGSSLMODE = 'require'
$db = $uri.AbsolutePath.TrimStart('/')
$common = @('-h', $uri.Host, '-p', $(if ($uri.Port -gt 0) { $uri.Port } else { 5432 }), '-U', [Uri]::UnescapeDataString($userInfo[0]))

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$file = Join-Path $OutDir "childguard-$stamp.dump"

try {
    & (Join-Path $PgBin 'pg_dump.exe') @common -d $db -Fc --no-owner --no-privileges -f $file
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE)." }

    & (Join-Path $PgBin 'pg_restore.exe') --list $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The archive does not read back (pg_restore --list failed).' }

    $countSql = @'
select table_name || '=' || (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text
from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1;
'@
    $counts = & (Join-Path $PgBin 'psql.exe') @common -d $db -At -c $countSql
    if ($LASTEXITCODE -ne 0) { throw 'Could not count rows for the verification file.' }
    $counts | Set-Content -Encoding utf8 "$file.counts.txt"
    (Get-FileHash $file -Algorithm SHA256).Hash | Set-Content -Encoding ascii "$file.sha256"
}
catch {
    Remove-Item -ErrorAction SilentlyContinue $file, "$file.counts.txt", "$file.sha256"
    throw
}
finally {
    Remove-Item Env:PGPASSWORD, Env:PGSSLMODE -ErrorAction SilentlyContinue
}

$size = [math]::Round((Get-Item $file).Length / 1KB, 1)
Write-Host "Backup written: $file ($size KB, $($counts.Count) tables)"

# Rotate only our own files.
$old = Get-ChildItem $OutDir -Filter 'childguard-*.dump' | Sort-Object Name -Descending | Select-Object -Skip $Keep
foreach ($d in $old) {
    Remove-Item $d.FullName, "$($d.FullName).sha256", "$($d.FullName).counts.txt" -ErrorAction SilentlyContinue
    Write-Host "Rotated out: $($d.Name)"
}
Write-Output $file
