[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
dotnet test (Join-Path $root 'tests\ShellCommand.Broker.Tests\ShellCommand.Broker.Tests.csproj') --no-restore
Write-Host 'Managed IPC integration checks passed. Windows Explorer/package checks require a registered Windows 11 shell session.'
