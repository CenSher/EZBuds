using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace FreeClipControl.Shell;

internal sealed partial class SettingsWindow : Window
{
    private const int MinimumWindowWidth = 800, MinimumWindowHeight = 600;
    public PanelViewModel ViewModel { get; }
    private readonly Action<ElementTheme> changeTheme;
    private readonly Action openPanel;
    private readonly Func<Task> openBluetooth;
    private readonly Action refreshMenuCopy;
    private readonly Action updateTrayBattery;
    private readonly UISettings motionSettings = new();
    private HeadsetBrand copyBrand;
    private bool initializing = true;
    private bool updatingStartup;
    private bool closed;
    private Flyout deleteConfirmation;
    private bool deletingBoundHeadset;
    private readonly CancellationToken windowCancellation;
    private readonly IntPtr hwnd;
    private readonly CancellationTokenSource catalogLifetime = new();
    private ulong? configurationAddress;
    private XamlRoot sizingRoot;
    private bool? wideContent;
    internal event Action LiveBatteryVisibilityChanged;
    internal object MemoryRoot => Root;
    internal bool NeedsLiveBattery => !closed && AppWindow.IsVisible && DevicePage.Visibility == Visibility.Visible
        && (AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State != OverlappedPresenterState.Minimized);

    internal SettingsWindow(PanelViewModel viewModel, ElementTheme theme, Action<ElementTheme> onTheme,
        Action onOpenPanel, Func<Task> onOpenBluetooth, Action onRefreshMenuCopy, Action onUpdateTrayBattery)
    {
        windowCancellation = catalogLifetime.Token;
        ViewModel = viewModel;
        changeTheme = onTheme;
        openPanel = onOpenPanel;
        openBluetooth = onOpenBluetooth;
        refreshMenuCopy = onRefreshMenuCopy;
        updateTrayBattery = onUpdateTrayBattery;
        InitializeComponent();
        AboutVersion.Text = AppIdentity.VersionLabel;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Root.RequestedTheme = theme;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Headphones.ico"));
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        // Choose a logical size appropriate to the monitor's actual DPI and work area.
        double scale = Native.GetWindowScale(hwnd);
        UpdateWindowSizeLimits();
        int width = Math.Min((int)(1080 * scale), display.WorkArea.Width);
        int height = Math.Min((int)(780 * scale), display.WorkArea.Height);
        AppWindow.MoveAndResize(new RectInt32(display.WorkArea.X + (display.WorkArea.Width - width) / 2,
            display.WorkArea.Y + (display.WorkArea.Height - height) / 2, width, height));
        ThemeSelector.SelectedIndex = theme switch { ElementTheme.Light => 1, ElementTheme.Dark => 2, _ => 0 };
        AbstractCopyToggle.IsOn = AppPreferences.AbstractCopyEnabled;
        TrayBatteryToggle.IsOn = AppPreferences.TrayBatteryEnabled;
        UpdateToggleLabels();
        PageContent.Loaded += OnPageContentLoaded;
        Root.Loaded += OnRootLoaded;
        UpdateCopyCount();
        Navigation.SelectedItem = DeviceItem;
        initializing = false;
        RefreshStartupRegistration();
        Activated += OnWindowActivated;
        LoadModels();
        _ = ReloadHeadsetsAsync();
        _ = RefreshBindingsAsync();
        ViewModel.PropertyChanged += OnCopyBrandChanged;
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args) => ReleaseResources();
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        ReleaseResources();
        RuntimeLog.Write("Settings closed; resource cleanup completed (object reclamation still unverified).");
    }
    internal void CloseAndRelease()
    {
        ReleaseResources();
        Close();
    }
    private void ReleaseResources()
    {
        if (closed) return;
        closed = true;
        UiCleanup.Run("settings delete confirmation", DismissDeleteConfirmation);
        ViewModel.PropertyChanged -= OnCopyBrandChanged;
        LiveBatteryVisibilityChanged = null;
        Root.Loaded -= OnRootLoaded;
        PageContent.Loaded -= OnPageContentLoaded;
        ContentViewport.SizeChanged -= OnContentViewportSizeChanged;
        Navigation.SelectionChanged -= OnNavigationChanged;
        Activated -= OnWindowActivated;
        UiCleanup.Run("settings window events", () =>
        {
            AppWindow.Changed -= OnAppWindowChanged;
            AppWindow.Closing -= OnWindowClosing;
        });
        if (sizingRoot != null)
            UiCleanup.Run("settings sizing events", () => sizingRoot.Changed -= OnSizingRootChanged);
        sizingRoot = null;
        UiCleanup.Run("settings cancellation", catalogLifetime.Cancel);
        UiCleanup.Run("settings bindings", () => Bindings.StopTracking());
        UiCleanup.Run("settings page animation", () =>
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.StopAnimation("Translation");
            visual.StopAnimation("Opacity");
        });
        // Release the custom title-bar registration while the HWND is still alive.
        // Clearing Content alone leaves Window's separate title-bar reference intact.
        UiCleanup.Run("settings custom title bar", () => SetTitleBar(null));
        UiCleanup.Run("settings extended title bar", () => ExtendsContentIntoTitleBar = false);
        UiCleanup.Run("settings backdrop", () => SystemBackdrop = null);
        UiCleanup.Run("settings content", () => Content = null);
        catalogLifetime.Dispose();
    }

    private void OnPageContentLoaded(object sender, RoutedEventArgs args) => AnimatePageEntrance();
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (!closed && args.WindowActivationState != WindowActivationState.Deactivated) RefreshStartupRegistration();
    }
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (closed) return;
        if (args.DidPositionChange || args.DidPresenterChange) UpdateWindowSizeLimits();
        LiveBatteryVisibilityChanged?.Invoke();
    }

    private void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        if (closed) return;
        var currentRoot = Root.XamlRoot;
        if (sizingRoot != currentRoot)
        {
            if (sizingRoot != null) sizingRoot.Changed -= OnSizingRootChanged;
            sizingRoot = currentRoot;
            if (sizingRoot != null) sizingRoot.Changed += OnSizingRootChanged;
        }
        UpdateWindowSizeLimits();
        UpdateResponsiveLayout();
    }
    private void OnContentViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateResponsiveLayout();
    private void UpdateResponsiveLayout()
    {
        if (closed) return;
        if (PageContent == null || ConfigurationPage == null || DevicePage == null || AppearanceSecondaryColumn == null) return;
        double width = Math.Min(PageContent.MaxWidth, ContentViewport.ActualWidth);
        if (width <= 0) return;
        // Use the actual navigation content viewport, not the overall window or
        // an ElementName binding across NavigationView's content namescope.
        if (double.IsNaN(PageContent.Width) || Math.Abs(PageContent.Width - width) > 0.1) PageContent.Width = width;
        bool wide = width >= 880;
        if (wideContent == wide) return;
        wideContent = wide;
        foreach (var grid in new[] { DevicePage, ConfigurationPage, AppearancePage })
        {
            grid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            grid.ColumnSpacing = wide ? 12 : 0;
        }
        Grid.SetColumn(ConnectionSection, wide ? 1 : 0);
        Grid.SetRow(ConnectionSection, wide ? 1 : 2);
        Grid.SetColumn(ModelCatalogCard, wide ? 1 : 0);
        Grid.SetRow(ModelCatalogCard, wide ? 0 : 1);
        Grid.SetColumn(AppearanceSecondaryColumn, wide ? 1 : 0);
        Grid.SetRow(AppearanceSecondaryColumn, wide ? 0 : 1);
    }
    private void OnSizingRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateWindowSizeLimits();
    private void UpdateWindowSizeLimits()
    {
        if (closed) return;
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        double scale = Root.XamlRoot?.RasterizationScale ?? Native.GetWindowScale(hwnd);
        // Presenter limits use physical pixels. Recompute for DPI/display moves,
        // and let small work areas fit the entire window rather than force overflow.
        int width = Math.Min((int)Math.Ceiling(MinimumWindowWidth * scale), display.WorkArea.Width);
        int height = Math.Min((int)Math.Ceiling(MinimumWindowHeight * scale), display.WorkArea.Height);
        if (presenter.PreferredMinimumWidth != width) presenter.PreferredMinimumWidth = width;
        if (presenter.PreferredMinimumHeight != height) presenter.PreferredMinimumHeight = height;
    }

    internal void ApplyTheme(ElementTheme theme) => Root.RequestedTheme = theme;
    internal void ShowAndActivate()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
            presenter.Restore();
        AppWindow.Show();
        Activate();
        Native.SetForegroundWindow(hwnd);
        if (NeedsLiveBattery) _ = ViewModel.RefreshIfDueAsync();
        _ = RefreshBindingsAsync();
    }
    internal void OpenConfiguration()
    {
        Navigation.SelectedItem = ConfigurationItem;
        _ = ReloadHeadsetsAsync();
    }
    private async Task RefreshBindingsAsync()
    {
        try { await ViewModel.ReloadBindingsAsync().WaitAsync(windowCancellation); }
        catch (OperationCanceledException) { }
        catch (Exception) when (closed) { }
        catch (Exception error) when (!closed) { BindingMessage.Message = error.Message; BindingMessage.IsOpen = true; }
    }
    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (closed) return;
        if (DevicePage == null || args.SelectedItem is not NavigationViewItem item) return;
        DismissDeleteConfirmation();
        string page = item.Tag as string;
        DevicePage.Visibility = page == "device" ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationPage.Visibility = page == "configuration" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = page == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = item.Content as string;
        PageScroll.ChangeView(null, 0, null, true);
        if (!initializing && PageContent.IsLoaded) AnimatePageEntrance();
        LiveBatteryVisibilityChanged?.Invoke();
        if (!initializing && page == "device") _ = ViewModel.RefreshIfDueAsync();
        if (page == "configuration") _ = ReloadHeadsetsAsync();
    }
    private void AnimatePageEntrance()
    {
        if (closed) return;
        ElementCompositionPreview.SetIsTranslationEnabled(PageContent, true);
        var visual = ElementCompositionPreview.GetElementVisual(PageContent);
        // Replace any interrupted entrance so rapid navigation never accumulates offsets.
        visual.StopAnimation("Translation");
        visual.StopAnimation("Opacity");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
        if (!motionSettings.AnimationsEnabled) return;
        var compositor = visual.Compositor;
        using var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1));
        using var move = compositor.CreateVector3KeyFrameAnimation();
        move.Duration = TimeSpan.FromMilliseconds(250);
        move.InsertKeyFrame(0, new Vector3(0, 28, 0));
        move.InsertKeyFrame(1, Vector3.Zero, easing);
        using var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(167);
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        visual.StartAnimation("Translation", move);
        visual.StartAnimation("Opacity", fade);
    }
    private void UpdateToggleLabels()
    {
        if (AbstractCopyState != null) AbstractCopyState.Text = AbstractCopyToggle.IsOn ? "开" : "关";
        if (TrayBatteryState != null) TrayBatteryState.Text = TrayBatteryToggle.IsOn ? "开" : "关";
    }
    private void OnTrayBatteryToggled(object sender, RoutedEventArgs args)
    {
        UpdateToggleLabels();
        if (initializing) return;
        try
        {
            AppPreferences.SaveTrayBatteryEnabled(TrayBatteryToggle.IsOn);
            updateTrayBattery();
            PreferenceError.IsOpen = false;
        }
        catch (Exception error)
        {
            initializing = true;
            TrayBatteryToggle.IsOn = AppPreferences.TrayBatteryEnabled;
            UpdateToggleLabels();
            initializing = false;
            RuntimeLog.Write("Tray battery preference failed: " + error);
            PreferenceError.Message = "托盘电量开关保存失败，请稍后重试。";
            PreferenceError.IsOpen = true;
        }
    }
    private void RefreshStartupRegistration()
    {
        if (closed) return;
        updatingStartup = true;
        try
        {
            string command = StartupRegistration.ReadCommand();
            StartupToggle.IsOn = !string.IsNullOrEmpty(command);
            StartupToggle.IsEnabled = true;
            StartupState.Text = StartupToggle.IsOn ? "开" : "关";
            RepairStartupButton.Visibility = StartupToggle.IsOn
                && !string.Equals(command, StartupRegistration.CurrentCommand, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception error)
        {
            StartupToggle.IsEnabled = false;
            StartupState.Text = "无法读取";
            StartupMessage.Message = "无法读取开机自启动设置：" + error.Message;
            StartupMessage.IsOpen = true;
        }
        finally { updatingStartup = false; }
    }
    private void OnStartupToggled(object sender, RoutedEventArgs args)
    {
        if (initializing || updatingStartup) return;
        SaveStartupRegistration(StartupToggle.IsOn);
    }
    private void OnRepairStartup(object sender, RoutedEventArgs args) => SaveStartupRegistration(true);
    private void SaveStartupRegistration(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            StartupMessage.IsOpen = false;
        }
        catch (Exception error)
        {
            RuntimeLog.Write("Startup registration failed: " + error);
            StartupMessage.Message = "开机自启动设置失败：" + error.Message;
            StartupMessage.IsOpen = true;
        }
        RefreshStartupRegistration();
    }
    private async void OnOpenWindowsStartup(object sender, RoutedEventArgs args)
    {
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps")))
                throw new InvalidOperationException("请手动打开 Windows 设置 → 应用 → 启动。");
        }
        catch (Exception error)
        {
            if (closed) return;
            StartupMessage.Message = error.Message;
            StartupMessage.IsOpen = true;
        }
    }
    private void OnThemeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (initializing) return;
        var previous = Root.RequestedTheme;
        try
        {
            changeTheme(ThemeSelector.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default });
            PreferenceError.IsOpen = false;
        }
        catch (Exception error)
        {
            RuntimeLog.Write("Theme setting failed: " + error);
            initializing = true;
            ThemeSelector.SelectedIndex = previous switch { ElementTheme.Light => 1, ElementTheme.Dark => 2, _ => 0 };
            initializing = false;
            PreferenceError.Message = "主题保存失败，请稍后重试。";
            PreferenceError.IsOpen = true;
        }
    }
    private async void OnConnect(object sender, RoutedEventArgs args)
    {
        try { await ViewModel.ConnectToHeadsetAsync().WaitAsync(windowCancellation); }
        catch (OperationCanceledException) when (closed) { }
    }
    private void UpdateCopyCount()
    {
        if (closed) return;
        copyBrand = ViewModel.CopyBrand;
        int count = AppPreferences.CustomCopyFor(copyBrand).Length;
        CopyCount.Text = $"当前品牌：{AbstractCopy.BrandName(copyBrand)} · 内置 {AbstractCopy.BuiltInCount(copyBrand)} 条 · 已导入 {count} 条";
        CopyDescription.Text = $"每次打开主菜单随机显示一条，关闭后显示“已连接到{AbstractCopy.BrandName(copyBrand)}耳机”。";
        ResetCopyButton.IsEnabled = count > 0;
    }
    private void OnCopyBrandChanged(object sender, PropertyChangedEventArgs args)
    {
        if (closed) return;
        if (args.PropertyName != nameof(PanelViewModel.CopyBrand) || copyBrand == ViewModel.CopyBrand) return;
        UpdateCopyCount();
        CopyImportMessage.IsOpen = false;
    }
    private void ShowCopyMessage(string message, bool error = false)
    {
        if (closed) return;
        CopyImportMessage.Message = message;
        CopyImportMessage.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        CopyImportMessage.IsOpen = true;
    }
    private void OnAbstractCopyToggled(object sender, RoutedEventArgs args)
    {
        UpdateToggleLabels();
        if (initializing) return;
        try
        {
            AppPreferences.SaveAbstractCopyEnabled(AbstractCopyToggle.IsOn);
            refreshMenuCopy();
            CopyImportMessage.IsOpen = false;
        }
        catch (Exception error)
        {
            initializing = true;
            AbstractCopyToggle.IsOn = AppPreferences.AbstractCopyEnabled;
            initializing = false;
            RuntimeLog.Write("Copy preference failed: " + error);
            ShowCopyMessage("文案开关保存失败，请稍后重试。", true);
        }
    }
    private async void OnImportCopy(object sender, RoutedEventArgs args)
    {
        var brand = ViewModel.CopyBrand;
        ImportCopyButton.IsEnabled = ResetCopyButton.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".txt");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (closed) return;
            if (closed || file == null) return;
            using var stream = File.OpenRead(file.Path);
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("文件过大，请选择不超过 1 MB 的文案文件。");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            var imported = AbstractCopy.Parse(await reader.ReadToEndAsync(windowCancellation), brand);
            if (closed) return;
            if (imported.Length == 0)
            {
                ShowCopyMessage("没有可新增的文案，请检查文件是否为空或只有内置文案。", true);
                return;
            }
            var previous = AppPreferences.CustomCopyFor(brand);
            var combined = AbstractCopy.Normalize(previous.Concat(imported), brand);
            int added = combined.Length - previous.Length;
            if (added == 0) { ShowCopyMessage("这些文案已经导入，无需重复添加。"); return; }
            AppPreferences.SaveCustomCopy(combined, brand);
            refreshMenuCopy();
            ShowCopyMessage($"已为{AbstractCopy.BrandName(brand)}新增 {added} 条文案。" + (AppPreferences.AbstractCopyEnabled ? "" : "开启抽象文案后即可显示。"));
        }
        catch (DecoderFallbackException) { ShowCopyMessage("无法读取文件编码，请将 TXT 文件另存为 UTF-8 后重试。", true); }
        catch (Exception error)
        {
            RuntimeLog.Write("Copy import failed: " + error);
            ShowCopyMessage("导入失败：" + error.Message, true);
        }
        finally { if (!closed) { ImportCopyButton.IsEnabled = true; UpdateCopyCount(); } }
    }
    private void OnResetCopy(object sender, RoutedEventArgs args)
    {
        try
        {
            var brand = ViewModel.CopyBrand;
            AppPreferences.SaveCustomCopy(Array.Empty<string>(), brand);
            UpdateCopyCount();
            refreshMenuCopy();
            ShowCopyMessage($"已恢复为{AbstractCopy.BrandName(brand)}的 {AbstractCopy.BuiltInCount(brand)} 条内置文案。");
        }
        catch (Exception error) { RuntimeLog.Write("Copy reset failed: " + error); ShowCopyMessage("恢复失败，请稍后重试。", true); }
    }
    private bool loadingHeadsets;
    private async Task ReloadHeadsetsAsync()
    {
        if (closed || loadingHeadsets) return;
        loadingHeadsets = true;
        try
        {
            var previous = HeadsetSelector.SelectedItem as HeadsetChoice;
            var headsets = await ViewModel.ListHeadsetsAsync().WaitAsync(windowCancellation);
            if (closed) return;
            HeadsetSelector.ItemsSource = headsets;
            ulong? address = configurationAddress ?? previous?.Address ?? ViewModel.SelectedHeadsetAddress;
            HeadsetSelector.SelectedItem = headsets.FirstOrDefault(d => d.Address == address)
                ?? headsets.FirstOrDefault(d => d.ProfileId != null && d.Connected)
                ?? headsets.FirstOrDefault(d => d.ProfileId != null) ?? headsets.FirstOrDefault();
            configurationAddress = null;
            // Re-evaluate even if the ComboBox reuses an equal selected record.
            SuggestModel();
            if (headsets.Length == 0) ShowSelectionMessage("未找到已配对设备，请先打开 Windows 蓝牙设置配对耳机。", false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowSelectionMessage("设备列表读取失败：" + error.Message, true); }
        finally { loadingHeadsets = false; }
    }
    private void OnHeadsetChanged(object sender, SelectionChangedEventArgs args)
    {
        if (closed) return;
        SuggestModel();
        HeadsetSelectionMessage.IsOpen = false;
    }
    private void SuggestModel()
    {
        if (closed) return;
        ModelSelector.SelectedItem = HeadsetSelector.SelectedItem is HeadsetChoice choice
            ? HeadsetProfiles.Find(choice.ProfileId) ?? HeadsetProfiles.Detect(choice.Name) : null;
    }
    private void LoadModels()
    {
        if (closed) return;
        ModelSelector.ItemsSource = HeadsetProfiles.All;
        SuggestModel();
        CatalogSummary.Text = HeadsetCatalog.Current.Summary;
    }
    private void OnModelChanged(object sender, SelectionChangedEventArgs args)
        => ModelHelp.Text = ModelSelector.SelectedItem is HeadsetProfile model ? model.Description : "名称未匹配，请选择耳机的实际型号。";
    private async void OnReloadHeadsets(object sender, RoutedEventArgs args) => await ReloadHeadsetsAsync();
    private async void OnApplyHeadset(object sender, RoutedEventArgs args)
    {
        if (closed || loadingHeadsets) return;
        try
        {
            var choice = HeadsetSelector.SelectedItem as HeadsetChoice;
            var model = ModelSelector.SelectedItem as HeadsetProfile;
            await ViewModel.SelectHeadsetAsync(choice, model).WaitAsync(windowCancellation);
            if (closed) return;
            // Inline confirmation survives the selection reset performed by the
            // reload below, which closes HeadsetSelectionMessage via OnHeadsetChanged.
            ApplySuccessText.Visibility = Visibility.Visible;
            await ReloadHeadsetsAsync();
            if (closed) return;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowSelectionMessage(error.Message, true); }
    }
    private void ShowSelectionMessage(string message, bool error)
    {
        if (closed) return;
        if (error) ApplySuccessText.Visibility = Visibility.Collapsed;
        HeadsetSelectionMessage.Message = message;
        HeadsetSelectionMessage.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        HeadsetSelectionMessage.IsOpen = true;
    }
    private void OnOpenPanel(object sender, RoutedEventArgs args) => openPanel();
    private async void OnOpenBluetooth(object sender, RoutedEventArgs args) => await openBluetooth();
    private void OnConfigureHeadset(object sender, RoutedEventArgs args) => OpenConfiguration();
    private void OnEditBoundHeadset(object sender, RoutedEventArgs args)
    {
        if (((Button)sender).Tag is not BoundHeadset headset) return;
        configurationAddress = headset.Address;
        OpenConfiguration();
    }
    private async void OnUseBoundHeadset(object sender, RoutedEventArgs args)
    {
        try
        {
            await ViewModel.SelectBoundHeadsetAsync(((Button)sender).Tag as BoundHeadset).WaitAsync(windowCancellation);
            if (closed) return;
            BindingMessage.IsOpen = false;
        }
        catch (Exception) when (closed) { }
        catch (Exception error) when (!closed) { BindingMessage.Message = error.Message; BindingMessage.IsOpen = true; }
    }
    private void OnDeleteBoundHeadset(object sender, RoutedEventArgs args)
    {
        if (closed || deletingBoundHeadset || sender is not Button button || button.Tag is not BoundHeadset headset) return;
        DismissDeleteConfirmation();
        bool confirmed = false;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "是否删除这副耳机的配置？",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        });
        content.Children.Add(new TextBlock
        {
            Text = "耳机：" + headset.Name,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        });
        content.Children.Add(new TextBlock
        {
            Text = "仅删除软件内的配置，保留 Windows 蓝牙配对。",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280
        });
        var confirm = new Button
        {
            Content = "是，删除配置",
            FontSize = 14,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(confirm);
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 16, 20, 20)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 220d));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        var flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.TopEdgeAlignedLeft,
            LightDismissOverlayMode = LightDismissOverlayMode.Off,
            FlyoutPresenterStyle = presenterStyle
        };
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(deleteConfirmation, flyout)) deleteConfirmation = null;
        };
        confirm.Click += async (_, _) =>
        {
            if (closed || confirmed || deletingBoundHeadset || !ReferenceEquals(deleteConfirmation, flyout)) return;
            confirmed = true;
            deletingBoundHeadset = true;
            confirm.IsEnabled = false;
            try
            {
                DismissDeleteConfirmation();
                await DeleteConfirmedBoundHeadsetAsync(headset);
            }
            finally { deletingBoundHeadset = false; }
        };
        try
        {
            deleteConfirmation = flyout;
            flyout.ShowAt(button);
        }
        catch (Exception error)
        {
            DismissDeleteConfirmation();
            RuntimeLog.Write("Delete confirmation failed: " + error);
            if (!closed)
            {
                BindingMessage.Message = "暂时无法显示删除确认，请稍后重试。";
                BindingMessage.IsOpen = true;
            }
        }
    }
    private void DismissDeleteConfirmation()
    {
        var flyout = deleteConfirmation;
        deleteConfirmation = null;
        if (flyout != null) UiCleanup.Run("settings delete flyout", flyout.Hide);
    }
    private async Task DeleteConfirmedBoundHeadsetAsync(BoundHeadset headset)
    {
        try
        {
            await ViewModel.RemoveBoundHeadsetAsync(headset).WaitAsync(windowCancellation);
            if (closed) return;
            BindingMessage.IsOpen = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (closed) { }
        catch (Exception error) when (!closed) { BindingMessage.Message = "删除失败：" + error.Message; BindingMessage.IsOpen = true; }
    }
    private void SetCatalogBusy(bool busy)
    {
        if (closed) return;
        ImportModelButton.IsEnabled = UpdateModelButton.IsEnabled = !busy;
        CatalogProgress.IsActive = busy;
        CatalogProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShowCatalogMessage(string message, bool error)
    {
        if (closed) return;
        CatalogMessage.Message = message;
        CatalogMessage.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        CatalogMessage.IsOpen = true;
    }
    private async void OnImportModels(object sender, RoutedEventArgs args)
    {
        SetCatalogBusy(true);
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (closed) return;
            if (closed || file == null) return;
            using var stream = await file.OpenStreamForReadAsync();
            if (closed) return;
            if (stream.Length > 1024 * 1024) throw new InvalidDataException("配置文件不能超过 1 MB。");
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            int count = await HeadsetCatalog.Current.ImportAsync(await reader.ReadToEndAsync(windowCancellation), windowCancellation);
            if (closed) return;
            LoadModels();
            await ReloadHeadsetsAsync();
            if (closed) return;
            await RefreshBindingsAsync();
            if (closed) return;
            ShowCatalogMessage($"已导入 {count} 个自定义型号配置。", false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ShowCatalogMessage("导入失败：" + error.Message, true); }
        finally { SetCatalogBusy(false); }
    }
    private async void OnUpdateModels(object sender, RoutedEventArgs args)
    {
        SetCatalogBusy(true);
        try
        {
            string result = await HeadsetCatalog.Current.UpdateFromOpenFreebudsAsync(windowCancellation);
            if (closed) return;
            LoadModels();
            await ReloadHeadsetsAsync();
            if (closed) return;
            await RefreshBindingsAsync();
            if (closed) return;
            ShowCatalogMessage(result, false);
        }
        catch (OperationCanceledException) when (windowCancellation.IsCancellationRequested) { }
        catch (Exception error) { ShowCatalogMessage("更新失败，已保留现有型号配置：" + error.Message, true); }
        finally { SetCatalogBusy(false); }
    }
}
