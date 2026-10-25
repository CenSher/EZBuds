using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FreeClipControl.Shell;

public sealed partial class HeadsetSwitchPage : Page
{
    private EarphonePanel owner;
    public HeadsetSwitchPage() { InitializeComponent(); }
    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        owner = (EarphonePanel)args.Parameter;
        HeadsetList.ItemsSource = owner.ViewModel.BoundHeadsets;
        EmptyMessage.Visibility = owner.ViewModel.NoBindingsVisibility;
        base.OnNavigatedTo(args);
    }
    private void OnBack(object sender, RoutedEventArgs args) => owner.NavigateHome();
    private void OnSettings(object sender, RoutedEventArgs args) => owner.RequestSettings();
    private async void OnHeadsetClick(object sender, ItemClickEventArgs args)
    {
        if (!owner.ViewModel.CanSelectHeadset) return;
        HeadsetList.IsEnabled = false;
        try { await owner.ViewModel.SelectBoundHeadsetAsync((BoundHeadset)args.ClickedItem); owner.NavigateHome(); }
        catch (Exception error) { SwitchError.Text = error.Message; SwitchError.Visibility = Visibility.Visible; }
        finally { HeadsetList.IsEnabled = true; }
    }
}
