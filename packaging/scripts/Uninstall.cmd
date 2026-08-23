@echo off
setlocal EnableExtensions

echo Removing ShellCommand 11 integration...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; Get-Process -Name 'ShellCommand','ShellCommand.Broker' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; $packages = @(Get-AppxPackage -Name 'ShellCommand11' -ErrorAction SilentlyContinue); foreach ($package in $packages) { Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop }; Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ShellCommand11.Broker' -ErrorAction SilentlyContinue"
if errorlevel 1 (
  echo Could not remove the ShellCommand package. 1>&2
  exit /b 1
)

echo Package registration and Broker startup removed.
echo Restarting Explorer to clear the cached context-menu extension...
taskkill /F /IM explorer.exe >nul 2>&1
start "" explorer.exe
echo ShellCommand 11 has been uninstalled.
exit /b 0
