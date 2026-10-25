using FreeClipControl.Shell;
using Microsoft.UI.Xaml;

int checks=0;
void Check(bool value,string label) { if(!value) throw new Exception(label);checks++;Console.WriteLine("PASS: "+label); }
HuaweiFrame Frame(byte[] value) => new(0x2B31,value==null?new():new(){{13,value}});
void Invalid(Action action) { try { action();throw new Exception("operation was accepted"); } catch(InvalidOperationException) { checks++; } }
string pc=BluetoothDiscovery.FormatAddress(BluetoothDiscovery.Computer), phone=BluetoothDiscovery.FormatAddress(BluetoothDiscovery.Phone);
byte[] mac=Convert.FromHexString("97258D1760E4");
Check(Convert.ToHexString(HuaweiFrameDecoder.Build(0x2B33,ConnectionLockProtocol.Parameter(true,mac)))=="5A000B002B33080697258D1760E4F32D","lock packet matches observed device trace");
Check(Convert.ToHexString(HuaweiFrameDecoder.Build(0x2B33,ConnectionLockProtocol.Parameter(false,mac)))=="5A000B002B33090697258D1760E4B4FE","unlock packet matches observed device trace");
Check(ConnectionLockProtocol.ReadState(Frame(new byte[]{0}))==false && ConnectionLockProtocol.ReadState(Frame(new byte[]{1}))==true,"read confirmed lock state");
Check(ConnectionLockProtocol.ReadState(Frame(null))==null && ConnectionLockProtocol.ReadState(Frame(new byte[]{2}))==null && ConnectionLockProtocol.ReadState(Frame(new byte[]{0,1}))==null,"missing or unsupported lock field stays unknown");
Invalid(()=>ConnectionLockProtocol.Parameter(true,new byte[]{1}));
using(var source=new HuaweiBluetoothDataSource(0))
{
    FakeRadio.Reset();
    var result=await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(result.Confirmed && result.Snapshot.Devices.Single(d=>d.Id==pc).IsConnectionLocked && FakeRadio.Mutations.Count==1,"one write and actual locked-state readback");
    Check(FakeRadio.AckReads==1 && !FakeRadio.QueriedBeforeAck,"acknowledgement is consumed before post-command queries");
    await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(FakeRadio.Mutations.Count==1,"already locked produces no extra write");
    result=await source.SetConnectionLockedAsync(pc,false,CancellationToken.None);
    Check(result.Confirmed && !result.Snapshot.Devices.Single(d=>d.Id==pc).IsConnectionLocked && FakeRadio.Mutations.Count==2,"unlock confirmed without preferred-device mutation");
    FakeRadio.Reset();FakeRadio.LockState=new byte[]{1};FakeRadio.StateAfterWrite=3;
    result=await source.SetConnectionLockedAsync(pc,false,CancellationToken.None);
    var state3=result.Snapshot.Devices.Single(d=>d.Id==pc);
    Check(result.Confirmed && state3.Connected && state3.ConnectionKnown && state3.Status=="已连接" && FakeRadio.Mutations.Count==1,"unlock state 3 remains connected and confirms without reopening panel");
    FakeRadio.Reset();FakeRadio.ComputerState=4;
    Invalid(()=>source.SetConnectionLockedAsync(pc,true,CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Mutations.Count==0,"unobserved connection values are still blocked");
    FakeRadio.Reset();
    FakeRadio.LockState=new byte[]{1};
    result=await source.SetConnectionLockedAsync(phone,true,CancellationToken.None);
    Check(result.Confirmed && result.Snapshot.Devices.Single(d=>d.Id==phone).IsConnectionLocked
        && !result.Snapshot.Devices.Single(d=>d.Id==pc).IsConnectionLocked && FakeRadio.Mutations.Count==1,
        "one addressed write transfers lock to the connected phone and verifies both rows");
    result=await source.SetConnectionLockedAsync(phone,false,CancellationToken.None);
    Check(result.Confirmed && !result.Snapshot.Devices.Single(d=>d.Id==phone).IsConnectionLocked
        && FakeRadio.Mutations.Count==2,"addressed unlock clears the phone lock without extra writes");
    FakeRadio.Reset();FakeRadio.PhoneLockState=null;
    Invalid(()=>source.SetConnectionLockedAsync(phone,true,CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Mutations.Count==0,"unknown phone lock state blocks writes");
    FakeRadio.Reset();FakeRadio.PhoneState=0;
    Invalid(()=>source.SetConnectionLockedAsync(phone,true,CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Mutations.Count==0,"disconnected phone cannot be locked");
    FakeRadio.Reset();FakeRadio.LockState=new byte[]{1};FakeRadio.KeepOtherLock=true;
    result=await source.SetConnectionLockedAsync(phone,true,CancellationToken.None);
    Check(!result.Confirmed && FakeRadio.Mutations.Count==1,"conflicting peer locks are not confirmed or retried");
    FakeRadio.Reset();
    FakeRadio.LockState=null;
    Invalid(()=>source.SetConnectionLockedAsync(pc,true,CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Mutations.Count==0,"unknown state blocks mutation");
    FakeRadio.Reset();FakeRadio.ComputerConnected=false;
    Invalid(()=>source.SetConnectionLockedAsync(pc,true,CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Mutations.Count==0,"disconnected target blocks mutation");
    FakeRadio.Reset();FakeRadio.SystemConnected=false;
    Invalid(()=>source.SetConnectionLockedAsync(pc,true,CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Mutations.Count==0,"system-disconnected earbud does not reconnect for lock");
    FakeRadio.Reset();FakeRadio.Echo=false;
    result=await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(!result.Confirmed && !result.Snapshot.Devices.Single(d=>d.Id==pc).IsConnectionLocked && FakeRadio.Mutations.Count==1,"reply alone does not fake success or retry write");
    FakeRadio.Reset();FakeRadio.ThrowAfterWrite=true;
    result=await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(!result.Confirmed && result.PauseAutomaticRead && result.Snapshot==null && FakeRadio.Mutations.Count==1,"link loss pauses reads without retrying mutation");
    FakeRadio.Reset();FakeRadio.AckStatus=null;
    result=await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(!result.Confirmed && result.PauseAutomaticRead && result.Snapshot==null && FakeRadio.Mutations.Count==1 && !FakeRadio.QueriedBeforeAck,"missing acknowledgement stops queries and does not resend or pretend success");
    FakeRadio.Reset();FakeRadio.AckStatus=2;
    result=await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(!result.Confirmed && !result.Snapshot.Devices.Single(d=>d.Id==pc).IsConnectionLocked && FakeRadio.Mutations.Count==1,"rejected command is not treated as accepted");
    FakeRadio.Reset();AudioEndpointDiagnostics.Counts.Enqueue(1);AudioEndpointDiagnostics.Counts.Enqueue(0);
    result=await source.SetConnectionLockedAsync(pc,true,CancellationToken.None);
    Check(!result.Confirmed && result.PauseAutomaticRead && result.Message.Contains("音频输出"),"audio endpoint loss is not represented as normal lock success");
}
FakeRadio.Reset();
using(var vm=new PanelViewModel(new HuaweiBluetoothDataSource(0)))
{
    await vm.RefreshAsync();var local=vm.Devices.Single(d=>d.Id==pc);var other=vm.Devices.Single(d=>d.Id==phone);
    Check(local.LockButtonVisibility==Visibility.Collapsed,"lock button hidden on collapsed card");
    vm.SelectDevice(local);
    Check(local.LockButtonVisibility==Visibility.Visible && local.CanChangeConnectionLock && other.LockButtonVisibility==Visibility.Collapsed,"only the expanded device shows its lock action");
    vm.SelectDevice(other);
    Check(other.LockButtonVisibility==Visibility.Visible && other.CanChangeConnectionLock
        && local.LockButtonVisibility==Visibility.Collapsed,"connected phone exposes the same guarded action when expanded");
    vm.SelectDevice(local);
    await vm.SetConnectionLockedAsync(local,true);
    Check(local.IsConnectionLocked && local.Status.Contains("已锁定") && local.LockActionLabel.Contains("解除"),"confirmed state updates existing bound row and action label");
    Check(vm.Message=="" && vm.StatusVisibility==Visibility.Collapsed,"successful lock adds no footer banner");
    FakeRadio.StateAfterWrite=3;
    await vm.SetConnectionLockedAsync(local,false);
    Check(local.ConnectionState==3 && local.Status=="已连接" && local.CanChangeConnectionLock && vm.ConnectionSummary=="2 / 2 已连接","unlock state 3 keeps row active and summary known");
    Check(vm.Message=="" && vm.StatusVisibility==Visibility.Collapsed,"successful unlock adds no footer banner");
    int reads=FakeRadio.Enumerations, writes=FakeRadio.Mutations.Count, sessions=FakeRadio.Connections;
    FakeRadio.ComputerState=1;
    await Task.Delay(2300);
    Check(FakeRadio.Enumerations==reads && FakeRadio.Connections==sessions && FakeRadio.Mutations.Count==writes,"lock completion does not automatically reopen SPP two seconds later");
    FakeRadio.LockState=null;await vm.RefreshAsync();
    Check(!local.CanChangeConnectionLock && !local.IsConnectionLocked,"unknown readback disables action without pretending unlocked capability");
    FakeRadio.SystemConnected=false;await vm.RefreshConnectedAsync();
    Check(!local.CanChangeConnectionLock && local.LockButtonVisibility==Visibility.Collapsed,"stale device hides actionable lock");
}
FakeRadio.Reset();
using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0)))
{
    Check(vm.IsConnectionNoticeVisible && FakeRadio.Connections == 0, "initial connection notice does not contact headset on startup");
    WindowsBluetoothAvailability.RadioState = BluetoothAvailability.RadioOff;
    await vm.RefreshConnectedAsync();
    Check(vm.ConnectionNoticeTitle == "蓝牙已关闭" && vm.ConnectedPageVisibility == Visibility.Collapsed && FakeRadio.Connections == 0,
        "radio off shows notice without opening RFCOMM");
    WindowsBluetoothAvailability.RadioState = BluetoothAvailability.Unknown;
    FakeRadio.SystemConnected = false;
    await vm.RefreshConnectedAsync();
    Check(vm.ConnectionNoticeTitle == "耳机未连接" && vm.BluetoothFooterLabel == "更多蓝牙设置" && FakeRadio.Connections == 0,
        "disconnected headset is distinguished from radio off and does not reconnect");
    FakeRadio.SystemConnected = true;
    await vm.RefreshConnectedAsync();
    Check(!vm.IsConnectionNoticeVisible && vm.ConnectedPageVisibility == Visibility.Visible && vm.Devices.Count == 2,
        "a connected headset restores the existing control page");
    int reads = FakeRadio.Connections;
    WindowsBluetoothAvailability.RadioState = BluetoothAvailability.RadioOff;
    await vm.RefreshConnectedAsync();
    Check(vm.IsConnectionNoticeVisible && vm.ConnectedPageVisibility == Visibility.Collapsed && FakeRadio.Connections == reads && !vm.CanInteract,
        "disconnect after successful data hides cached controls and does not contact headset");
    WindowsBluetoothAvailability.RadioState = BluetoothAvailability.Unavailable;
    await vm.RefreshConnectedAsync();
    Check(vm.ConnectionNoticeTitle == "蓝牙不可用" && FakeRadio.Mutations.Count == 0,
        "missing radio is distinct and no settings or lock commands are written");
}
WindowsBluetoothAvailability.RadioState = BluetoothAvailability.Unknown;
FakeRadio.Reset();
using (var source = new HuaweiBluetoothDataSource(0))
{
    var result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(result.Confirmed && HeadsetAudioOutput.Requests == 0 && FakeRadio.Mutations.Count == 0,
        "idle connected headset never requests audio output or sends a redundant connect");
    FakeRadio.ComputerState = 9; FakeRadio.PhoneState = 9;
    await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(HeadsetAudioOutput.Requests == 0, "local playback never requests audio output even with remote playback");
    FakeRadio.ComputerState = 1; FakeRadio.PhoneState = 3;
    await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(HeadsetAudioOutput.Requests == 0, "connected state 3 is not misread as remote audio occupancy");
    FakeRadio.PhoneState = 4;
    result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(!result.Confirmed && HeadsetAudioOutput.Requests == 0, "unknown device state blocks audio requests");
    FakeRadio.PhoneState = 9; FakeRadio.CompleteList = false;
    result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(!result.Confirmed && HeadsetAudioOutput.Requests == 0, "incomplete device list blocks audio requests");
    FakeRadio.CompleteList = true;
    result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(result.Confirmed && HeadsetAudioOutput.Requests == 1 && FakeRadio.Mutations.Count == 0,
        "confirmed remote playback requests exactly one output and verifies local playback without disconnecting peers");
    FakeRadio.Reset(); FakeRadio.ComputerConnected = false;
    result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(result.Confirmed && FakeRadio.Mutations.Count == 1 && HeadsetAudioOutput.Requests == 0,
        "idle explicit connection sends only connect and never requests output");
    FakeRadio.Reset(); FakeRadio.PhoneState = 9; HeadsetAudioOutput.Confirm = false;
    result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(!result.Confirmed && result.Message.Contains("已请求") && HeadsetAudioOutput.Requests == 1,
        "request without playback readback is not represented as a confirmed transfer or retried");
    FakeRadio.Reset(); FakeRadio.ComputerConnected = false; FakeRadio.PhoneState = 9;
    result = await source.ConnectToHeadsetAsync(CancellationToken.None);
    Check(result.Confirmed && FakeRadio.Mutations.Count == 1 && HeadsetAudioOutput.Requests == 1,
        "explicit connect confirms local connection before conditionally requesting output");
    FakeRadio.Reset(); WindowsBluetoothAvailability.RadioState = BluetoothAvailability.RadioOff;
    Invalid(() => source.ConnectToHeadsetAsync(CancellationToken.None).GetAwaiter().GetResult());
    Check(FakeRadio.Connections == 0 && HeadsetAudioOutput.Requests == 0, "radio off does not attempt connection or audio");
    WindowsBluetoothAvailability.RadioState = BluetoothAvailability.Unknown;
}
FakeRadio.Reset();
using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0)))
{
    Check(vm.TrayToolTip == "FreeClip2（--%）", "initial tooltip shows unknown instead of invented battery");
    FakeRadio.Battery = new byte[] { 72, 41, 10 };
    await vm.RefreshConnectedAsync();
    Check(vm.TrayToolTip == "FreeClip2（41%）", "tooltip uses lower earbud and excludes case battery");
    FakeRadio.Battery = new byte[] { 0, 65, 90 };
    await vm.RefreshConnectedAsync();
    Check(vm.TrayToolTip == "FreeClip2（0%）", "real zero-percent earbud is retained");
    FakeRadio.Battery = new byte[] { 70, 255, 90 };
    await vm.RefreshConnectedAsync();
    Check(vm.TrayToolTip == "FreeClip2（--%）", "missing earbud does not substitute global or case battery");
    FakeRadio.Battery = new byte[] { 72, 41, 10 };
    await vm.RefreshConnectedAsync();
    FakeRadio.SystemConnected = false;
    await vm.RefreshConnectedAsync();
    Check(vm.TrayToolTip == "FreeClip2（--%）", "offline tooltip clears old battery");
    FakeRadio.Reset(); FakeRadio.PhoneState = 9; HeadsetAudioOutput.Fail = true;
    await vm.ConnectToHeadsetAsync();
    Check(vm.CanConnectToHeadset && vm.Message.Contains("暂时无法连接") && HeadsetAudioOutput.Requests == 1,
        "failed audio request reports error and restores command availability without retry");
}
FakeRadio.Reset();
using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0)))
{
    int tooltipNotifications = 0;
    vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(PanelViewModel.TrayToolTip)) tooltipNotifications++; };
    vm.SetTrayBatteryEnabled(false);
    await vm.RefreshTrayBatteryAsync();
    await vm.RefreshTrayBatteryAsync();
    Check(vm.TrayToolTip == "FreeClip2" && FakeRadio.Connections == 0 && FakeRadio.Queries.Count == 0,
        "disabled tray battery skips startup and repeated background reads entirely");
    vm.SetTrayBatteryEnabled(true);
    await vm.RefreshTrayBatteryAsync();
    Check(FakeRadio.Connections == 1 && vm.TrayToolTip == "FreeClip2（80%）",
        "enabling tray battery resumes passive reads and updates tooltip");
    Check(FakeRadio.Queries.SequenceEqual(new ushort[] { 0x0108 }) && FakeRadio.Mutations.Count == 0,
        "idle tray reads only battery, without peer-table queries or writes");
    Check(!vm.CanInteract && vm.Devices.Count == 0, "battery-only results do not claim a complete peer table");
    await vm.RefreshIfDueAsync();
    Check(FakeRadio.Queries.Contains(0x2B31) && vm.Devices.Count > 0,
        "opening a data page after a battery read still performs a full refresh");
    vm.SetTrayBatteryEnabled(false);
    int connections = FakeRadio.Connections;
    await vm.RefreshTrayBatteryAsync();
    Check(vm.TrayToolTip == "FreeClip2" && FakeRadio.Connections == connections && tooltipNotifications >= 3,
        "disabling immediately removes cached percentage and notifies tray without a new query");
    FakeRadio.Battery = new byte[] { 72, 41, 90 };
    await vm.RefreshConnectedAsync();
    Check(vm.LeftBattery.Level == 72 && vm.RightBattery.Level == 41 && vm.TrayToolTip == "FreeClip2",
        "explicit panel refresh still reads battery while tray stays name only");
    vm.SetTrayBatteryEnabled(true);
    Check(vm.TrayToolTip == "FreeClip2（41%）", "reenabling immediately restores the current battery tooltip");
}
await ProfileChecks.Run(Check, Invalid);
await BindingChecks.Run(Check);
await CatalogChecks.Run(Check);
Console.WriteLine($"{checks} checks passed; fake Bluetooth only, no device commands sent.");
