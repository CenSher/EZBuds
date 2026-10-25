using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FreeClipControl.Shell;

public sealed partial class ProductIcon : UserControl
{
    private bool initialized;
    // Keep the XAML property primitive; device classification remains an enum in the model.
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(ProductIcon), new PropertyMetadata("Unknown", Changed));
    public static readonly DependencyProperty IsConnectedProperty = DependencyProperty.Register(nameof(IsConnected), typeof(bool), typeof(ProductIcon), new PropertyMetadata(false, Changed));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsConnected { get => (bool)GetValue(IsConnectedProperty); set => SetValue(IsConnectedProperty, value); }
    public ProductIcon()
    {
        try
        {
            InitializeComponent();
            initialized = true;
            Render();
        }
        catch (Exception error)
        {
            RuntimeLog.Write("ICON INITIALIZATION FAILED: " + error);
            throw;
        }
    }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((ProductIcon)target).Render();
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void Render()
    {
        if (!initialized) return;
        ProductIconKind kind = Enum.TryParse(Kind, out ProductIconKind parsed) ? parsed : ProductIconKind.Unknown;
        bool device = kind is ProductIconKind.Unknown or ProductIconKind.Computer or ProductIconKind.Phone or ProductIconKind.Tablet;
        DeviceDrawing.Visibility = Visible(device);
        LeftEarDrawing.Visibility = Visible(kind == ProductIconKind.LeftEar);
        RightEarDrawing.Visibility = Visible(kind == ProductIconKind.RightEar);
        CaseDrawing.Visibility = Visible(kind == ProductIconKind.ChargingCase);
        ActiveBadge.Visibility = ActiveDevice.Visibility = Visible(IsConnected);
        InactiveBadge.Visibility = InactiveDevice.Visibility = Visible(!IsConnected);
        ActivePhone.Visibility = InactivePhone.Visibility = Visible(kind == ProductIconKind.Phone);
        ActiveTablet.Visibility = InactiveTablet.Visibility = Visible(kind == ProductIconKind.Tablet);
        ActiveComputer.Visibility = InactiveComputer.Visibility = Visible(kind == ProductIconKind.Computer);
        ActiveUnknown.Visibility = InactiveUnknown.Visibility = Visible(kind == ProductIconKind.Unknown);
    }
}
