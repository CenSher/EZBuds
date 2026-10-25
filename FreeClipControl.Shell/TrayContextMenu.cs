using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FreeClipControl.Shell;

internal sealed class TrayContextMenu : IDisposable
{
    internal enum Command { None, Open, Connect, Settings, Exit }
    private IntPtr owner;
    private bool showing;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Native.Point point);

    public Command Show(bool canConnect)
    {
        if (showing) return Command.None;
        showing = true;
        IntPtr menu = IntPtr.Zero;
        try
        {
            // A zero-area native owner lets the menu get foreground activation
            // without revealing or animating the main WinUI panel.
            if (owner == IntPtr.Zero)
            {
                owner = CreateWindowEx(0x80, "STATIC", "FreeClip tray menu", 0x80000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                if (owner == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new Win32Exception();
            Add(menu, Command.Open, "打开控制面板");
            Add(menu, Command.Connect, "连接到耳机", !canConnect);
            AppendMenu(menu, 0x800, UIntPtr.Zero, null);
            Add(menu, Command.Settings, "设置");
            Add(menu, Command.Exit, "退出");
            if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
            ShowWindow(owner, 4);
            Native.SetForegroundWindow(owner);
            return (Command)TrackPopupMenuEx(menu, 0x10A, point.X, point.Y, owner, IntPtr.Zero);
        }
        finally
        {
            if (owner != IntPtr.Zero) { Native.PostMessage(owner, 0, UIntPtr.Zero, IntPtr.Zero); ShowWindow(owner, 0); }
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            showing = false;
        }
    }
    private static void Add(IntPtr menu, Command command, string text, bool disabled = false)
    {
        if (!AppendMenu(menu, disabled ? 3u : 0, new UIntPtr((uint)command), text)) throw new Win32Exception();
    }
    public void Dispose() { if (owner != IntPtr.Zero) { DestroyWindow(owner); owner = IntPtr.Zero; } }
}
