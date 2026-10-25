using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace FreeClipControl.Shell;

public sealed partial class PopupSurface : Grid, IDisposable
{
    private readonly UISettings settings = new();
    private bool shadowLoaded;
    private bool disposed;
    private readonly TaskCompletionSource<bool> shadowReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public EarphonePanel Panel => ContentPanel;
    internal PopupAnimation Animation { get; }
    public bool HasShadow => shadowLoaded;

    public PopupSurface()
    {
        InitializeComponent();
        Animation = new PopupAnimation(this);
        Loaded += (_, _) => RefreshPolicy();
        ActualThemeChanged += (_, _) => RefreshPolicy();
        settings.ColorValuesChanged += OnColorsChanged;
    }

    private void OnColorsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(RefreshPolicy);
    private void OnShadowOpened(object sender, RoutedEventArgs args)
    {
        shadowLoaded = true;
        shadowReady.TrySetResult(true);
        RuntimeLog.Write("Nine-grid popup shadow loaded; transparent center; 12 DIP exterior.");
    }
    private void OnShadowFailed(object sender, ExceptionRoutedEventArgs args)
    {
        shadowLoaded = false;
        shadowReady.TrySetException(new InvalidOperationException("菜单阴影资源加载失败：" + args.ErrorMessage));
        RuntimeLog.Write("Popup shadow asset failed to load: " + args.ErrorMessage);
    }

    public void RefreshPolicy()
    {
        if (disposed) return;
        ShadowImage.Opacity = Native.IsHighContrastEnabled() ? 0 : ActualTheme == ElementTheme.Dark ? .30 : .22;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        UiCleanup.Run("popup animation", Animation.Dispose);
        settings.ColorValuesChanged -= OnColorsChanged;
        Panel.Dispose();
        UiCleanup.Run("popup shadow", () => ShadowImage.Source = null);
    }

    internal async Task WaitUntilReadyAsync(CancellationToken cancellation)
    {
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler handler = (_, _) => loaded.TrySetResult(true);
        Loaded += handler;
        try
        {
            if (IsLoaded) loaded.TrySetResult(true);
            await Task.WhenAll(loaded.Task, shadowReady.Task).WaitAsync(cancellation);
            UpdateLayout();
            // Allow queued binding/layout work to finish while DWM keeps the host cloaked.
            var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => idle.TrySetResult(true)))
                throw new InvalidOperationException("菜单调度队列已关闭。");
            await idle.Task.WaitAsync(cancellation);
            UpdateLayout();
            if (ActualWidth <= 0 || ActualHeight <= 0 || !Panel.IsLoaded)
                throw new InvalidOperationException("菜单布局尚未完成。");
        }
        finally { Loaded -= handler; }
    }
}
