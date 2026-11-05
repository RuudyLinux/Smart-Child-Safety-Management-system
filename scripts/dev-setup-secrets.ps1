<#
.SYNOPSIS
  Local development setup for ChildGuard: creates the dev database/user and stores
  generated credentials in .NET user-secrets. Nothing is written to the repository
  and no secret is printed.

.DESCRIPTION
  * ConnectionStrings:ChildGuardDb missing  -> generates a random password, creates
    the `childguard_app` MySQL user + `childguard_db` database, stores the string.
  * -RotateDbPassword                        -> sets a new random password on the
    existing `childguard_app` user and updates user-secrets.
  * SeedAdmin:Email / SeedAdmin:Password missing -> generates them (used only when
    the Admins table is empty).
  * Jwt:Secret missing                       -> generates a 64-byte random secret.

  Read the values later with:  dotnet user-secrets list --project src\ChildGuard.Api
  Development only. Production secrets come from environment variables / a vault.
#>
param(
    [string]$MySql = "C:\xampp\mysql\bin\mysql.exe",
    [string]$RootUser = "root",
    [switch]$RotateDbPassword
)

$ErrorActionPreference = "Stop"
$api = Join-Path $PSScriptRoot "..\src\ChildGuard.Api"

function New-Secret([int]$bytes = 24) {
    $buffer = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    # URL-safe, no quotes/semicolons, so it is safe inside a connection string and SQL literal.
    return ([Convert]::ToBase64String($buffer)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-Secrets {
    $map = @{}
    foreach ($line in (dotnet user-secrets list --project $api 2>$null)) {
        if ($line -match '^\s*(.+?)\s*=\s*(.*)$') { $map[$Matches[1]] = $Matches[2] }
    }
    return $map
}

function Set-Secret([string]$key, [string]$value) {
    dotnet user-secrets set $key $value --project $api | Out-Null
}

function Invoke-MySql([string]$sql) {
    # SQL goes in on stdin so the password never appears in the process list.
    $sql | & $MySql -u $RootUser -h 127.0.0.1 -P 3306
    if ($LASTEXITCODE -ne 0) { throw "mysql exited with $LASTEXITCODE" }
}

$secrets = Get-Secrets

$connectionTemplate = "Server=localhost;Port=3306;Database=childguard_db;User=childguard_app;Password={0};"

if ($RotateDbPassword) {
    # One statement for both host entries, and the secret is saved the moment it
    # succeeds — so the stored password can never drift from the server's.
    # (CREATE/ALTER USER and GRANT update privileges immediately; no FLUSH needed.)
    $password = New-Secret
    Invoke-MySql "ALTER USER 'childguard_app'@'localhost' IDENTIFIED BY '$password', 'childguard_app'@'127.0.0.1' IDENTIFIED BY '$password';"
    Set-Secret "ConnectionStrings:ChildGuardDb" ($connectionTemplate -f $password)
    Write-Host "      [OK] Database password rotated and stored in user-secrets."
}
elseif (-not $secrets.ContainsKey("ConnectionStrings:ChildGuardDb")) {
    $password = New-Secret
    Invoke-MySql @"
CREATE DATABASE IF NOT EXISTS childguard_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'childguard_app'@'localhost' IDENTIFIED BY '$password';
CREATE USER IF NOT EXISTS 'childguard_app'@'127.0.0.1' IDENTIFIED BY '$password';
ALTER USER 'childguard_app'@'localhost' IDENTIFIED BY '$password', 'childguard_app'@'127.0.0.1' IDENTIFIED BY '$password';
"@
    # Saved before GRANT: if GRANT fails, the password still matches and GRANT can be retried.
    Set-Secret "ConnectionStrings:ChildGuardDb" ($connectionTemplate -f $password)
    Invoke-MySql "GRANT ALL PRIVILEGES ON childguard_db.* TO 'childguard_app'@'localhost', 'childguard_app'@'127.0.0.1';"
    Write-Host "      [OK] Database user credentials generated and stored in user-secrets."
}

if (-not $secrets.ContainsKey("SeedAdmin:Email")) { Set-Secret "SeedAdmin:Email" "admin@childguard.local" }
if (-not $secrets.ContainsKey("SeedAdmin:Password")) {
    Set-Secret "SeedAdmin:Password" ("Cg-" + (New-Secret 12) + "9a!")
    Write-Host "      [OK] Initial admin password generated and stored in user-secrets."
}
if (-not $secrets.ContainsKey("Jwt:Secret")) {
    Set-Secret "Jwt:Secret" (New-Secret 64)
    Write-Host "      [OK] JWT signing secret generated and stored in user-secrets."
}
