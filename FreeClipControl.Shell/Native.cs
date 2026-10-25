using System;
using System.Runtime.InteropServices;

namespace FreeClipControl.Shell
{
    internal static class Native
    {
        internal const uint TrayMessage = 0x8000 + 37;
        internal static readonly uint ShowPanelMessage = RegisterWindowMessage("FreeClipControl.Shell.ShowPanel");
        internal static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct NotifyIconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NotifyIconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, CallbackMessage;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        internal delegate IntPtr SubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")]
        internal static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Shell_NotifyIcon(uint action, ref NotifyIconData data);
        [DllImport("shell32.dll")]
        internal static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier id, out Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        internal static bool IsForegroundWithin(IntPtr window)
        {
            var foreground = GetForegroundWindow();
            return foreground == window || foreground != IntPtr.Zero && GetAncestor(foreground, 3) == window;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadImage(IntPtr instance, string path, uint kind, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
        [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr cursor);
        private static readonly IntPtr arrowCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512));
        internal static void UseArrowCursor()
        {
            if (arrowCursor != IntPtr.Zero) SetCursor(arrowCursor);
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out Rect rect);

        internal static void ConfigurePopup(IntPtr window, bool switcherEntry)
        {
            // WS_POPUP, without WS_CAPTION / WS_THICKFRAME / system-menu bits.
            // SetBorderAndTitleBar leaves a native frame on affected WinUI versions.
            long style = GetWindowLongPtr(window, -16).ToInt64();
            style = (style & ~0x00CF0000L) | 0x80000000L;
            SetWindowLongPtr(window, -16, new IntPtr(style));
            SetToolWindow(window, !switcherEntry);
            int noBorder = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(window, 34, ref noBorder, sizeof(int));
            int noRound = 1;
            DwmSetWindowAttribute(window, 33, ref noRound, sizeof(int));
            int noTransition = 1;
            DwmSetWindowAttribute(window, 3, ref noTransition, sizeof(int));
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        }

        internal static bool HasNoNativeFrame(IntPtr window)
        {
            long style = GetWindowLongPtr(window, -16).ToInt64();
            return (style & 0x00CF0000L) == 0 && (style & 0x80000000L) != 0
                && GetWindowRect(window, out var outer) && GetClientRect(window, out var client)
                && outer.Right - outer.Left == client.Right && outer.Bottom - outer.Top == client.Bottom;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        internal static bool PrimaryButtonDown => (GetAsyncKeyState(1) & 0x8000) != 0;
        internal static bool IsPointerOverTrayIcon(IntPtr window)
        {
            var id = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = window, Id = 1 };
            return Shell_NotifyIconGetRect(ref id, out var rect) == 0 && GetCursorPos(out var point)
                && point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
        internal static double GetWindowScale(IntPtr window) => Math.Max(96u, GetDpiForWindow(window)) / 96.0;
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
        [StructLayout(LayoutKind.Sequential)]
        private struct HighContrast { public uint Size, Flags; public IntPtr Scheme; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrast value, uint flags);

        internal static bool IsHighContrastEnabled()
        {
            var value = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
            return SystemParametersInfo(0x0042, value.Size, ref value, 0) && (value.Flags & 1) != 0;
        }

        internal static void SetToolWindow(IntPtr window, bool enabled = true)
        {
            long style = GetWindowLongPtr(window, -20).ToInt64();
            SetWindowLongPtr(window, -20, new IntPtr(enabled
                ? (style | 0x80L) & ~0x40000L : (style & ~0x80L) | 0x40000L));
        }

        internal static Rect GetTrayWorkArea(IntPtr window, out double scale, out Rect anchor)
        {
            var id = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf(typeof(NotifyIconIdentifier)), Window = window, Id = 1 };
            Point point;
            if (Shell_NotifyIconGetRect(ref id, out anchor) == 0)
                point = new Point { X = (anchor.Left + anchor.Right) / 2, Y = (anchor.Top + anchor.Bottom) / 2 };
            else
            {
                GetCursorPos(out point);
                anchor = new Rect { Left = point.X, Top = point.Y, Right = point.X, Bottom = point.Y };
            }
            IntPtr monitor = MonitorFromPoint(point, 2);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref info)) throw new System.ComponentModel.Win32Exception();
            uint dx, dy;
            scale = GetDpiForMonitor(monitor, 0, out dx, out dy) == 0 ? dx / 96.0 : GetDpiForWindow(window) / 96.0;
            if (scale <= 0) scale = 1;
            return info.Work;
        }
    }
}
