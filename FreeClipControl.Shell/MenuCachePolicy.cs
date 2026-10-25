using System;

namespace FreeClipControl.Shell;

// Caller supplies monotonic elapsed time so clock/time-zone changes cannot evict an active menu.
internal sealed class MenuCachePolicy
{
    internal static readonly TimeSpan Retention = TimeSpan.FromMinutes(7);
    private TimeSpan? hiddenSince;
    internal void Open() => hiddenSince = null;
    internal void Hide(TimeSpan now) => hiddenSince ??= now;
    internal bool CanRelease(TimeSpan now, bool visible, bool preparing, bool animating, bool confirming)
        => hiddenSince.HasValue && now - hiddenSince.Value >= Retention
            && !visible && !preparing && !animating && !confirming;
}
