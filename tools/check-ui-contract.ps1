param([switch]$SourceOnly)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $workspace 'FreeClipControl.Shell'
function Require-Condition([bool]$condition, [string]$message) { if (-not $condition) { throw ('UI contract failed: ' + $message) } }
$app = Get-Content -LiteralPath (Join-Path $source 'App.xaml.cs') -Raw
$shell = Get-Content -LiteralPath (Join-Path $source 'ShellWindow.cs') -Raw
$surfaceCode = Get-Content -LiteralPath (Join-Path $source 'PopupSurface.cs') -Raw
Require-Condition ($app.Contains('ShellLaunchOptions.Parse(options)') -and $app.Contains('if (launch.ShowPanelOnLaunch)')) 'App must use the shared launch policy.'
Require-Condition ($shell.Contains('ShellBehaviorPolicy.Decide(') -and $shell.Contains('Native.IsForegroundWithin(hwnd)')) 'Dismissal must use actual foreground state and the shared policy.'
Require-Condition ($shell.Contains('ShellBehaviorPolicy.ShouldTrack(') -and $shell -notmatch 'fallback\s*\|\|\s*preview') 'Preview must not disable dismissal.'
Require-Condition ($shell.Contains('await viewModel.PrepareMenuSelectionAsync(pendingCheck.Token)') -and $shell.IndexOf('await viewModel.PrepareMenuSelectionAsync(pendingCheck.Token)') -lt $shell.IndexOf('bool cold = surface == null;')) 'Connected-headset selection must finish before the menu is created or shown.'
Require-Condition ($surfaceCode -notmatch 'GetAlphaMask|ShadowMask|CreateDropShadow') 'Shadow cannot depend on live/offscreen XAML alpha.'
$panelCode = Get-Content -LiteralPath (Join-Path $source 'EarphonePanel.xaml.cs') -Raw
$acrylicCode = Get-Content -LiteralPath (Join-Path $source 'PanelAcrylicBackdrop.cs') -Raw
Require-Condition ($panelCode.Contains('backdrop.DetachForRetirement(Material)') -and $acrylicCode.Contains('DispatcherQueue.HasThreadAccess') -and $acrylicCode.Contains('target.As<IDisposable>().Dispose()')) 'Retiring the panel must close its backdrop links on the UI thread, not leave native teardown to the finalizer.'
[xml]$surface = Get-Content -LiteralPath (Join-Path $source 'PopupSurface.xaml') -Raw
$image = $surface.SelectSingleNode('//*[local-name()="Image"]')
Require-Condition ($null -ne $image -and $image.Source -eq 'Assets/PopupShadow.png' -and $image.NineGrid -eq '32' -and $image.Stretch -eq 'Fill') 'A complete nine-grid shadow asset is required.'
$panel = $surface.SelectSingleNode('//*[local-name()="EarphonePanel"]')
Require-Condition ($panel.Margin -eq '12') 'Preserve the shadow drawing margin.'
[xml]$menu = Get-Content -LiteralPath (Join-Path $source 'EarphonePanel.xaml') -Raw
$header = $menu.SelectSingleNode('//*[local-name()="Button" and @*[local-name()="Name"]="HeadsetHeaderButton"]')
Require-Condition ($null -ne $header -and $header.Click -eq 'OnSwitchHeadset') 'The entire title must be one switch button.'
Require-Condition ($header.SelectNodes('.//*[local-name()="Button"]').Count -eq 0) 'No separate arrow button.'
Require-Condition ($header.GetAttribute('ToolTipService.ToolTip').Contains('HeadsetSwitchToolTip')) 'Use the same device-name tooltip across the title.'
$style = $menu.SelectSingleNode('//*[local-name()="Style" and @*[local-name()="Key"]="HeadsetHeaderButtonStyle"]')
Require-Condition ($style.SelectNodes('.//*[local-name()="ContentPresenter"]').Count -eq 1 -and $style.SelectNodes('.//*[local-name()="Border"]').Count -eq 0) 'Header hover must not draw a box.'
foreach ($launcher in @('build.ps1','build-shell.ps1','启动EZBuds.cmd','启动耳机控制中心.cmd')) {
    $file = Join-Path $workspace $launcher
    if (Test-Path -LiteralPath $file) {
        Require-Condition ((Get-Content -LiteralPath $file -Raw) -notmatch '(?m)^\s*(?:Start-Process|start\b|&.*ArgumentList).*--(?:preview|no-tray|keep-open)') ('No diagnostic switches in ' + $launcher)
    }
}
Write-Output 'UI source contract passed (not a rendered UI acceptance).'
if (-not $SourceOnly) {
    & python (Join-Path $PSScriptRoot 'generate-popup-shadow.py') --check
    if ($LASTEXITCODE -ne 0) { throw 'Shadow asset verification failed.' }
    & dotnet run --project (Join-Path $workspace 'probe\ShellChecks\ShellChecks.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Shell policy checks failed.' }
}
