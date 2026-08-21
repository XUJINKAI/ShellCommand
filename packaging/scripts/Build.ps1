[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $root "artifacts\$Configuration"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
dotnet build (Join-Path $root 'ShellCommand11.sln') -c $Configuration
$nativeBuilder = Join-Path $PSScriptRoot 'Build-Native.cmd'
if (Test-Path $nativeBuilder) {
  & cmd.exe /d /c $nativeBuilder $Configuration
  if ($LASTEXITCODE -ne 0) { throw "Native Explorer build failed with exit code $LASTEXITCODE." }
}
Copy-Item (Join-Path $root "src\ShellCommand.App\bin\$Configuration\net10.0-windows\*") $artifacts -Recurse -Force
Copy-Item (Join-Path $root "src\ShellCommand.Broker\bin\$Configuration\net10.0\*") $artifacts -Recurse -Force
$native = Join-Path $root "src\ShellCommand.Explorer\x64\$Configuration\ShellCommand.Explorer.dll"
if (Test-Path $native) { Copy-Item $native $artifacts -Force }
Write-Host "Build output: $artifacts"
