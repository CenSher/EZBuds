using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FreeClipControl.Shell;

// Data-only profile updates. Downloaded Python is inspected as text, never run.
internal sealed class HeadsetCatalog
{
    internal sealed class CatalogData
    {
        public int FormatVersion { get; set; } = 1;
        public HeadsetProfile[] Profiles { get; set; } = Array.Empty<HeadsetProfile>();
        public HeadsetProfile[] UpstreamProfiles { get; set; } = Array.Empty<HeadsetProfile>();
        public string UpstreamRevision { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
    private static readonly Lazy<HeadsetCatalog> current = new(() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HuaweiHeadsetControl", "model-catalog.json")));
    internal static HeadsetCatalog Current => current.Value;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CatalogData data = new();
    private static readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HttpClient http = CreateClient();
    private readonly HttpClient client;
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HuaweiHeadsetControl/1.0");
        return client;
    }
    internal HeadsetCatalog(string filePath, HttpClient transport = null)
    {
        path = filePath;
        client = transport ?? http;
        try
        {
            if (path != null && File.Exists(path))
            {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("型号库文件过大。");
                var loaded = JsonSerializer.Deserialize<CatalogData>(File.ReadAllText(path), json) ?? throw new InvalidDataException("型号库为空。");
                Validate(loaded.Profiles, custom: true);
                Validate(loaded.UpstreamProfiles, custom: false);
                data = loaded;
            }
        }
        catch (Exception error) { RuntimeLog.Write("Model catalog could not be loaded: " + error.Message); }
    }
    internal HeadsetProfile[] All => data.Profiles.Concat(data.UpstreamProfiles).Concat(HeadsetProfiles.BuiltIn)
        .GroupBy(p => p.Id).Select(g => g.First()).ToArray();
    internal string Summary => $"可用型号 {All.Length} 个 · 自定义 {data.Profiles.Length} 个"
        + (data.UpdatedAt.HasValue ? " · 上次更新 " + data.UpdatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : " · 尚未从上游更新");
    internal static void Validate(HeadsetProfile[] profiles, bool custom)
    {
        if (profiles == null || profiles.Length > 300) throw new InvalidDataException("型号列表无效或超过 300 项。");
        if (profiles.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || !Regex.IsMatch(p.Id, @"^[a-zA-Z0-9:_-]{1,100}$")
            || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 120 || p.Name.Any(char.IsControl)
            || p.Channel < 1 || p.Channel > 30 || p.ConnectDelayMilliseconds < 0 || p.ConnectDelayMilliseconds > 10000
            || p.Keywords == null || p.Keywords.Length > 30 || p.Keywords.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 120 || k.Any(char.IsControl))))
            throw new InvalidDataException("型号配置包含无效的名称、通道、关键词或连接等待时间。");
        if (profiles.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != profiles.Length)
            throw new InvalidDataException("型号配置中有重复的 ID。");
        if (profiles.Any(p => p.ConnectionLock && (custom || p.Id != "freeclip2")))
            throw new InvalidDataException("导入和更新不能为其他型号启用 FreeClip 2 的连接锁定命令。");
        if (custom && profiles.Any(p => HeadsetProfiles.BuiltIn.Any(b => b.Id == p.Id)))
            throw new InvalidDataException("自定义型号请使用独立 ID，例如 custom:my-headset，不能覆盖内置型号。");
    }
    internal async Task<int> ImportAsync(string text, CancellationToken token)
    {
        if (text.Length > 1024 * 1024) throw new InvalidDataException("配置文件不能超过 1 MB。");
        var imported = JsonSerializer.Deserialize<CatalogData>(text, json) ?? throw new InvalidDataException("配置文件为空。");
        if (imported.FormatVersion != 1 || imported.Profiles == null || imported.Profiles.Length == 0) throw new InvalidDataException("请选择 formatVersion 为 1、包含 profiles 列表的 JSON 文件。");
        Validate(imported.Profiles, custom: true);
        await gate.WaitAsync(token);
        try
        {
            var merged = imported.Profiles.Concat(data.Profiles).GroupBy(p => p.Id).Select(g => g.First()).ToArray();
            Validate(merged, custom: true);
            var next = new CatalogData { Profiles = merged, UpstreamProfiles = data.UpstreamProfiles, UpstreamRevision = data.UpstreamRevision, UpdatedAt = data.UpdatedAt };
            Save(next);
            return imported.Profiles.Length;
        }
        finally { gate.Release(); }
    }
    private void Save(CatalogData next)
    {
        if (path != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(next, json));
                if (File.Exists(path)) File.Copy(path, path + ".previous", true);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        data = next;
    }
    internal async Task<string> UpdateFromOpenFreebudsAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            const string api = "https://api.github.com/repos/melianmiko/OpenFreebuds/git/trees/main?recursive=1";
            using var tree = JsonDocument.Parse(await client.GetStringAsync(api, token));
            string revision = tree.RootElement.GetProperty("sha").GetString();
            if (revision == null || !Regex.IsMatch(revision, "^[0-9a-f]{40}$")) throw new InvalidDataException("上游版本信息无效。");
            if (tree.RootElement.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean()) throw new InvalidDataException("上游目录未完整返回，已保留现有配置。");
            string root = "https://raw.githubusercontent.com/melianmiko/OpenFreebuds/" + revision + "/";
            string map = await client.GetStringAsync(root + "openfreebuds/driver/constants.py", token);
            string generic = await client.GetStringAsync(root + "openfreebuds/driver/generic/spp.py", token);
            var defaultPort = Regex.Match(generic, @"_spp_service_port\s*:\s*int\s*=\s*(\d+)");
            if (!defaultPort.Success) throw new InvalidDataException("上游连接配置结构已变化，已保留现有配置。");
            int port = int.Parse(defaultPort.Groups[1].Value);
            var sources = new List<string>();
            foreach (var entry in tree.RootElement.GetProperty("tree").EnumerateArray())
            {
                string file = entry.GetProperty("path").GetString();
                if (Regex.IsMatch(file ?? "", @"^openfreebuds/driver/huawei/driver/per_model/[a-zA-Z0-9_]+\.py$") && !file.EndsWith("/__init__.py"))
                    sources.Add(file);
            }
            if (sources.Count == 0 || sources.Count > 100) throw new InvalidDataException("上游型号目录无效，已保留现有配置。");
            var drivers = new Dictionary<string, (string Source, int Port)>();
            // Bounded parallel downloads to keep the settings window responsive.
            foreach (var batch in sources.Chunk(4))
            {
                var downloaded = await Task.WhenAll(batch.Select(async file => await client.GetStringAsync(root + file, token)));
                foreach (string source in downloaded)
                {
                    string active = Regex.Replace(source, @"(?m)^\s*#.*$", "");
                    var declaration = Regex.Match(active, @"class\s+(OfbDriverHuawei\w+)\(OfbDriverHuaweiGeneric\)\s*:");
                    if (!declaration.Success) continue;
                    var explicitPort = Regex.Match(active, @"self\._spp_service_port\s*=\s*(\d+)");
                    if (active.Contains("self._spp_service_port") && !explicitPort.Success) continue;
                    drivers[declaration.Groups[1].Value] = (active, explicitPort.Success ? int.Parse(explicitPort.Groups[1].Value) : port);
                }
            }
            var entries = Regex.Matches(map, "[\"']([^\"']+)[\"']\\s*:\\s*(OfbDriverHuawei\\w+)");
            var profiles = new List<HeadsetProfile>();
            int skipped = 0;
            foreach (Match entry in entries)
            {
                string name = entry.Groups[1].Value;
                if (!drivers.TryGetValue(entry.Groups[2].Value, out var driver)) { skipped++; continue; }
                // Only reuse capabilities represented by existing, supported SPP handlers.
                if (!driver.Source.Contains("OfbHuaweiBatteryHandler(")) { skipped++; continue; }
                var builtin = HeadsetProfiles.BuiltIn.FirstOrDefault(p => HeadsetProfiles.Normalize(p.Name) == HeadsetProfiles.Normalize(name));
                if (name == "FreeBuds 3") builtin = HeadsetProfiles.BuiltIn.First(p => p.Id == "buds3");
                string id = builtin?.Id ?? "ofb:" + Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                bool split = !Regex.IsMatch(driver.Source, @"OfbHuaweiBatteryHandler\(\s*w_tws\s*=\s*False") && !name.Contains("FreeLace");
                var delay = Regex.Match(driver.Source, @"self\._spp_connect_delay\s*=\s*(\d+)");
                profiles.Add(new(id, builtin?.Name ?? name, driver.Port, split, driver.Source.Contains("OfbHuaweiDualConnectHandler("),
                    builtin?.ConnectionLock == true, delay.Success ? int.Parse(delay.Groups[1].Value) * 1000 : 0));
            }
            var updated = profiles.GroupBy(p => p.Id).Select(g => g.First()).ToArray();
            if (updated.Length == 0) throw new InvalidDataException("未解析到可用的型号配置，已保留现有配置。");
            Validate(updated, custom: false);
            int added = updated.Count(p => !All.Any(old => old.Id == p.Id));
            // Keep older downloaded profiles if upstream temporarily removes an alias.
            var retained = updated.Concat(data.UpstreamProfiles).GroupBy(p => p.Id).Select(g => g.First()).ToArray();
            var next = new CatalogData { Profiles = data.Profiles, UpstreamProfiles = retained, UpstreamRevision = revision, UpdatedAt = DateTimeOffset.Now };
            Save(next);
            return $"已更新 {updated.Length} 个型号配置，新增 {added} 个。" + (skipped > 0 ? $"另有 {skipped} 项需要更新软件才能适配。" : "");
        }
        finally { gate.Release(); }
    }
}
