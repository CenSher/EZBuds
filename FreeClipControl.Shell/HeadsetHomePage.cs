using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FreeClipControl.Shell;

public sealed partial class HeadsetHomePage : Page
{
    public HeadsetHomePage() { InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Required; Background = null; }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        if (Content == null && args.Parameter is EarphonePanel owner) Content = owner.MainContent;
        base.OnNavigatedTo(args);
    }
}
