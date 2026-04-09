#!/usr/bin/env pwsh
# Builds BrowserMux in Release mode and compiles the Inno Setup installer.
# Output: dist\BrowserMux-Setup-<version>.exe

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# 1. Build Release + deploy to out\
& "$root\build.ps1" -Config Release
if ($LASTEXITCODE -ne 0) { throw "Release build failed" }

# 2. Locate iscc.exe (winget installs Inno Setup per-user under LocalAppData)
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 not found. Install with: winget install -e --id JRSoftware.InnoSetup"
}

# 3. Compile installer
& $iscc "$root\installer\setup.iss"
if ($LASTEXITCODE -ne 0) { throw "iscc failed" }

# 4. Show result
$installer = Get-ChildItem "$root\dist\BrowserMux-Setup-*.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($installer) {
    $sizeMb = [math]::Round($installer.Length / 1MB, 1)
    Write-Host ""
    Write-Host "Installer ready: $($installer.FullName) ($sizeMb MB)" -ForegroundColor Green
}
