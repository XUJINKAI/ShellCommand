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
Copy-Item (Join-Path $root 'packaging\manifest\AppxManifest.xml') $stage -Force
Copy-Item (Join-Path $root 'docs\screenshot\win10-preview.png') (Join-Path $stage 'Assets\StoreLogo.png') -Force
Copy-Item (Join-Path $root 'docs\screenshot\win10-preview.png') (Join-Path $stage 'Assets\Square150x150Logo.png') -Force
Copy-Item (Join-Path $root 'docs\screenshot\win10-preview.png') (Join-Path $stage 'Assets\Square44x44Logo.png') -Force
Write-Host "Package staging output: $stage"
