using System.Net;
using System.Net.Http;
using FreeClipControl.Shell;

internal static class CatalogChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var catalog = new HeadsetCatalog(null);
        await catalog.ImportAsync("""
            {"formatVersion":1,"profiles":[{"id":"custom:demo","name":"自定义测试型号","channel":16,"splitBattery":true,"deviceManagement":false,"keywords":["Demo Buds"]}]}
            """, CancellationToken.None);
        check(catalog.All.Any(p => p.Id == "custom:demo") && HeadsetProfiles.Detect("我的 Demo Buds 耳机", catalog.All)?.Id == "custom:demo",
            "imported keyword profile is available to name matching");
        foreach (string bad in new[] {
            """{"formatVersion":1,"profiles":[{"id":"custom:bad","name":"Bad","channel":31}]}""",
            """{"formatVersion":1,"profiles":[{"id":"custom:bad","name":"Bad","channel":1,"connectionLock":true}]}""",
            """{"formatVersion":1,"profiles":[{"id":"freeclip2","name":"Bad","channel":1}]}""",
            """{"formatVersion":2,"profiles":[]}""",
            """{"formatVersion":1,"profiles":null}"""
        })
        {
            bool rejected = false;
            try { await catalog.ImportAsync(bad, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && catalog.All.Any(p => p.Id == "custom:demo"), "invalid import preserves existing profiles");
        }
        using var transport = new HttpClient(new FixtureHandler());
        var updated = new HeadsetCatalog(null, transport);
        string result = await updated.UpdateFromOpenFreebudsAsync(CancellationToken.None);
        var model = updated.All.Single(p => p.Name == "HUAWEI Future Buds");
        check(model.Channel == 16 && model.SplitBattery && !model.DeviceManagement && !model.ConnectionLock && result.Contains("新增 1"),
            "upstream update uses inherited port and ignores commented-out capabilities");
        check(updated.All.Any(p => p.Id == "freeclip2") && !updated.All.Any(p => p.Name == "HUAWEI Unknown Protocol"),
            "upstream update retains builtin profiles and skips unsupported driver structure");
        using var failing = new HttpClient(new FixtureHandler { Fail = true });
        var offline = new HeadsetCatalog(null, failing);
        int count = offline.All.Length;
        try { await offline.UpdateFromOpenFreebudsAsync(CancellationToken.None); throw new Exception("update should fail"); }
        catch (HttpRequestException) { }
        check(offline.All.Length == count, "failed upstream request does not replace existing catalog");

        FakeRadio.Reset();
        FakeRadio.Devices = new() { new(BluetoothDiscovery.Headset, "我的 FreeClip 2 耳机", true, 0, true) };
        using (var source = new HuaweiBluetoothDataSource(0))
        {
            var status = await source.GetAvailabilityAsync(CancellationToken.None);
            var pairs = await source.ListBindingsAsync(CancellationToken.None);
            check(status == BluetoothAvailability.Connected && source.SelectedProfileId == "freeclip2" && pairs.Single().Name == "我的 FreeClip 2 耳机"
                && FakeRadio.Connections == 0 && FakeRadio.Mutations.Count == 0, "first-run keyword auto-configuration binds and selects without opening control channel");
        }
        FakeRadio.Reset();
        FakeRadio.Devices = new() { new(BluetoothDiscovery.Headset, "未知设备", true, 0, true) };
        using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0)))
        {
            await vm.RefreshConnectedAsync();
            check(vm.NeedsConfiguration && vm.ConnectionNoticeTitle == "未能匹配耳机" && vm.BluetoothFooterLabel == "打开软件设置" && FakeRadio.Connections == 0,
                "failed first-run match shows software-configuration guidance without querying unknown device");
            await vm.SelectHeadsetAsync(new(BluetoothDiscovery.Headset, "未知设备", true, "freeclip2"), HeadsetProfiles.Find("freeclip2"));
            check(!vm.NeedsConfiguration && vm.BoundHeadsets.Count == 1 && vm.BoundHeadsets.Single().Selected,
                "manual configuration recovers failed matching and populates bound-device list");
        }
        FakeRadio.Reset();
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        internal bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            string path = request.RequestUri.AbsolutePath;
            string value = path.Contains("/git/trees/") ? """
                {"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"openfreebuds/driver/huawei/driver/per_model/future.py"}]}
                """ : path.EndsWith("constants.py") ? """
                DEVICE_TO_DRIVER_MAP = {"HUAWEI Future Buds": OfbDriverHuaweiFuture, "HUAWEI Unknown Protocol": OfbDriverHuaweiUnknown}
                """ : path.EndsWith("spp.py") ? "self._spp_service_port: int = 16" : """
                class OfbDriverHuaweiFuture(OfbDriverHuaweiGeneric):
                    def __init__(self, address):
                        self.handlers = [OfbHuaweiBatteryHandler()]
                        # OfbHuaweiDualConnectHandler()
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value) });
        }
    }
}
