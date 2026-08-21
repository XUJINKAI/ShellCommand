[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
$source = Join-Path $root "artifacts\$Configuration"
$stage = Join-Path $root 'artifacts\dev-package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Assets') | Out-Null
Copy-Item (Join-Path $source '*') $stage -Recurse -Force
Copy-Item (Join-Path $root 'docs\screenshot\win10-preview.png') (Join-Path $stage 'Assets\StoreLogo.png') -Force
Copy-Item (Join-Path $root 'docs\screenshot\win10-preview.png') (Join-Path $stage 'Assets\Square150x150Logo.png') -Force
Copy-Item (Join-Path $root 'docs\screenshot\win10-preview.png') (Join-Path $stage 'Assets\Square44x44Logo.png') -Force
Copy-Item (Join-Path $root 'packaging\manifest\AppxManifest.xml') $stage -Force
if (-not (Test-Path (Join-Path $stage 'ShellCommand.Explorer.dll'))) { throw 'Native Explorer DLL was not built. Install the MSVC workload and rebuild.' }
$manifest = Join-Path $stage 'AppxManifest.xml'
Add-AppxPackage -Register $manifest -ExternalLocation $stage -ForceApplicationShutdown
$broker = Join-Path $stage 'ShellCommand.Broker.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item $runKey -Force | Out-Null
New-ItemProperty $runKey -Name 'ShellCommand11.Broker' -Value "`"$broker`"" -PropertyType String -Force | Out-Null
$config = Join-Path $env:LOCALAPPDATA 'ShellCommand11\config'
New-Item -ItemType Directory -Force -Path $config | Out-Null
$global = Join-Path $config 'global.shellcommand.yaml'
if (-not (Test-Path $global)) { Set-Content -Path $global -Value "GlobalCommands: []`nFunctions:`n  CopyPath: false`n  EditGlobal: false`n" -Encoding utf8 }
Start-Process $broker
Write-Host 'Dev registration complete. Run Restart-Explorer.ps1 to reload Explorer.'
