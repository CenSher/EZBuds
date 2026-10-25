using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeClipControl.Shell;

public enum ProductIconKind { Unknown, Computer, Phone, Tablet, LeftEar, RightEar, ChargingCase }
internal sealed record DeviceTypeHint(ProductIconKind Kind, string Description);

internal static class DeviceTypeResolver
{
    private static readonly object sync = new();
    private static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeClipControl", "device-types.json");
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static Dictionary<string, ProductIconKind> overrides;

    private static void Load()
    {
        if (overrides != null) return;
        try { overrides = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, ProductIconKind>>(File.ReadAllText(path), json) : null; }
        catch (Exception error) { RuntimeLog.Write("Device type preferences could not be read: " + error.Message); }
        overrides ??= new(StringComparer.OrdinalIgnoreCase);
    }
    public static ProductIconKind? GetOverride(string address)
    {
        lock (sync)
        {
            Load();
            return address != null && overrides.TryGetValue(address, out var kind)
                && kind is ProductIconKind.Computer or ProductIconKind.Phone or ProductIconKind.Tablet ? kind : null;
        }
    }
    public static void SetOverride(string address, ProductIconKind kind)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        lock (sync)
        {
            Load();
            var next = new Dictionary<string, ProductIconKind>(overrides, StringComparer.OrdinalIgnoreCase);
            if (kind == ProductIconKind.Unknown) next.Remove(address); else next[address] = kind;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(next, json));
            File.Move(path + ".tmp", path, true);
            overrides = next;
        }
    }

    public static DeviceTypeHint Detect(string name, bool local, uint? deviceClass)
    {
        if (local) return new(ProductIconKind.Computer, "电脑：与本机蓝牙适配器地址一致");
        if (deviceClass.HasValue)
        {
            uint major = (deviceClass.Value >> 8) & 31, minor = (deviceClass.Value >> 2) & 63;
            if (major == 1) return minor == 7 ? new(ProductIconKind.Tablet, "平板：Windows 蓝牙设备类别")
                : new(ProductIconKind.Computer, "电脑：Windows 蓝牙设备类别");
            if (major == 2 && minor is 0 or 1 or 3) return new(ProductIconKind.Phone, "手机：Windows 蓝牙设备类别");
        }
        string value = (name ?? "").ToLowerInvariant();
        if (value.Contains("matepad") || value.Contains("ipad") || value.Contains("tablet") || value.Contains("平板"))
            return new(ProductIconKind.Tablet, "根据名称推测为平板；可右键更正");
        if (value.Contains("iphone") || value.Contains("phone") || value.Contains("手机") || value.Contains("pixel"))
            return new(ProductIconKind.Phone, "根据名称推测为手机；可右键更正");
        if (value.StartsWith("desktop-") || value.StartsWith("laptop-") || value.Contains("macbook") || value.Contains("电脑"))
            return new(ProductIconKind.Computer, "根据名称推测为电脑；可右键更正");
        return new(ProductIconKind.Unknown, "无法可靠识别设备类型；右键可指定电脑、手机或平板");
    }
    public static string Label(ProductIconKind kind) => kind switch
    {
        ProductIconKind.Computer => "电脑", ProductIconKind.Phone => "手机", ProductIconKind.Tablet => "平板", _ => "未知设备"
    };
}
