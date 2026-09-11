[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [Parameter(Mandatory)][uri]$TimestampUrl
)
$ErrorActionPreference = 'Stop'
$stage = (Resolve-Path $Directory).Path
$certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
[xml]$manifest = Get-Content "$stage/AppxManifest.xml"
if ($certificate.Subject -ne $manifest.Package.Identity.Publisher) {
    throw 'The certificate subject must equal the manifest Publisher. Rebuild the manifest with the intended publisher before signing.'
}
if (!$certificate.HasPrivateKey) { throw 'The signing certificate has no private key.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('sc-identity-' + [guid]::NewGuid().ToString('N'))
New-Item $work -ItemType Directory | Out-Null
try {
    Copy-Item "$stage/AppxManifest.xml" $work
    Copy-Item "$stage/Assets" $work -Recurse
    & MakeAppx.exe pack /o /d $work /nv /p "$stage/ShellCommand.Identity.msix"
    if ($LASTEXITCODE) { throw 'MakeAppx failed.' }
    & SignTool.exe sign /fd SHA256 /sha1 $CertificateThumbprint /tr $TimestampUrl.AbsoluteUri /td SHA256 "$stage/ShellCommand.Identity.msix"
    if ($LASTEXITCODE) { throw 'SignTool failed.' }
    & SignTool.exe verify /pa "$stage/ShellCommand.Identity.msix"
    if ($LASTEXITCODE) { throw 'Signature verification failed.' }
    # The signed identity is part of the immutable build, so refresh its inventory.
    $files = @(Get-ChildItem $stage -Recurse -File | Where-Object Name -ne 'build-manifest.json' | Sort-Object FullName | ForEach-Object {
        @{ Path=$_.FullName.Substring($stage.Length+1).Replace('\','/'); Sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    })
    @{ Protocol=2; Files=$files } | ConvertTo-Json -Depth 4 | Set-Content "$stage/build-manifest.json" -Encoding utf8
} finally { Remove-Item $work -Recurse -Force }
