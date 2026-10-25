using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FreeClipControl.Shell;

public sealed record HeadsetProfile(string Id, string Name, int Channel, bool SplitBattery, bool DeviceManagement,
    bool ConnectionLock = false, int ConnectDelayMilliseconds = 0)
{
    public string[] Keywords { get; init; } = Array.Empty<string>();
    public string Description => (SplitBattery ? "分项电量" : "整体电量")
        + (DeviceManagement ? " · 设备连接管理" : " · 不提供设备连接管理")
        + (ConnectionLock ? " · 本次连接锁定" : "")
        + (ConnectionLock ? " · 已有 FreeClip 2 实机记录" : " · 适配待实机验证");
}

// Transport and supported-handler facts checked against OpenFreebuds per_model
// drivers. See docs/MODEL_SUPPORT.md for upstream references and scope.
internal static class HeadsetProfiles
{
    internal static readonly HeadsetProfile[] BuiltIn = {
        new("freeclip2", "HUAWEI FreeClip 2", 1, true, true, true),
        new("freeclip", "HUAWEI FreeClip", 1, true, true),
        new("buds3", "HUAWEI FreeBuds 3", 1, true, false),
        new("buds4i", "HUAWEI FreeBuds 4i", 16, true, false),
        new("buds5i", "HUAWEI FreeBuds 5i", 16, true, true),
        new("buds6i", "HUAWEI FreeBuds 6i", 1, true, true),
        new("buds7i", "HUAWEI FreeBuds 7i", 1, true, true),
        new("pro", "HUAWEI FreeBuds Pro", 1, true, true),
        new("pro2", "HUAWEI FreeBuds Pro 2", 1, true, true),
        new("pro3", "HUAWEI FreeBuds Pro 3", 1, true, true),
        new("pro4", "HUAWEI FreeBuds Pro 4", 1, true, true),
        new("pro5", "HUAWEI FreeBuds Pro 5", 1, true, true),
        new("se", "HUAWEI FreeBuds SE", 16, true, false),
        new("se2", "HUAWEI FreeBuds SE 2", 1, true, false),
        new("se4anc", "HUAWEI FreeBuds SE 4 ANC", 1, true, false),
        new("studio", "HUAWEI FreeBuds Studio", 1, false, false),
        new("lacepro", "HUAWEI FreeLace Pro", 16, false, false, false, 2000),
        new("lacepro2", "HUAWEI FreeLace Pro 2", 1, false, true),
        new("honor2", "HONOR Earbuds 2", 16, true, false),
        new("honor2se", "HONOR Earbuds 2 SE", 16, true, false),
        new("honor2lite", "HONOR Earbuds 2 Lite", 16, true, false),
    };
    public static HeadsetProfile[] All => HeadsetCatalog.Current.All;
    public static HeadsetProfile Find(string id) => All.FirstOrDefault(p => p.Id == id);
    internal static HeadsetProfile ResolveConfigured(string savedId, string reportedName)
    {
        // Before a 7i profile existed, it was configured using the working 6i
        // transport. Migrate only that legacy choice with an exact 7i name.
        // Other saved mappings and renamed devices retain their normal priority.
        if (savedId == "buds6i" && (Normalize(reportedName) is "HUAWEIFREEBUDS7I" or "FREEBUDS7I"))
            return Find("buds7i");
        return Find(savedId) ?? Detect(reportedName);
    }
    internal static string Normalize(string name) => string.Concat((name ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    public static HeadsetProfile Detect(string name) => Detect(name, All);
    internal static HeadsetProfile Detect(string name, HeadsetProfile[] profiles)
    {
        string value = Normalize(name);
        var exact = profiles.FirstOrDefault(p => Normalize(p.Name) == value);
        if (exact != null) return exact;
        return profiles.SelectMany(p => p.Keywords.Append(p.Name).Append(p.Name.Replace("HUAWEI ", "").Replace("HONOR ", ""))
            .Select(k => (Profile: p, Keyword: Normalize(k))))
            .Where(k => k.Keyword.Length > 0).OrderByDescending(k => k.Keyword.Length)
            .FirstOrDefault(k => KeywordMatches(value, k.Keyword)).Profile;
    }
    private static bool KeywordMatches(string value, string keyword)
    {
        int start = 0;
        while ((start = value.IndexOf(keyword, start, StringComparison.Ordinal)) >= 0)
        {
            int end = start + keyword.Length;
            // A numeric/model suffix is part of the model, not a match for the
            // shorter model: Pro 4 != Pro, SE 2 != SE, FreeClip 2 S != FreeClip 2.
            if (end == value.Length || !(value[end] is >= 'A' and <= 'Z' or >= '0' and <= '9')) return true;
            start++;
        }
        return false;
    }
    public static ulong ParseAddress(string address)
    {
        string value = (address ?? "").Replace(":", "").Replace("-", "");
        if (value.Length != 12 || !ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            || parsed == 0 || parsed == 0xFFFFFFFFFFFF)
            throw new InvalidOperationException("请选择有效的耳机蓝牙地址。");
        return parsed;
    }
}

public sealed record HeadsetChoice(ulong Address, string Name, bool Connected, string ProfileId)
{
    public string DisplayName => $"{Name} · {(Connected ? "已连接" : "已配对")} · {BluetoothDiscovery.FormatAddress(Address)}";
}

public sealed record BoundHeadset(ulong Address, string Name, string ProfileId, bool Connected, bool Paired, bool Selected)
{
    public string AddressText => BluetoothDiscovery.FormatAddress(Address);
    public string ModelName => HeadsetProfiles.Find(ProfileId)?.Name ?? "型号配置不可用";
    public string Status => !Paired ? "尚未与 Windows 配对" : Connected ? "已连接" : "未连接";
    public string Detail => ModelName + " · " + Status + (Selected ? " · 当前设备" : "");
    public bool CanSelect => Paired && HeadsetProfiles.Find(ProfileId) != null;
    public bool CanSwitch => CanSelect && !Selected;
}

internal interface IHeadsetSelection
{
    ulong? SelectedAddress { get; }
    string SelectedProfileId { get; }
    string SelectedName => null;
    bool RequiresSelection { get; }
    Task InitializeSelectionAsync(CancellationToken token) => Task.CompletedTask;
    Task<(bool Changed, BluetoothAvailability Availability)> SelectConnectedHeadsetForMenuAsync(CancellationToken token)
        => Task.FromResult((false, BluetoothAvailability.Unknown));
    Task<HeadsetChoice[]> ListHeadsetsAsync(CancellationToken token);
    Task SelectHeadsetAsync(ulong address, string profileId, CancellationToken token);
    Task<BoundHeadset[]> ListBindingsAsync(CancellationToken token);
    Task RemoveBindingAsync(ulong address, CancellationToken token);
}

internal sealed class HeadsetSelectionStore
{
    public sealed class Settings
    {
        public string SelectedAddress { get; set; }
        public Dictionary<string, string> Profiles { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
        public HashSet<string> RemovedAddresses { get; set; } = new();
        public bool RequiresSelection { get; set; }
    }
    private readonly string path;
    private Settings data = new();
    public HeadsetSelectionStore(string filePath)
    {
        path = filePath;
        try
        {
            if (path != null && File.Exists(path)) data = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
            data.Profiles ??= new();
            data.Names ??= new();
            data.RemovedAddresses = (data.RemovedAddresses ?? new())
                .Select(a => BluetoothDiscovery.FormatAddress(HeadsetProfiles.ParseAddress(a))).ToHashSet();
            // Normalize keys once, including data written with lower-case MACs.
            data.Profiles = data.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.Value))
                .GroupBy(p => BluetoothDiscovery.FormatAddress(HeadsetProfiles.ParseAddress(p.Key)))
                .ToDictionary(g => g.Key, g => g.Last().Value);
            if (data.SelectedAddress != null)
                data.SelectedAddress = BluetoothDiscovery.FormatAddress(HeadsetProfiles.ParseAddress(data.SelectedAddress));
        }
        catch (Exception error)
        {
            // Preserve corrupt data for recovery; never silently overwrite it.
            RuntimeLog.Write("Headset selection could not be read: " + error.Message);
            data = new();
            loadFailed = true;
        }
    }
    private readonly bool loadFailed;
    public static HeadsetSelectionStore Default() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HuaweiHeadsetControl", "headsets.json"));
    public ulong? SelectedAddress => data.SelectedAddress == null ? null : HeadsetProfiles.ParseAddress(data.SelectedAddress);
    public string ProfileFor(ulong address) => data.Profiles.GetValueOrDefault(BluetoothDiscovery.FormatAddress(address));
    public KeyValuePair<string, string>[] Bindings => data.Profiles.ToArray();
    public string NameFor(ulong address) => data.Names.GetValueOrDefault(BluetoothDiscovery.FormatAddress(address));
    public bool RequiresSelection => data.RequiresSelection;
    public bool IsRemoved(ulong address) => data.RemovedAddresses.Contains(BluetoothDiscovery.FormatAddress(address));
    public void Save(ulong address, string profileId, string name = null, bool select = true)
    {
        if (HeadsetProfiles.Find(profileId) == null) throw new InvalidOperationException("请选择支持的耳机型号。");
        string key = BluetoothDiscovery.FormatAddress(HeadsetProfiles.ParseAddress(BluetoothDiscovery.FormatAddress(address)));
        var next = CopySettings();
        if (select) { next.SelectedAddress = key; next.RequiresSelection = false; }
        next.RemovedAddresses.Remove(key);
        next.Profiles[key] = profileId;
        if (!string.IsNullOrWhiteSpace(name)) next.Names[key] = name;
        Commit(next);
    }
    public void Remove(ulong address)
    {
        string key = BluetoothDiscovery.FormatAddress(HeadsetProfiles.ParseAddress(BluetoothDiscovery.FormatAddress(address)));
        if (!data.Profiles.ContainsKey(key)) return;
        var next = CopySettings();
        next.Profiles.Remove(key);
        next.Names.Remove(key);
        next.RemovedAddresses.Add(key);
        if (next.SelectedAddress == key || next.Profiles.Count == 0)
        {
            next.SelectedAddress = null;
            next.RequiresSelection = true;
        }
        Commit(next);
    }
    private Settings CopySettings() => new()
    {
        SelectedAddress = data.SelectedAddress, Profiles = new(data.Profiles), Names = new(data.Names),
        RemovedAddresses = new(data.RemovedAddresses), RequiresSelection = data.RequiresSelection
    };
    private void Commit(Settings next)
    {
        if (path != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (loadFailed && File.Exists(path)) File.Copy(path, path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), false);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        data = next;
    }
}
