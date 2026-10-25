using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace FreeClipControl.Shell;

public sealed partial class EarphonePanel : UserControl, IDisposable
{
    private readonly InputSystemCursor normalCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    private MenuFlyout activeDeviceMenu;
    private Flyout activeConfirmation;
    private bool applyingConfirmation;
    private HeadsetBrand menuCopyBrand;
    public PanelViewModel ViewModel { get; private set; }
    private bool disposed;
    private bool syncingNoiseMode;
    public event EventHandler MenuOpened;
    public event EventHandler MenuClosed;
    public event EventHandler ConfirmationOpened;
    public event EventHandler ConfirmationClosed;
    public event EventHandler SettingsRequested;
    internal UIElement MainContent => Root;
    internal bool HasConfirmation => activeConfirmation != null || applyingConfirmation;
    internal void HideConfirmation() => activeConfirmation?.Hide();
    internal CornerRadius SurfaceCorners => Surface.CornerRadius;
    internal bool HasAcrylic => Material.IsLoaded && Material.SystemBackdrop is PanelAcrylicBackdrop;

    public EarphonePanel()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception error) { RuntimeLog.Write("Earphone panel startup failed: " + error); throw; }
    }
    internal void Initialize(PanelViewModel viewModel)
    {
        ViewModel = viewModel;
        try
        {
            ContentHost.Children.Remove(Root);
            ContentFrame.Navigate(typeof(HeadsetHomePage), this, new SuppressNavigationTransitionInfo());
            RefreshMenuCopy();
            ViewModel.PropertyChanged += OnCopyBrandChanged;
            ViewModel.PropertyChanged += OnNoiseModeChanged;
            ProtectedCursor = normalCursor;
            Bindings.Update();
            SyncNoiseMode();
        }
        catch (Exception error) { RuntimeLog.Write("Earphone panel startup failed: " + error); throw; }
    }
    internal void RefreshMenuCopy()
    {
        menuCopyBrand = ViewModel.CopyBrand;
        bool abstractEnabled = AppPreferences.AbstractCopyEnabled;
        MenuCopy.Text = AbstractCopy.Pick(abstractEnabled, AppPreferences.CustomCopyFor(menuCopyBrand), MenuCopy.Text, menuCopyBrand);
        ToolTipService.SetToolTip(MenuCopy, abstractEnabled
            ? menuCopyBrand == HeadsetBrand.Honor ? "已连接到Honor耳机" : "已连接到WAWEI耳机"
            : MenuCopy.Text);
    }
    private void OnCopyBrandChanged(object sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PanelViewModel.CopyBrand) && menuCopyBrand != ViewModel.CopyBrand) RefreshMenuCopy();
    }
    internal void FocusPrimaryAction()
    {
        if (ContentFrame.Content is HeadsetSwitchPage) return;
        if (ViewModel.IsConnectionNoticeVisible) BluetoothSettingsButton.Focus(FocusState.Programmatic);
        else RefreshButton.Focus(FocusState.Programmatic);
    }
    internal void HideDeviceMenu()
    {
        activeDeviceMenu?.Hide();
        NoiseModeSelector.IsDropDownOpen = false;
        NoiseLevelSelector.IsDropDownOpen = false;
    }
    private void OnNoiseModeChanged(object sender, PropertyChangedEventArgs args)
    {
        if (!disposed && (args.PropertyName is nameof(PanelViewModel.NoiseMode) or nameof(PanelViewModel.NoiseLevel)
            or nameof(PanelViewModel.NoiseLevels))) SyncNoiseMode();
    }
    private void SyncNoiseMode()
    {
        syncingNoiseMode = true;
        try
        {
            NoiseModeSelector.SelectedIndex = Array.FindIndex(ViewModel.NoiseChoices, choice => choice.Mode == ViewModel.NoiseMode);
            var levels = ViewModel.NoiseLevels;
            NoiseLevelSelector.ItemsSource = levels;
            NoiseLevelSelector.SelectedIndex = Array.FindIndex(levels, choice => choice.Value == ViewModel.NoiseLevel);
        }
        finally { syncingNoiseMode = false; }
    }
    private async void OnNoiseModeSelected(object sender, SelectionChangedEventArgs args)
    {
        if (disposed || syncingNoiseMode || ViewModel == null || NoiseModeSelector.SelectedItem is not NoiseControlChoice choice) return;
        // The selected value represents confirmed device state, not an optimistic local setting.
        SyncNoiseMode();
        await ViewModel.SetNoiseControlAsync(choice.Mode);
    }
    private void OnNoiseDropDownOpened(object sender, object args) => MenuOpened?.Invoke(this, EventArgs.Empty);
    private async void OnNoiseLevelSelected(object sender, SelectionChangedEventArgs args)
    {
        if (disposed || syncingNoiseMode || ViewModel == null || NoiseLevelSelector.SelectedItem is not NoiseLevelChoice choice) return;
        SyncNoiseMode();
        await ViewModel.SetNoiseLevelAsync(choice.Value);
    }
    private void OnNoiseDropDownClosed(object sender, object args) => MenuClosed?.Invoke(this, EventArgs.Empty);
    internal void SetBackdropInputActive(bool value)
    {
        if (Material.SystemBackdrop is PanelAcrylicBackdrop backdrop) backdrop.SetHostInputActive(value);
    }
    private void OnDeviceSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var selected = DeviceList.SelectedItem as DeviceInfo;
        ViewModel.SelectDevice(selected);
        if (selected != null) DispatcherQueue.TryEnqueue(() => DeviceList.ScrollIntoView(selected));
    }
    private async void OnDeviceAction(object sender, RoutedEventArgs args) => await ViewModel.ToggleAsync(((Button)sender).Tag as DeviceInfo);
    private void OnConnectionLock(object sender, RoutedEventArgs args)
    {
        if (HasConfirmation || !ViewModel.CanInteract || ((Button)sender).Tag is not DeviceInfo device || !device.CanChangeConnectionLock) return;
        bool locked = !device.IsConnectionLocked;
        bool confirmed = false;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = locked ? "是否锁定本次连接？" : "是否解除本次连接锁定？",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium
        });
        var confirm = new Button
        {
            Content = locked ? "是，锁定到此设备" : "是，解除锁定",
            FontSize = 14,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        content.Children.Add(new TextBlock
        {
            Text = "设备：" + device.Name + (device.IsLocalComputer ? "（本机）" : ""),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260
        });
        if (locked) content.Children.Add(new TextBlock
        {
            Text = "音频将锁定到此设备，其他设备已有的锁定会解除。",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260
        });
        content.Children.Add(confirm);
        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 16, 20, 20)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 220d));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        var flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
            LightDismissOverlayMode = LightDismissOverlayMode.Off,
            FlyoutPresenterStyle = presenterStyle
        };
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(activeConfirmation, flyout)) activeConfirmation = null;
            if (!confirmed) RuntimeLog.Write("Connection-lock confirmation dismissed; no command sent.");
            if (!applyingConfirmation) ConfirmationClosed?.Invoke(this, EventArgs.Empty);
        };
        confirm.Click += async (_, _) =>
        {
            if (confirmed) return;
            confirmed = true;
            confirm.IsEnabled = false;
            applyingConfirmation = true;
            flyout.Hide();
            try { await ViewModel.SetConnectionLockedAsync(device, locked); }
            catch (Exception error) { RuntimeLog.Write("Connection-lock action failed: " + error); ViewModel.ReportStatus("暂时无法完成锁定操作，请刷新后重试。"); }
            finally { applyingConfirmation = false; ConfirmationClosed?.Invoke(this, EventArgs.Empty); }
        };
        try
        {
            activeConfirmation = flyout;
            ConfirmationOpened?.Invoke(this, EventArgs.Empty);
            RuntimeLog.Write("Connection-lock confirmation opened; targetLocked=" + locked);
            flyout.ShowAt((FrameworkElement)sender);
        }
        catch (Exception error)
        {
            activeConfirmation = null;
            ConfirmationClosed?.Invoke(this, EventArgs.Empty);
            RuntimeLog.Write("Connection-lock confirmation failed: " + error);
            ViewModel.ReportStatus("暂时无法显示锁定确认，请稍后重试。");
        }
    }
    private async void OnRefresh(object sender, RoutedEventArgs args) => await ViewModel.RefreshAsync();
    private async void OnSwitchHeadset(object sender, RoutedEventArgs args)
    {
        HideDeviceMenu(); HideConfirmation();
        try
        {
            await ViewModel.ReloadBindingsAsync();
            if (disposed) return;
            ContentFrame.Navigate(typeof(HeadsetSwitchPage), this, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
        catch (Exception error) { ViewModel.ReportStatus("暂时无法列出耳机：" + error.Message); }
    }
    internal void NavigateHome(bool animate = true)
    {
        if (disposed || ContentFrame.Content is HeadsetHomePage) return;
        ContentFrame.Navigate(typeof(HeadsetHomePage), this,
            animate ? new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft } : new SuppressNavigationTransitionInfo());
    }
    internal void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);
    private async void OnOpenBluetoothSettings(object sender, RoutedEventArgs args) => await OpenBluetoothSettingsAsync();
    internal async Task OpenBluetoothSettingsAsync()
    {
        if (ViewModel.NeedsConfiguration) { RequestSettings(); return; }
        BluetoothSettingsButton.IsEnabled = false;
        try
        {
            bool launched = await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));
            RuntimeLog.Write("Bluetooth Settings launch; success=" + launched);
            if (!launched) ViewModel.ReportStatus("暂时无法打开系统蓝牙设置，请稍后重试。");
        }
        catch (Exception error)
        {
            RuntimeLog.Write("Bluetooth Settings launch failed: " + error);
            ViewModel.ReportStatus("暂时无法打开系统蓝牙设置，请稍后重试。");
        }
        finally { if (!disposed) BluetoothSettingsButton.IsEnabled = true; }
    }
    private void OnMenuOpened(object sender, object args)
    {
        activeDeviceMenu = sender as MenuFlyout;
        Native.UseArrowCursor();
        MenuOpened?.Invoke(this, EventArgs.Empty);
    }
    private void OnMenuClosed(object sender, object args)
    {
        if (ReferenceEquals(activeDeviceMenu, sender)) activeDeviceMenu = null;
        MenuClosed?.Invoke(this, EventArgs.Empty);
    }
    private void OnTypeAuto(object sender, RoutedEventArgs args) => SetDeviceType(sender, ProductIconKind.Unknown);
    private void OnTypeComputer(object sender, RoutedEventArgs args) => SetDeviceType(sender, ProductIconKind.Computer);
    private void OnTypePhone(object sender, RoutedEventArgs args) => SetDeviceType(sender, ProductIconKind.Phone);
    private void OnTypeTablet(object sender, RoutedEventArgs args) => SetDeviceType(sender, ProductIconKind.Tablet);
    private void SetDeviceType(object sender, ProductIconKind kind)
    {
        if (((MenuFlyoutItem)sender).Tag is not DeviceInfo device) return;
        try { device.SetTypeOverride(kind); }
        catch (Exception error) { RuntimeLog.Write("Device type could not be saved: " + error.Message); ViewModel.ReportStatus("设备类型保存失败，请稍后重试。"); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        UiCleanup.Run("panel bindings", () => Bindings.StopTracking());
        if (ViewModel != null) ViewModel.PropertyChanged -= OnCopyBrandChanged;
        if (ViewModel != null) ViewModel.PropertyChanged -= OnNoiseModeChanged;
        MenuOpened = MenuClosed = ConfirmationOpened = ConfirmationClosed = SettingsRequested = null;
        UiCleanup.Run("panel flyouts", () => { HideDeviceMenu(); HideConfirmation(); });
        // The complete Frame is retired with its owner. Do not mutate Content,
        // CacheSize or navigation stacks during unload: WinUI may still be navigating.
        UiCleanup.Run("panel acrylic", () =>
        {
            if (Material.SystemBackdrop is PanelAcrylicBackdrop backdrop)
                backdrop.DetachForRetirement(Material);
            else Material.SystemBackdrop = null;
        });
        UiCleanup.Run("panel cursor", () => { ProtectedCursor = null; normalCursor.Dispose(); });
    }
}
