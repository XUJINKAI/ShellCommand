[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
# Run on a disposable Windows 11 VM with Developer Mode off and a trusted package.
# Never replace an existing user's integration as part of a smoke test.
if (Get-AppxPackage -Name ShellCommand11) { throw 'Use a clean test user with no ShellCommand registration.' }
if (!(Test-Path "$PackageDirectory/ShellCommand.Identity.msix")) { throw 'A trusted signed identity package is required.' }
$source = Join-Path $env:TEMP ('ShellCommand-P0-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $env:LOCALAPPDATA 'ShellCommand11'
$sentinel = Join-Path $data ('config/p0-preserve-' + [guid]::NewGuid().ToString('N') + '.txt')
Copy-Item $PackageDirectory $source -Recurse
New-Item (Split-Path $sentinel) -ItemType Directory -Force | Out-Null
Set-Content $sentinel 'preserve during install and uninstall'
$installed = $false
$recoveryExe = "$source/ShellCommand.exe"
try {
    $process = Start-Process "$source/ShellCommand.exe" -ArgumentList '--install' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Install failed: $($process.ExitCode)" }
    $installed = $true
    $record = Get-Content "$data/state/installation.json" | ConvertFrom-Json
    $recoveryExe = "$($record.Root)/ShellCommand.exe"
    Remove-Item $source -Recurse -Force
    $process = Start-Process "$($record.Root)/ShellCommand.exe" -ArgumentList '--check-installation' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Health check failed after removing the download directory.' }
    Write-Host 'Runner health passed after removal of the source. Explorer/Surrogate UI verification is still a separate gate.'
} finally {
    if ($installed) {
        $process = Start-Process $recoveryExe -ArgumentList '--uninstall' -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Uninstall failed.' }
        if (Get-AppxPackage -Name ShellCommand11) { throw 'Package remains registered.' }
        if ((Get-Content $sentinel) -ne 'preserve during install and uninstall') { throw 'Configuration was not preserved.' }
    }
    if (Test-Path $source) { Remove-Item $source -Recurse -Force }
    Remove-Item $sentinel -ErrorAction SilentlyContinue
}
