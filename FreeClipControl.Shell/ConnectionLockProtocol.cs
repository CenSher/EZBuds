using System;

namespace FreeClipControl.Shell;

internal static class ConnectionLockProtocol
{
    internal static bool? ReadState(HuaweiFrame frame)
    {
        byte[] value = frame.Get(0x0D);
        return value.Length == 1 ? value[0] switch { 0 => false, 1 => true, _ => null } : null;
    }

    internal static (byte Id, byte[] Value) Parameter(bool locked, byte[] protocolAddress)
    {
        if (protocolAddress == null || protocolAddress.Length != 6)
            throw new InvalidOperationException("耳机未返回有效设备地址，请刷新后重试。");
        return ((byte)(locked ? 8 : 9), (byte[])protocolAddress.Clone());
    }
}
