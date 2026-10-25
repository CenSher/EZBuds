param([Parameter(Mandatory = $true)][string]$OutputFile)
$ErrorActionPreference = 'Stop'
# All COM calls below are getters. No audio stream or test sound is created.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace FreeClipReadOnlyAudio {
 [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class EnumeratorClass {}
 [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Enumerator {
  [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out Collection devices);
  [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out Device device);
  [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out Device device);
 }
 [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Collection {
  [PreserveSig] int GetCount(out uint count);
  [PreserveSig] int Item(uint index, out Device device);
 }
 [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Device {
  [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
  [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
  [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
  [PreserveSig] int GetState(out uint state);
 }
 [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Meter {
  [PreserveSig] int GetPeakValue(out float peak);
  [PreserveSig] int GetMeteringChannelCount(out uint count);
  [PreserveSig] int GetChannelsPeakValues(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0)] float[] values);
  [PreserveSig] int QueryHardwareSupport(out uint support);
 }
 [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Volume {
  [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
  [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
  [PreserveSig] int GetChannelCount(out uint count);
  [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context);
  [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
  [PreserveSig] int GetMasterVolumeLevel(out float value);
  [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
  [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, ref Guid context);
  [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
  [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
  [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
  [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
  [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
 }
 [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Client {
  [PreserveSig] int Initialize(int mode, uint flags, long buffer, long period, IntPtr format, IntPtr session);
  [PreserveSig] int GetBufferSize(out uint frames);
  [PreserveSig] int GetStreamLatency(out long latency);
  [PreserveSig] int GetCurrentPadding(out uint frames);
  [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
  [PreserveSig] int GetMixFormat(out IntPtr format);
  [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
 }
 public class Endpoint {
  public string Id; public uint State; public float? Volume; public bool? Muted;
  public float[] Peaks; public string Error; public string FormatError; public int? MixRate; public int? MixChannels;
 }
 public static class Reader {
  static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
  public static string DefaultId(int role) {
   var e=(Enumerator)new EnumeratorClass(); Device d=null;
   try { Check(e.GetDefaultAudioEndpoint(0,role,out d)); string id; Check(d.GetId(out id)); return id; }
   catch(Exception x) { return "unavailable: "+x.Message; }
   finally { if(d!=null) Marshal.ReleaseComObject(d); Marshal.ReleaseComObject(e); }
  }
  public static Endpoint[] Capture() {
   var e=(Enumerator)new EnumeratorClass(); Collection c=null;
   try {
    Check(e.EnumAudioEndpoints(0,15,out c)); uint count; Check(c.GetCount(out count));
    var rows=new Endpoint[count];
    for(uint i=0;i<count;i++) {
     Device d=null; object v=null,m=null,client=null; var row=new Endpoint(); rows[i]=row;
     try {
      Check(c.Item(i,out d)); Check(d.GetId(out row.Id)); Check(d.GetState(out row.State));
      if(row.State!=1) continue;
      var vi=new Guid("5CDF2C82-841E-4546-9722-0CF74078229A"); Check(d.Activate(ref vi,23,IntPtr.Zero,out v));
      float volume; bool mute; Check(((Volume)v).GetMasterVolumeLevelScalar(out volume)); Check(((Volume)v).GetMute(out mute));
      row.Volume=volume; row.Muted=mute;
      // Query engine metadata without Initialize, Start, or rendering a stream.
      try {
       var ci=new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"); Check(d.Activate(ref ci,23,IntPtr.Zero,out client));
       IntPtr format; Check(((Client)client).GetMixFormat(out format));
       try { row.MixChannels=(ushort)Marshal.ReadInt16(format,2); row.MixRate=Marshal.ReadInt32(format,4); }
       finally { Marshal.FreeCoTaskMem(format); }
      } catch(Exception x) { row.FormatError="0x"+x.HResult.ToString("X8")+" "+x.Message; }
      var mi=new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"); Check(d.Activate(ref mi,23,IntPtr.Zero,out m));
      row.Peaks=new float[10];
      for(int n=0;n<10;n++) { Check(((Meter)m).GetPeakValue(out row.Peaks[n])); System.Threading.Thread.Sleep(100); }
     } catch(Exception x) { row.Error=x.Message; }
     finally { if(client!=null) Marshal.ReleaseComObject(client); if(m!=null) Marshal.ReleaseComObject(m); if(v!=null) Marshal.ReleaseComObject(v); if(d!=null) Marshal.ReleaseComObject(d); }
    }
    return rows;
   } finally { if(c!=null) Marshal.ReleaseComObject(c); Marshal.ReleaseComObject(e); }
  }
 }
}
'@
[ordered]@{
    Time = Get-Date -Format o
    ConsoleDefault = [FreeClipReadOnlyAudio.Reader]::DefaultId(0)
    MultimediaDefault = [FreeClipReadOnlyAudio.Reader]::DefaultId(1)
    CommunicationsDefault = [FreeClipReadOnlyAudio.Reader]::DefaultId(2)
    RenderEndpoints = [FreeClipReadOnlyAudio.Reader]::Capture()
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputFile -Encoding UTF8
Get-Content -LiteralPath $OutputFile
