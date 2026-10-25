using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FreeClipControl.Shell;

internal sealed class ShellWindow : Window
{
    public const string WindowTitle = AppIdentity.Name;
    private const double DesignWidth = 408, DesignHeight = 624, WindowScale = 0.945;
    private readonly IntPtr hwnd;
    private readonly Grid root = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private PopupSurface surface;
    private readonly PanelViewModel viewModel = new(new HuaweiBluetoothDataSource());
    private readonly DispatcherQueueTimer releaseTimer;
    private CancellationTokenSource preparation;
    private CancellationTokenSource menuCheck;
    private bool checkingHeadset;
    private bool preparing;
    private readonly System.Diagnostics.Stopwatch cacheClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly MenuCachePolicy cachePolicy = new();
    private readonly UiMemoryDiagnostics memoryDiagnostics;
    private readonly PopupFrame frame;
    private readonly TrayIcon tray;
    private readonly TrayContextMenu trayMenu = new();
    private SettingsWindow settings;
    private readonly DispatcherQueueTimer dismissTimer;
    private readonly DispatcherQueueTimer retryTimer;
    private readonly DispatcherQueueTimer refreshTimer;
    private readonly ShellLaunchOptions launch = ShellLaunchOptions.Parse(Environment.GetCommandLineArgs());
    private readonly TrayClickIntent trayClick = new();
    private readonly RectangleGeometry viewportClip = new();
    private Task<bool> motionTask = Task.FromResult(true);
    private bool open, active, menuOpen, trayMenuOpen, exitRequested, closing, fallback;
    public string TrayStatus => tray?.RegistrationDetails ?? "Preview without tray";

