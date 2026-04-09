param(
    [Parameter(Mandatory)]
    [string]$Version
)

# Validate semver format (major.minor.patch, optional pre-release)
if ($Version -notmatch '^\d+\.\d+\.\d+(-[\w.]+)?$') {
    Write-Host "ERROR: Invalid version format '$Version'. Expected: X.Y.Z or X.Y.Z-beta.1" -ForegroundColor Red
    exit 1
}

$root = Split-Path $PSScriptRoot -Parent

# ── AppInfo.cs ────────────────────────────────────────────────────────
$appInfoPath = "$root\src\BrowserMux.Core\AppInfo.cs"
$appInfo = Get-Content $appInfoPath -Raw
$appInfo = $appInfo -replace '(AppVersion\s*=\s*")[^"]+(")', "`${1}$Version`${2}"
Set-Content $appInfoPath $appInfo -NoNewline
Write-Host "Updated $appInfoPath" -ForegroundColor Green

# ── setup.iss ─────────────────────────────────────────────────────────
$issPath = "$root\installer\setup.iss"
$iss = Get-Content $issPath -Raw
$iss = $iss -replace '(#define\s+AppVersion\s+")[^"]+(")', "`${1}$Version`${2}"
Set-Content $issPath $iss -NoNewline
Write-Host "Updated $issPath" -ForegroundColor Green

Write-Host "Version set to $Version" -ForegroundColor Cyan
