using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace FreeClipControl.Shell;

internal sealed record PreferenceData
{
    public string Theme { get; init; } = "Default";
    public bool AbstractCopyEnabled { get; init; } = true;
    public bool TrayBatteryEnabled { get; init; } = true;
    public string[] CustomCopy { get; init; } = Array.Empty<string>();
    public string[] HonorCustomCopy { get; init; } = Array.Empty<string>();
}

internal static class AppPreferences
{
    private static readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FreeClipControl", "settings.json");
    private static PreferenceData current = Load();
    internal static bool AbstractCopyEnabled => current.AbstractCopyEnabled;
    internal static bool TrayBatteryEnabled => current.TrayBatteryEnabled;
    internal static string[] CustomCopyFor(HeadsetBrand brand) => (string[])(brand == HeadsetBrand.Honor ? current.HonorCustomCopy : current.CustomCopy).Clone();
    // Older versions stored just the theme name as a JSON string.
    internal static PreferenceData Decode(string json)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.ValueKind == JsonValueKind.String
            ? new PreferenceData { Theme = document.RootElement.GetString() }
            : JsonSerializer.Deserialize<PreferenceData>(json) ?? new PreferenceData();
        return data with
        {
            CustomCopy = AbstractCopy.Normalize(data.CustomCopy ?? Array.Empty<string>(), HeadsetBrand.Huawei),
            HonorCustomCopy = AbstractCopy.Normalize(data.HonorCustomCopy ?? Array.Empty<string>(), HeadsetBrand.Honor)
        };
    }
    private static PreferenceData Load()
    {
        try
        {
            if (File.Exists(path)) return Decode(File.ReadAllText(path));
        }
        catch (Exception error) { RuntimeLog.Write("Settings could not be read: " + error.Message); }
        return new();
    }
    internal static ElementTheme ReadTheme() => Enum.TryParse<ElementTheme>(current.Theme, out var value)
        && Enum.IsDefined(value) ? value : ElementTheme.Default;
    internal static void SaveTheme(ElementTheme theme) => Save(current with { Theme = theme.ToString() });
    internal static void SaveAbstractCopyEnabled(bool enabled) => Save(current with { AbstractCopyEnabled = enabled });
    internal static void SaveTrayBatteryEnabled(bool enabled) => Save(current with { TrayBatteryEnabled = enabled });
    internal static void SaveCustomCopy(string[] lines, HeadsetBrand brand) => Save(brand == HeadsetBrand.Honor
        ? current with { HonorCustomCopy = AbstractCopy.Normalize(lines, brand) }
        : current with { CustomCopy = AbstractCopy.Normalize(lines, brand) });
    private static void Save(PreferenceData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data));
        File.Move(path + ".tmp", path, true);
        current = data;
    }
}
