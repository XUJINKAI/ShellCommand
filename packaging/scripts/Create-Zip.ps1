[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Destination
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$destination = [System.IO.Path]::GetFullPath($Destination)
$destinationParent = [System.IO.Path]::GetDirectoryName($destination)
if ($destinationParent -and -not (Test-Path -LiteralPath $destinationParent)) {
    [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $source,
    $destination,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$archive = [System.IO.Compression.ZipFile]::OpenRead($destination)
try {
    $names = @($archive.Entries | ForEach-Object FullName)
    $required = @(
        'ShellCommand.exe',
        'ShellCommand.Broker.exe',
        'ShellCommand.Explorer.dll',
        'AppxManifest.xml',
        'Uninstall.cmd')

    $missing = @($required | Where-Object { $names -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "ZIP is missing required root entries: $($missing -join ', ')"
    }

    $invalidNames = @($names | Where-Object { $_.StartsWith('./', [System.StringComparison]::Ordinal) -or $_ -match '(^|[\\/])win-x64([\\/]|$)' })
    if ($invalidNames.Count -gt 0) {
        throw "ZIP contains invalid or nested runtime entries: $($invalidNames -join ', ')"
    }
}
finally {
    $archive.Dispose()
}
