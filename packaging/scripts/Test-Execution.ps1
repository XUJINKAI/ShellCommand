param([string]$Directory, [string]$Probe)
$ErrorActionPreference = 'Stop'
$app = Join-Path (Resolve-Path $Directory) 'ShellCommand.exe'
$probePath = (Resolve-Path $Probe).Path
$root = Join-Path $env:LOCALAPPDATA ('sc-execution-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$ids = @()
function Invoke-Plan($actions) {
    $id = [guid]::NewGuid()
    $script:ids += $id
    $json = @{ Id=$id; Plan=@{ Title='Execution integration'; SourcePath='test'; Actions=@($actions) } } | ConvertTo-Json -Depth 15 -Compress
    $start = [Diagnostics.ProcessStartInfo]::new($app)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.ArgumentList.Add('--execute')
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Write($json)
        $process.StandardInput.Close()
        if (!$process.WaitForExit(30000)) { $process.Kill($true); throw 'Executor deadline exceeded' }
        if ($process.ExitCode -ne 0) { throw "Executor exit $($process.ExitCode)" }
        $journal = Get-Content (Join-Path $env:LOCALAPPDATA "ShellCommand11/state/tasks/$($id.ToString('N')).json") -Raw | ConvertFrom-Json
        if ($journal.Status -ne 'completed') { throw "Unexpected journal status: $($journal.Status)" }
    } finally { $process.Dispose() }
}
function Invoke-Preparation {
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path (Resolve-Path $Directory) 'ShellCommand.Broker.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    $start.ArgumentList.Add('--prepare')
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        $request = @{ Directory=$root; DataDirectory=$root; AppDirectory=(Resolve-Path $Directory).Path } | ConvertTo-Json -Compress
        $process.StandardInput.Write($request)
        $process.StandardInput.Close()
        if (!$process.WaitForExit(10000)) { $process.Kill($true); throw 'Preparation deadline exceeded' }
        if ($process.ExitCode -ne 0) { throw $errors.GetAwaiter().GetResult() }
        return ($output.GetAwaiter().GetResult() | ConvertFrom-Json)
    } finally { $process.Dispose() }
}
try {
    New-Item -ItemType Directory -Path (Join-Path $root 'config') | Out-Null
    $config = Join-Path $root 'config/global.shellcommand.yaml'
    [IO.File]::WriteAllText($config, "version: 2
menu:
 - {id: a, title: 中文菜单, copy: literal}
")
    $prepared = Invoke-Preparation
    if ($prepared.Global.Config.Menu[0].Title -cne '中文菜单') { throw 'Isolated YAML preparation failed' }
    [IO.File]::WriteAllText($config, 'invalid: [')
    $prepared = Invoke-Preparation
    if ($prepared.Global.Config.Menu[0].Title -cne '中文菜单' -or !$prepared.Global.Diagnostics.Count) { throw 'Isolated last-known-good failed' }
    $destination = Join-Path $root 'argv.json'
    $expected = @('', 'space path', "one's file", '中文 & %PATH% ! ^ | < >', 'quote"inside', 'C:\ends with slash\')
    Invoke-Plan @(@{ Kind='run'; Exe=$probePath; Args=$expected; Cwd=$root; Output='hidden'; Env=@{ SC_PROBE_DEST=$destination; SC_PROBE_VALUE='literal & 中文' } })
    $actual = Get-Content $destination -Raw | ConvertFrom-Json
    if (($actual.Args | ConvertTo-Json -Compress) -cne ($expected | ConvertTo-Json -Compress)) { throw ("Arguments were changed. Expected: " + ($expected | ConvertTo-Json -Compress) + " Actual: " + ($actual.Args | ConvertTo-Json -Compress)) }
    if ($actual.Cwd -ne $root -or $actual.Value -ne 'literal & 中文') { throw ("Cwd or environment changed. Expected cwd: " + $root + " Actual: " + ($actual | ConvertTo-Json -Compress)) }
    $literal = Join-Path $root 'script.txt'
    $scriptText = '[IO.File]::WriteAllText($env:SC_PROBE_DEST, ''${directory}'')'
    Invoke-Plan @(@{ Kind='script'; Shell='powershell'; Cwd=$root; Output='hidden'; Text=$scriptText; Env=@{ SC_PROBE_DEST=$literal } })
    if ((Get-Content $literal -Raw) -cne '${directory}') { throw 'Script literal was expanded' }
    Write-Output 'Packaged preparation/LKG and executor: exact argv/cwd/env, 4 MiB stdout+stderr drainage and literal script passed.'
} finally {
    Remove-Item $root -Recurse -Force
    foreach ($id in $ids) { Remove-Item (Join-Path $env:LOCALAPPDATA "ShellCommand11/state/tasks/$($id.ToString('N')).json") -Force -ErrorAction SilentlyContinue }
}
