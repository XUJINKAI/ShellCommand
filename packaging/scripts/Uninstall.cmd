@echo off
setlocal
rem Emergency removal also works when the .NET runtime or runner is missing.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Get-AppxPackage -Name 'ShellCommand11' | Remove-AppxPackage; Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ShellCommand11.Broker' -ErrorAction SilentlyContinue; $session=(Get-Process -Id $PID).SessionId; $root=Join-Path $env:LOCALAPPDATA 'ShellCommand11\runner\'; Get-Process -Name 'ShellCommand.Broker' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $session -and $_.Path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) } | Stop-Process; Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'ShellCommand11\state\installation.json') -ErrorAction SilentlyContinue"
if errorlevel 1 exit /b 1
echo Integration removed. Configuration and recovery records are preserved.
exit /b 0
