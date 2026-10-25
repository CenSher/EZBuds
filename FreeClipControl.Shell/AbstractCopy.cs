using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FreeClipControl.Shell;

internal enum HeadsetBrand { Huawei, Honor }

internal static class AbstractCopy
{
    internal const int MaxLineLength = 200;
    internal const int MaxCustomLines = 1000;
    private static readonly string[] huawei =
    {
        "加油华为，加油China",
        "已使用高端芯片骁龙778G",
        "正在体验Nova11se",
        "修破修破法斯特恰鸡",
        "呃啊哇...这个这个...哇",
        "已说服余承东打造华为智驾",
        "已连接到4.99G",
        "手机发烫以后，我们的性能更加出色",
        "遥遥领先",
        "我们领先的不止一点",
        "遥遥遥遥遥遥遥遥遥遥领先",
        "已将系统语言切换为玄武语言",
        "别酱，从未如此mate",
        "原神牛逼"
    };
    private static readonly string[] honor =
    {
        "哦爸爸妈妈给我的不少不多",
        "在公司门口捡到了华为最新图纸",
        "空气凝胶",
        "螺丝导热",
        "系统已升级为HarmonyOS",
        "前往松山湖参加会议",
        "谁把这么多麒麟放在我家冰箱的",
        "荣耀可能会倒闭，但永远不会变味"
    };
    private static string[] BuiltIn(HeadsetBrand brand) => brand == HeadsetBrand.Honor ? honor : huawei;
    internal static string BrandName(HeadsetBrand brand) => brand == HeadsetBrand.Honor ? "荣耀" : "华为";
    internal static int BuiltInCount(HeadsetBrand brand) => BuiltIn(brand).Length;
    internal static HeadsetBrand DetectBrand(string modelName, string deviceName)
    {
        // A saved model identifies renamed earphones more reliably than their alias.
        static HeadsetBrand? FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.Contains("HONOR", StringComparison.OrdinalIgnoreCase) || name.Contains("荣耀", StringComparison.Ordinal)) return HeadsetBrand.Honor;
            if (name.Contains("HUAWEI", StringComparison.OrdinalIgnoreCase) || name.Contains("华为", StringComparison.Ordinal)) return HeadsetBrand.Huawei;
            return null;
        }
        return FromName(modelName) ?? FromName(deviceName) ?? HeadsetBrand.Huawei;
    }
    internal static string[] Normalize(IEnumerable<string> lines, HeadsetBrand brand = HeadsetBrand.Huawei)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(BuiltIn(brand), StringComparer.Ordinal);
        foreach (string raw in lines)
        {
            string line = (raw ?? "").Trim().Trim('\uFEFF');
            if (line.Length >= 2 && (line[0] == '“' && line[^1] == '”' || line[0] == '"' && line[^1] == '"'))
                line = line[1..^1].Trim();
            if (line.Length == 0) continue;
            if (line.Length > MaxLineLength || line.Any(char.IsControl))
                throw new InvalidDataException($"每条文案最多 {MaxLineLength} 个字符，且不能包含控制字符。请每行填写一条文案。");
            if (seen.Add(line)) result.Add(line);
            if (result.Count > MaxCustomLines) throw new InvalidDataException($"自定义文案最多 {MaxCustomLines} 条。");
        }
        return result.ToArray();
    }
    internal static string[] Parse(string text, HeadsetBrand brand = HeadsetBrand.Huawei)
        => Normalize(text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None), brand);
    internal static string Pick(bool enabled, IEnumerable<string> custom, string previous, HeadsetBrand brand = HeadsetBrand.Huawei)
    {
        if (!enabled) return "已连接到" + BrandName(brand) + "耳机";
        var choices = BuiltIn(brand).Concat(custom).Distinct(StringComparer.Ordinal).Where(line => line != previous).ToArray();
        return choices[Random.Shared.Next(choices.Length)];
    }
}
