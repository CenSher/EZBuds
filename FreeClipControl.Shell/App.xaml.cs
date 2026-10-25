using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FreeClipControl.Shell;

public partial class App : Application
{
    private Mutex singleton;
    private ShellWindow window;
    private DispatcherQueueTimer heartbeat;
    public App()
    {
        var args = Environment.GetCommandLineArgs();
        int diagnostic = Array.IndexOf(args, "--diagnose");
        RuntimeLog.Path = diagnostic >= 0 && diagnostic + 1 < args.Length ? args[diagnostic + 1] : Path.Combine(AppContext.BaseDirectory, "startup-error.log");
        RuntimeLog.WriteEnvironment();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RuntimeLog.Write("PROCESS EXIT; code=" + Environment.ExitCode);
        UnhandledException += (_, error) => RuntimeLog.Write("UNHANDLED: " + error.Exception);
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = Environment.GetCommandLineArgs();
        // Ordinary launches, including the former --show switch, stay in the tray.
        // Visible startup is reserved for the explicit window debugging modes.
        var launch = ShellLaunchOptions.Parse(options);
        singleton = new Mutex(true, @"Local\FreeClipControl.Shell.Instance", out bool first);
        if (!first)
        {
            // Reopening the executable must not reveal the resident tray panel.
            // Only the explicit window/preview modes may request a visible window.
            if (launch.ShowPanelOnLaunch)
            {
                var existing = Native.FindWindow(null, ShellWindow.WindowTitle);
                if (existing != IntPtr.Zero)
                {
                    Native.AllowSetForegroundWindow(uint.MaxValue);
                    Native.PostMessage(existing, Native.ShowPanelMessage, UIntPtr.Zero, IntPtr.Zero);
                }
            }
            else RuntimeLog.Write("Already running; keeping panel in its current tray state.");
            singleton.Dispose(); singleton = null; Exit(); return;
        }
        try
        {
            window = new ShellWindow(launch.UseTray);
            window.Closed += (_, _) => { heartbeat?.Stop(); singleton?.ReleaseMutex(); singleton?.Dispose(); singleton = null; Exit(); };
            heartbeat = window.DispatcherQueue.CreateTimer();
            heartbeat.Interval = TimeSpan.FromMinutes(1);
            heartbeat.Tick += (_, _) => RuntimeLog.Write("ALIVE; " + window.TrayStatus);
            heartbeat.Start();
            if (launch.ShowPanelOnLaunch)
            {
                RuntimeLog.Write("Started with panel visible; preview=" + options.Contains("--preview"));
                window.ShowPanel();
            }
            else RuntimeLog.Write("Started in tray only; waiting for user activation.");
            if (launch.OpenSettings) window.ShowSettings();
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            RuntimeLog.Write("STARTUP FAILED: " + error);
            Native.MessageBox(IntPtr.Zero,
                AppIdentity.Name + " 启动失败：" + error.Message + "\n\n详细日志：\n" + RuntimeLog.Path,
                AppIdentity.Name + " 启动失败", 0x10);
            Exit();
        }
    }
}
