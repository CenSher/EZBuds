using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace FreeClipControl.Shell;

internal sealed class PopupAnimation : IDisposable
{
    private readonly Visual visual;
    private readonly UISettings settings = new();
    private TaskCompletionSource<bool> completion;
    private int generation;
    public bool IsRunning { get; private set; }
    public Vector3 ClosedOffset { get; set; } = new(0, 624, 0);

    public PopupAnimation(UIElement surface)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(surface, true);
        // Use only the composition translation path. Mixing UIElement.Translation
        // with the backing Visual translation can desynchronize WinUI hit testing.
        visual = ElementCompositionPreview.GetElementVisual(surface);
        visual.Properties.InsertVector3("Translation", ClosedOffset);
        visual.Opacity = 1;
    }

    public Task<bool> RunAsync(bool open, bool fresh = false)
    {
        Cancel();
        int current = generation;
        // No depth/perspective transform: open visuals and input share the same plane.
        var target = open ? Vector3.Zero : ClosedOffset;
        if (fresh)
        {
            visual.Properties.InsertVector3("Translation", ClosedOffset);
        }
        if (!settings.AnimationsEnabled)
        {
            visual.Properties.InsertVector3("Translation", target);
            return Task.FromResult(true);
        }
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = completion;
        IsRunning = true;
        var compositor = visual.Compositor;
        var easing = open ? compositor.CreateCubicBezierEasingFunction(Vector2.Zero, new Vector2(0, 1))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(1, 0), Vector2.One);
        var duration = TimeSpan.Parse((string)Application.Current.Resources[open ? "ControlNormalAnimationDuration" : "ControlFastAnimationDuration"], CultureInfo.InvariantCulture);
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.Duration = duration;
        move.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        move.InsertKeyFrame(1, target, easing);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            if (current == generation)
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", target);
                IsRunning = false;
                completion = null;
                RuntimeLog.Write($"ANIMATION {(open ? "OPEN" : "CLOSE")} completed; {duration.TotalMilliseconds}ms; edgeOffset={ClosedOffset}; opacity={visual.Opacity}");
                result.TrySetResult(true);
            }
            batch.Dispose(); move.Dispose(); easing.Dispose();
        };
        visual.StartAnimation("Translation", move);
        batch.End();
        return result.Task;
    }

    internal void PrepareClosed()
    {
        Cancel();
        visual.Properties.InsertVector3("Translation", ClosedOffset);
    }

    private void Cancel()
    {
        generation++;
        visual.StopAnimation("Translation");
        completion?.TrySetResult(false);
        completion = null;
        IsRunning = false;
    }
    public void Dispose() => Cancel();
}
