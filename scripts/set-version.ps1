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

# ── Directory.Build.props ─────────────────────────────────────────────
$propsPath = "$root\Directory.Build.props"
$props = Get-Content $propsPath -Raw
$props = $props -replace '(<Version>)[^<]+(</Version>)', "`${1}$Version`${2}"
$props = $props -replace '(<FileVersion>)[^<]+(</FileVersion>)', "`${1}$Version.0`${2}"
$props = $props -replace '(<InformationalVersion>)[^<]+(</InformationalVersion>)', "`${1}$Version`${2}"
Set-Content $propsPath $props -NoNewline
Write-Host "Updated $propsPath" -ForegroundColor Green

# ── setup.iss ─────────────────────────────────────────────────────────
$issPath = "$root\installer\setup.iss"
$iss = Get-Content $issPath -Raw
$iss = $iss -replace '(#define\s+AppVersion\s+")[^"]+(")', "`${1}$Version`${2}"
Set-Content $issPath $iss -NoNewline
Write-Host "Updated $issPath" -ForegroundColor Green

Write-Host "Version set to $Version" -ForegroundColor Cyan
