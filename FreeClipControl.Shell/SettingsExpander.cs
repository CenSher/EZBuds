using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FreeClipControl.Shell;

// Keep Expander's native content animation, clipping, keyboard handling and
// automation peer. Only replace its header presentation for settings cards.
public sealed class SettingsExpander : Expander
{
    public static readonly DependencyProperty HeaderButtonStyleProperty = DependencyProperty.Register(
        nameof(HeaderButtonStyle), typeof(Style), typeof(SettingsExpander),
        new PropertyMetadata(null, OnHeaderPresentationChanged));
    public static readonly DependencyProperty HeaderPaddingProperty = DependencyProperty.Register(
        nameof(HeaderPadding), typeof(Thickness), typeof(SettingsExpander),
        new PropertyMetadata(new Thickness(20, 18, 20, 18), OnHeaderPresentationChanged));

    private ToggleButton headerButton;
    public Style HeaderButtonStyle
    {
        get => (Style)GetValue(HeaderButtonStyleProperty);
        set => SetValue(HeaderButtonStyleProperty, value);
    }
    public Thickness HeaderPadding
    {
        get => (Thickness)GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }
    public SettingsExpander() => DefaultStyleKey = typeof(Expander);

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        headerButton = GetTemplateChild("ExpanderHeader") as ToggleButton;
        ApplyHeaderPresentation();
    }
    private static void OnHeaderPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsExpander)sender).ApplyHeaderPresentation();
    private void ApplyHeaderPresentation()
    {
        if (headerButton == null) return;
        if (HeaderButtonStyle != null) headerButton.Style = HeaderButtonStyle;
        headerButton.Padding = HeaderPadding;
        headerButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }
}
