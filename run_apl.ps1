# run_apl.ps1 — Run APL scripts via dyascript.exe
# Usage: .\run_apl.ps1 <script.apls> [timeout_seconds]
#
# Substitutes ##DLLPATH## with the published DLL path.
# Sets WorkingDirectory and PATH so the shim can find _impl.

param(
    [Parameter(Mandatory=$true)]
    [string]$ScriptPath,
    [int]$Timeout = 60
)

$ScriptPath = (Resolve-Path $ScriptPath).Path
$ProjectDir = $PSScriptRoot
$publishDir = Join-Path $ProjectDir "src\Dyalog.OTel\bin\Release\net10.0\win-x64\publish"
$DllPath    = Join-Path $publishDir "Dyalog.OTel.dll"
$dyascript  = "D:\devel\dyalog\20.0\dyascript.exe"

# --- placeholder substitution ---
$content = [System.IO.File]::ReadAllText($ScriptPath, [System.Text.Encoding]::UTF8)
$needsSubst = $content.Contains('##DLLPATH##')
$runPath = $ScriptPath

if ($needsSubst) {
    $content = $content -replace '##DLLPATH##', $DllPath
    $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(),
           [System.IO.Path]::GetFileName($ScriptPath))
    [System.IO.File]::WriteAllText($tmp, $content, [System.Text.UTF8Encoding]::new($false))
    $runPath = $tmp
}

# --- auto-detect companion .ini for OTEL config ---
$iniPath = [System.IO.Path]::ChangeExtension($ScriptPath, '.ini')
if (Test-Path $iniPath) {
    $env:DYALOG_OTEL_CONFIG = (Resolve-Path $iniPath).Path
    Write-Host "Using OTEL config: $($env:DYALOG_OTEL_CONFIG)"
}

# --- launch dyascript.exe ---
$env:DYALOG_NOPOPUPS = "1"
$env:ErrorOnExternalException = "1"
$env:PATH = "$publishDir;$env:PATH"

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName  = $dyascript
$psi.Arguments = "-script `"$runPath`""
$psi.UseShellExecute = $false
$psi.WorkingDirectory = $publishDir

$p = [System.Diagnostics.Process]::Start($psi)

$timeoutMs = $Timeout * 1000
$exited = $p.WaitForExit($timeoutMs)

if (-not $exited) {
    [Console]::Error.WriteLine("TIMED OUT after ${Timeout}s")
    $p.Kill()
    $p.WaitForExit(3000)
    if ($needsSubst -and $tmp -and (Test-Path $tmp)) { Remove-Item $tmp -ErrorAction SilentlyContinue }
    exit 1
}

# --- cleanup temp ---
if ($needsSubst -and $tmp -and (Test-Path $tmp)) { Remove-Item $tmp -ErrorAction SilentlyContinue }

exit $p.ExitCode
