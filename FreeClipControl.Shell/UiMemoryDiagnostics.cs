using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace FreeClipControl.Shell;

// Bounded passive observations after retiring UI. Never induce GC or trim the working set.
internal sealed class UiMemoryDiagnostics : IDisposable
{
    private readonly DispatcherQueueTimer timer;
    private readonly Func<bool> isIdle;
    private readonly List<(string Name, WeakReference Reference)> retired = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan lastObservation = TimeSpan.FromMinutes(-1);
    private int phase;
    private bool disposed;

    internal UiMemoryDiagnostics(DispatcherQueue queue, Func<bool> idle)
    {
        isIdle = idle;
        timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.Tick += OnTick;
    }

    internal void Retire(string name, object instance)
    {
        if (disposed || instance == null) return;
        // Only weak references: diagnostics must not keep a retired window alive.
        if (retired.Count == 64) retired.RemoveAt(0);
        retired.Add((name, new WeakReference(instance)));
        phase = 0;
        timer.Stop();
        timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (disposed || !isIdle()) return;
        try
        {
            if (phase == 0)
            {
                // Coalesce rapid open/close cycles and limit log volume.
                if (clock.Elapsed - lastObservation < TimeSpan.FromMinutes(1)) return;
                LogMemory("before idle observation");
                lastObservation = clock.Elapsed;
                phase = 1;
            }
            else if (phase == 1)
            {
                // Allow pending UI cleanup to drain; do not force collection or wait for finalizers.
                phase = 2;
            }
            else
            {
                LogMemory("after idle observation (natural GC; not proof of native resource release)");
                foreach (var item in retired)
                    RuntimeLog.Write("UI MEMORY object=" + item.Name + "; managedAlive=" + item.Reference.IsAlive);
                retired.Clear();
                phase = 0;
                timer.Stop();
            }
        }
        catch (Exception error)
        {
            RuntimeLog.Write("UI memory observation failed: " + error.Message);
            retired.Clear();
            phase = 0;
            timer.Stop();
        }
    }

    internal static void LogMemory(string reason)
    {
        using var process = Process.GetCurrentProcess();
        RuntimeLog.Write($"UI MEMORY {reason}; managedBytes={GC.GetTotalMemory(false)}; "
            + $"gcCommittedBytes={GC.GetGCMemoryInfo().TotalCommittedBytes}; "
            + $"privateBytes={process.PrivateMemorySize64}; workingSetBytes={process.WorkingSet64}; "
            + $"gen2Collections={GC.CollectionCount(2)}");
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        timer.Tick -= OnTick;
        retired.Clear();
    }
}
