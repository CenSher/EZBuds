namespace FreeClipControl.Shell;

// Deactivation may dismiss the flyout before Explorer delivers NIN_SELECT.
// Preserve the intention of that pointer gesture rather than toggling the
// possibly changed state at mouse-up. Keyboard activation uses current state.
internal sealed class TrayClickIntent
{
    private bool? openAtPointerDown;
    public void PointerDown(bool open) => openAtPointerDown = open;
    public void CaptureIfMissing(bool open) => openAtPointerDown ??= open;
    public void CancelPointer() => openAtPointerDown = null;
    public bool Activate(bool open, bool keyboard)
    {
        bool wasOpen = keyboard ? open : openAtPointerDown ?? open;
        openAtPointerDown = null;
        return !wasOpen;
    }
}
