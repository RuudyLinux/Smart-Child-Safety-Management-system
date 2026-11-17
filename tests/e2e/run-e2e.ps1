# Runs the Playwright E2E suite. The suite starts its own isolated stack (throwaway
# database, API :5375, Admin Web :5180, stub face service :8101) and tears it down;
# it never touches childguard_db or a running dev system. Needs local MySQL/MariaDB
# reachable as root without a password (XAMPP default), like the integration tests.
#   .\run-e2e.ps1                      all projects
#   .\run-e2e.ps1 --project=chromium   any Playwright arguments
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path node_modules)) { npm ci; npx playwright install chromium firefox webkit }
    npx playwright test @args
    node support/summary.js
    Write-Host "HTML report: npx playwright show-report  (from tests\e2e)"
} finally { Pop-Location }
