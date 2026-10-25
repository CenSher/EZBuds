using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace FreeClipControl.Shell;

internal static class WindowsBluetoothAvailability
{
    // Read operational state only; never request control access or change a radio.
    internal static async Task<BluetoothAvailability> ReadRadioAsync(CancellationToken token)
    {
        try { return await ReadAsync().WaitAsync(TimeSpan.FromSeconds(2), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            RuntimeLog.Write("Bluetooth radio state unavailable: " + error.Message);
            return BluetoothAvailability.Unknown;
        }
    }

    private static async Task<BluetoothAvailability> ReadAsync()
    {
        var radios = (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.Bluetooth).ToArray();
        if (radios.Any(r => r.State == RadioState.On)) return BluetoothAvailability.Unknown;
        if (radios.Length > 0 && radios.All(r => r.State == RadioState.Off || r.State == RadioState.Disabled))
            return BluetoothAvailability.RadioOff;
        // Empty WinRT enumeration is not by itself proof that Bluetooth is off.
        return BluetoothDiscovery.GetLocalAddresses().Count == 0
            ? BluetoothAvailability.Unavailable : BluetoothAvailability.Unknown;
    }
}
