[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Stop-Process -Name explorer -Force
Start-Process explorer.exe
