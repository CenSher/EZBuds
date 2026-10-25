using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace FreeClipControl.Shell;

internal static class HeadsetAudioOutput
{
    internal static async Task RequestAsync(ulong address, CancellationToken token)
    {
        var output = await FindEndpointAsync(address, token);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(CreateSilence());
            await writer.StoreAsync().AsTask(token);
        }
        stream.Seek(0);
        using var source = MediaSource.CreateFromStream(stream, "audio/wav");
        using var player = new MediaPlayer { AudioDevice = output, AudioCategory = MediaPlayerAudioCategory.Media };
        player.CommandManager.IsEnabled = false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaEnded += (_, _) => completion.TrySetResult(true);
        player.MediaFailed += (_, args) => completion.TrySetException(new InvalidOperationException("无法请求耳机音频输出：" + args.ErrorMessage));
        player.Source = source;
        token.ThrowIfCancellationRequested();
        RuntimeLog.Write("Requesting headphone audio output after confirmed remote playback.");
        player.Play();
        // A bounded, silent stream asks Windows for this endpoint. Firmware decides
        // whether to transfer audio; stream creation is never reported as proof.
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(6), token); }
        finally { player.Pause(); player.Source = null; }
    }

    // Also used by the read-only endpoint check; resolving metadata opens no stream.
    internal static async Task<DeviceInformation> FindEndpointAsync(ulong address, CancellationToken token)
    {
        const string containerProperty = "System.Devices.ContainerId";
        const string aepContainerProperty = "System.Devices.Aep.ContainerId";
        using var headset = await BluetoothDevice.FromBluetoothAddressAsync(address).AsTask(token);
        if (headset == null) throw new InvalidOperationException("Windows 暂时无法识别这副耳机。");
        var device = await DeviceInformation.CreateFromIdAsync(headset.DeviceInformation.Id,
            new[] { containerProperty, aepContainerProperty }, headset.DeviceInformation.Kind).AsTask(token);
        if ((!device.Properties.TryGetValue(containerProperty, out var value) || value == null)
            && !device.Properties.TryGetValue(aepContainerProperty, out value))
            throw new InvalidOperationException("暂时无法确认耳机的音频输出，请检查 Windows 蓝牙连接。");
        if (!Guid.TryParse(value?.ToString(), out var container) || container == Guid.Empty)
            throw new InvalidOperationException("暂时无法确认耳机的音频输出，请检查 Windows 蓝牙连接。");
        var outputs = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(),
            new[] { containerProperty }).AsTask(token);
        var matching = outputs.Where(d => d.IsEnabled && d.Properties.TryGetValue(containerProperty, out var id)
            && Guid.TryParse(id?.ToString(), out var actual) && actual == container).ToArray();
        // Do not guess from a friendly name, fall back to speakers, or select an
        // arbitrary profile when Windows exposes multiple matching endpoints.
        if (matching.Length != 1)
            throw new InvalidOperationException("尚未找到唯一可用的耳机音频输出，请检查 Windows 蓝牙连接。");

        return matching[0];
    }

    private static byte[] CreateSilence()
    {
        const int sampleRate = 48000, channels = 2, bytesPerSample = 2, seconds = 2;
        int length = sampleRate * channels * bytesPerSample * seconds;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)channels); writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bytesPerSample);
        writer.Write((short)(channels * bytesPerSample)); writer.Write((short)(8 * bytesPerSample));
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(length); writer.Write(new byte[length]);
        return buffer.ToArray();
    }
}
