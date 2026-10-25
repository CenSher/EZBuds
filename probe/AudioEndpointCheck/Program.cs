using System.Globalization;
using FreeClipControl.Shell;

if (args.Length != 1 || !ulong.TryParse(args[0].Replace(":", "").Replace("-", ""), NumberStyles.HexNumber,
    CultureInfo.InvariantCulture, out ulong address))
{
    Console.Error.WriteLine("Usage: AudioEndpointCheck AA:BB:CC:DD:EE:FF (read-only; no audio playback)");
    return 2;
}
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var endpoint = await HeadsetAudioOutput.FindEndpointAsync(address, cancellation.Token);
Console.WriteLine("Matched enabled audio output: " + endpoint.Name);
Console.WriteLine("No audio stream created; no playback, default-device change, or Bluetooth command.");
return 0;

namespace FreeClipControl.Shell
{
    internal static class RuntimeLog { internal static void Write(string message) => Console.WriteLine(message); }
}
