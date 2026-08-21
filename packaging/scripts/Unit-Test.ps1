[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
dotnet test (Join-Path $root 'ShellCommand11.sln') --no-restore
