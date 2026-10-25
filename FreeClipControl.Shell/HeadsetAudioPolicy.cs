using System.Linq;

namespace FreeClipControl.Shell;

internal static class HeadsetAudioPolicy
{
    // Only state 9 proves playback. State 3 is connected, with no known audio meaning.
    internal static bool ShouldRequestOutput(HeadsetSnapshot snapshot) =>
        snapshot.DeviceListComplete && snapshot.Devices.All(d => d.ConnectionKnown)
        && snapshot.Devices.Count(d => d.IsLocalComputer) == 1
        && snapshot.Devices.Any(d => d.IsLocalComputer && d.Connected && d.ConnectionState != 9)
        && snapshot.Devices.Any(d => !d.IsLocalComputer && d.ConnectionState == 9);
}
