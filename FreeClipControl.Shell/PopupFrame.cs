using System;

namespace FreeClipControl.Shell;

// Owns only the native shell of this popup. All pixels, including the shadow,
// belong to the XAML/composition content, so no second window frame can appear.
internal sealed class PopupFrame : IDisposable
{
    private readonly IntPtr handle;
    private readonly Native.SubclassProc callback;
    private bool activationPaintLogged;
    public PopupFrame(IntPtr hwnd)
    {
        handle = hwnd;
        callback = ProcessMessage;
        if (!Native.SetWindowSubclass(handle, callback, new UIntPtr(2), UIntPtr.Zero))
            throw new System.ComponentModel.Win32Exception();
    }

    public void Configure(bool switcherEntry) => Native.ConfigurePopup(handle, switcherEntry);
    public bool IsBorderless => Native.HasNoNativeFrame(handle);

    private IntPtr ProcessMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        // Entire HWND is client area. Never ask DefWindowProc to paint a frame.
        if (message == 0x0083 || message == 0x0085) return IntPtr.Zero; // NCCALCSIZE / NCPAINT
        if (message == 0x0020 && (lParam.ToInt64() & 0xFFFF) == 1) // WM_SETCURSOR / HTCLIENT
        {
            Native.UseArrowCursor();
            return new IntPtr(1);
        }
        if (message == 0x0086) // WM_NCACTIVATE
        {
            // DefWindowProc can paint the classic caption on activation even
            // though NCCALCSIZE/NCPAINT are handled. We own all frame pixels.
            // TRUE permits activation/deactivation; WM_ACTIVATE still flows
            // normally, so WinUI focus and the light-dismiss timer keep working.
            if (!activationPaintLogged)
            {
                activationPaintLogged = true;
                RuntimeLog.Write("Native caption activation painting suppressed.");
            }
            return new IntPtr(1);
        }
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        Native.RemoveWindowSubclass(handle, callback, new UIntPtr(2));
        GC.KeepAlive(callback);
    }
}
