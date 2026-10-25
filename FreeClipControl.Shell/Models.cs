using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace FreeClipControl.Shell;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void Changed(params string[] names)
    {
        foreach (string name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class BatteryReading : Observable
{
    private readonly string name;
    private int? level;
    private bool? charging;
    public BatteryReading(string label) => name = label;
    public string Text => level.HasValue ? level.Value + "%" : "--";
    internal int? Level => level;
    public double Value => level ?? 0;
    public string Label => name + (charging == true ? " · 充电中" : "");
    public string AccessibleText => name + (level.HasValue ? "电量 " + level.Value + "%" : "电量未知") + (charging == true ? "，充电中" : "");
    public Visibility BarVisibility => level.HasValue ? Visibility.Visible : Visibility.Collapsed;
    internal void Update(int? value, bool? isCharging)
    {
        level = value;
        charging = isCharging;
        Changed(nameof(Text), nameof(Value), nameof(Label), nameof(AccessibleText), nameof(BarVisibility));
    }
}

public sealed class DeviceInfo : Observable
{
    private bool expanded, stale;
    public string Id { get; internal set; }
    public string Name { get; internal set; }
    internal ProductIconKind DetectedKind { get; set; }
    internal string AutomaticTypeDescription { get; set; }
    public ProductIconKind IconKind => DeviceTypeResolver.GetOverride(Id) ?? DetectedKind;
    public string IconKey => IconKind.ToString();
    public string TypeDescription => DeviceTypeResolver.GetOverride(Id) is { } kind
        ? "手动指定为" + DeviceTypeResolver.Label(kind) + "；右键可更改" : AutomaticTypeDescription;
    internal int Slot { get; set; }
    internal byte[] ProtocolAddress { get; set; }
    internal byte? ConnectionState { get; set; }
    internal bool IsLocalComputer { get; set; }
    internal bool Preferred { get; set; }
    internal bool AutoConnect { get; set; }
    internal bool? ConnectionLocked { get; set; }
    internal bool LockSupported { get; set; } = true;
    internal bool SupportsState3 { get; set; } = true;
    public bool IsConnectionLocked => ConnectionLocked == true;
    public bool CanChangeConnectionLock => LockSupported && ConnectionKnown && Connected && ConnectionLocked.HasValue;
    public Visibility LockButtonVisibility => LockSupported && IsExpanded && ConnectionKnown && Connected ? Visibility.Visible : Visibility.Collapsed;
    public string LockGlyph => IsConnectionLocked ? "\uE72E" : "\uE785";
    public string LockActionLabel => !ConnectionLocked.HasValue ? "锁定状态未知，请刷新" : IsConnectionLocked ? "解除本次连接锁定" : "锁定本次连接";
    // FreeClip 2 also reports 3 while connected (observed around audio changes).
    // Do not infer a particular audio profile from it, or accept other unknown values.
    public bool ConnectionKnown => !stale && (ConnectionState is 0 or 1 or 9 || SupportsState3 && ConnectionState == 3);
    public bool Connected => ConnectionState is 1 or 3 or 9;
    public bool IsActive => ConnectionKnown && Connected;
    public double ContentOpacity => IsActive ? 1 : 0.5;
    public bool IsExpanded
    {
        get => expanded;
        set { if (expanded == value) return; expanded = value; Changed(nameof(IsExpanded), nameof(ActionVisibility), nameof(LockButtonVisibility)); }
    }
    public Visibility ActionVisibility => IsExpanded && ConnectionKnown ? Visibility.Visible : Visibility.Collapsed;
    public string Status => stale ? "状态待刷新" : !ConnectionKnown ? "状态未知" + (ConnectionState.HasValue ? "（" + ConnectionState.Value + "）" : "")
        : (ConnectionState == 9 ? "播放中" : Connected ? "已连接" : "已配对")
            + (LockSupported && Connected && IsConnectionLocked ? " · 已锁定" : "");
    public string ActionLabel => Connected ? "断开连接" : "连接";
    internal void MarkStale()
    {
        stale = true;
        Changed(nameof(ConnectionKnown), nameof(IsActive), nameof(ContentOpacity), nameof(Status), nameof(ActionVisibility), nameof(LockButtonVisibility), nameof(CanChangeConnectionLock));
    }
    internal void UpdateFrom(DeviceInfo next)
    {
        Name = next.Name; DetectedKind = next.DetectedKind; AutomaticTypeDescription = next.AutomaticTypeDescription; Slot = next.Slot;
        ProtocolAddress = next.ProtocolAddress; ConnectionState = next.ConnectionState;
        IsLocalComputer = next.IsLocalComputer; Preferred = next.Preferred; AutoConnect = next.AutoConnect;
        ConnectionLocked = next.ConnectionLocked;
        LockSupported = next.LockSupported; SupportsState3 = next.SupportsState3;
        stale = false;
        Changed(nameof(Name), nameof(IconKind), nameof(IconKey), nameof(TypeDescription), nameof(Connected), nameof(ConnectionKnown), nameof(IsActive), nameof(ContentOpacity), nameof(Status), nameof(ActionLabel), nameof(ActionVisibility), nameof(IsConnectionLocked), nameof(LockGlyph), nameof(LockActionLabel), nameof(LockButtonVisibility), nameof(CanChangeConnectionLock));
    }
    internal void SetTypeOverride(ProductIconKind kind)
    {
        DeviceTypeResolver.SetOverride(Id, kind);
        Changed(nameof(IconKind), nameof(IconKey), nameof(TypeDescription));
    }
}

internal sealed class HeadsetSnapshot
{
    public HeadsetProfile Profile { get; set; } = HeadsetProfiles.Find("freeclip2");
    public string Name { get; set; }
    public string Address { get; set; }
    public int? Global { get; set; }
    public int? Left { get; set; }
    public int? Right { get; set; }
    public int? Case { get; set; }
    public bool? LeftCharging { get; set; }
    public bool? RightCharging { get; set; }
    public bool? CaseCharging { get; set; }
    public bool? DualConnectEnabled { get; set; }
    public int? ReportedSlots { get; set; }
    public bool DeviceListComplete { get; set; }
    public DeviceInfo[] Devices { get; set; } = Array.Empty<DeviceInfo>();
    public DateTimeOffset ReadAt { get; set; }
    public NoiseControlMode? NoiseMode { get; set; }
    public byte? NoiseLevel { get; set; }
    public bool HasBattery => Global.HasValue || Left.HasValue || Right.HasValue || Case.HasValue;
}

internal sealed record DeviceCommandResult(HeadsetSnapshot Snapshot, bool Confirmed, bool PauseAutomaticRead, string Message);
internal enum BluetoothAvailability { Unknown, Connected, RadioOff, NotConnected, Unavailable, MatchFailed }
internal interface IDeviceDataSource : IDisposable
{
    Task<BluetoothAvailability> GetAvailabilityAsync(CancellationToken token) => Task.FromResult(BluetoothAvailability.Connected);
    Task<HeadsetSnapshot> ReadAsync(CancellationToken token, bool allowReconnect = false);
    Task<HeadsetSnapshot> ReadBatteryAsync(CancellationToken token) => ReadAsync(token, false);
    Task<DeviceCommandResult> SetConnectedAsync(string id, bool connected, CancellationToken token);
    Task<DeviceCommandResult> SetConnectionLockedAsync(string id, bool locked, CancellationToken token);
    Task<DeviceCommandResult> ConnectToHeadsetAsync(CancellationToken token);
    Task<NoiseControlResult> SetNoiseControlAsync(NoiseControlMode mode, CancellationToken token)
        => Task.FromException<NoiseControlResult>(new NotSupportedException("当前数据源不支持噪声控制。"));
    Task<NoiseControlResult> SetNoiseLevelAsync(byte level, CancellationToken token)
        => Task.FromException<NoiseControlResult>(new NotSupportedException("当前数据源不支持降噪强度。"));
}

public sealed class PanelViewModel : Observable, IDisposable
{
    private readonly IDeviceDataSource source;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken cancellation;
    private bool busy, online, paused, disposed, trayAvailable = true;
    private bool trayBatteryEnabled = true;
    private Task startupSelectionTask;
    private TaskCompletionSource<bool> idleCompletion;
    private NoiseControlMode? noiseMode;
    private byte? noiseLevel;
    public byte? NoiseLevel => noiseLevel;
    public NoiseLevelChoice[] NoiseLevels => NoiseControlProtocol.LevelChoices(profile);
    public Visibility NoiseLevelVisibility => NoiseControlVisibility == Visibility.Visible
        && noiseMode == NoiseControlMode.Cancellation && NoiseLevels.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool CanChangeNoiseLevel => CanChangeNoiseControl && noiseMode == NoiseControlMode.Cancellation
        && noiseLevel.HasValue && NoiseControlProtocol.SupportsLevel(profile, noiseLevel.Value);
    private bool noiseControlConfirmed;
    public NoiseControlChoice[] NoiseChoices { get; } = new NoiseControlChoice[]
    {
        new(NoiseControlMode.Cancellation, "降噪"),
        new(NoiseControlMode.Off, "关闭"),
        new(NoiseControlMode.Awareness, "透传")
    };
    public NoiseControlMode? NoiseMode => noiseMode;
    public Visibility NoiseControlVisibility => online && noiseControlConfirmed && NoiseControlProtocol.Supports(profile)
        ? Visibility.Visible : Visibility.Collapsed;
    public bool CanChangeNoiseControl => !busy && !disposed && online && noiseControlConfirmed
        && noiseMode.HasValue && NoiseControlProtocol.Supports(profile);
    private bool? pendingReconnect;
    private int connectionRevision;
    private int bindingRevision;
    private bool listComplete;
    private bool? dualConnect;
    private HeadsetProfile profile = HeadsetProfiles.Find("freeclip2");
    private BluetoothAvailability availability;
    private string message = "", headsetName = "FreeClip 2", headsetAddress;
    private DateTimeOffset lastAttempt = DateTimeOffset.MinValue, lastSuccess = DateTimeOffset.MinValue;
    private DateTimeOffset lastFullAttempt = DateTimeOffset.MinValue;
    public ObservableCollection<DeviceInfo> Devices { get; } = new();
    public ObservableCollection<BoundHeadset> BoundHeadsets { get; } = new();
    public Visibility NoBindingsVisibility => BoundHeadsets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public BatteryReading LeftBattery { get; } = new("左耳");
    public BatteryReading RightBattery { get; } = new("右耳");
    public BatteryReading CaseBattery { get; } = new("充电盒");
    public BatteryReading GlobalBattery { get; } = new("耳机");
    public Visibility SplitBatteryVisibility => profile.SplitBattery ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GlobalBatteryVisibility => profile.SplitBattery ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DeviceManagementVisibility => profile.DeviceManagement ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoDeviceManagementVisibility => profile.DeviceManagement ? Visibility.Collapsed : Visibility.Visible;
    public string ModelDescription => profile.Name + " · " + profile.Description;
    public bool CanSelectHeadset => !busy && !disposed;
    internal ulong? SelectedHeadsetAddress => (source as IHeadsetSelection)?.SelectedAddress;
    internal string SelectedProfileId => (source as IHeadsetSelection)?.SelectedProfileId;
    public string HeadsetName => headsetName;
    public string SelectedHeadsetTitle => SelectedHeadsetAddress.HasValue ? HeadsetName : "选择蓝牙耳机";
    public string SelectedHeadsetStatus => SelectedHeadsetAddress.HasValue ? ConnectionLabel : "展开以切换或添加耳机";
    public Visibility SelectedHeadsetVisibility => SelectedHeadsetAddress.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public string HeadsetSwitchToolTip => HeadsetName + "(点击以切换)";
    internal HeadsetBrand CopyBrand => AbstractCopy.DetectBrand(profile?.Name, headsetName);
    public string HeadsetAddress => headsetAddress ?? "尚未识别耳机";
    public bool IsConnectionNoticeVisible => availability != BluetoothAvailability.Connected;
    public Visibility ConnectedPageVisibility => IsConnectionNoticeVisible ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ConnectionNoticeVisibility => IsConnectionNoticeVisible ? Visibility.Visible : Visibility.Collapsed;
    public bool NeedsConfiguration => availability == BluetoothAvailability.MatchFailed;
    public string BluetoothFooterAction => NeedsConfiguration ? "打开软件设置" : "打开系统蓝牙设置";
    public string BluetoothFooterLabel => NeedsConfiguration ? "打开软件设置" : IsConnectionNoticeVisible ? "更多蓝牙设置" : HeadsetName;
    public string ConnectionNoticeTitle => availability switch
    {
        BluetoothAvailability.RadioOff => "蓝牙已关闭",
        BluetoothAvailability.NotConnected => "耳机未连接",
        BluetoothAvailability.Unavailable => "蓝牙不可用",
        BluetoothAvailability.MatchFailed => "未能匹配耳机",
        _ => string.IsNullOrWhiteSpace(message) ? "正在检查连接" : "暂时无法读取耳机"
    };
    public string ConnectionNoticeDescription => availability switch
    {
        BluetoothAvailability.RadioOff => "请在 Windows 设置中打开蓝牙，并连接耳机。",
        BluetoothAvailability.NotConnected => "请在 Windows 蓝牙设置中连接耳机，连接后即可查看电量和管理设备。",
        BluetoothAvailability.Unavailable => "未检测到可用的蓝牙适配器，请检查电脑的蓝牙是否可用。",
        BluetoothAvailability.MatchFailed => "未找到可自动匹配的蓝牙设备和耳机型号。请打开软件设置，在耳机配置中选择设备和型号。",
        _ => string.IsNullOrWhiteSpace(message) ? "正在检查 Windows 蓝牙和耳机的连接状态。" : message
    };
    public bool CanRefresh => !disposed;
    public bool CanInteract => !busy && !disposed && online && listComplete && profile.DeviceManagement;
    public bool CanConnectToHeadset => !busy && !disposed
        && source is not IHeadsetSelection { RequiresSelection: true, SelectedAddress: null };
    public string TrayToolTip
    {
        get
        {
            if (source is IHeadsetSelection { RequiresSelection: true, SelectedAddress: null }) return "未选择耳机";
            string name = profile.Name.Replace("HUAWEI ", "").Replace("HONOR ", "");
            if (profile.Id == "freeclip2") name = "FreeClip2";
            if (!trayBatteryEnabled) return name;
            int? level = profile.SplitBattery
                ? LeftBattery.Level.HasValue && RightBattery.Level.HasValue ? Math.Min(LeftBattery.Level.Value, RightBattery.Level.Value) : null
                : GlobalBattery.Level;
            return name + "（" + (online && level.HasValue ? level.Value.ToString() : "--") + "%）";
        }
    }
    public string Message => trayAvailable ? message : "托盘暂不可用，已切换为窗口模式" + (string.IsNullOrWhiteSpace(message) ? "" : " · " + message);
    public Visibility StatusVisibility => !trayAvailable || !string.IsNullOrWhiteSpace(message) ? Visibility.Visible : Visibility.Collapsed;
    public string ConnectionLabel => online ? listComplete ? "已连接" : "部分数据"
        : lastSuccess != DateTimeOffset.MinValue ? "上次数据" : "未连接";
    public string ConnectionSummary => !online ? "状态待刷新" : !listComplete ? "设备状态未收全"
        : Devices.Any(d => !d.ConnectionKnown) ? "部分设备状态未知"
        : Devices.Count(d => d.ConnectionKnown && d.Connected) + (dualConnect.HasValue ? " / " + (dualConnect.Value ? 2 : 1) : "") + " 已连接";

    internal PanelViewModel(IDeviceDataSource dataSource)
    {
        source = dataSource;
        cancellation = lifetime.Token;
        profile = HeadsetProfiles.Find(SelectedProfileId) ?? profile;
        headsetName = profile.Name;
        if (source is IHeadsetSelection { RequiresSelection: true, SelectedAddress: null }) headsetName = "未选择耳机";
        if (SelectedHeadsetAddress.HasValue) headsetAddress = BluetoothDiscovery.FormatAddress(SelectedHeadsetAddress.Value);
    }
    internal Task InitializeStartupSelectionAsync() => startupSelectionTask ??= InitializeStartupSelectionCoreAsync();
    private async Task InitializeStartupSelectionCoreAsync()
    {
        if (disposed || source is not IHeadsetSelection selection) return;
        SetBusy(true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            // Local stack metadata only. This runs even with background battery reads off.
            await selection.InitializeSelectionAsync(timeout.Token).WaitAsync(timeout.Token);
            if (disposed) return;
            profile = HeadsetProfiles.Find(selection.SelectedProfileId) ?? profile;
            headsetAddress = selection.SelectedAddress.HasValue
                ? BluetoothDiscovery.FormatAddress(selection.SelectedAddress.Value) : null;
            headsetName = selection.SelectedAddress.HasValue ? selection.SelectedName ?? profile.Name : "未选择耳机";
            MarkOffline();
            SetAvailability(selection.RequiresSelection && !selection.SelectedAddress.HasValue
                ? BluetoothAvailability.MatchFailed : BluetoothAvailability.Unknown);
            NotifyProfile();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (disposed) return;
            ReportStatus("启动时暂未能确认已连接耳机，请刷新后重试。");
            RuntimeLog.Write("Startup headset selection failed: " + error);
        }
        finally { if (!disposed) SetBusy(false); }
    }

    internal async Task PrepareMenuSelectionAsync(CancellationToken requestCancellation)
    {
        if (disposed || source is not IHeadsetSelection selection) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, requestCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        bool ownsBusy = false;
        try
        {
            // Let an existing read/write finish before changing its target. All UI
            // entry points share busy; waiting does not cancel a submitted command.
            while (busy) await idleCompletion.Task.WaitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            SetBusy(true);
            ownsBusy = true;
            var result = await selection.SelectConnectedHeadsetForMenuAsync(timeout.Token);
            if (disposed) return;
            if (result.Changed && selection.SelectedAddress.HasValue)
            {
                var next = HeadsetProfiles.Find(selection.SelectedProfileId);
                ApplyHeadsetSelection(selection.SelectedAddress.Value, selection.SelectedName ?? next.Name, next);
            }
            if (result.Availability != BluetoothAvailability.Unknown)
            {
                SetAvailability(result.Availability);
                if (result.Availability != BluetoothAvailability.Connected) MarkOffline();
            }
            RuntimeLog.Write("Menu preflight complete; switched=" + result.Changed + "; availability=" + result.Availability);
        }
        catch (OperationCanceledException)
        {
            RuntimeLog.Write("Menu preflight cancelled or timed out; no deferred selection will be applied.");
        }
        catch (Exception error)
        {
            RuntimeLog.Write("Menu preflight failed; retaining current selection: " + error);
        }
        finally { if (ownsBusy && !disposed) SetBusy(false); }
    }

    internal Task<HeadsetChoice[]> ListHeadsetsAsync() => source is IHeadsetSelection selection
        ? selection.ListHeadsetsAsync(cancellation) : Task.FromResult(Array.Empty<HeadsetChoice>());

    internal async Task ReloadBindingsAsync()
    {
        if (source is not IHeadsetSelection selection || disposed) return;
        int revision = ++bindingRevision;
        var rows = await selection.ListBindingsAsync(cancellation);
        if (disposed || revision != bindingRevision) return;
        BoundHeadsets.Clear();
        foreach (var row in rows) BoundHeadsets.Add(row);
        var selected = rows.FirstOrDefault(row => row.Selected);
        if (selected != null)
        {
            headsetName = selected.Name;
            headsetAddress = selected.AddressText;
            profile = HeadsetProfiles.Find(selected.ProfileId) ?? profile;
        }
        Changed(nameof(NoBindingsVisibility));
        NotifyProfile();
    }
    internal Task SelectBoundHeadsetAsync(BoundHeadset headset)
    {
        if (headset?.CanSelect != true) throw new InvalidOperationException("请先配对设备并确认对应型号配置可用。");
        return SelectHeadsetAsync(new HeadsetChoice(headset.Address, headset.Name, headset.Connected, headset.ProfileId), HeadsetProfiles.Find(headset.ProfileId));
    }

    internal async Task RemoveBoundHeadsetAsync(BoundHeadset headset)
    {
        if (!CanSelectHeadset) throw new InvalidOperationException("正在读取或操作耳机，请稍后再删除。");
        if (headset == null || source is not IHeadsetSelection selection)
            throw new InvalidOperationException("未找到要删除的耳机配置。");
        bool removedCurrent = selection.SelectedAddress == headset.Address;
        ++bindingRevision;
        SetBusy(true);
        try
        {
            await selection.RemoveBindingAsync(headset.Address, cancellation);
            if (removedCurrent)
            {
                ++connectionRevision;
                pendingReconnect = null;
                paused = true;
                MarkOffline();
                Devices.Clear();
                LeftBattery.Update(null, null); RightBattery.Update(null, null);
                CaseBattery.Update(null, null); GlobalBattery.Update(null, null);
                headsetName = "未选择耳机";
                headsetAddress = null;
                lastAttempt = lastSuccess = DateTimeOffset.MinValue;
                listComplete = false;
                dualConnect = null;
                SetAvailability(BluetoothAvailability.MatchFailed);
                ReportStatus("");
                NotifyProfile();
            }
        }
        finally { SetBusy(false); }
        await ReloadBindingsAsync();
    }

    internal async Task SelectHeadsetAsync(HeadsetChoice headset, HeadsetProfile next)
    {
        if (!CanSelectHeadset) throw new InvalidOperationException("正在读取或操作耳机，请稍后再切换。");
        if (headset == null || next == null) throw new InvalidOperationException("请选择耳机和对应的型号。");
        if (source is not IHeadsetSelection selection) throw new InvalidOperationException("当前数据源不支持切换型号。");
        SetBusy(true);
        try
        {
            await selection.SelectHeadsetAsync(headset.Address, next.Id, cancellation);
            ApplyHeadsetSelection(headset.Address, headset.Name, next);
        }
        finally { SetBusy(false); }
        // Selecting a model saves local preferences only. Read only if Windows
        // already reports the chosen earphone connected; never connect for setup.
        await RefreshConnectedAsync();
        await ReloadBindingsAsync();
    }
    private void ApplyHeadsetSelection(ulong address, string name, HeadsetProfile next)
    {
        ++connectionRevision;
        ++bindingRevision;
        pendingReconnect = null;
        paused = false;
        MarkOffline();
        Devices.Clear();
        LeftBattery.Update(null, null); RightBattery.Update(null, null); CaseBattery.Update(null, null); GlobalBattery.Update(null, null);
        profile = next;
        headsetName = name;
        headsetAddress = BluetoothDiscovery.FormatAddress(address);
        lastAttempt = lastSuccess = lastFullAttempt = DateTimeOffset.MinValue;
        listComplete = false;
        dualConnect = null;
        SetAvailability(BluetoothAvailability.Unknown);
        ReportStatus("");
        NotifyProfile();
    }
    private void NotifyProfile() => Changed(nameof(SplitBatteryVisibility), nameof(GlobalBatteryVisibility),
        nameof(DeviceManagementVisibility), nameof(NoDeviceManagementVisibility), nameof(ModelDescription),
        nameof(HeadsetName), nameof(HeadsetSwitchToolTip), nameof(CopyBrand), nameof(HeadsetAddress), nameof(CanInteract), nameof(ConnectionLabel), nameof(TrayToolTip), nameof(BluetoothFooterLabel),
        nameof(SelectedHeadsetTitle), nameof(SelectedHeadsetStatus), nameof(SelectedHeadsetVisibility), nameof(CanConnectToHeadset),
        nameof(NoiseControlVisibility), nameof(CanChangeNoiseControl), nameof(NoiseLevels), nameof(NoiseLevelVisibility), nameof(CanChangeNoiseLevel));
    public void SetTrayAvailable(bool available) { trayAvailable = available; Changed(nameof(Message), nameof(StatusVisibility)); }
    internal void ReportStatus(string text) { message = text; Changed(nameof(Message), nameof(StatusVisibility), nameof(ConnectionNoticeTitle), nameof(ConnectionNoticeDescription)); }
    private void SetAvailability(BluetoothAvailability value)
    {
        availability = value;
        Changed(nameof(IsConnectionNoticeVisible), nameof(ConnectedPageVisibility), nameof(ConnectionNoticeVisibility),
            nameof(BluetoothFooterLabel), nameof(ConnectionNoticeTitle), nameof(ConnectionNoticeDescription));
        Changed(nameof(NeedsConfiguration), nameof(BluetoothFooterAction));
    }
    private void SetBusy(bool value)
    {
        if (value && !busy) idleCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = !value ? idleCompletion : null;
        busy = value;
        Changed(nameof(CanInteract), nameof(CanConnectToHeadset), nameof(CanSelectHeadset), nameof(CanChangeNoiseControl), nameof(CanChangeNoiseLevel));
        if (!value && pendingReconnect.HasValue && !disposed)
        {
            bool allowReconnect = pendingReconnect.Value;
            pendingReconnect = null;
            _ = RefreshCoreAsync(allowReconnect);
        }
        completed?.TrySetResult(true);
    }
    public void SelectDevice(DeviceInfo selected)
    {
        foreach (var device in Devices) device.IsExpanded = ReferenceEquals(device, selected);
    }

    // Keep the refresh button enabled; extra clicks during an operation are no-ops.
    public Task RefreshAsync() => busy || disposed ? Task.CompletedTask : RequestRefreshAsync(true);
    internal Task RefreshConnectedAsync() => RequestRefreshAsync(false);
    internal void SetTrayBatteryEnabled(bool enabled)
    {
        trayBatteryEnabled = enabled;
        Changed(nameof(TrayToolTip));
    }
    // Background/startup reads are optional; explicit UI reads remain available.
    internal Task RefreshTrayBatteryAsync() => !trayBatteryEnabled || paused || busy || disposed
        || DateTimeOffset.Now - lastAttempt < TimeSpan.FromSeconds(30)
        ? Task.CompletedTask : RefreshCoreAsync(false, batteryOnly: true);
    private Task RequestRefreshAsync(bool allowReconnect)
    {
        if (disposed) return Task.CompletedTask;
        // Passive refreshes stay passive even when queued behind another read.
        // Only an explicit refresh-button request permits reconnection.
        paused = false;
        if (busy)
        {
            pendingReconnect = pendingReconnect == true || allowReconnect;
            return Task.CompletedTask;
        }
        return RefreshCoreAsync(allowReconnect);
    }
    internal Task RefreshIfDueAsync()
    {
        if (paused || busy || disposed || DateTimeOffset.Now - lastFullAttempt < TimeSpan.FromSeconds(30)) return Task.CompletedTask;
        return RefreshCoreAsync(false);
    }
    private async Task RefreshCoreAsync(bool allowReconnect, bool batteryOnly = false)
    {
        if (busy || disposed) return;
        if (allowReconnect) paused = false;
        lastAttempt = DateTimeOffset.Now;
        if (!batteryOnly) lastFullAttempt = lastAttempt;
        SetBusy(true);
        try
        {
            var status = await source.GetAvailabilityAsync(cancellation);
            if (disposed) return;
            SetAvailability(status);
            if (!batteryOnly) await ReloadBindingsAsync();
            if (status == BluetoothAvailability.RadioOff || status == BluetoothAvailability.NotConnected || status == BluetoothAvailability.Unavailable || status == BluetoothAvailability.MatchFailed)
            {
                MarkOffline();
                ReportStatus("");
                return;
            }
            var snapshot = batteryOnly ? await source.ReadBatteryAsync(cancellation)
                : await source.ReadAsync(cancellation, allowReconnect);
            if (!disposed)
            {
                if (snapshot != null)
                {
                    if (batteryOnly) ApplyTrayBattery(snapshot);
                    else ApplySnapshot(snapshot);
                }
                else
                {
                    SetAvailability(BluetoothAvailability.NotConnected);
                    MarkOffline();
                    ReportStatus("耳机未连接到这台电脑。");
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!disposed)
            {
                MarkOffline();
                ReportStatus(Friendly(error) + (lastSuccess != DateTimeOffset.MinValue ? " 当前显示上次读取的数据。" : ""));
                RuntimeLog.Write("Bluetooth read failed: " + error);
            }
        }
        finally { if (!disposed) SetBusy(false); }
    }

    private void ApplyTrayBattery(HeadsetSnapshot snapshot)
    {
        // Battery-only reads must not imply a complete peer table or overwrite lock state.
        if (headsetAddress != snapshot.Address) { Devices.Clear(); listComplete = false; UpdateNoiseMode(null, resetSupport: true); }
        if (!online) listComplete = false;
        online = true;
        headsetName = string.IsNullOrWhiteSpace(snapshot.Name) ? "FreeClip 2" : snapshot.Name;
        headsetAddress = snapshot.Address;
        profile = snapshot.Profile;
        if (!NoiseControlProtocol.Supports(profile)) UpdateNoiseMode(null, resetSupport: true);
        GlobalBattery.Update(snapshot.Global, null);
        LeftBattery.Update(snapshot.Left, snapshot.LeftCharging);
        RightBattery.Update(snapshot.Right, snapshot.RightCharging);
        CaseBattery.Update(snapshot.Case, snapshot.CaseCharging);
        lastSuccess = DateTimeOffset.Now;
        NotifyProfile();
        Changed(nameof(TrayToolTip), nameof(HeadsetName), nameof(HeadsetAddress), nameof(ConnectionLabel), nameof(CanInteract));
    }
    private void ApplySnapshot(HeadsetSnapshot snapshot)
    {
        if (headsetAddress != snapshot.Address) { Devices.Clear(); UpdateNoiseMode(null, resetSupport: true); }
        headsetName = string.IsNullOrWhiteSpace(snapshot.Name) ? "FreeClip 2" : snapshot.Name;
        headsetAddress = snapshot.Address;
        profile = snapshot.Profile;
        GlobalBattery.Update(snapshot.Global, null);
        LeftBattery.Update(snapshot.Left, snapshot.LeftCharging);
        RightBattery.Update(snapshot.Right, snapshot.RightCharging);
        CaseBattery.Update(snapshot.Case, snapshot.CaseCharging);
        var ids = snapshot.Devices.Select(d => d.Id).ToHashSet();
        foreach (var existing in Devices.ToArray())
        {
            if (ids.Contains(existing.Id)) continue;
            if (snapshot.DeviceListComplete) Devices.Remove(existing); else existing.MarkStale();
        }
        foreach (var reported in snapshot.Devices)
        {
            var existing = Devices.FirstOrDefault(d => d.Id == reported.Id);
            if (existing == null) Devices.Add(reported); else existing.UpdateFrom(reported);
        }
        // Connected devices first; retain the earphone's stable slot order
        // within each group, without losing selection or expanded actions.
        var order = Devices.OrderByDescending(d => d.IsActive).ThenBy(d => d.Slot).ToArray();
        for (int i = 0; i < order.Length; i++)
        {
            int current = Devices.IndexOf(order[i]);
            if (current != i) Devices.Move(current, i);
        }
        online = true;
        UpdateNoiseMode(snapshot.NoiseMode, resetSupport: !NoiseControlProtocol.Supports(profile), level: snapshot.NoiseLevel);
        SetAvailability(BluetoothAvailability.Connected);
        listComplete = snapshot.DeviceListComplete;
        dualConnect = snapshot.DualConnectEnabled;
        lastSuccess = snapshot.ReadAt;
        RuntimeLog.Write("Headset snapshot read; profile=" + profile.Id + "; left=" + snapshot.Left + "; right=" + snapshot.Right
            + "; case=" + snapshot.Case + "; global=" + snapshot.Global + "; devices=" + snapshot.Devices.Length + "; complete=" + snapshot.DeviceListComplete);
        var notes = new List<string>();
        if (profile.DeviceManagement && !snapshot.DeviceListComplete)
            notes.Add(snapshot.ReportedSlots.HasValue ? $"设备列表未收全 {snapshot.Devices.Length}/{snapshot.ReportedSlots}" : "设备列表未返回");
        if (profile.SplitBattery && (!snapshot.Left.HasValue || !snapshot.Right.HasValue || !snapshot.Case.HasValue))
            notes.Add(snapshot.Global.HasValue ? $"综合电量 {snapshot.Global}% · 分项电量未完整返回" : "部分电量未知");
        ReportStatus(notes.Count > 0 ? string.Join(" · ", notes) : "");
        Changed(nameof(HeadsetName), nameof(HeadsetAddress), nameof(ConnectionSummary), nameof(ConnectionLabel), nameof(CanInteract), nameof(TrayToolTip));
        NotifyProfile();
    }

    private void MarkOffline()
    {
        online = false;
        UpdateNoiseMode(null, resetSupport: true);
        foreach (var device in Devices) device.MarkStale();
        Changed(nameof(ConnectionSummary), nameof(ConnectionLabel), nameof(SelectedHeadsetStatus), nameof(CanInteract), nameof(TrayToolTip));
    }

    private void UpdateNoiseMode(NoiseControlMode? mode, bool resetSupport = false, byte? level = null)
    {
        if (resetSupport) noiseControlConfirmed = false;
        noiseMode = NoiseControlProtocol.Supports(profile) && !resetSupport ? mode : null;
        noiseLevel = noiseMode == NoiseControlMode.Cancellation && level.HasValue
            && NoiseControlProtocol.SupportsLevel(profile, level.Value) ? level : null;
        if (noiseMode.HasValue) noiseControlConfirmed = true;
        Changed(nameof(NoiseMode), nameof(NoiseLevel), nameof(NoiseControlVisibility), nameof(CanChangeNoiseControl),
            nameof(NoiseLevelVisibility), nameof(CanChangeNoiseLevel));
    }
    public async Task SetNoiseControlAsync(NoiseControlMode mode)
    {
        if (!CanChangeNoiseControl || !Enum.IsDefined(mode) || noiseMode == mode) return;
        SetBusy(true);
        try
        {
            var result = await source.SetNoiseControlAsync(mode, cancellation);
            if (disposed) return;
            UpdateNoiseMode(result.Mode, level: result.Level);
            ReportStatus(result.Confirmed ? ""
                : mode == NoiseControlMode.Cancellation && result.Mode.HasValue && result.Mode != mode
                    ? "耳机未进入降噪，请检查双耳佩戴状态或手机 App 的单耳降噪设置。"
                    : "耳机尚未确认目标模式，请稍后刷新。");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (disposed) return;
            // Do not leave an optimistic selection after an uncertain write.
            UpdateNoiseMode(null);
            ReportStatus("模式切换未确认，请刷新核对。" + Friendly(error));
            RuntimeLog.Write("Noise control failed: " + error);
        }
        finally { if (!disposed) SetBusy(false); }
    }

    public async Task SetNoiseLevelAsync(byte level)
    {
        if (!CanChangeNoiseLevel || !NoiseControlProtocol.SupportsLevel(profile, level) || noiseLevel == level) return;
        SetBusy(true);
        try
        {
            var result = await source.SetNoiseLevelAsync(level, cancellation);
            if (disposed) return;
            UpdateNoiseMode(result.Mode, level: result.Level);
            ReportStatus(result.Confirmed ? "" : "耳机尚未确认目标降噪强度，请刷新核对。");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (disposed) return;
            UpdateNoiseMode(null);
            ReportStatus("降噪强度切换未确认，请刷新核对。" + Friendly(error));
            RuntimeLog.Write("Noise level failed: " + error);
        }
        finally { if (!disposed) SetBusy(false); }
    }

    public async Task ConnectToHeadsetAsync()
    {
        if (!CanConnectToHeadset) return;
        ++connectionRevision;
        SetBusy(true);
        try
        {
            var result = await source.ConnectToHeadsetAsync(cancellation);
            if (disposed) return;
            if (result.Snapshot != null) ApplySnapshot(result.Snapshot);
            paused = result.PauseAutomaticRead;
            if (paused) MarkOffline();
            ReportStatus(result.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!disposed)
            {
                MarkOffline();
                ReportStatus("暂时无法连接到耳机。" + Friendly(error));
                RuntimeLog.Write("Connect-to-headset failed: " + error);
            }
        }
        finally { if (!disposed) SetBusy(false); }
    }

    public async Task ToggleAsync(DeviceInfo device)
    {
        if (!CanInteract || device == null || !Devices.Contains(device) || !device.ConnectionKnown) return;
        bool connecting = !device.Connected;
        bool needsFollowUp = connecting && !device.IsLocalComputer;
        bool scheduleFollowUp = false;
        int revision = ++connectionRevision;
        long started = Stopwatch.GetTimestamp();
        SetBusy(true);
        try
        {
            var result = await source.SetConnectedAsync(device.Id, connecting, cancellation);
            if (disposed) return;
            if (result.Snapshot != null) ApplySnapshot(result.Snapshot);
            if (result.PauseAutomaticRead) { paused = true; MarkOffline(); }
            scheduleFollowUp = needsFollowUp;
            ReportStatus(needsFollowUp && !result.Confirmed
                ? "连接结果待确认，稍后将自动刷新设备状态。" : result.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (InvalidOperationException error) { if (!disposed) ReportStatus(error.Message); }
        catch (Exception error)
        {
            if (!disposed)
            {
                paused = true;
                MarkOffline();
                scheduleFollowUp = needsFollowUp;
                ReportStatus((needsFollowUp ? "连接结果待确认，稍后将自动刷新设备状态。"
                    : "操作未确认，自动读取已暂停。请手动刷新检查实际状态。") + Friendly(error));
                RuntimeLog.Write("Bluetooth command failed: " + error);
            }
        }
        finally
        {
            if (!disposed)
            {
                SetBusy(false);
                if (scheduleFollowUp) _ = RefreshAfterConnectionAsync(revision, started);
            }
        }
    }

    public async Task SetConnectionLockedAsync(DeviceInfo device, bool locked)
    {
        if (!CanInteract || device == null || !Devices.Contains(device) || !device.CanChangeConnectionLock) return;
        ++connectionRevision;
        SetBusy(true);
        try
        {
            var result = await source.SetConnectionLockedAsync(device.Id, locked, cancellation);
            if (disposed) return;
            if (result.Snapshot != null) ApplySnapshot(result.Snapshot);
            if (result.PauseAutomaticRead) { paused = true; MarkOffline(); }
            // A confirmed result is already represented by the lock icon/status.
            // Preserve snapshot warnings, but don't add a redundant success banner.
            if (!result.Confirmed) ReportStatus(result.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (InvalidOperationException error) { if (!disposed) ReportStatus(error.Message); }
        catch (Exception error)
        {
            if (!disposed)
            {
                MarkOffline();
                ReportStatus("锁定操作未确认，请刷新检查实际状态。" + Friendly(error));
                RuntimeLog.Write("Bluetooth connection-lock command failed: " + error);
            }
        }
        finally
        {
            if (!disposed)
            {
                SetBusy(false);
            }
        }
    }

    private async Task RefreshAfterConnectionAsync(int revision, long started, TimeSpan? delay = null)
    {
        try
        {
            // Count from the click, not from completion of the immediate readback.
            var remaining = (delay ?? TimeSpan.FromSeconds(6)) - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellation);
            // A newer connection/disconnection supersedes this confirmation.
            if (disposed || revision != connectionRevision) return;
            RuntimeLog.Write("Refreshing device status after explicit device operation.");
            await RefreshConnectedAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private static string Friendly(Exception error) => error switch
    {
        TimeoutException => "耳机响应超时，请取出耳机并检查蓝牙连接。",
        SocketException socket => "蓝牙控制通道不可用，请检查耳机是否开机、已配对，以及其他控制软件是否占用。错误 " + socket.ErrorCode + "。",
        _ => error.Message
    };
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        source.Dispose();
        lifetime.Dispose();
    }
}
