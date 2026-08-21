[CmdletBinding()]
param([switch]$PurgeUserData)
$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty $runKey -Name 'ShellCommand11.Broker' -ErrorAction SilentlyContinue
Get-Process ShellCommand.Broker -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-AppxPackage -Name 'ShellCommand11' -ErrorAction SilentlyContinue | Remove-AppxPackage
if ($PurgeUserData) {
  $state = Join-Path $env:LOCALAPPDATA 'ShellCommand11'
  if (Test-Path $state) { Remove-Item $state -Recurse -Force }
}
Write-Host 'Dev integration removed. User config is preserved unless -PurgeUserData was supplied.'
