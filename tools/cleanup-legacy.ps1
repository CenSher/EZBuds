$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$baseline = Join-Path $workspace 'baselines\FreeClip2-20261022-154252'
if (-not (Test-Path -LiteralPath (Join-Path $baseline 'SHA256.csv'))) { throw 'Verified baseline is required.' }
$release = Join-Path $baseline 'dist-shell\FreeClipControl.Shell.exe'
if (-not (Test-Path -LiteralPath $release)) { throw 'Baseline release is missing.' }
$cutoff = [datetime]'2026-10-22T15:42:52'
$targets = [Collections.Generic.List[string]]::new()
foreach ($name in @('FreeClipControl','FreeClipControl.WinUI','dist','dist-winui3','build-winui3.ps1',
    'FreeClipControl-prototype.zip','FreeClipControl-WinUI3-source.zip','FreeClipControl-Shell-source.zip')) {
    $candidate = Join-Path $workspace $name
    if ((Test-Path -LiteralPath $candidate) -and (Get-Item -LiteralPath $candidate).LastWriteTime -lt $cutoff) { $targets.Add($candidate) }
}
$oldRelease = Join-Path $workspace 'dist-shell'
if (Test-Path -LiteralPath (Join-Path $oldRelease 'FreeClipControl.Shell.exe')) {
    if ((Get-FileHash -LiteralPath (Join-Path $oldRelease 'FreeClipControl.Shell.exe')).Hash -eq (Get-FileHash -LiteralPath $release).Hash) {
        $targets.Add($oldRelease)
    }
}
$scratch = Join-Path $workspace '.scratch'
Get-ChildItem -LiteralPath $scratch -Directory | Where-Object {
    $_.LastWriteTime -lt $cutoff -and $_.Name -match '^(before-|mica|shadow|winui|shell|visible|icons|adjusted|divider|edge|footer|list|panel|tray|real|sdk|lifecycle|connection-lock|disconnected-page)'
} | ForEach-Object { $targets.Add($_.FullName) }
Get-ChildItem -LiteralPath $scratch -File -Filter '*.zip' | Where-Object { $_.LastWriteTime -lt $cutoff } | ForEach-Object { $targets.Add($_.FullName) }
foreach ($name in @('Qt','reference')) {
    $candidate = Join-Path $workspace $name
    if ((Test-Path -LiteralPath $candidate) -and @(Get-ChildItem -LiteralPath $candidate -Force).Count -eq 0) { $targets.Add($candidate) }
}
# Resolve and validate EVERY deletion before starting any recursive deletion.
$validated = foreach ($target in $targets) {
    $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $target).Path)
    if (-not $resolved.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $resolved.StartsWith($baseline, [StringComparison]::OrdinalIgnoreCase)) { throw ('Unsafe cleanup target: ' + $resolved) }
    $item = Get-Item -LiteralPath $resolved -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('Refusing reparse point: ' + $resolved) }
    $bytes = if ($item.PSIsContainer) { (Get-ChildItem -LiteralPath $resolved -Recurse -File -Force | Measure-Object Length -Sum).Sum } else { $item.Length }
    [pscustomobject]@{ Path=$resolved.Substring($workspace.Length+1); Bytes=[long]$bytes }
}
$report = [pscustomobject]@{ Created=(Get-Date -Format o); Baseline=$baseline; Removed=$validated; Bytes=($validated | Measure-Object Bytes -Sum).Sum }
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $workspace 'docs\LEGACY_CLEANUP.json') -Encoding utf8
foreach ($target in $validated) { Remove-Item -LiteralPath (Join-Path $workspace $target.Path) -Recurse -Force }
Write-Output ('Removed ' + @($validated).Count + ' obsolete items; released ' + [math]::Round($report.Bytes/1GB,2) + ' GiB.')
