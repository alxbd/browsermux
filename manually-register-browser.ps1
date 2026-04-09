# ─── App identity (Dev channel — keep in sync with BrowserMux.Core/AppInfo.cs #if DEBUG) ───
$appName    = "BrowserMux Dev"
$progId     = "BrowserMuxDevURL"
# Disk filename stays "BrowserMux.exe" — only the display name and identifiers change.
$exeBase    = "BrowserMux"
# ────────────────────────────────────────────────────────────────────────────────────────────

$root = $PSScriptRoot
$handlerExe = "$root\src\BrowserMux.Handler\bin\x64\Debug\net9.0-windows10.0.22621.0\$exeBase.Handler.exe"
$appExe     = "$root\src\BrowserMux.App\bin\x64\Debug\net9.0-windows10.0.22621.0\$exeBase.exe"
$capKey     = "Software\Clients\StartMenuInternet\$appName\Capabilities"

if (!(Test-Path $handlerExe)) { Write-Error "Handler not found: $handlerExe"; exit 1 }
if (!(Test-Path $appExe))     { Write-Error "App not found: $appExe"; exit 1 }

Write-Host "Registering BrowserMux..." -ForegroundColor Cyan

# 1. ProgId — the handler that receives URLs
$classKey = "HKCU:\Software\Classes\$progId"
New-Item -Path $classKey -Force | Out-Null
Set-ItemProperty -Path $classKey -Name "(default)"    -Value "BrowserMux URL Handler"
Set-ItemProperty -Path $classKey -Name "URL Protocol" -Value ""
New-Item -Path "$classKey\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$classKey\shell\open\command" -Name "(default)" -Value "`"$handlerExe`" `"%1`""

# 2. Capabilities — declares BrowserMux as a browser
$cap = "HKCU:\$capKey"
New-Item -Path "$cap\URLAssociations" -Force | Out-Null
Set-ItemProperty -Path $cap -Name "ApplicationName"        -Value $appName
Set-ItemProperty -Path $cap -Name "ApplicationDescription" -Value "Browser selector"
Set-ItemProperty -Path "$cap\URLAssociations" -Name "http"  -Value $progId
Set-ItemProperty -Path "$cap\URLAssociations" -Name "https" -Value $progId

# 3. RegisteredApplications — makes it visible in Settings > Default Apps
New-Item -Path "HKCU:\Software\RegisteredApplications" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\RegisteredApplications" -Name $appName -Value $capKey

Write-Host "Done. Opening default apps settings..." -ForegroundColor Green
Start-Process "ms-settings:defaultapps"
