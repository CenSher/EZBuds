using System.Text.Json;
using FreeClipControl.Shell;

int passed = 0;
void Check(bool success, string name) { if (!success) throw new Exception(name); passed++; }
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { passed++; return; }
    throw new Exception(name);
}
Check(AbstractCopy.BuiltInCount(HeadsetBrand.Huawei) == 14, "14 Huawei phrases");
Check(AbstractCopy.BuiltInCount(HeadsetBrand.Honor) == 8, "8 Honor phrases");
Check(AbstractCopy.DetectBrand("HONOR Earbuds 2", "我的耳机") == HeadsetBrand.Honor, "renamed Honor uses model");
Check(AbstractCopy.DetectBrand("HUAWEI FreeClip 2", "荣耀昵称") == HeadsetBrand.Huawei, "model brand has priority over alias");
Check(AbstractCopy.DetectBrand(null, "我的荣耀耳机") == HeadsetBrand.Honor, "Chinese Honor alias fallback");
Check(AbstractCopy.DetectBrand("custom model", "honor Earbuds") == HeadsetBrand.Honor, "English Honor alias fallback");
Check(AbstractCopy.DetectBrand(null, null) == HeadsetBrand.Huawei, "unknown brand preserves existing default");
Check(AbstractCopy.Pick(false, Array.Empty<string>(), null, HeadsetBrand.Honor) == "已连接到荣耀耳机", "Honor disabled copy");
Check(AbstractCopy.Parse("空气凝胶\n螺丝导热\n荣耀自定义\n荣耀自定义", HeadsetBrand.Honor).SequenceEqual(new[] { "荣耀自定义" }), "Honor import deduplicates against Honor built-ins");
Check(AbstractCopy.Pick(false, new[] { "自定义" }, null) == "已连接到华为耳机", "disabled copy");
Check(AbstractCopy.Parse("\uFEFF“测试文案”\r\n\n\"另一个文案\"\r测试文案\n遥遥领先\n ").SequenceEqual(new[] { "测试文案", "另一个文案" }), "line endings, quotes, BOM, blanks, deduplication");
Check(AbstractCopy.Normalize(new[] { "旧文案", "旧文案", "新文案" }).Length == 2, "append deduplication");
Check(AbstractCopy.Parse(new string('华', 200)).Length == 1, "length boundary");
Reject(() => AbstractCopy.Parse(new string('华', 201)), "oversized phrase");
Reject(() => AbstractCopy.Parse("坏\0文案"), "control characters");
Check(AbstractCopy.Normalize(Enumerable.Range(0, 1000).Select(i => $"文案{i}")).Length == 1000, "count boundary");
Reject(() => AbstractCopy.Normalize(Enumerable.Range(0, 1001).Select(i => $"文案{i}")), "too many phrases");
var seen = new HashSet<string>();
string previous = null;
for (int i = 0; i < 2000; i++)
{
    string next = AbstractCopy.Pick(true, new[] { "自定义测试" }, previous);
    if (next == previous) throw new Exception("consecutive repetition");
    seen.Add(next); previous = next;
}
Check(seen.Count == 15 && seen.Contains("自定义测试"), "all built-ins and imported phrases reachable, no consecutive repeats");
string[] expectedHonor = { "哦爸爸妈妈给我的不少不多", "在公司门口捡到了华为最新图纸", "空气凝胶", "螺丝导热", "系统已升级为HarmonyOS", "前往松山湖参加会议", "谁把这么多麒麟放在我家冰箱的", "荣耀可能会倒闭，但永远不会变味" };
var honorSeen = new HashSet<string>();
previous = null;
for (int i = 0; i < 2000; i++)
{
    string next = AbstractCopy.Pick(true, Array.Empty<string>(), previous, HeadsetBrand.Honor);
    if (next == previous || !expectedHonor.Contains(next)) throw new Exception("Honor pool leaks another brand or repeats");
    honorSeen.Add(next); previous = next;
}
Check(honorSeen.SetEquals(expectedHonor), "Honor pool exactly matches all 8 supplied phrases");
Check(seen.Except(new[] { "自定义测试" }).Intersect(expectedHonor).Count() == 0, "brand built-in pools stay isolated");
var old = AppPreferences.Decode("\"Dark\"");
Check(old.Theme == "Dark" && old.AbstractCopyEnabled && old.CustomCopy.Length == 0, "legacy theme migration");
var roundTrip = AppPreferences.Decode(JsonSerializer.Serialize(new PreferenceData { Theme = "Light", AbstractCopyEnabled = false, CustomCopy = new[] { "测试文案" } }));
Check(roundTrip.Theme == "Light" && !roundTrip.AbstractCopyEnabled && roundTrip.CustomCopy.SequenceEqual(new[] { "测试文案" }), "settings round trip");
Check(AppPreferences.Decode("{\"Theme\":\"Dark\"}").AbstractCopyEnabled, "missing fields default on");
Check(old.TrayBatteryEnabled && AppPreferences.Decode("{\"Theme\":\"Dark\"}").TrayBatteryEnabled,
    "legacy preferences preserve enabled tray battery");
var batteryPreferences = AppPreferences.Decode(JsonSerializer.Serialize(new PreferenceData
    { Theme = "Dark", TrayBatteryEnabled = false, CustomCopy = new[] { "保留文案" } }));
Check(!batteryPreferences.TrayBatteryEnabled && batteryPreferences.Theme == "Dark"
    && batteryPreferences.CustomCopy.SequenceEqual(new[] { "保留文案" }), "disabled tray battery survives persistence with other preferences");
Check(AppPreferences.Decode("{\"CustomCopy\":null}").CustomCopy.Length == 0, "null custom library");
var brandRoundTrip = AppPreferences.Decode(JsonSerializer.Serialize(new PreferenceData { Theme = "Dark", CustomCopy = new[] { "华为导入" }, HonorCustomCopy = new[] { "荣耀导入", "空气凝胶" } }));
Check(brandRoundTrip.Theme == "Dark" && brandRoundTrip.CustomCopy.SequenceEqual(new[] { "华为导入" }) && brandRoundTrip.HonorCustomCopy.SequenceEqual(new[] { "荣耀导入" }), "separate brand libraries survive persistence");
Check(AppPreferences.Decode("{\"CustomCopy\":[\"历史导入文案\"]}").HonorCustomCopy.Length == 0, "legacy imports stay Huawei only");
Console.WriteLine($"PASS: {passed} copy and preference checks (no settings written, no Bluetooth commands).");

namespace Microsoft.UI.Xaml { public enum ElementTheme { Default, Light, Dark } }
namespace FreeClipControl.Shell { internal static class RuntimeLog { internal static void Write(string value) { } } }
