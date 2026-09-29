$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name Moss -ErrorAction SilentlyContinue
Get-Process Moss -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process llama-server -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Moss.lnk') -Force -ErrorAction SilentlyContinue
$target = Join-Path $env:LOCALAPPDATA 'Programs\Moss'
if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Write-Host 'Moss and its local engine were removed. Preferences remain in LocalAppData\Moss.' -ForegroundColor Green
