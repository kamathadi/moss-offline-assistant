$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\Moss'
$model = Join-Path $source 'Models\MossAI.gguf'
$exe = Join-Path $target 'Moss.exe'
$menu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $menu 'Moss.lnk'

if (-not (Test-Path (Join-Path $source 'Moss.exe')) -or -not (Test-Path $model)) {
  throw 'Moss.exe or MossAI is missing. Extract the complete offline package before installing.'
}

Get-Process Moss -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process llama-server -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
New-Item -ItemType Directory -Force -Path $target,$menu | Out-Null
Copy-Item (Join-Path $source '*') $target -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Moss — private writing tools'
$shortcut.Save()

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name Moss -Value ('"' + $exe + '" --background')
$settingsDirectory = Join-Path $env:LOCALAPPDATA 'Moss'
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
$settingsPath = Join-Path $settingsDirectory 'settings.json'
if (-not (Test-Path $settingsPath)) {
  '{"Startup":true}' | Set-Content -Encoding UTF8 $settingsPath
}
Start-Process $exe -ArgumentList '--background'
Write-Host 'Moss is installed. Search for Moss in Start to open Preferences. It will start quietly when you sign in.' -ForegroundColor Green
