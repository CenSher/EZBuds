using System;
using System.Collections.Generic;

namespace FreeClipControl.Shell;

public enum NoiseControlMode { Off = 0, Cancellation = 1, Awareness = 2 }
public sealed record NoiseControlChoice(NoiseControlMode Mode, string Label);
public sealed record NoiseLevelChoice(byte Value, string Label);
internal sealed record NoiseControlState(NoiseControlMode Mode, byte SubMode);
internal sealed record NoiseControlResult(NoiseControlMode? Mode, bool Confirmed, byte? Level = null);

// Wire facts and per-model evidence are pinned in docs/NOISE_CONTROL.md.
internal static class NoiseControlProtocol
{
    internal const ushort ReadCommand = 0x2B2A, WriteCommand = 0x2B04;
    private static readonly HashSet<string> supportedIds = new(StringComparer.Ordinal)
    {
        "buds4i", "buds5i", "buds6i", "buds7i", "pro", "pro2", "pro3", "pro4", "pro5",
        "se4anc", "studio", "lacepro", "lacepro2", "honor2", "honor2se", "honor2lite"
    };
    private static readonly HashSet<string> supportedNames = new(StringComparer.Ordinal)
    {
        "HUAWEIFREEBUDS4I", "HUAWEIFREEBUDS5I", "HUAWEIFREEBUDS6I", "HUAWEIFREEBUDS7I",
        "HUAWEIFREEBUDSPRO", "HUAWEIFREEBUDSPRO2", "HUAWEIFREEBUDSPRO3", "HUAWEIFREEBUDSPRO4",
        "HUAWEIFREEBUDSPRO5", "HUAWEIFREEBUDSSE4ANC", "HUAWEIFREEBUDSSTUDIO",
        "HUAWEIFREELACEPRO", "HUAWEIFREELACEPRO2", "HONOREARBUDS2", "HONOREARBUDS2SE", "HONOREARBUDS2LITE"
    };
    internal static bool Supports(HeadsetProfile profile)
    {
        if (profile == null || profile.Id is "freeclip" or "freeclip2" or "buds3" or "se" or "se2") return false;
        // Consult the configured model, never a user's renameable Bluetooth alias.
        return supportedIds.Contains(profile.Id) || supportedNames.Contains(HeadsetProfiles.Normalize(profile.Name));
    }
    private static readonly NoiseLevelChoice[] standardThree =
    {
        new(1, "轻度"), new(0, "均衡"), new(2, "深度")
    };
    private static readonly NoiseLevelChoice[] standardFour =
    {
        new(3, "智慧动态"), new(1, "轻度"), new(0, "均衡"), new(2, "深度")
    };
    private static readonly NoiseLevelChoice[] pro3Levels =
    {
        new(1, "智慧动态"), new(0, "轻度"), new(2, "均衡"), new(3, "深度")
    };
    internal static NoiseLevelChoice[] LevelChoices(HeadsetProfile profile)
    {
        if (!Supports(profile)) return Array.Empty<NoiseLevelChoice>();
        // Saved model identity takes precedence over a name. Pro 4 is deliberately
        // excluded: sharing the main-mode protocol does not prove its level mapping.
        return profile.Id switch
        {
            // The user's 7i readback/label comparison (2026-10-25) takes
            // precedence over the conflicting upstream Pro 3/7i grouping.
            "buds5i" or "buds6i" or "buds7i" or "pro" or "pro5" or "studio" or "lacepro2" or "se4anc" => standardFour,
            "pro2" or "lacepro" => standardThree,
            "pro3" => pro3Levels,
            "pro4" or "buds4i" or "honor2" or "honor2se" or "honor2lite" => Array.Empty<NoiseLevelChoice>(),
            _ => HeadsetProfiles.Normalize(profile.Name) switch
            {
                "HUAWEIFREEBUDS5I" or "HUAWEIFREEBUDS6I" or "HUAWEIFREEBUDS7I" or "HUAWEIFREEBUDSPRO" or "HUAWEIFREEBUDSPRO5"
                    or "HUAWEIFREEBUDSSTUDIO" or "HUAWEIFREELACEPRO2" or "HUAWEIFREEBUDSSE4ANC" => standardFour,
                "HUAWEIFREEBUDSPRO2" or "HUAWEIFREELACEPRO" => standardThree,
                "HUAWEIFREEBUDSPRO3" => pro3Levels,
                _ => Array.Empty<NoiseLevelChoice>()
            }
        };
    }
    internal static bool SupportsLevel(HeadsetProfile profile, byte value)
        => Array.Exists(LevelChoices(profile), option => option.Value == value);
    internal static byte[] LevelParameter(HeadsetProfile profile, byte value)
        => SupportsLevel(profile, value) ? new byte[] { 1, value }
            : throw new InvalidOperationException("当前型号不支持此降噪强度。");

    internal static NoiseControlMode? Parse(HuaweiFrame frame) => ParseState(frame)?.Mode;
    internal static NoiseControlState ParseState(HuaweiFrame frame)
    {
        if (frame?.Command != ReadCommand) return null;
        var state = frame.Get(1);
        // Readback is [sub-mode, main-mode]; writes use the opposite order.
        return state.Length == 2 && state[1] <= 2 ? new((NoiseControlMode)state[1], state[0]) : null;
    }
    internal static byte[] Parameter(NoiseControlMode mode) => mode switch
    {
        NoiseControlMode.Off => new byte[] { 0, 0 },
        NoiseControlMode.Cancellation => new byte[] { 1, 0xFF },
        NoiseControlMode.Awareness => new byte[] { 2, 0xFF },
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
