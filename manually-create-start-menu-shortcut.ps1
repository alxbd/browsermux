$ws = New-Object -ComObject WScript.Shell
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\BrowserMux Dev.lnk'
$target = Join-Path $PSScriptRoot 'src\BrowserMux.App\bin\x64\Debug\net9.0-windows10.0.22621.0\BrowserMux.exe'
if (!(Test-Path $target)) { Write-Error "target missing: $target"; exit 1 }
$s = $ws.CreateShortcut($lnk)
$s.TargetPath = $target
$s.WorkingDirectory = Split-Path $target
$s.IconLocation = "$target,0"
$s.Description = 'BrowserMux Dev build'
$s.Save()
Write-Host "Created: $lnk"
