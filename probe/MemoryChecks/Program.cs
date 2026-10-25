using FreeClipControl.Shell;
using Microsoft.UI.Dispatching;

int checks = 0;
void Check(bool condition) { if (!condition) throw new Exception("Memory lifecycle check failed"); checks++; }
var queue = new DispatcherQueue();
bool idle = false;
using var reclaimer = new UiMemoryDiagnostics(queue, () => idle);
reclaimer.Retire("test", new object());
queue.Timer.Fire();
Check(RuntimeLog.Messages.Count == 0 && queue.Timer.IsRunning);
idle = true;
queue.Timer.Fire();
Check(RuntimeLog.Messages.Any(x => x.Contains("before idle observation")));
queue.Timer.Fire();
queue.Timer.Fire();
Check(!queue.Timer.IsRunning && RuntimeLog.Messages.Any(x => x.Contains("object=test; managedAlive=")));
int messages = RuntimeLog.Messages.Count;
reclaimer.Retire("rapid close", new object());
queue.Timer.Fire();
Check(RuntimeLog.Messages.Count == messages); // cooldown coalesces rapid closures
reclaimer.Dispose();
reclaimer.Retire("after shutdown", new object());
Check(!queue.Timer.IsRunning);
Console.WriteLine($"{checks} idle observation scheduling checks passed; no app windows or Bluetooth.");

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public DispatcherQueueTimer Timer { get; } = new();
        public DispatcherQueueTimer CreateTimer() => Timer;
    }
    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event Action<DispatcherQueueTimer, object> Tick;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (IsRunning) Tick?.Invoke(this, null); }
    }
}
namespace FreeClipControl.Shell
{
    internal static class RuntimeLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Write(string message) => Messages.Add(message);
    }
}
