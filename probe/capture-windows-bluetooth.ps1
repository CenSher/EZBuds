param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(30, 300)][int]$Seconds = 120
)

# Passive Windows tracing only. Does not connect, lock, reset, or play audio.
$ErrorActionPreference = 'Stop'
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$profile = Join-Path $output 'BluetoothStack.wprp'
$statusFile = Join-Path $output 'windows-trace-status.json'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$started = $false
$status = [ordered]@{ State = 'starting'; Started = (Get-Date -Format o); Finished = $null; Error = $null; Trace = $null }
try {
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Windows Bluetooth tracing requires an administrator terminal.'
    }
    if (-not (Test-Path -LiteralPath $profile)) { throw 'Microsoft BluetoothStack.wprp is missing.' }
    [xml]$xml = Get-Content -LiteralPath $profile -Raw
    if (-not ($xml.WindowsPerformanceRecorder.Profiles.Profile | Where-Object Id -eq 'BluetoothStack.Verbose.File')) {
        throw 'Expected Microsoft BluetoothStack recording profile was not found.'
    }
    & wpr.exe -start ($profile + '!BluetoothStack') -filemode 2>&1 | Out-File (Join-Path $output 'windows-trace-start.txt')
    if ($LASTEXITCODE -ne 0) { throw 'WPR could not start; see windows-trace-start.txt. No existing recording was stopped.' }
    $started = $true
    $status.State = 'recording'
    $status | ConvertTo-Json | Set-Content -LiteralPath $statusFile -Encoding UTF8
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    while ([DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
}
catch {
    $status.State = 'failed'
    $status.Error = $_.Exception.Message
}
finally {
    if ($started) {
        $trace = Join-Path $output ('windows-bluetooth-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.etl')
        & wpr.exe -stop $trace 2>&1 | Out-File (Join-Path $output 'windows-trace-stop.txt')
        if ($LASTEXITCODE -eq 0) { $status.State = 'saved'; $status.Trace = $trace }
        else { $status.State = 'stop-failed'; $status.Error = 'WPR stop failed; see windows-trace-stop.txt.' }
    }
    $status.Finished = Get-Date -Format o
    $status | ConvertTo-Json | Set-Content -LiteralPath $statusFile -Encoding UTF8
}
