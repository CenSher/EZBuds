using FreeClipControl.Shell;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
var cache = new MenuCachePolicy();
bool Evict(double minutes, bool visible = false, bool preparing = false, bool animating = false, bool confirming = false)
    => cache.CanRelease(TimeSpan.FromMinutes(minutes), visible, preparing, animating, confirming);
Check(!Evict(100), "No idle deadline before a menu has been hidden");
cache.Hide(TimeSpan.Zero);
Check(!Evict(6.99) && Evict(7), "Keep the menu warm for seven minutes");
cache.Hide(TimeSpan.FromMinutes(6));
Check(Evict(7), "Unrelated repeated hide requests do not extend cache retention");
Check(!Evict(8, visible: true) && !Evict(8, preparing: true) && !Evict(8, animating: true)
    && !Evict(8, confirming: true), "Never release an active operation or visible menu");
cache.Open();
Check(!Evict(100), "Reopening cancels the previous deadline");
cache.Hide(TimeSpan.FromMinutes(100));
Check(!Evict(106) && Evict(107), "A later close starts a fresh retention period");
foreach (var argsCase in new[] { Array.Empty<string>(), new[] { "--background" }, new[] { "--show" }, new[] { "--diagnose", "sample.log" } })
{
    var options = ShellLaunchOptions.Parse(argsCase);
    Check(!options.ShowPanelOnLaunch && options.UseTray && !options.KeepOpen && !options.OpenSettings,
        "Ordinary first and repeated launch remains tray-only");
}
var settings = ShellLaunchOptions.Parse(new[] { "--settings" });
Check(settings.OpenSettings && !settings.ShowPanelOnLaunch && settings.UseTray, "Settings switch never opens the menu");
var preview = ShellLaunchOptions.Parse(new[] { "--preview" });
Check(preview.ShowPanelOnLaunch && !preview.KeepOpen && preview.UseTray, "Preview never disables dismissal");
Check(ShellLaunchOptions.Parse(new[] { "--keep-open" }).KeepOpen, "Only an explicit keep-open switch disables dismissal");

var state = new DismissState(true, false, false, false, false, false, false, false, true, false, false);
Check(ShellBehaviorPolicy.Decide(state) == DismissAction.Wait, "Focused popup remains visible");
state = state with { ForegroundWithin = false };
Check(ShellBehaviorPolicy.Decide(state) == DismissAction.Close, "Actual foreground change closes even when activation notification is stale");
Check(ShellBehaviorPolicy.Decide(state with { Animating = true }) == DismissAction.Wait, "Animation is allowed to complete");
Check(ShellBehaviorPolicy.Decide(state) == DismissAction.Close, "Dismiss resumes after animation");
Check(ShellBehaviorPolicy.Decide(state with { ContextMenuOpen = true }) == DismissAction.Wait, "Device context menu holds dismissal");
Check(ShellBehaviorPolicy.Decide(state) == DismissAction.Close, "Dismiss resumes after context menu closes");
Check(ShellBehaviorPolicy.Decide(state with { TrayMenuOpen = true }) == DismissAction.Wait, "Tray menu holds dismissal");
Check(ShellBehaviorPolicy.Decide(state with { ConfirmationOpen = true }) == DismissAction.Wait, "Confirmation holds dismissal");
Check(ShellBehaviorPolicy.Decide(state) == DismissAction.Close, "Dismiss resumes after confirmation closes");
Check(ShellBehaviorPolicy.Decide(state with { Open = false }) == DismissAction.StopTracking, "Hidden popup stops tracking");
Check(ShellBehaviorPolicy.Decide(state with { Fallback = true }) == DismissAction.StopTracking, "Standalone fallback remains usable");
Check(ShellBehaviorPolicy.ShouldTrack(true, false, false, false), "Tracking resumes after tray recovery or repeated show");
Check(ShellBehaviorPolicy.Decide(state with { KeepOpen = true }) == DismissAction.StopTracking, "Explicit keep-open is respected");
Check(ShellBehaviorPolicy.Decide(state with { Exiting = true }) == DismissAction.StopTracking, "Exit owns its close sequence");
Check(ShellBehaviorPolicy.Decide(state with { PointerOverTray = true, PointerDown = true }) == DismissAction.PreserveTrayGesture,
    "Tray mouse-down preserves the original toggle intent");
var intent = new TrayClickIntent();
intent.CaptureIfMissing(true);
Check(!intent.Activate(false, false), "Tray click closes instead of reopening a dismissed popup");
Check(intent.Activate(false, true), "Keyboard tray activation can open a closed popup");
Console.WriteLine($"{checks} shell policy checks passed. No windows opened; no UI automation or Bluetooth access.");
