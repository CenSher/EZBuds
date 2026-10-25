using System;

namespace FreeClipControl.Shell;

internal static class UiCleanup
{
    // Continue detaching owners if a native resource has already closed.
    // This is scoped to teardown, not global exception suppression.
    internal static void Run(string resource, Action release)
    {
        try { release(); }
        catch (Exception error) { RuntimeLog.Write("UI cleanup failed (" + resource + "): " + error); }
    }
}
