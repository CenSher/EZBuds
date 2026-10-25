using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FreeClipControl.Shell
{
    internal static class RuntimeLog
    {
        private static readonly object sync = new object();
        internal static string Path { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "startup-error.log");

        internal static void Write(string message)
        {
            try
            {
                lock (sync)
                    File.AppendAllText(Path, DateTime.Now.ToString("O") + " [PID " + Environment.ProcessId + "] " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        internal static void WriteEnvironment()
        {
            bool inJob;
            IsProcessInJob(new IntPtr(-1), IntPtr.Zero, out inJob);
            var info = new JobLimits();
            uint length;
            bool hasLimits = QueryInformationJobObject(IntPtr.Zero, 9, ref info, (uint)Marshal.SizeOf<JobLimits>(), out length);
            IntPtr token;
            string tokenDetails = "unknown";
            if (OpenProcessToken(new IntPtr(-1), 0x0008, out token))
            {
                try
                {
                    int appContainer;
                    bool read = GetTokenInformation(token, 29, out appContainer, sizeof(int), out length);
                    tokenDetails = "restricted=" + IsTokenRestricted(token) + "; appContainer=" + (read ? appContainer.ToString() : "unknown");
                }
                finally { CloseHandle(token); }
            }
            Write("START; session=" + Process.GetCurrentProcess().SessionId + "; inJob=" + inJob
                + "; jobLimits=" + (hasLimits ? "0x" + info.Basic.Flags.ToString("X") : "unknown") + "; " + tokenDetails);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { public ulong ReadCount, WriteCount, OtherCount, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct JobLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll")]
        private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
        [DllImport("kernel32.dll")]
        private static extern bool QueryInformationJobObject(IntPtr job, int type, ref JobLimits info, uint length, out uint returned);
        [DllImport("advapi32.dll")]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll")]
        private static extern bool GetTokenInformation(IntPtr token, int type, out int info, int length, out uint returned);
        [DllImport("advapi32.dll")]
        private static extern bool IsTokenRestricted(IntPtr token);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
