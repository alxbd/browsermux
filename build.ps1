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
$appBin  = "$root\src\$appName.App\bin\x64\$Config\net9.0-windows10.0.22621.0"
$outDir  = "$root\out"
$exe     = "$outDir\$appName.exe"

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
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item "$appBin\*" -Destination $outDir -Recurse -Force

# Copy Handler to out/ (side by side with BrowserMux.exe)
$handlerSrc = "$root\src\$appName.Handler\bin\x64\$Config\net9.0-windows10.0.22621.0\$appName.Handler.exe"
if (Test-Path $handlerSrc) {
    Copy-Item $handlerSrc -Destination $outDir -Force
    Write-Host "Handler deployed to $outDir" -ForegroundColor DarkGray
}

if ($Run) {
    Write-Host "Launching: $Url" -ForegroundColor Cyan
    Start-Process $exe -ArgumentList "`"$Url`""
}
