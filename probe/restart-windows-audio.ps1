param([Parameter(Mandatory = $true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$status = [ordered]@{ Started = (Get-Date -Format o); State = 'starting'; Finished = $null; Error = $null }
try {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator access required.' }
    $service = Get-Service -Name Audiosrv
    $service | Select-Object Name, Status, DependentServices | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $output 'audio-service-before-restart.json')
    # Refuse to silently stop unrelated dependent services.
    if (@($service.DependentServices | Where-Object Status -eq 'Running').Count -gt 0) { throw 'Audio has running dependent services; recovery was not performed.' }
    Restart-Service -Name Audiosrv -ErrorAction Stop
    (Get-Service -Name Audiosrv).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
    $status.State = 'service-running'
}
catch { $status.State = 'failed'; $status.Error = $_.Exception.Message }
finally {
    $status.Finished = Get-Date -Format o
    $status | ConvertTo-Json | Set-Content (Join-Path $output 'audio-service-restart-status.json') -Encoding UTF8
}
