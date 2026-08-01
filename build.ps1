param(
    [string]$Config = "Debug",
    [switch]$Clean,
    [switch]$Run,
    [string]$Url = "https://github.com"
)

# ─── App identity (keep in sync with BrowserMux.Core/AppInfo.cs) ───
$appName = "BrowserMux"
# ─────────────────────────────────────────────────────────────────────

$root = $PSScriptRoot
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe"
$outDir  = "$root\out"
$exe     = "$outDir\$appName.exe"

# Target framework folder is resolved by glob, never hardcoded: a TFM bump used to leave this
# script copying from a path that no longer existed.
function Resolve-TfmDir([string]$binRoot) {
    $dir = Get-ChildItem "$binRoot\net*-windows*" -Directory -ErrorAction SilentlyContinue |
           Sort-Object Name -Descending | Select-Object -First 1
    if ($dir) { return $dir.FullName }
    return $null
}

# Stop running instance (may need elevation if tray icon holds a handle)
$procs = Get-Process $appName -ErrorAction SilentlyContinue
if ($procs) {
    try { $procs | Stop-Process -Force -ErrorAction Stop }
    catch { & taskkill.exe /F /PID ($procs | ForEach-Object { $_.Id }) 2>$null }
    Start-Sleep -Milliseconds 500
}

if ($Clean) {
    Write-Host "Cleaning obj/ and bin/ ..." -ForegroundColor Yellow
    Get-ChildItem $root\src -Recurse -Directory -Filter obj | Remove-Item -Recurse -Force
    Get-ChildItem $root\src -Recurse -Directory -Filter bin | Remove-Item -Recurse -Force
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
}

Write-Host "Restoring NuGet packages..." -ForegroundColor DarkGray
& $msbuild "$root\BrowserMux.sln" /t:Restore /p:Configuration=$Config /m /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "RESTORE FAILED" -ForegroundColor Red; exit 1 }

Write-Host "Building ($Config)..." -ForegroundColor Cyan
& $msbuild "$root\BrowserMux.sln" /t:Build /p:Configuration=$Config /m /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

Write-Host "Build OK" -ForegroundColor Green

# Deploy the full app to out/ (WinUI 3 runtime included)
Write-Host "Deploying to $outDir ..." -ForegroundColor DarkGray
$appBin = Resolve-TfmDir "$root\src\$appName.App\bin\x64\$Config"
if (-not $appBin) {
    Write-Host "DEPLOY FAILED: no build output under src\$appName.App\bin\x64\$Config" -ForegroundColor Red
    exit 1
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item "$appBin\*" -Destination $outDir -Recurse -Force

# Copy Handler to out/ (side by side with BrowserMux.exe). Missing is fatal: an out/ without
# the handler looks fine until links stop opening, so don't skip it quietly.
$handlerBin = Resolve-TfmDir "$root\src\$appName.Handler\bin\x64\$Config"
$handlerSrc = if ($handlerBin) { Join-Path $handlerBin "$appName.Handler.exe" } else { $null }
if (-not $handlerSrc -or -not (Test-Path $handlerSrc)) {
    Write-Host "DEPLOY FAILED: $appName.Handler.exe not found under src\$appName.Handler\bin\x64\$Config" -ForegroundColor Red
    exit 1
}
Copy-Item $handlerSrc -Destination $outDir -Force
Write-Host "Handler deployed to $outDir" -ForegroundColor DarkGray

if ($Run) {
    Write-Host "Launching: $Url" -ForegroundColor Cyan
    Start-Process $exe -ArgumentList "`"$Url`""
}
