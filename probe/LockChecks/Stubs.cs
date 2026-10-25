using System.Text;
using FreeClipControl.Shell;

namespace Microsoft.UI.Xaml { public enum Visibility { Visible, Collapsed } }
namespace FreeClipControl.Shell
{
    public enum ProductIconKind { Unknown, Computer, Phone, Tablet }
    internal sealed record DeviceTypeHint(ProductIconKind Kind, string Description);
    internal static class DeviceTypeResolver
    {
        internal static ProductIconKind? GetOverride(string id) => null;
        internal static void SetOverride(string id, ProductIconKind kind) { }
        internal static string Label(ProductIconKind kind) => kind.ToString();
        internal static DeviceTypeHint Detect(string name, bool local, uint? type) => new(local ? ProductIconKind.Computer : ProductIconKind.Phone, "test");
    }
    internal static class RuntimeLog { internal static void Write(string value) { } }
    internal static class AudioEndpointDiagnostics
    {
        internal static readonly Queue<int?> Counts = new();
        internal static int? CountMatchingRender(string name) => Counts.Count>0 ? Counts.Dequeue() : 1;
        internal static Task LogAfterDelayAsync(string name,CancellationToken token) => Task.CompletedTask;
    }
    internal static class HeadsetAudioOutput
    {
        internal static int Requests;
        internal static bool Confirm = true, Fail;
        internal static Task RequestAsync(ulong address, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests++;
            if (Fail) throw new InvalidOperationException("simulated output unavailable");
            if (Confirm) { FakeRadio.ComputerState = 9; FakeRadio.PhoneState = 1; }
            return Task.CompletedTask;
        }
    }
    internal sealed record PairedBluetoothDevice(ulong Address, string Name, bool Connected, uint DeviceClass, bool IsPaired);
    internal static class BluetoothDiscovery
    {
        internal const ulong Headset = 0xAABBCCDDEEFF, Computer = 0x112233445566, Phone = 0x778899001122;
        internal static List<PairedBluetoothDevice> GetKnownDevices() => FakeRadio.Devices ?? new() { new(Headset,"HUAWEI FreeClip 2",FakeRadio.SystemConnected,0,true) };
        internal static HashSet<ulong> GetLocalAddresses() => new() { Computer };
        internal static bool IsConnected(ulong address) => FakeRadio.SystemConnected;
        internal static string FormatAddress(ulong address) => string.Join(":",Convert.ToHexString(BitConverter.GetBytes(address).Take(6).Reverse().ToArray()).Chunk(2).Select(c=>new string(c)));
    }
    internal static class WindowsBluetoothAvailability
    {
        internal static BluetoothAvailability RadioState = BluetoothAvailability.Unknown;
        internal static Task<BluetoothAvailability> ReadRadioAsync(CancellationToken token) => Task.FromResult(RadioState);
    }
    internal static class FakeRadio
    {
        internal static bool SystemConnected = true, ComputerConnected = true, Echo = true, ThrowAfterWrite;
        internal static byte[] LockState = new byte[] { 0 };
        internal static byte[] PhoneLockState = new byte[] { 0 };
        internal static bool KeepOtherLock;
        internal static byte? ComputerState, StateAfterWrite, PhoneState;
        internal static byte[] Battery = new byte[] { 80, 80, 90 };
        internal static bool CompleteList = true;
        internal static int Enumerations;
        internal static int Connections, AckReads;
        internal static int LastChannel;
        internal static ulong LastAddress;
        internal static List<PairedBluetoothDevice> Devices;
        internal static readonly List<ushort> Queries = new();
        internal static byte? AckStatus=0;
        internal static bool AwaitingAck, QueriedBeforeAck;
        internal static readonly List<byte[]> Mutations = new();
        internal static void Reset() { Devices=null;Queries.Clear();LastChannel=0;LastAddress=0;SystemConnected=ComputerConnected=Echo=CompleteList=true;ThrowAfterWrite=false;LockState=new byte[]{0};PhoneLockState=new byte[]{0};KeepOtherLock=false;ComputerState=StateAfterWrite=PhoneState=null;Battery=new byte[]{80,80,90};Enumerations=Connections=AckReads=0;AckStatus=0;AwaitingAck=QueriedBeforeAck=false;AudioEndpointDiagnostics.Counts.Clear();Mutations.Clear();HeadsetAudioOutput.Requests=0;HeadsetAudioOutput.Confirm=true;HeadsetAudioOutput.Fail=false; }
    }
    internal sealed class RfcommConnection : IDisposable
    {
        private readonly Queue<byte[]> replies = new();
        public RfcommConnection(ulong address,CancellationToken token,int channel=1) { FakeRadio.Connections++;FakeRadio.LastChannel=channel;FakeRadio.LastAddress=address; }
        public void Send(byte[] packet,CancellationToken token)
        {
            var decoder=new HuaweiFrameDecoder();decoder.Append(packet);if(!decoder.TryRead(out var frame)) throw new Exception("invalid request");
            FakeRadio.Queries.Add(frame.Command);
            if(frame.Command!=0x2B33 && FakeRadio.AwaitingAck) FakeRadio.QueriedBeforeAck=true;
            switch(frame.Command)
            {
                case 0x0108: replies.Enqueue(HuaweiFrameDecoder.Build(0x0108,(1,new byte[]{80}),(2,FakeRadio.Battery)));break;
                case 0x2B2F: replies.Enqueue(HuaweiFrameDecoder.Build(0x2B2F,(1,new byte[]{1})));break;
                case 0x2B31:
                    FakeRadio.Enumerations++;
                    replies.Enqueue(Row(0,BluetoothDiscovery.Computer,"DESKTOP-TEST",FakeRadio.ComputerConnected,FakeRadio.LockState));
                    replies.Enqueue(Row(1,BluetoothDiscovery.Phone,"Phone",true,FakeRadio.PhoneLockState));break;
                case 0x2B33:
                    FakeRadio.Mutations.Add(packet);
                    if(frame.Parameters.Count!=1) throw new Exception("unexpected extra mutation");
                    byte operation=frame.Parameters.Keys.Single();
                    if(operation==1) { FakeRadio.ComputerConnected=true;FakeRadio.ComputerState=1;break; }
                    if(operation!=8 && operation!=9) throw new Exception("unexpected command");
                    FakeRadio.AwaitingAck=true;
                    if(FakeRadio.Echo && FakeRadio.AckStatus==0)
                    {
                        var address=frame.Get(operation);
                        if(address.SequenceEqual(BitConverter.GetBytes(BluetoothDiscovery.Computer).Take(6)))
                        {
                            FakeRadio.LockState=new byte[]{(byte)(operation==8?1:0)};
                            if(operation==8 && !FakeRadio.KeepOtherLock) FakeRadio.PhoneLockState=new byte[]{0};
                        }
                        else if(address.SequenceEqual(BitConverter.GetBytes(BluetoothDiscovery.Phone).Take(6)))
                        {
                            FakeRadio.PhoneLockState=new byte[]{(byte)(operation==8?1:0)};
                            if(operation==8 && !FakeRadio.KeepOtherLock) FakeRadio.LockState=new byte[]{0};
                        }
                        else throw new Exception("unexpected lock target address");
                    }
                    if(FakeRadio.StateAfterWrite.HasValue) FakeRadio.ComputerState=FakeRadio.StateAfterWrite;
                    if(FakeRadio.AckStatus.HasValue) replies.Enqueue(HuaweiFrameDecoder.Build(0x2B33,(30,new byte[]{operation}),(31,new byte[]{FakeRadio.AckStatus.Value})));break;
                default: throw new Exception("unexpected command");
            }
        }
        private static byte[] Row(byte slot,ulong address,string name,bool connected,byte[] lockState)
        {
            byte state=address==BluetoothDiscovery.Computer && FakeRadio.ComputerState.HasValue ? FakeRadio.ComputerState.Value : (byte)(connected?1:0);
            if(address==BluetoothDiscovery.Phone && FakeRadio.PhoneState.HasValue) state=FakeRadio.PhoneState.Value;
            var fields=new List<(byte,byte[])> { (2,new byte[]{(byte)(FakeRadio.CompleteList?2:3)}),(3,new[]{slot}),(4,BitConverter.GetBytes(address).Take(6).ToArray()),(5,new[]{state}),(9,Encoding.UTF8.GetBytes(name)) };
            if(lockState!=null) fields.Add((13,lockState));
            return HuaweiFrameDecoder.Build(0x2B31,fields.ToArray());
        }
        public int Receive(byte[] buffer,int milliseconds,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(FakeRadio.ThrowAfterWrite && FakeRadio.Mutations.Count>0) throw new IOException("simulated link loss");
            if(replies.Count==0) { Thread.Sleep(Math.Min(milliseconds,2));return 0; }
            byte[] reply=replies.Dequeue();
            if(reply[4]==0x2B && reply[5]==0x33) {FakeRadio.AwaitingAck=false;FakeRadio.AckReads++;}
            reply.CopyTo(buffer,0);return reply.Length;
        }
        public void Dispose() { }
    }
}
