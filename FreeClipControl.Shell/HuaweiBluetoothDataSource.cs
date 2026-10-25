using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeClipControl.Shell;

internal sealed class HuaweiBluetoothDataSource : IDeviceDataSource, IHeadsetSelection
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private RfcommConnection active;
    private bool disposed;
    private string preferredAddress;
    private readonly int lockSettleMilliseconds;
    private ulong? selectedAddress;
    private HeadsetProfile profile = HeadsetProfiles.Find("freeclip2");
    private readonly HeadsetSelectionStore selection;
    private bool initialized;
    private bool startupChoiceRequired;
    public ulong? SelectedAddress => selectedAddress;
    public string SelectedProfileId => selectedAddress.HasValue ? profile.Id : null;
    public string SelectedName => selectedAddress.HasValue ? selection.NameFor(selectedAddress.Value) : null;
    public bool RequiresSelection => selection.RequiresSelection || startupChoiceRequired;
    public Task InitializeSelectionAsync(CancellationToken token) => AutoConfigureAsync(token);

    public HuaweiBluetoothDataSource() : this(1200, HeadsetSelectionStore.Default()) { }
    internal HuaweiBluetoothDataSource(int settleMilliseconds) : this(settleMilliseconds, new HeadsetSelectionStore(null)) { }
    internal HuaweiBluetoothDataSource(int settleMilliseconds, HeadsetSelectionStore store)
    {
        selection = store;
        selectedAddress = store.SelectedAddress;
        if (selectedAddress.HasValue)
        {
            var saved = HeadsetProfiles.Find(store.ProfileFor(selectedAddress.Value));
            if (saved == null) selectedAddress = null; else profile = saved;
        }
        lockSettleMilliseconds = Math.Max(0, settleMilliseconds);
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--bluetooth-address");
        preferredAddress = index >= 0 ? index + 1 < args.Length ? args[index + 1] : "" : null;
        if (preferredAddress != null) selectedAddress = null;
    }

    public Task<HeadsetChoice[]> ListHeadsetsAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        return BluetoothDiscovery.GetKnownDevices().Where(d => d.IsPaired).GroupBy(d => d.Address).Select(g => g.First())
            .Select(d => new HeadsetChoice(d.Address, d.Name, BluetoothDiscovery.IsConnected(d.Address),
                HeadsetProfiles.ResolveConfigured(selection.ProfileFor(d.Address), d.Name)?.Id))
            .OrderByDescending(d => d.ProfileId != null).ThenByDescending(d => d.Connected).ThenBy(d => d.Name).ToArray();
    }, token);

    public Task<BoundHeadset[]> ListBindingsAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var known = BluetoothDiscovery.GetKnownDevices().Where(d => d.IsPaired).GroupBy(d => d.Address).ToDictionary(g => g.Key, g => g.First());
        return selection.Bindings.Select(binding =>
        {
            ulong address = HeadsetProfiles.ParseAddress(binding.Key);
            known.TryGetValue(address, out var device);
            return new BoundHeadset(address, device?.Name ?? selection.NameFor(address) ?? "已保存的耳机", binding.Value,
                device != null && BluetoothDiscovery.IsConnected(address), device != null, selectedAddress == address);
        }).OrderByDescending(d => d.Selected).ThenByDescending(d => d.Connected).ThenBy(d => d.Name).ToArray();
    }, token);

    private async Task AutoConfigureAsync(CancellationToken token)
    {
        if (initialized) return;
        await gate.WaitAsync(token);
        try
        {
            if (initialized) return;
            var paired = await Task.Run(() => BluetoothDiscovery.GetKnownDevices().Where(d => d.IsPaired)
                .Select(d => d with { Connected = BluetoothDiscovery.IsConnected(d.Address) }).ToArray(), token);
            token.ThrowIfCancellationRequested();
            var matches = paired.Select(d => (Device: d, Model: HeadsetProfiles.ResolveConfigured(selection.ProfileFor(d.Address), d.Name)))
                .Where(d => d.Model != null && !selection.IsRemoved(d.Device.Address)).GroupBy(d => d.Device.Address).Select(g => g.First())
                .OrderByDescending(d => d.Device.Connected).ThenBy(d => d.Device.Address).ToArray();
            foreach (var match in matches)
            {
                token.ThrowIfCancellationRequested();
                selection.Save(match.Device.Address, match.Model.Id, match.Device.Name, select: false);
            }
            // The selected address may stay unchanged while its legacy model is corrected.
            if (selectedAddress.HasValue) profile = HeadsetProfiles.Find(selection.ProfileFor(selectedAddress.Value)) ?? profile;
            // Startup follows the local Windows connection, even when the last
            // selected headset remains paired but is currently offline.
            if (!selection.RequiresSelection && preferredAddress == null)
            {
                var connected = matches.Where(m => m.Device.Connected).ToArray();
                var match = connected.Length == 1 ? connected[0]
                    : connected.Length > 1 ? connected.FirstOrDefault(m => m.Device.Address == selectedAddress)
                    : !selectedAddress.HasValue && matches.Length == 1 ? matches[0] : default;
                if (match.Device != null)
                {
                    token.ThrowIfCancellationRequested();
                    selection.Save(match.Device.Address, match.Model.Id, match.Device.Name);
                    selectedAddress = match.Device.Address;
                    profile = match.Model;
                    RuntimeLog.Write("Startup headset selected; profile=" + profile.Id + "; connected=" + match.Device.Connected);
                }
                else if (connected.Length > 1 || !selectedAddress.HasValue && matches.Length > 1)
                {
                    // Do not pick an arbitrary headset just because it sorts first.
                    selectedAddress = null;
                    startupChoiceRequired = true;
                    RuntimeLog.Write("Startup headset selection is ambiguous; awaiting explicit choice.");
                }
            }
            initialized = true;
        }
        finally { gate.Release(); }
    }

    public async Task<(bool Changed, BluetoothAvailability Availability)> SelectConnectedHeadsetForMenuAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (disposed || preferredAddress != null) return (false, BluetoothAvailability.Unknown);
            if (selection.RequiresSelection) return (false, BluetoothAvailability.MatchFailed);
            var radio = await WindowsBluetoothAvailability.ReadRadioAsync(token);
            token.ThrowIfCancellationRequested();
            if (radio is BluetoothAvailability.RadioOff or BluetoothAvailability.Unavailable) return (false, radio);
            var current = selectedAddress;
            // Query Windows only: no discovery inquiry, RFCOMM or connection command.
            if (current.HasValue && await Task.Run(() => BluetoothDiscovery.IsConnected(current.Value), token).WaitAsync(token))
                return (false, BluetoothAvailability.Connected);

            var paired = await Task.Run(() => BluetoothDiscovery.GetKnownDevices().Where(d => d.IsPaired)
                .GroupBy(d => d.Address).Select(g => g.First())
                .Select(d => d with { Connected = BluetoothDiscovery.IsConnected(d.Address) }).ToArray(), token).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            // Preserve a current headset that connected while enumeration was running.
            if (paired.Any(d => d.Address == current && d.Connected)) return (false, BluetoothAvailability.Connected);
            var savedOrder = selection.Bindings.Select((binding, index) =>
                (Address: HeadsetProfiles.ParseAddress(binding.Key), Index: index)).ToDictionary(p => p.Address, p => p.Index);
            var match = paired.Where(d => d.Connected && !selection.IsRemoved(d.Address))
                .Select(d => (Device: d, Model: HeadsetProfiles.ResolveConfigured(selection.ProfileFor(d.Address), d.Name)))
                .Where(m => m.Model != null)
                .OrderBy(m => savedOrder.GetValueOrDefault(m.Device.Address, int.MaxValue))
                .ThenBy(m => m.Device.Address).FirstOrDefault();
            var unavailable = current.HasValue ? BluetoothAvailability.NotConnected : BluetoothAvailability.MatchFailed;
            if (match.Device == null) return (false, unavailable);
            // Recheck before committing; a disappearing device must not become the new target.
            if (!await Task.Run(() => BluetoothDiscovery.IsConnected(match.Device.Address), token).WaitAsync(token)) return (false, unavailable);
            token.ThrowIfCancellationRequested();
            selection.Save(match.Device.Address, match.Model.Id, match.Device.Name);
            selectedAddress = match.Device.Address;
            profile = match.Model;
            startupChoiceRequired = false;
            initialized = true;
            RuntimeLog.Write("Menu preflight selected connected headset; profile=" + profile.Id);
            return (true, BluetoothAvailability.Connected);
        }
        finally { gate.Release(); }
    }

    public async Task SelectHeadsetAsync(ulong address, string profileId, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var next = HeadsetProfiles.Find(profileId) ?? throw new InvalidOperationException("请选择支持的耳机型号。");
            var paired = await Task.Run(() => BluetoothDiscovery.GetKnownDevices().FirstOrDefault(d => d.Address == address && d.IsPaired), token);
            if (paired == null) throw new InvalidOperationException("这副耳机尚未配对，请先在 Windows 蓝牙设置中配对。");
            token.ThrowIfCancellationRequested();
            selection.Save(address, profileId, paired.Name);
            preferredAddress = null;
            selectedAddress = address;
            profile = next;
            startupChoiceRequired = false;
            initialized = true;
            RuntimeLog.Write("Headset model selected; profile=" + profile.Id);
        }
        finally { gate.Release(); }
    }

    public async Task RemoveBindingAsync(ulong address, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            // Commit first: a disk failure must leave the live target intact.
            selection.Remove(address);
            if (selectedAddress == address)
            {
                selectedAddress = null;
                preferredAddress = null;
            }
            initialized = true;
        }
        finally { gate.Release(); }
    }

    public async Task<BluetoothAvailability> GetAvailabilityAsync(CancellationToken token)
    {
        var radio = await WindowsBluetoothAvailability.ReadRadioAsync(token);
        if (radio == BluetoothAvailability.RadioOff || radio == BluetoothAvailability.Unavailable) return radio;
        await AutoConfigureAsync(token);
        if (RequiresSelection && !selectedAddress.HasValue && preferredAddress == null)
            return BluetoothAvailability.MatchFailed;
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var paired = BluetoothDiscovery.GetKnownDevices().Where(d => d.IsPaired).ToList();
            if (preferredAddress == null && !selectedAddress.HasValue
                && !paired.Any(d => !selection.IsRemoved(d.Address)
                    && (selection.ProfileFor(d.Address) != null || HeadsetProfiles.Detect(d.Name) != null)))
                return BluetoothAvailability.MatchFailed;
            if (selectedAddress.HasValue && !paired.Any(d => d.Address == selectedAddress.Value)) return BluetoothAvailability.NotConnected;
            var target = SelectHeadset(paired);
            return BluetoothDiscovery.IsConnected(target.Address)
                ? BluetoothAvailability.Connected : BluetoothAvailability.NotConnected;
        }, token);
    }

    public async Task<HeadsetSnapshot> ReadAsync(CancellationToken token, bool allowReconnect = false)
    {
        await gate.WaitAsync(token);
        try { return await Task.Run(() => WithSession(token, (session, target, locals, classes) => ReadSnapshot(session, target, locals, classes), requireConnected: !allowReconnect), token); }
        finally { gate.Release(); }
    }

    public async Task<DeviceCommandResult> SetConnectedAsync(string id, bool connected, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() => WithSession(token, (session, target, locals, classes) =>
            {
                RequireDeviceManagement();
                var before = ReadSnapshot(session, target, locals, classes);
                if (!before.DeviceListComplete || before.Devices.Any(d => !d.ConnectionKnown))
                    throw new InvalidOperationException("耳机设备状态尚未完整读取，请刷新后再切换连接。");
                var device = before.Devices.FirstOrDefault(d => d.Id == id);
                if (device == null || !device.ConnectionKnown)
                    throw new InvalidOperationException("耳机未返回该设备的有效状态，请刷新设备列表。");
                if (device.Connected == connected) return new DeviceCommandResult(before, true, false, "耳机已处于目标连接状态。");
                if (connected && before.DualConnectEnabled.HasValue)
                {
                    int capacity = before.DualConnectEnabled.Value ? 2 : 1;
                    if (before.Devices.Count(d => d.ConnectionKnown && d.Connected) >= capacity)
                        throw new InvalidOperationException($"已有 {capacity} 台设备连接，请先断开一台。");
                }
                // Only this explicit user action writes a setting. No retry of
                // an uncertain write, no unpair/preferred/auto-connect writes.
                session.Send(0x2B33, ((byte)(connected ? 1 : 2), device.ProtocolAddress.ToArray()));
                RuntimeLog.Write("Bluetooth connection command sent; connect=" + connected + "; local=" + device.IsLocalComputer);
                if (!connected && device.IsLocalComputer)
                    return new DeviceCommandResult(null, false, true, "已发送断开本机命令，自动读取已暂停。再次刷新会重建控制连接。");
                HeadsetSnapshot latest = null;
                try
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        latest = ReadSnapshot(session, target, locals, classes);
                        var reported = latest.Devices.FirstOrDefault(d => d.Id == id);
                        if (reported?.ConnectionKnown == true && reported.Connected == connected)
                            return new DeviceCommandResult(latest, true, false, "耳机已确认" + (connected ? "连接。" : "断开。"));
                    }
                }
                catch (Exception error) when (error is IOException || error is System.Net.Sockets.SocketException)
                {
                    // Reopening RFCOMM can reconnect this PC. Let the user
                    // decide whether to refresh after a control-link loss.
                    return new DeviceCommandResult(null, false, true, "命令已发送，但控制连接已断开；结果待确认，自动读取已暂停。");
                }
                return new DeviceCommandResult(latest, false, false, "命令已发送，耳机尚未确认目标状态，请稍后刷新。");
            }), token);
        }
        finally { gate.Release(); }
    }

    public async Task<DeviceCommandResult> SetConnectionLockedAsync(string id, bool locked, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var result = await Task.Run(() => WithSession(token, (session, target, locals, classes) =>
            {
                if (!profile.ConnectionLock) throw new InvalidOperationException("当前型号未适配本次连接锁定。");
                var before = ReadSnapshot(session, target, locals, classes);
                var device = before.Devices.FirstOrDefault(d => d.Id == id);
                if (!before.DeviceListComplete || device?.CanChangeConnectionLock != true)
                    throw new InvalidOperationException("仅支持已连接且锁定状态明确的设备，请刷新后重试。");
                if (device.ConnectionLocked == locked)
                {
                    bool matches = LockStateMatches(before, id, locked);
                    return new DeviceCommandResult(before, matches, false, matches
                        ? locked ? "该设备本次连接已锁定。" : "该设备本次连接已解锁。"
                        : "设备锁定信息尚未一致，请刷新检查；未发送命令。");
                }
                // A deliberate transfer to another device is not a local-PC audio failure.
                int? audioBefore = device.IsLocalComputer ? AudioEndpointDiagnostics.CountMatchingRender(target.Name) : null;
                RuntimeLog.Write("Lock operation begin; locked=" + locked + "; connectionState=" + device.ConnectionState
                    + "; matchingRenderEndpoints=" + (audioBefore?.ToString() ?? "unknown"));
                token.ThrowIfCancellationRequested();
                // One observed command only. Never retry a write or alter preferred devices.
                session.Send(0x2B33, ConnectionLockProtocol.Parameter(locked, device.ProtocolAddress));
                RuntimeLog.Write("Bluetooth connection-lock command sent; locked=" + locked + "; targetSlot=" + device.Slot + "; local=" + device.IsLocalComputer);
                if (device.IsLocalComputer) _ = AudioEndpointDiagnostics.LogAfterDelayAsync(target.Name, token);
                HeadsetSnapshot latest = null;
                try
                {
                    byte operation = (byte)(locked ? 8 : 9);
                    byte? reply = WaitForLockReply(session, operation);
                    RuntimeLog.Write("Lock acknowledgement; operation=" + operation + "; status=" + (reply?.ToString() ?? "missing"));
                    if (!reply.HasValue)
                        return new DeviceCommandResult(null, false, true, "未收到耳机的操作确认，自动读取已暂停。请稍后手动刷新核对，勿重复点击。");
                    if (reply.Value != 0)
                        return new DeviceCommandResult(before, false, false, "耳机未接受锁定操作（返回码 " + reply.Value + "），未重试。");
                    // Mirror the successful probe: drain notifications and allow firmware
                    // to settle before any new query, using this same SPP session.
                    DrainDuringSettle(session, lockSettleMilliseconds);
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        if (attempt > 0) DrainDuringSettle(session, Math.Min(lockSettleMilliseconds, 250));
                        latest = ReadSnapshot(session, target, locals, classes);
                        var reported = latest.Devices.FirstOrDefault(d => d.Id == id);
                        RuntimeLog.Write("Lock readback; attempt=" + (attempt + 1) + "; connectionState=" + reported?.ConnectionState + "; locked=" + reported?.ConnectionLocked);
                        if (LockStateMatches(latest, id, locked))
                        {
                            int? audioAfter = device.IsLocalComputer ? AudioEndpointDiagnostics.CountMatchingRender(target.Name) : null;
                            RuntimeLog.Write("Lock audio diagnostic after readback; matchingRenderEndpoints=" + (audioAfter?.ToString() ?? "unknown"));
                            if (audioBefore > 0 && audioAfter == 0)
                                return new DeviceCommandResult(latest, false, true, "Windows 未再提供耳机音频输出，自动读取已暂停。锁定状态不能代表音频正常，请先检查系统音频连接。");
                            return new DeviceCommandResult(latest, true, false, locked ? "耳机已确认该设备锁定，其他连接未锁定。" : "耳机已确认该设备解除锁定。");
                        }
                    }
                }
                catch (Exception error) when (error is IOException || error is System.Net.Sockets.SocketException)
                {
                    return new DeviceCommandResult(null, false, true, "锁定命令已发送，但控制连接已断开；结果待确认，请手动刷新。");
                }
                return new DeviceCommandResult(latest, false, false, "命令已发送，耳机尚未确认锁定状态，请刷新检查。");
            }, requireConnected: true), token);
            return result ?? throw new InvalidOperationException("耳机已与这台电脑断开，请连接后再设置锁定。");
        }
        finally { gate.Release(); }
    }

    private static bool LockStateMatches(HeadsetSnapshot snapshot, string id, bool locked)
    {
        if (snapshot?.DeviceListComplete != true) return false;
        var target = snapshot.Devices.FirstOrDefault(device => device.Id == id);
        if (target?.ConnectionKnown != true || !target.Connected || target.ConnectionLocked != locked) return false;
        // Locking a new target transfers the lock. Unknown or conflicting peer
        // states must not be represented as a confirmed transfer.
        return !locked || snapshot.Devices.All(device => device.Id == id
            || device.ConnectionKnown && (!device.Connected || device.ConnectionLocked == false));
    }

    public async Task<DeviceCommandResult> ConnectToHeadsetAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var radio = await WindowsBluetoothAvailability.ReadRadioAsync(token);
            if (radio is BluetoothAvailability.RadioOff or BluetoothAvailability.Unavailable)
                throw new InvalidOperationException("请先打开这台电脑的蓝牙，并确认耳机已配对。");
            return await Task.Run(() => WithSession(token, (session, target, locals, classes) =>
            {
                // This explicit action may establish the local control connection.
                // Re-read the earphone table, never use a cached playback decision.
                var before = ReadSnapshot(session, target, locals, classes);
                if (!profile.DeviceManagement)
                    return new DeviceCommandResult(before, true, false, "已读取耳机电量。音频连接请在 Windows 蓝牙设置中管理。");
                var local = before.Devices.SingleOrDefault(d => d.IsLocalComputer);
                if (!before.DeviceListComplete || before.Devices.Any(d => !d.ConnectionKnown) || local == null)
                    return new DeviceCommandResult(before, false, false, "设备状态尚未完整读取，请稍后再试。");
                if (!local.Connected)
                {
                    if (before.DualConnectEnabled.HasValue)
                    {
                        int capacity = before.DualConnectEnabled.Value ? 2 : 1;
                        if (before.Devices.Count(d => d.Connected) >= capacity)
                            return new DeviceCommandResult(before, false, false, $"耳机已连接 {capacity} 台设备，请先断开一台再连接这台电脑。");
                    }
                    token.ThrowIfCancellationRequested();
                    session.Send(0x2B33, ((byte)1, local.ProtocolAddress.ToArray()));
                    DrainDuringSettle(session, lockSettleMilliseconds);
                    before = ReadSnapshot(session, target, locals, classes);
                    local = before.Devices.SingleOrDefault(d => d.IsLocalComputer);
                    if (local?.ConnectionKnown != true || !local.Connected)
                        return new DeviceCommandResult(before, false, false, "已请求连接这台电脑，耳机尚未确认连接。");
                }
                if (!before.DeviceListComplete || before.Devices.Any(d => !d.ConnectionKnown))
                    return new DeviceCommandResult(before, false, false, "设备状态尚未完整读取，请稍后再试。");
                if (!profile.ConnectionLock || !HeadsetAudioPolicy.ShouldRequestOutput(before))
                    return new DeviceCommandResult(before, true, false, local.ConnectionState == 9
                        ? "这台电脑正在使用耳机。" : "耳机已连接到这台电脑。");
                if (before.Devices.Any(d => !d.IsLocalComputer && d.ConnectionState == 9 && d.IsConnectionLocked))
                    return new DeviceCommandResult(before, false, false, "耳机已锁定到其他设备，请先在该设备上解除锁定。");

                // Request the standard Windows render channel once, only after proving
                // remote playback. Never disconnect another device or change defaults.
                token.ThrowIfCancellationRequested();
                HeadsetAudioOutput.RequestAsync(target.Address, token).GetAwaiter().GetResult();
                var after = ReadSnapshot(session, target, locals, classes);
                bool confirmed = after.Devices.Any(d => d.IsLocalComputer && d.ConnectionKnown && d.ConnectionState == 9);
                return new DeviceCommandResult(after, confirmed, false, confirmed
                    ? "耳机已切换到这台电脑。" : "已请求使用耳机音频，请在这台电脑上开始播放。");
            }), token);
        }
        finally { gate.Release(); }
    }

    private static byte? WaitForLockReply(HuaweiSession session, byte operation)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 1800)
        {
            var frame = session.ReadUntil(elapsed, 1800);
            if (frame == null) break;
            var action = frame.Get(0x1E);
            var status = frame.Get(0x1F);
            if (frame.Command == 0x2B33 && action.Length == 1 && action[0] == operation && status.Length == 1)
                return status[0];
        }
        return null;
    }

    private static void DrainDuringSettle(HuaweiSession session, int milliseconds)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < milliseconds) session.ReadUntil(elapsed, milliseconds);
    }

    private T WithSession<T>(CancellationToken token, Func<HuaweiSession, PairedBluetoothDevice, HashSet<ulong>, Dictionary<ulong, uint>, T> operation, bool requireConnected = false)
    {
        token.ThrowIfCancellationRequested();
        lock (sync) { if (disposed) throw new ObjectDisposedException(nameof(HuaweiBluetoothDataSource)); }
        var known = BluetoothDiscovery.GetKnownDevices();
        var paired = known.Where(d => d.IsPaired).ToList();
        var classes = known.GroupBy(d => d.Address).ToDictionary(group => group.Key, group => group.First().DeviceClass);
        var target = SelectHeadset(paired);
        selectedAddress = target.Address;
        var localAddresses = BluetoothDiscovery.GetLocalAddresses();
        if (requireConnected && !BluetoothDiscovery.IsConnected(target.Address))
            return default;
        // OpenFreebuds delays every FreeLace Pro control connection by 2s.
        if (profile.ConnectDelayMilliseconds > 0 && token.WaitHandle.WaitOne(profile.ConnectDelayMilliseconds)) token.ThrowIfCancellationRequested();
        using var transport = new RfcommConnection(target.Address, token, profile.Channel);
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(HuaweiBluetoothDataSource));
            active = transport;
        }
        try
        {
            var result = operation(new HuaweiSession(transport, token), target, localAddresses, classes);
            if (result != null && (selection.SelectedAddress != target.Address || selection.ProfileFor(target.Address) != profile.Id))
            {
                // A disk error must not turn a confirmed device command into a failure.
                try { selection.Save(target.Address, profile.Id); }
                catch (Exception error) { RuntimeLog.Write("Automatic model persistence failed: " + error.Message); }
            }
            return result;
        }
        finally { lock (sync) { if (ReferenceEquals(active, transport)) active = null; } }
    }

    private PairedBluetoothDevice SelectHeadset(List<PairedBluetoothDevice> paired)
    {
        if (preferredAddress != null)
        {
            string normalized = preferredAddress.Replace(":", "").Replace("-", "");
            if (normalized.Length != 12 || !ulong.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address))
                throw new InvalidOperationException("--bluetooth-address 必须是 6 字节蓝牙地址。");
            var target = paired.FirstOrDefault(d => d.Address == address)
                ?? throw new InvalidOperationException("指定耳机尚未与 Windows 配对，请先打开系统蓝牙设置配对。");
            profile = HeadsetProfiles.Find(selection.ProfileFor(address)) ?? HeadsetProfiles.Detect(target.Name)
                ?? throw new InvalidOperationException("无法确认指定耳机的型号，请在设置中选择设备和型号。");
            return target;
        }
        if (selectedAddress.HasValue)
            return paired.FirstOrDefault(d => d.Address == selectedAddress.Value)
                ?? throw new InvalidOperationException("当前耳机已不在系统配对列表中，请重新配对后刷新。");
        if (RequiresSelection) throw new InvalidOperationException("请在设置中选择或添加要控制的耳机。");
        var candidates = paired.Where(d => !selection.IsRemoved(d.Address)
            && (selection.ProfileFor(d.Address) != null || HeadsetProfiles.Detect(d.Name) != null)).ToArray();
        var connected = candidates.Where(d => d.Connected).ToArray();
        var match = connected.Length == 1 ? connected[0] : candidates.Length == 1 ? candidates[0] : null;
        if (match != null)
        {
            selectedAddress = match.Address;
            profile = HeadsetProfiles.Find(selection.ProfileFor(match.Address)) ?? HeadsetProfiles.Detect(match.Name);
            return match;
        }
        if (candidates.Length > 1) throw new InvalidOperationException("检测到多副耳机，请在设置中选择要控制的设备和型号。");
        throw new InvalidOperationException("未找到支持的已配对耳机。请先配对；自定义名称可在设置中指定型号。");
    }

    private void RequireDeviceManagement()
    {
        if (!profile.DeviceManagement) throw new InvalidOperationException("当前型号不提供耳机侧设备连接管理。");
    }

    public Task<NoiseControlResult> SetNoiseControlAsync(NoiseControlMode mode, CancellationToken token)
        => ChangeNoiseControlAsync(mode, null, token);
    public Task<NoiseControlResult> SetNoiseLevelAsync(byte level, CancellationToken token)
        => ChangeNoiseControlAsync(NoiseControlMode.Cancellation, level, token);
    private async Task<NoiseControlResult> ChangeNoiseControlAsync(NoiseControlMode mode, byte? requestedLevel, CancellationToken token)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await gate.WaitAsync(token);
        try
        {
            var result = await Task.Run(() => WithSession(token, (session, target, locals, classes) =>
            {
                if (!NoiseControlProtocol.Supports(profile))
                    throw new InvalidOperationException("当前型号未适配噪声控制。");
                session.TraceNoiseControl = true;
                if (requestedLevel.HasValue && !NoiseControlProtocol.SupportsLevel(profile, requestedLevel.Value))
                    throw new InvalidOperationException("当前型号不支持此降噪强度。");
                var before = ReadNoiseState(session);
                if (before == null) throw new InvalidOperationException("未读到当前模式，未发送切换命令。");
                RuntimeLog.Write("Noise control before; mode=" + before.Mode + "; level=" + before.SubMode + "; profile=" + profile.Id);
                if (requestedLevel.HasValue && (before.Mode != NoiseControlMode.Cancellation
                    || !NoiseControlProtocol.SupportsLevel(profile, before.SubMode)))
                    throw new InvalidOperationException("耳机模式或强度已变化，请刷新后重试。");
                if (before.Mode == mode && (!requestedLevel.HasValue || before.SubMode == requestedLevel.Value))
                    return NoiseResult(before, true);
                token.ThrowIfCancellationRequested();
                // One user-requested write only. Never reconnect or retry an uncertain write.
                session.Send(NoiseControlProtocol.WriteCommand, ((byte)1, requestedLevel.HasValue
                    ? NoiseControlProtocol.LevelParameter(profile, requestedLevel.Value) : NoiseControlProtocol.Parameter(mode)));
                RuntimeLog.Write("Noise control command sent; mode=" + mode + "; level=" + requestedLevel + "; profile=" + profile.Id);
                var elapsed = Stopwatch.StartNew();
                bool acknowledged = false;
                while (elapsed.ElapsedMilliseconds < 1200)
                {
                    var reply = session.ReadUntil(elapsed, 1200);
                    if (reply == null) break;
                    if (reply.Command == NoiseControlProtocol.WriteCommand) { acknowledged = true; break; }
                }
                // Some firmware does not send a separate 0x2B04 reply. Missing
                // ACK must not prevent an explicit state query; never resend the write.
                RuntimeLog.Write("Noise control ACK received=" + acknowledged + "; confirming by state query.");
                // Only matching state readback proves success, with or without ACK.
                NoiseControlState actual = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (attempt > 0) DrainDuringSettle(session, 250);
                    actual = ReadNoiseState(session);
                    RuntimeLog.Write("Noise control readback; attempt=" + (attempt + 1)
                        + "; mode=" + (actual?.Mode.ToString() ?? "unknown")
                        + "; level=" + (actual?.SubMode.ToString() ?? "unknown") + "; profile=" + profile.Id);
                    if (actual?.Mode == mode && (!requestedLevel.HasValue || actual.SubMode == requestedLevel.Value))
                        return NoiseResult(actual, true);
                }
                return NoiseResult(actual, false);
            }, requireConnected: true), token);
            return result ?? throw new InvalidOperationException("耳机未连接到这台电脑。");
        }
        finally { gate.Release(); }
    }

    private static NoiseControlResult NoiseResult(NoiseControlState state, bool confirmed)
        => new(state?.Mode, confirmed, state?.Mode == NoiseControlMode.Cancellation ? state.SubMode : null);

    private static NoiseControlState ReadNoiseState(HuaweiSession session)
    {
        session.Query(NoiseControlProtocol.ReadCommand, 1);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 1400)
        {
            var reply = session.ReadUntil(elapsed, 1400);
            if (reply == null) break;
            var state = NoiseControlProtocol.ParseState(reply);
            if (state != null) return state;
        }
        return null;
    }

    public async Task<HeadsetSnapshot> ReadBatteryAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() => WithSession(token,
                (session, target, locals, classes) => ReadSnapshot(session, target, locals, classes, batteryOnly: true),
                requireConnected: true), token);
        }
        finally { gate.Release(); }
    }

    private HeadsetSnapshot ReadSnapshot(HuaweiSession session, PairedBluetoothDevice target, HashSet<ulong> locals, Dictionary<ulong, uint> classes, bool batteryOnly = false)
    {
        var snapshot = new HeadsetSnapshot { Name = target.Name, Address = BluetoothDiscovery.FormatAddress(target.Address), Profile = profile };
        session.Query(0x0108, 1, 2, 3);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 2400)
        {
            var frame = session.ReadUntil(elapsed, 2400);
            if (frame == null) break;
            if (frame.Command != 0x0108 && frame.Command != 0x0127) continue;
            ApplyBattery(snapshot, frame);
            if (frame.Command == 0x0108 && snapshot.HasBattery) break;
        }

        if (batteryOnly)
        {
            if (!snapshot.HasBattery) throw new IOException("未收到有效电量。");
            snapshot.ReadAt = DateTimeOffset.Now;
            return snapshot;
        }
        if (NoiseControlProtocol.Supports(profile))
        {
            var noise = ReadNoiseState(session);
            snapshot.NoiseMode = noise?.Mode;
            snapshot.NoiseLevel = noise?.Mode == NoiseControlMode.Cancellation ? noise.SubMode : null;
        }
        if (!profile.DeviceManagement)
        {
            if (!snapshot.HasBattery) throw new IOException("未收到当前型号的有效电量，请检查型号选择、耳机固件和控制通道占用。");
            snapshot.DeviceListComplete = true;
            snapshot.ReadAt = DateTimeOffset.Now;
            return snapshot;
        }
        var rows = new Dictionary<int, DeviceInfo>();
        int? total = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            session.Query(0x2B31, 1);
            elapsed.Restart();
            while (elapsed.ElapsedMilliseconds < 1600)
            {
                var frame = session.ReadUntil(elapsed, 1600);
                if (frame == null) break;
                if (frame.Command == 0x0108 || frame.Command == 0x0127) { ApplyBattery(snapshot, frame); continue; }
                if (frame.Command != 0x2B31) continue;
                int? reported = frame.Number(2);
                if (reported.HasValue && reported.Value >= 0 && reported.Value <= 64) total = reported;
                var raw = frame.Get(4);
                int? index = frame.Number(3);
                if (raw.Length == 6 && raw.Any(value => value != 0) && raw.Any(value => value != 255)
                    && index.HasValue && index >= 0 && index < 64)
                {
                    ulong address = 0;
                    for (int i = 0; i < raw.Length; i++) address |= (ulong)raw[i] << (8 * i);
                    string name = Encoding.UTF8.GetString(frame.Get(9)).TrimEnd('\0');
                    bool local = locals.Contains(address);
                    var type = DeviceTypeResolver.Detect(name, local, classes.TryGetValue(address, out uint deviceClass) ? deviceClass : null);
                    var state = frame.Get(5);
                    rows[index.Value] = new DeviceInfo
                    {
                        Id = BluetoothDiscovery.FormatAddress(address), Slot = index.Value,
                        Name = string.IsNullOrWhiteSpace(name) ? "未命名设备 · " + BluetoothDiscovery.FormatAddress(address) : name,
                        DetectedKind = type.Kind, AutomaticTypeDescription = type.Description,
                        ProtocolAddress = raw, IsLocalComputer = local,
                        ConnectionState = state.Length == 1 ? state[0] : null,
                        Preferred = frame.Number(7) == 1, AutoConnect = frame.Number(8) == 1,
                        LockSupported = profile.ConnectionLock,
                        SupportsState3 = profile.ConnectionLock,
                        ConnectionLocked = profile.ConnectionLock ? ConnectionLockProtocol.ReadState(frame) : null
                    };
                }
                if (total.HasValue && rows.Count == total.Value) break;
            }
            if (total.HasValue && rows.Count == total.Value) break;
        }
        snapshot.Devices = rows.OrderBy(row => row.Key).Select(row => row.Value).GroupBy(d => d.Id).Select(group => group.First()).ToArray();
        snapshot.ReportedSlots = total;
        snapshot.DeviceListComplete = total.HasValue && rows.Count == total.Value && snapshot.Devices.Length == total.Value;
        session.Query(0x2B2F, 1);
        elapsed.Restart();
        while (elapsed.ElapsedMilliseconds < 1000)
        {
            var frame = session.ReadUntil(elapsed, 1000);
            if (frame == null) break;
            if (frame.Command == 0x0108 || frame.Command == 0x0127) ApplyBattery(snapshot, frame);
            if (frame.Command == 0x2B2F && frame.Get(1).Length == 1)
            { snapshot.DualConnectEnabled = frame.Get(1)[0] switch { 0 => false, 1 => true, _ => null }; break; }
        }
        if (!snapshot.HasBattery && !total.HasValue && rows.Count == 0)
            throw new IOException("控制通道已连接，但未收到有效电量或设备列表，请确认型号、固件或其他 SPP 客户端占用。");
        snapshot.ReadAt = DateTimeOffset.Now;
        return snapshot;
    }

    private static void ApplyBattery(HeadsetSnapshot snapshot, HuaweiFrame frame)
    {
        var global = frame.Get(1);
        if (global.Length == 1) snapshot.Global = Level(global[0]);
        var levels = frame.Get(2);
        if (snapshot.Profile.SplitBattery && levels.Length == 3) { snapshot.Left = Level(levels[0]); snapshot.Right = Level(levels[1]); snapshot.Case = Level(levels[2]); }
        var charging = frame.Get(3);
        if (charging.Length >= 3)
        { snapshot.LeftCharging = charging[0] == 1; snapshot.RightCharging = charging[1] == 1; snapshot.CaseCharging = charging[2] == 1; }
    }
    private static int? Level(byte value) => value <= 100 ? value : null;

    public void Dispose()
    {
        lock (sync) { disposed = true; active?.Dispose(); active = null; }
    }
}
