[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$stage = (Resolve-Path $Directory).Path
# Publish output must contain bundled application code, not a bundled framework.
if (Test-Path (Join-Path $stage 'coreclr.dll')) { throw 'Runtime must not be shipped.' }
Get-ChildItem $stage -File | Where-Object { $_.Name -notin @('ShellCommand.exe','ShellCommand.Broker.exe','ShellCommand.Explorer.dll','ShellCommand.Identity.msix') } | Remove-Item
Copy-Item "$root/packaging/manifest/AppxManifest.xml" $stage
Copy-Item "$PSScriptRoot/Uninstall.cmd" $stage
New-Item "$stage/Assets" -ItemType Directory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
foreach ($asset in @{ StoreLogo=50; Square150x150Logo=150; Square44x44Logo=44 }.GetEnumerator()) {
    $bitmap = [Drawing.Bitmap]::new($asset.Value, $asset.Value)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::FromArgb(32,41,61))
        $pen = [Drawing.Pen]::new([Drawing.Color]::White, [single]($asset.Value / 12))
        try {
            $graphics.DrawLines($pen, [Drawing.PointF[]]@(
                [Drawing.PointF]::new($asset.Value*.24,$asset.Value*.3),
                [Drawing.PointF]::new($asset.Value*.44,$asset.Value*.5),
                [Drawing.PointF]::new($asset.Value*.24,$asset.Value*.7)))
            $graphics.DrawLine($pen,[single]($asset.Value*.52),[single]($asset.Value*.7),[single]($asset.Value*.78),[single]($asset.Value*.7))
        } finally { $pen.Dispose() }
        $bitmap.Save("$stage/Assets/$($asset.Key).png", [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
@'
ShellCommand v2 P0 development build
Requires Windows 11 x64 and .NET 10 Desktop Runtime x64 (not included).

This artifact verifies installation and native IPC failures. YAML v2 and the new
menu editor are not implemented yet. Do not use it as the completed redesign.

Production installation needs a trusted signed ShellCommand.Identity.msix.
--developer-install explicitly uses manifest registration on a developer machine.
--uninstall / --disable-integration removes integration without loading YAML.
Uninstall.cmd also works without .NET and does not restart Explorer.

Data: %LOCALAPPDATA%\ShellCommand11\config
Programs: %LOCALAPPDATA%\ShellCommand11\runner\<build-id>
Configuration and third-party recovery records are preserved by uninstall.
'@ | Set-Content "$stage/README.txt" -Encoding utf8
$files = @(Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    @{ Path=$_.FullName.Substring($stage.Length+1).Replace('\','/'); Sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash }
})
@{ Protocol=3; Files=$files } | ConvertTo-Json -Depth 4 | Set-Content "$stage/build-manifest.json" -Encoding utf8
