using System;
using System.Linq;

namespace FreeClipControl.Shell;

// These defaults are product behavior, shared by App and ShellWindow. UI
// changes must not introduce independent interpretations of launch switches.
internal sealed record ShellLaunchOptions(bool ShowPanelOnLaunch, bool UseTray, bool KeepOpen, bool OpenSettings)
{
    internal static ShellLaunchOptions Parse(string[] args) => new(
        args.Contains("--preview") || args.Contains("--no-tray"),
        !args.Contains("--no-tray"), args.Contains("--keep-open"), args.Contains("--settings"));
}

internal readonly record struct DismissState(bool Open, bool KeepOpen, bool Fallback, bool Exiting,
    bool ContextMenuOpen, bool TrayMenuOpen, bool ConfirmationOpen, bool Animating, bool ForegroundWithin,
    bool PointerOverTray, bool PointerDown);

internal enum DismissAction { StopTracking, Wait, PreserveTrayGesture, Close }

internal static class ShellBehaviorPolicy
{
    internal static bool ShouldTrack(bool open, bool keepOpen, bool fallback, bool exiting)
        => open && !keepOpen && !fallback && !exiting;
    internal static DismissAction Decide(DismissState state)
    {
        if (!ShouldTrack(state.Open, state.KeepOpen, state.Fallback, state.Exiting)) return DismissAction.StopTracking;
        if (state.ContextMenuOpen || state.TrayMenuOpen || state.ConfirmationOpen || state.Animating || state.ForegroundWithin)
            return DismissAction.Wait;
        if (state.PointerOverTray && state.PointerDown) return DismissAction.PreserveTrayGesture;
        return DismissAction.Close;
    }
}
