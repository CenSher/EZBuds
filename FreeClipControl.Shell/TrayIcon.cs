using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FreeClipControl.Shell
{
    internal sealed class TrayIcon : IDisposable
    {
        private readonly IntPtr window;
        private readonly Native.SubclassProc callback;
        private Native.NotifyIconData data;
        private bool ownsIcon;
        private bool disposed;
        private bool version4;
        public bool IsRegistered { get; private set; }
        public string RegistrationDetails { get; private set; }
        public event EventHandler PointerPressed;
        public event Action<bool> ToggleRequested;
        public event EventHandler ShowRequested;
        public event EventHandler MenuRequested;
        public event EventHandler PlacementChanged;
        public event EventHandler RegistrationChanged;

        public TrayIcon(IntPtr handle, string toolTip)
        {
            window = handle;
            callback = ProcessMessage;
            if (!Native.SetWindowSubclass(window, callback, new UIntPtr(1), UIntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法初始化托盘窗口。");
            var icon = Native.LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Headphones.ico"), 1, 0, 0, 0x50);
            ownsIcon = icon != IntPtr.Zero;
            if (!ownsIcon) icon = Native.LoadIcon(IntPtr.Zero, new IntPtr(32512));
            data = new Native.NotifyIconData
            {
                Size = (uint)Marshal.SizeOf(typeof(Native.NotifyIconData)),
                Window = window, Id = 1, Flags = 0x07, CallbackMessage = Native.TrayMessage,
                Icon = icon, Tip = toolTip, Info = "", InfoTitle = ""
            };
            TryRegister();
        }

        public bool TryRegister()
        {
            if (disposed) return false;
            if (IsRegistered) return true;
            data.Flags = 0x07;
            data.Version = 0;
            if (!Native.Shell_NotifyIcon(0, ref data))
            {
                int error = Marshal.GetLastWin32Error();
                RegistrationDetails = "NIM_ADD returned false; lastError=" + error
                    + "; hwnd=" + window + "; icon=" + data.Icon + "; structSize=" + data.Size;
                return false;
            }
            IsRegistered = true;
            data.Version = 4;
            version4 = Native.Shell_NotifyIcon(4, ref data);
            int versionError = Marshal.GetLastWin32Error();
            // A rejected notification version does not invalidate the added icon.
            RegistrationDetails = version4 ? "NIM_ADD and NIM_SETVERSION succeeded"
                : "NIM_ADD succeeded; NIM_SETVERSION returned false; lastError=" + versionError + "; using legacy callbacks";
            if (version4) data.Flags |= 0x80;
            UpdateTheme();
            RegistrationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void SetToolTip(string text)
        {
            if (disposed || data.Tip == text) return;
            data.Tip = text;
            // Preserve the cached tip when Explorer restarts or registration fails.
            if (IsRegistered)
            {
                if (Native.Shell_NotifyIcon(1, ref data)) RuntimeLog.Write("Tray tooltip updated: " + text);
                else RuntimeLog.Write("Tray tooltip update failed; lastError=" + Marshal.GetLastWin32Error());
            }
        }

        private void UpdateTheme()
        {
            if (disposed || !IsRegistered) return;
            bool dark = true;
            try
            {
                // Taskbar theme is independent of the app's light/dark preference.
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = Convert.ToInt32(key?.GetValue("SystemUsesLightTheme", 0) ?? 0) == 0;
            }
            catch (Exception) { }
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", dark ? "Headphones.ico" : "Headphones-dark.ico");
            var next = Native.LoadImage(IntPtr.Zero, path, 1, 0, 0, 0x50);
            if (next == IntPtr.Zero) return;
            var previous = data.Icon;
            bool previousOwned = ownsIcon;
            data.Icon = next;
            if (Native.Shell_NotifyIcon(1, ref data))
            {
                ownsIcon = true;
                if (previousOwned) Native.DestroyIcon(previous);
            }
            else { data.Icon = previous; Native.DestroyIcon(next); }
        }

        private IntPtr ProcessMessage(IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr reference)
        {
            if (message == Native.TrayMessage)
            {
                uint notification = (uint)(lParam.ToInt64() & 0xFFFF);
                if (notification == 0x0201) PointerPressed?.Invoke(this, EventArgs.Empty);
                else if (version4 && (notification == 0x400 || notification == 0x401)
                    || !version4 && notification == 0x0202)
                {
                    RuntimeLog.Write($"TRAY activation; keyboard={notification == 0x401}");
                    ToggleRequested?.Invoke(notification == 0x401);
                }
                else if (notification == 0x7B || !version4 && notification == 0x0205) MenuRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }
            if (message == Native.ShowPanelMessage) { ShowRequested?.Invoke(this, EventArgs.Empty); return IntPtr.Zero; }
            if (message == Native.TaskbarCreatedMessage)
            {
                IsRegistered = false;
                TryRegister();
                RegistrationChanged?.Invoke(this, EventArgs.Empty);
            }
            if (message == 0x001A) UpdateTheme();
            if (message == 0x02E0 || message == 0x007E || message == 0x001A)
                PlacementChanged?.Invoke(this, EventArgs.Empty);
            return Native.DefSubclassProc(handle, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (IsRegistered) Native.Shell_NotifyIcon(2, ref data);
            IsRegistered = false;
            Native.RemoveWindowSubclass(window, callback, new UIntPtr(1));
            if (ownsIcon) Native.DestroyIcon(data.Icon);
            GC.KeepAlive(callback);
        }
    }
}
