using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace FreeClipControl.Shell;

// Enumerates render-device metadata only; never opens an audio stream or changes settings.
// Matching by reported name is diagnostic evidence, not a guaranteed device identity.
internal static class AudioEndpointDiagnostics
{
    internal static int? CountMatchingRender(string headsetName)
    {
        try { return CaptureAsync(headsetName).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch (Exception error) { RuntimeLog.Write("Audio render enumeration unavailable: " + error.Message); return null; }
    }

    private static async Task<int> CaptureAsync(string headsetName)
    {
        string Normalize(string name) => new((name ?? "").Where(char.IsLetterOrDigit).ToArray());
        string expected = Normalize(headsetName);
        if (expected.Length == 0) return 0;
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        return devices.Count(d => d.IsEnabled && Normalize(d.Name).Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task LogAfterDelayAsync(string headsetName, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), token);
            if (token.IsCancellationRequested) return;
            int? count = await Task.Run(() => CountMatchingRender(headsetName), token);
            RuntimeLog.Write("Lock audio diagnostic after 6s; matchingRenderEndpoints=" + (count?.ToString() ?? "unknown") + "; no Bluetooth session opened.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { RuntimeLog.Write("Delayed audio diagnostic unavailable: " + error.Message); }
    }
}
