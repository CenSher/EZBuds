param([switch]$Run, [switch]$NoRestore, [switch]$SkipRuntimeChecks, [string]$OutputDirectory = 'dist-current')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'FreeClipControl.Shell\FreeClipControl.Shell.csproj'
$outputDirectory = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory)) }
if (Test-Path -LiteralPath (Join-Path $outputDirectory 'FreeClipControl.Shell.exe')) {
    throw 'This output directory contains a pre-EZBuds release. Choose a new -OutputDirectory to avoid mixing old and renamed binaries.'
}
& (Join-Path $projectRoot 'tools\check-ui-contract.ps1') -SourceOnly:$SkipRuntimeChecks
function Get-SourceFingerprint {
    $sourceRoot = Join-Path $projectRoot 'FreeClipControl.Shell'
    $files = @(Get-ChildItem -LiteralPath $sourceRoot -File | Where-Object Extension -In '.cs','.xaml','.csproj','.manifest')
    $files += @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Assets') -File -Recurse)
    @($files | Sort-Object FullName | ForEach-Object { [pscustomobject]@{File=$_.FullName.Substring($sourceRoot.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
}
$sourceBefore = Get-SourceFingerprint
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'Install the x64 .NET 8 SDK (not just the Runtime): https://dotnet.microsoft.com/download/dotnet/8.0'
}
$sdks = & $dotnet.Source --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^(8|9|[1-9][0-9])\.' })) {
    throw 'No compatible .NET SDK found. Install the x64 .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0 . The old Framework compiler cannot build WinUI 3.'
}
$publishArguments = @('publish', $project, '-c', 'Release', '-p:Platform=x64', '-r', 'win-x64', '--self-contained', 'true', '-o', $outputDirectory)
if ($NoRestore) { $publishArguments += '--no-restore' }
& $dotnet.Source @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 publish failed. Check NuGet connectivity and install the Windows SDK / WinUI build tools if requested by the error above.' }
$sourceAfter = Get-SourceFingerprint
if (($sourceBefore | ConvertTo-Json -Compress) -ne ($sourceAfter | ConvertTo-Json -Compress)) {
    throw 'Source changed during compilation. Do not deliver this output; rebuild from an isolated snapshot.'
}
$exe = Join-Path $outputDirectory 'EZBuds.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Publish finished without the expected executable.' }
foreach ($resource in @('EZBuds.pri', 'App.xbf', 'EarphonePanel.xbf', 'HeadsetHomePage.xbf', 'HeadsetSwitchPage.xbf', 'PopupSurface.xbf', 'ProductIcon.xbf', 'SettingsWindow.xbf', 'Assets\PopupShadow.png', 'Assets\PopupShadow.scale-200.png', 'Assets\PopupShadow.scale-300.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory $resource))) {
        throw ('Publish is missing the WinUI resource file: ' + $resource)
    }
}
# A Low integrity label inherited from a development folder also lowers the
# launched process and prevents it from registering with the desktop taskbar.
& icacls.exe $exe /setintegritylevel M | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Publish completed, but the executable integrity label could not be set to Medium. Run this build script from your normal local PowerShell before launching the app.'
}
Write-Host ('Built WinUI 3: ' + $exe)
$launcherTarget = if ($exe.StartsWith($projectRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { '%~dp0' + $exe.Substring($projectRoot.TrimEnd('\').Length+1) } else { $exe }
$launcher = '@echo off' + "`r`n" + 'start "" "' + $launcherTarget + '"' + "`r`n"
foreach ($entry in @('启动EZBuds.cmd', '启动耳机控制中心.cmd')) {
    [IO.File]::WriteAllText((Join-Path $projectRoot $entry), $launcher, (New-Object Text.UTF8Encoding($false)))
}
$buildInfo = [pscustomobject]@{
    Built=(Get-Date -Format o); Output=$outputDirectory;
    UiContract=$(if ($SkipRuntimeChecks) { 'source checks only; asset and executable checks skipped' } else { 'source, asset and policy checks passed' });
    UiAcceptance='pending user acceptance';
    Sources=$sourceBefore
}
$buildInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'BUILD_INFO.json') -Encoding utf8
Write-Host 'Keep the entire output folder together. Normal launch is tray-only. Visual acceptance is pending.'
if ($Run) { Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList $exe -WindowStyle Hidden }