    public ShellWindow(bool useTray)
    {
        Title = WindowTitle;
        root.Clip = viewportClip;
        Content = root;
        root.RequestedTheme = AppPreferences.ReadTheme();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // WinUI owns a caption layer independently of the native WS_CAPTION bits.
        // Collapse it as well, so the transparent shadow margin cannot reveal it.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        AppWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        frame = new PopupFrame(hwnd);
        AppWindow.IsShownInSwitchers = !useTray;
        frame.Configure(!useTray);

        dismissTimer = DispatcherQueue.CreateTimer();
        dismissTimer.Interval = TimeSpan.FromMilliseconds(100);
        dismissTimer.IsRepeating = true;
        dismissTimer.Tick += (_, _) =>
        {
            bool overTray = tray != null && Native.IsPointerOverTrayIcon(hwnd);
            var action = ShellBehaviorPolicy.Decide(new(open, launch.KeepOpen, fallback, exitRequested,
                menuOpen, trayMenuOpen, (surface?.Panel.HasConfirmation == true), (surface?.Animation.IsRunning == true),
                Native.IsForegroundWithin(hwnd), overTray, Native.PrimaryButtonDown));
            if (action == DismissAction.StopTracking) { dismissTimer.Stop(); return; }
            if (action == DismissAction.Wait) return;
            if (action == DismissAction.PreserveTrayGesture)
            {
                trayClick.CaptureIfMissing(open);
                ScheduleDismiss();
                return;
            }
            if (!overTray) trayClick.CancelPointer();
            HidePanel();
        };
        retryTimer = DispatcherQueue.CreateTimer();
        retryTimer.Interval = TimeSpan.FromSeconds(5);
        retryTimer.Tick += (_, _) => { if (tray?.TryRegister() == true) { retryTimer.Stop(); UpdateTrayMode(); } };
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(30);
        refreshTimer.Tick += async (_, _) =>
        {
            if (exitRequested || checkingHeadset || menuOpen || (surface?.Panel.HasConfirmation == true)) return;
            if (open || settings?.NeedsLiveBattery == true) await viewModel.RefreshIfDueAsync();
            else await viewModel.RefreshTrayBatteryAsync();
        };
        viewModel.SetTrayBatteryEnabled(AppPreferences.TrayBatteryEnabled);

        releaseTimer = DispatcherQueue.CreateTimer();
        releaseTimer.Interval = TimeSpan.FromSeconds(15);
        releaseTimer.Tick += (_, _) =>
        {
            if (!cachePolicy.CanRelease(cacheClock.Elapsed, open || AppWindow.IsVisible, preparing || checkingHeadset,
                surface?.Animation.IsRunning == true, surface?.Panel.HasConfirmation == true)) return;
            ReleaseSurface();
        };
        memoryDiagnostics = new UiMemoryDiagnostics(DispatcherQueue, () => !exitRequested && !open
            && !preparing && !checkingHeadset && settings == null && !trayMenuOpen
            && surface?.Animation.IsRunning != true && surface?.Panel.HasConfirmation != true);
        root.ActualThemeChanged += (_, _) =>
        {
            int dark = root.ActualTheme == ElementTheme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        };
        root.PointerPressed += (_, args) => { if (ReferenceEquals(args.OriginalSource, root)) HidePanel(); };
        root.KeyDown += OnKeyDown;
        Activated += (_, args) =>
        {
            active = args.WindowActivationState != WindowActivationState.Deactivated;
            if (active)
            {
                surface?.Panel.SetBackdropInputActive(true);
                ScheduleDismiss();
            }
            else
            {
                if (!AppWindow.IsVisible) surface?.Panel.SetBackdropInputActive(false);
                if (tray != null && Native.IsPointerOverTrayIcon(hwnd) && Native.PrimaryButtonDown)
                    trayClick.PointerDown(open || checkingHeadset);
                ScheduleDismiss();
            }
        };
        AppWindow.Closing += (_, args) => { if (!closing) { args.Cancel = true; if (fallback) ExitPanel(); else HidePanel(); } };
        Closed += (_, _) =>
        {
            dismissTimer.Stop(); retryTimer.Stop(); refreshTimer.Stop(); releaseTimer.Stop();
            preparation?.Cancel();
            menuCheck?.Cancel();
            memoryDiagnostics.Dispose();
            settings?.CloseAndRelease();
            tray?.Dispose(); trayMenu.Dispose(); ReleaseSurface(); viewModel.Dispose(); frame.Dispose();
            RuntimeLog.Write("WINDOW CLOSED");
        };
        if (useTray)
        {
            tray = new TrayIcon(hwnd, viewModel.TrayToolTip);
            tray.PointerPressed += (_, _) => trayClick.PointerDown(open || checkingHeadset);
            tray.ToggleRequested += async keyboard => await ActivateTrayAsync(keyboard);
            tray.ShowRequested += (_, _) => ShowPanel();
            tray.MenuRequested += async (_, _) => await ShowTrayMenuAsync();
            tray.RegistrationChanged += (_, _) => UpdateTrayMode();
            tray.PlacementChanged += (_, _) => { surface?.RefreshPolicy(); if (open) Position(); };
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PanelViewModel.TrayToolTip)) tray.SetToolTip(viewModel.TrayToolTip);
            };
            UpdateTrayMode();
        }
        AppWindow.Hide();
        UpdateRefreshPolicy();
        // Selection is independent of optional SPP battery reads.
        _ = InitializeHeadsetAsync();
    }

    private async Task InitializeHeadsetAsync()
    {
        await viewModel.InitializeStartupSelectionAsync();
        if (!exitRequested) await viewModel.RefreshTrayBatteryAsync();
    }

    private void EnsureSurface()
    {
        if (surface != null) return;
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop();
        surface = new PopupSurface();
        surface.Panel.Initialize(viewModel);
        surface.Panel.MenuOpened += (_, _) => { menuOpen = true; dismissTimer.Stop(); };
        surface.Panel.MenuClosed += (_, _) => { menuOpen = false; ScheduleDismiss(); };
        surface.Panel.ConfirmationOpened += (_, _) => dismissTimer.Stop();
        surface.Panel.ConfirmationClosed += (_, _) => ScheduleDismiss();
        surface.Panel.SettingsRequested += (_, _) => ShowSettings(configuration: true);
        root.Children.Add(surface);
        RuntimeLog.Write("Menu UI created on demand.");
    }
    private void ReleaseSurface()
    {
        releaseTimer.Stop();
        if (surface == null) return;
        var retired = surface;
        surface = null;
        memoryDiagnostics.Retire("menu surface", retired);
        memoryDiagnostics.Retire("menu content", retired.Panel);
        RuntimeLog.Write("Menu UI retirement started.");
        // Detach external listeners first, then unload the whole tree as one unit.
        UiCleanup.Run("popup resources", retired.Dispose);
        UiCleanup.Run("popup tree", () => root.Children.Remove(retired));
        UiCleanup.Run("popup host backdrop", () => SystemBackdrop = null);
        menuOpen = false;
        motionTask = Task.FromResult(true);
        RuntimeLog.Write("Menu UI released; background model retained.");
    }
    private void SetCloaked(bool value)
    {
        int enabled = value ? 1 : 0;
        int result = Native.DwmSetWindowAttribute(hwnd, 13, ref enabled, sizeof(int));
        if (result < 0) System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(result);
    }
    private async Task OpenBluetoothSettingsAsync()
    {
        if (viewModel.NeedsConfiguration) { ShowSettings(configuration: true); return; }
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));
    }

    private void UpdateRefreshPolicy()
    {
        bool wasRunning = refreshTimer.IsRunning;
        if (!exitRequested && (AppPreferences.TrayBatteryEnabled || open || settings?.NeedsLiveBattery == true))
            refreshTimer.Start();
        else refreshTimer.Stop();
        if (wasRunning != refreshTimer.IsRunning)
            RuntimeLog.Write("Battery refresh timer " + (refreshTimer.IsRunning ? "started" : "stopped"));
    }
    private void UpdateTrayBattery()
    {
        viewModel.SetTrayBatteryEnabled(AppPreferences.TrayBatteryEnabled);
        UpdateRefreshPolicy();
        RuntimeLog.Write("Tray battery " + (AppPreferences.TrayBatteryEnabled ? "enabled" : "disabled")
            + "; refresh timer=" + refreshTimer.IsRunning);
        if (AppPreferences.TrayBatteryEnabled) _ = viewModel.RefreshTrayBatteryAsync();
    }

    private void UpdateTrayMode()
    {
        fallback = !tray.IsRegistered;
        AppWindow.IsShownInSwitchers = fallback;
        frame.Configure(fallback);
        viewModel.SetTrayAvailable(!fallback);
        if (fallback) retryTimer.Start(); else retryTimer.Stop();
        RuntimeLog.Write("TRAY " + TrayStatus);
        ScheduleDismiss();
    }

    private async Task ShowTrayMenuAsync()
    {
        if (exitRequested || trayMenuOpen) return;
        trayMenuOpen = true;
        dismissTimer.Stop();
        TrayContextMenu.Command command;
        try { command = trayMenu.Show(viewModel.CanConnectToHeadset); }
        catch (Exception error) { RuntimeLog.Write("Tray menu failed: " + error); return; }
        finally { trayMenuOpen = false; ScheduleDismiss(); }
        switch (command)
        {
            case TrayContextMenu.Command.Open: await ShowAsync(); break;
            case TrayContextMenu.Command.Connect: await ConnectFromTrayAsync(); break;
            case TrayContextMenu.Command.Settings: ShowSettings(); break;
            case TrayContextMenu.Command.Exit: ExitPanel(); break;
        }
    }

    private void ApplyTheme(ElementTheme theme)
    {
        AppPreferences.SaveTheme(theme);
        root.RequestedTheme = theme;
        settings?.ApplyTheme(theme);
    }

    public async void ShowSettings(bool configuration = false)
    {
        await viewModel.InitializeStartupSelectionAsync();
        if (exitRequested) return;
        HidePanel();
        if (settings == null)
        {
            UiMemoryDiagnostics.LogMemory("before settings creation; menuCached=" + (surface != null));
            settings = new SettingsWindow(viewModel, root.RequestedTheme, ApplyTheme,
                ShowPanel, OpenBluetoothSettingsAsync, () => surface?.Panel.RefreshMenuCopy(), UpdateTrayBattery);
            settings.LiveBatteryVisibilityChanged += UpdateRefreshPolicy;
            settings.Closed += (sender, _) =>
            {
                memoryDiagnostics.Retire("settings window", sender);
                memoryDiagnostics.Retire("settings content", ((SettingsWindow)sender).MemoryRoot);
                settings = null;
                UpdateRefreshPolicy();
                RuntimeLog.Write("Settings retired; menuCached=" + (surface != null));
            };
        }
        settings.ShowAndActivate();
        UpdateRefreshPolicy();
        if (configuration) settings.OpenConfiguration();
    }

    private async Task ConnectFromTrayAsync()
    {
        await viewModel.InitializeStartupSelectionAsync();
        if (exitRequested) return;
        // Keep feedback visible if the firmware cannot transfer the audio route.
        // Starting the command first prevents flyout reads racing the operation.
        var action = viewModel.ConnectToHeadsetAsync();
        // This explicit connection command must keep the user's chosen target.
        await ShowAsync(checkConnected: false);
        await action;
    }

    private void Position()
    {
        var area = Native.GetTrayWorkArea(hwnd, out double scale, out var anchor);
        int width = Math.Min((int)Math.Round(DesignWidth * WindowScale * scale), area.Right - area.Left);
        int height = Math.Min((int)Math.Round(DesignHeight * WindowScale * scale), area.Bottom - area.Top);
        int x = anchor.Right <= area.Left ? area.Left : area.Right - width;
        int y = anchor.Bottom <= area.Top ? area.Top : area.Bottom - height;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        float logicalWidth = (float)(width / scale), logicalHeight = (float)(height / scale);
        // Fixed clipping edge is the work-area boundary, i.e. the taskbar edge.
        // The whole card begins outside that edge; there is no opacity animation.
        viewportClip.Rect = new Windows.Foundation.Rect(0, 0, logicalWidth, logicalHeight);
        Vector2 direction = anchor.Bottom <= area.Top ? new(0, -logicalHeight)
            : anchor.Right <= area.Left ? new(-logicalWidth, 0)
            : anchor.Left >= area.Right ? new(logicalWidth, 0)
            : new(0, logicalHeight);
        surface.Animation.ClosedOffset = new Vector3(direction, 0);
    }

    public async void ShowPanel() => await ShowAsync();
    private async Task ShowAsync(bool checkConnected = true)
    {
        try { await ShowCoreAsync(checkConnected); }
        catch (Exception error)
        {
            RuntimeLog.Write("Menu open failed: " + error);
            open = false;
            AppWindow.Hide();
            UpdateRefreshPolicy();
            ReleaseSurface();
        }
    }
    private async Task ShowCoreAsync(bool checkConnected)
    {
        await viewModel.InitializeStartupSelectionAsync();
        if (exitRequested || preparing || checkingHeadset) return;
        releaseTimer.Stop();
        cachePolicy.Open();
        if (open)
        {
            // A show request for an already open/opening flyout only activates it.
            ScheduleDismiss();
            surface?.Panel.SetBackdropInputActive(true);
            Activate();
            Native.SetForegroundWindow(hwnd);
            await motionTask;
            ScheduleDismiss();
            return;
        }
        if (checkConnected)
        {
            using var pendingCheck = new CancellationTokenSource();
            menuCheck = pendingCheck;
            checkingHeadset = true;
            try { await viewModel.PrepareMenuSelectionAsync(pendingCheck.Token); }
            finally { menuCheck = null; checkingHeadset = false; }
            if (pendingCheck.IsCancellationRequested || exitRequested) return;
        }
        // Both cached and cold menus receive the final selected model before any show.
        bool cold = surface == null;
        EnsureSurface();
        bool fresh = !AppWindow.IsVisible;
        surface.Panel.NavigateHome(animate: false);
        surface.Panel.RefreshMenuCopy();
        open = true;
        UpdateRefreshPolicy();
        surface?.Panel.SetBackdropInputActive(true);
        dismissTimer.Stop();
        Position();
        frame.Configure(fallback || !launch.UseTray);
        surface?.RefreshPolicy();
        if (cold)
        {
            preparing = true;
            using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            preparation = pending;
            try
            {
                SetCloaked(true);
                surface.Animation.PrepareClosed();
                // A cloaked window participates in layout/composition but cannot flash on screen.
                AppWindow.Show(false);
                await surface.WaitUntilReadyAsync(pending.Token);
                if (!open || exitRequested) return;
            }
            catch (Exception error)
            {
                RuntimeLog.Write("Menu preparation cancelled/failed: " + error.Message);
                open = false;
                return;
            }
            finally
            {
                preparation = null;
                preparing = false;
                if (!open || exitRequested) AppWindow.Hide();
                SetCloaked(false);
                if (!open && !exitRequested) { UpdateRefreshPolicy(); ReleaseSurface(); }
                if (exitRequested) { closing = true; Close(); }
            }
        }
        motionTask = surface.Animation.RunAsync(true, fresh);
        var animation = motionTask;
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(hwnd);
        if (surface.Panel.IsLoaded)
        {
            surface.Panel.FocusPrimaryAction();
        }
        _ = viewModel.RefreshConnectedAsync();
        if (await animation) ScheduleDismiss();
    }

    public async void HidePanel() { if (!fallback && !exitRequested) await HideAsync(false); }
    public async void ExitPanel() { if (!exitRequested) { exitRequested = true; await HideAsync(true); } }
    private async Task ActivateTrayAsync(bool keyboard)
    {
        bool show = trayClick.Activate(open || checkingHeadset, keyboard);
        RuntimeLog.Write($"TRAY intent: {(show ? "open" : "close")}; currentOpen={open}");
        if (show) await ShowAsync(); else await HideAsync(false);
    }
    private async Task HideAsync(bool exit)
    {
        menuCheck?.Cancel();
        bool wasOpen = open;
        open = false;
        UpdateRefreshPolicy();
        dismissTimer.Stop();
        surface?.Panel.HideDeviceMenu();
        surface?.Panel.HideConfirmation();
        if (preparing)
        {
            preparation?.Cancel();
            AppWindow.Hide();
        }
        if (!preparing && surface != null && AppWindow.IsVisible)
        {
            RuntimeLog.Write(exit ? "EXIT requested" : "DISMISS requested");
            if (wasOpen || !(surface?.Animation.IsRunning == true)) motionTask = surface.Animation.RunAsync(false);
            if (!await motionTask) return;
            if (open) return;
            AppWindow.Hide();
            // Keep the material active while its close animation is visible,
            // including when focus moves into a device context menu.
            surface?.Panel.SetBackdropInputActive(false);
        }
        if (!exit && surface != null) { cachePolicy.Hide(cacheClock.Elapsed); releaseTimer.Start(); }
        if (exit && !preparing) { closing = true; Close(); }
    }
    private void ScheduleDismiss()
    {
        if (!preparing && ShellBehaviorPolicy.ShouldTrack(open, launch.KeepOpen, fallback, exitRequested)) dismissTimer.Start();
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Escape) return;
        if ((surface?.Panel.HasConfirmation == true)) return;
        if (menuOpen) surface.Panel.HideDeviceMenu(); else HidePanel();
        args.Handled = true;
    }

}
