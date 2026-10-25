using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace FreeClipControl.Shell;

internal sealed record HuaweiFrame(ushort Command, Dictionary<byte, byte[]> Parameters)
{
    public byte[] Get(byte id) => Parameters.TryGetValue(id, out var value) ? value : Array.Empty<byte>();
    public int? Number(byte id)
    {
        var bytes = Get(id);
        if (bytes.Length == 0 || bytes.Length > 4) return null;
        uint value = 0;
        foreach (byte item in bytes) value = (value << 8) | item;
        return value <= int.MaxValue ? (int)value : null;
    }
}

// Independent implementation from the wire format documented in PROTOCOL.md.
// It deliberately validates every frame, unlike the old exploratory probe.
internal sealed class HuaweiFrameDecoder
{
    private readonly List<byte> pending = new();
    public static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0;
        foreach (byte item in bytes)
        {
            crc ^= (ushort)(item << 8);
            for (int bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static byte[] Build(ushort command, params (byte Id, byte[] Value)[] parameters)
    {
        if (parameters.Any(p => p.Value.Length > 255)) throw new ArgumentException("TLV value is too large.");
        int total = 8 + parameters.Sum(p => 2 + p.Value.Length);
        if (total > 4096) throw new ArgumentException("Protocol frame is too large.");
        var packet = new byte[total];
        packet[0] = 0x5A;
        packet[1] = (byte)((total - 5) >> 8);
        packet[2] = (byte)(total - 5);
        packet[4] = (byte)(command >> 8);
        packet[5] = (byte)command;
        int offset = 6;
        foreach (var parameter in parameters)
        {
            packet[offset++] = parameter.Id;
            packet[offset++] = (byte)parameter.Value.Length;
            parameter.Value.CopyTo(packet, offset);
            offset += parameter.Value.Length;
        }
        ushort crc = Crc(packet.AsSpan(0, total - 2));
        packet[^2] = (byte)(crc >> 8);
        packet[^1] = (byte)crc;
        return packet;
    }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) pending.Add(value);
        if (pending.Count > 16384) pending.RemoveRange(0, pending.Count - 8192);
    }

    public bool TryRead(out HuaweiFrame frame)
    {
        frame = null;
        while (pending.Count >= 4)
        {
            if (pending[0] != 0x5A) { pending.RemoveAt(0); continue; }
            int total = ((pending[1] << 8) | pending[2]) + 5;
            if (total < 8 || total > 4096 || pending[3] != 0) { pending.RemoveAt(0); continue; }
            if (pending.Count < total) return false; // fragmented stream
            var packet = pending.GetRange(0, total).ToArray();
            if (Crc(packet.AsSpan(0, total - 2)) != ((packet[^2] << 8) | packet[^1]))
            {
                pending.RemoveAt(0);
                RuntimeLog.Write("Discarded Bluetooth frame with invalid CRC.");
                continue;
            }
            pending.RemoveRange(0, total);
            var parameters = new Dictionary<byte, byte[]>();
            int offset = 6, end = total - 2;
            bool valid = true;
            while (offset < end)
            {
                if (offset + 2 > end) { valid = false; break; }
                byte id = packet[offset++];
                int length = packet[offset++];
                if (offset + length > end) { valid = false; break; }
                parameters[id] = packet.AsSpan(offset, length).ToArray();
                offset += length;
            }
            if (!valid) { RuntimeLog.Write("Discarded Bluetooth frame with invalid TLV bounds."); continue; }
            frame = new HuaweiFrame((ushort)((packet[4] << 8) | packet[5]), parameters);
            return true;
        }
        return false;
    }
}

internal sealed class HuaweiSession
{
    private readonly RfcommConnection connection;
    private readonly HuaweiFrameDecoder decoder = new();
    private readonly byte[] receiveBuffer = new byte[4096];
    private readonly CancellationToken token;
    internal bool TraceNoiseControl { get; set; }
    public HuaweiSession(RfcommConnection transport, CancellationToken cancellation) { connection = transport; token = cancellation; }
    public void Send(ushort command, params (byte Id, byte[] Value)[] parameters)
    {
        var packet = HuaweiFrameDecoder.Build(command, parameters);
        if (TraceNoiseControl && command is 0x2B04 or 0x2B2A)
            RuntimeLog.Write("NOISE TX " + Convert.ToHexString(packet));
        connection.Send(packet, token);
    }
    private void TraceNoiseFrame(HuaweiFrame frame)
    {
        if (!TraceNoiseControl) return;
        if (frame.Command is 0x2B04 or 0x2B2A or 0x2B03)
            RuntimeLog.Write("NOISE RX command=" + frame.Command.ToString("X4") + "; fields="
                + string.Join(",", frame.Parameters.Select(p => p.Key.ToString("X2") + "=" + Convert.ToHexString(p.Value))));
        else RuntimeLog.Write("NOISE RX other-command=" + frame.Command.ToString("X4"));
    }
    public void Query(ushort command, params byte[] ids) => Send(command, ids.Select(id => (id, Array.Empty<byte>())).ToArray());

    public HuaweiFrame ReadUntil(Stopwatch elapsed, int limitMilliseconds)
    {
        while (elapsed.ElapsedMilliseconds < limitMilliseconds)
        {
            token.ThrowIfCancellationRequested();
            if (decoder.TryRead(out var frame)) { TraceNoiseFrame(frame); return frame; }
            int remaining = Math.Max(1, limitMilliseconds - (int)elapsed.ElapsedMilliseconds);
            int count = connection.Receive(receiveBuffer, Math.Min(200, remaining), token);
            if (count > 0) decoder.Append(receiveBuffer.AsSpan(0, count));
        }
        if (!decoder.TryRead(out var final)) return null;
        TraceNoiseFrame(final);
        return final;
    }
}
