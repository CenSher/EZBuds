using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace FreeClipControl.Shell;

internal sealed record PairedBluetoothDevice(ulong Address, string Name, bool Connected, uint DeviceClass, bool IsPaired);

internal static class BluetoothDiscovery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceRecord
    {
        public uint Size;
        public ulong Address;
        public uint DeviceClass;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen, LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SearchParameters
    {
        public uint Size;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated, Remembered, Unknown, Connected, Inquiry;
        public byte TimeoutMultiplier;
        public IntPtr Radio;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RadioSearch { public uint Size; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RadioInfo
    {
        public uint Size;
        public ulong Address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
        public uint DeviceClass;
        public ushort Subversion, Manufacturer;
    }
    [DllImport("bthprops.cpl", SetLastError = true)] private static extern IntPtr BluetoothFindFirstDevice(ref SearchParameters search, ref DeviceRecord info);
    [DllImport("bthprops.cpl", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindNextDevice(IntPtr find, ref DeviceRecord info);
    [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindDeviceClose(IntPtr find);
    [DllImport("bthprops.cpl", SetLastError = true)] private static extern IntPtr BluetoothFindFirstRadio(ref RadioSearch search, out IntPtr radio);
    [DllImport("bthprops.cpl", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindNextRadio(IntPtr find, out IntPtr radio);
    [DllImport("bthprops.cpl")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BluetoothFindRadioClose(IntPtr find);
    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)] private static extern uint BluetoothGetRadioInfo(IntPtr radio, ref RadioInfo info);
    [DllImport("bthprops.cpl")] private static extern uint BluetoothGetDeviceInfo(IntPtr radio, ref DeviceRecord info);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

    public static List<PairedBluetoothDevice> GetKnownDevices()
    {
        var search = new SearchParameters
        {
            Size = (uint)Marshal.SizeOf<SearchParameters>(), Authenticated = true,
            Remembered = true, Unknown = true, Connected = true, Inquiry = false
        };
        var info = new DeviceRecord { Size = (uint)Marshal.SizeOf<DeviceRecord>() };
        var result = new List<PairedBluetoothDevice>();
        IntPtr find = BluetoothFindFirstDevice(ref search, ref info);
        if (find == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 259 || error == 1168 || error == 0) return result;
            throw new Win32Exception(error, "无法枚举已配对蓝牙设备，请检查系统蓝牙是否开启。");
        }
        try
        {
            do
            {
                result.Add(new(info.Address, info.Name ?? "", info.Connected, info.DeviceClass, info.Authenticated));
                info.Size = (uint)Marshal.SizeOf<DeviceRecord>();
            } while (BluetoothFindNextDevice(find, ref info));
            int error = Marshal.GetLastWin32Error();
            if (error != 259 && error != 0) throw new Win32Exception(error);
        }
        finally { BluetoothFindDeviceClose(find); }
        return result;
    }

    public static bool IsConnected(ulong address)
    {
        // Query the local stack only. No inquiry, authentication, or connection.
        var search = new RadioSearch { Size = (uint)Marshal.SizeOf<RadioSearch>() };
        IntPtr find = BluetoothFindFirstRadio(ref search, out var radio);
        if (find == IntPtr.Zero) return false;
        try
        {
            do
            {
                try
                {
                    var info = new DeviceRecord { Size = (uint)Marshal.SizeOf<DeviceRecord>(), Address = address };
                    if (BluetoothGetDeviceInfo(radio, ref info) == 0 && info.Connected) return true;
                }
                finally { CloseHandle(radio); }
            } while (BluetoothFindNextRadio(find, out radio));
        }
        finally { BluetoothFindRadioClose(find); }
        return false;
    }

    public static HashSet<ulong> GetLocalAddresses()
    {
        var addresses = new HashSet<ulong>();
        var search = new RadioSearch { Size = (uint)Marshal.SizeOf<RadioSearch>() };
        IntPtr find = BluetoothFindFirstRadio(ref search, out var radio);
        if (find == IntPtr.Zero) return addresses;
        try
        {
            do
            {
                try
                {
                    var info = new RadioInfo { Size = (uint)Marshal.SizeOf<RadioInfo>() };
                    if (BluetoothGetRadioInfo(radio, ref info) == 0) addresses.Add(info.Address);
                }
                finally { CloseHandle(radio); }
            } while (BluetoothFindNextRadio(find, out radio));
        }
        finally { BluetoothFindRadioClose(find); }
        return addresses;
    }

    public static string FormatAddress(ulong address)
    {
        var parts = new string[6];
        for (int i = 0; i < parts.Length; i++) parts[i] = ((address >> (8 * (5 - i))) & 255).ToString("X2");
        return string.Join(":", parts);
    }
}

// Native RFCOMM avoids UWP package identity and does not require Python or an
// additional Bluetooth library. Nothing in this type pairs or unpairs devices.
internal sealed class RfcommConnection : IDisposable
{
    private static readonly IntPtr InvalidSocket = new(-1);
    private static readonly Lazy<int> Winsock = new(() => WSAStartup(0x0202, new byte[512]));
    private IntPtr socket = InvalidSocket;
    // ws2bth.h uses pshpack1.h: SOCKADDR_BTH is 30 bytes, NOT a padded 40-byte struct.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BluetoothEndpoint { public ushort Family; public ulong Address; public Guid Service; public uint Channel; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DescriptorSet
    {
        public uint Count;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public IntPtr[] Handles;
        public static DescriptorSet Single(IntPtr value)
        {
            var result = new DescriptorSet { Count = 1, Handles = new IntPtr[64] };
            result.Handles[0] = value;
            return result;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct TimeValue { public int Seconds, Microseconds; }
    [DllImport("ws2_32.dll")] private static extern int WSAStartup(ushort version, [Out] byte[] data);
    [DllImport("ws2_32.dll", EntryPoint = "socket")] private static extern IntPtr CreateSocket(int family, int type, int protocol);
    [DllImport("ws2_32.dll")] private static extern int WSAGetLastError();
    [DllImport("ws2_32.dll")] private static extern int ioctlsocket(IntPtr socket, uint command, ref uint value);
    [DllImport("ws2_32.dll")] private static extern int connect(IntPtr socket, ref BluetoothEndpoint address, int length);
    [DllImport("ws2_32.dll")] private static extern int closesocket(IntPtr socket);
    [DllImport("ws2_32.dll")] private static extern int send(IntPtr socket, byte[] data, int length, int flags);
    [DllImport("ws2_32.dll")] private static extern int recv(IntPtr socket, [Out] byte[] data, int length, int flags);
    [DllImport("ws2_32.dll")] private static extern int getsockopt(IntPtr socket, int level, int option, out int error, ref int length);
    [DllImport("ws2_32.dll", EntryPoint = "select")] private static extern int SelectRead(int ignored, ref DescriptorSet read, IntPtr write, IntPtr except, ref TimeValue timeout);
    [DllImport("ws2_32.dll", EntryPoint = "select")] private static extern int SelectWrite(int ignored, IntPtr read, ref DescriptorSet write, ref DescriptorSet except, ref TimeValue timeout);

    public RfcommConnection(ulong address, CancellationToken token, int channel = 1)
    {
        if (channel < 1 || channel > 30) throw new ArgumentOutOfRangeException(nameof(channel));
        int startup = Winsock.Value;
        if (startup != 0) throw new SocketException(startup);
        token.ThrowIfCancellationRequested();
        socket = CreateSocket(32, 1, 3); // AF_BTH / SOCK_STREAM / BTHPROTO_RFCOMM
        if (socket == InvalidSocket) throw new SocketException(WSAGetLastError());
        try
        {
            uint nonBlocking = 1;
            if (ioctlsocket(socket, 0x8004667E, ref nonBlocking) != 0) throw new SocketException(WSAGetLastError());
            var endpoint = new BluetoothEndpoint { Family = 32, Address = address, Channel = (uint)channel };
            if (connect(socket, ref endpoint, Marshal.SizeOf<BluetoothEndpoint>()) != 0)
            {
                int error = WSAGetLastError();
                if (error != 10035 && error != 10036 && error != 10037) throw new SocketException(error);
                var deadline = Stopwatch.StartNew();
                while (!Poll(true, 100, token))
                    if (deadline.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("耳机连接超时，请取出耳机并检查系统蓝牙。");
                int length = sizeof(int);
                if (getsockopt(socket, 0xFFFF, 0x1007, out error, ref length) != 0) throw new SocketException(WSAGetLastError());
                if (error != 0) throw new SocketException(error);
            }
        }
        catch { Dispose(); throw; }
    }

    private bool Poll(bool write, int milliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (socket == InvalidSocket) throw new ObjectDisposedException(nameof(RfcommConnection));
        var set = DescriptorSet.Single(socket);
        var except = DescriptorSet.Single(socket);
        var timeout = new TimeValue { Microseconds = milliseconds * 1000 };
        int result = write ? SelectWrite(0, IntPtr.Zero, ref set, ref except, ref timeout)
            : SelectRead(0, ref set, IntPtr.Zero, IntPtr.Zero, ref timeout);
        token.ThrowIfCancellationRequested();
        if (result < 0) throw new SocketException(WSAGetLastError());
        return result > 0;
    }

    public void Send(byte[] packet, CancellationToken token)
    {
        int offset = 0;
        var elapsed = Stopwatch.StartNew();
        while (offset < packet.Length)
        {
            token.ThrowIfCancellationRequested();
            var remaining = packet.AsSpan(offset).ToArray();
            int sent = send(socket, remaining, remaining.Length, 0);
            if (sent > 0) { offset += sent; continue; }
            int error = WSAGetLastError();
            if (sent < 0 && error == 10035)
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("蓝牙命令发送超时。");
                Poll(true, 100, token);
                continue;
            }
            throw new SocketException(sent == 0 ? 10054 : error);
        }
    }

    // A receive timeout is not EOF. EOF is reported separately so a dropped
    // control connection cannot accidentally be presented as an empty table.
    public int Receive(byte[] buffer, int milliseconds, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds)
        {
            if (!Poll(false, (int)Math.Clamp(milliseconds - elapsed.ElapsedMilliseconds, 1, 100), token)) continue;
            int count = recv(socket, buffer, buffer.Length, 0);
            if (count > 0) return count;
            if (count == 0) throw new EndOfStreamException("耳机已关闭蓝牙控制链路。");
            int error = WSAGetLastError();
            if (error != 10035) throw new SocketException(error);
        }
        token.ThrowIfCancellationRequested();
        return 0;
    }

    public void Dispose()
    {
        var value = Interlocked.Exchange(ref socket, InvalidSocket);
        if (value != InvalidSocket) closesocket(value);
    }
}
