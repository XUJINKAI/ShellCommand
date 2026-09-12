[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory, [string]$ComProbe)
$ErrorActionPreference = 'Stop'
# Run on a disposable Windows test user with development deployment enabled.
# Never replace an existing user's integration as part of a smoke test.
if (Get-AppxPackage -Name ShellCommand11) { throw 'Use a clean test user with no ShellCommand registration.' }
$source = Join-Path $env:TEMP ('ShellCommand-P0-' + [guid]::NewGuid().ToString('N'))
$data = Join-Path $env:LOCALAPPDATA 'ShellCommand11'
$sentinel = Join-Path $data ('config/p0-preserve-' + [guid]::NewGuid().ToString('N') + '.txt')
Copy-Item $PackageDirectory $source -Recurse
New-Item (Split-Path $sentinel) -ItemType Directory -Force | Out-Null
Set-Content $sentinel 'preserve during install and uninstall'
function Run-App([string]$exe, [string]$argument) {
    $stdout = Join-Path $env:TEMP ('sc-install-' + [guid]::NewGuid().ToString('N') + '.out')
    $stderr = $stdout + '.err'
    try {
        $process = Start-Process $exe -ArgumentList $argument -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        if (!$process.WaitForExit(90000)) { $process.Kill($true); throw 'Installation operation timed out' }
        $process.Refresh()
        if ($process.ExitCode -ne 0) {
            $details = (Get-Content $stdout,$stderr -Raw -ErrorAction SilentlyContinue) -join [Environment]::NewLine
            throw "Operation $argument failed ($($process.ExitCode)): $details"
        }
    } finally { Remove-Item $stdout,$stderr -Force -ErrorAction SilentlyContinue }
}
$installed = $false
$recoveryExe = "$source/ShellCommand.exe"
try {
    Run-App "$source/ShellCommand.exe" '--install'
    $installed = $true
    $record = Get-Content "$data/state/installation.json" | ConvertFrom-Json
    $recoveryExe = "$($record.Root)/ShellCommand.exe"
    Remove-Item $source -Recurse -Force
    Run-App "$($record.Root)/ShellCommand.exe" '--check-installation'
    if ($ComProbe) {
        $probe = Start-Process $ComProbe -ArgumentList '--registered' -PassThru -NoNewWindow
        if (!$probe.WaitForExit(30000)) { $probe.Kill($true); throw 'Registered COM probe timed out.' }
        $probe.Refresh()
        if ($probe.ExitCode -ne 0) { throw "Registered COM probe failed ($($probe.ExitCode))." }
    }
    Write-Host 'Runner health passed after removal of the source. Explorer/Surrogate UI verification is still a separate gate.'
} finally {
    if ($installed) {
        Run-App $recoveryExe '--uninstall'
        if (Get-AppxPackage -Name ShellCommand11) { throw 'Package remains registered.' }
        if ((Get-Content $sentinel) -ne 'preserve during install and uninstall') { throw 'Configuration was not preserved.' }
    }
    if (Test-Path $source) { Remove-Item $source -Recurse -Force }
    Remove-Item $sentinel -ErrorAction SilentlyContinue
}
