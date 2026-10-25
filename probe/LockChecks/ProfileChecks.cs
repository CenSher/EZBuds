using FreeClipControl.Shell;
using Microsoft.UI.Xaml;

internal static class ProfileChecks
{
    internal static async Task Run(Action<bool, string> check, Action<Action> invalid)
    {
        check(HeadsetProfiles.BuiltIn.Length == 21 && HeadsetProfiles.BuiltIn.Select(p => p.Id).Distinct().Count() == 21,
            "21 distinct model profiles are registered");
        check(HeadsetProfiles.ResolveConfigured("buds6i", "HUAWEI FreeBuds 7i")?.Id == "buds7i",
            "exact 7i identity corrects the former 6i compatibility mapping");
        check(HeadsetProfiles.ResolveConfigured("buds6i", "我的耳机")?.Id == "buds6i",
            "other saved or renamed models are not migrated");
        foreach (var model in HeadsetProfiles.BuiltIn)
        {
            check(HeadsetProfiles.Detect(model.Name.ToLowerInvariant()) == model, model.Name + " name recognition");
            FakeRadio.Reset();
            using var source = new HuaweiBluetoothDataSource(0);
            await source.SelectHeadsetAsync(BluetoothDiscovery.Headset, model.Id, CancellationToken.None);
            check(FakeRadio.Connections == 0 && FakeRadio.Mutations.Count == 0, model.Id + " selection only saves local data");
            var snapshot = await source.ReadAsync(CancellationToken.None);
            check(FakeRadio.LastChannel == model.Channel && snapshot.Profile == model && snapshot.Global == 80,
                model.Id + " uses correct channel and parses reported battery");
            check(FakeRadio.Queries.Contains(0x2B31) == model.DeviceManagement && FakeRadio.Queries.Contains(0x2B2F) == model.DeviceManagement,
                model.Id + " only queries supported device management");
            check(snapshot.Left.HasValue == model.SplitBattery && snapshot.Devices.All(d => d.LockSupported == model.ConnectionLock),
                model.Id + " battery shape and lock capability follow profile");
            if (!model.ConnectionLock)
            {
                invalid(() => source.SetConnectionLockedAsync(BluetoothDiscovery.FormatAddress(BluetoothDiscovery.Computer), true, CancellationToken.None).GetAwaiter().GetResult());
                check(FakeRadio.Mutations.Count == 0, model.Id + " never sends FreeClip 2 lock commands");
            }
            if (!model.DeviceManagement)
            {
                invalid(() => source.SetConnectedAsync(BluetoothDiscovery.FormatAddress(BluetoothDiscovery.Phone), true, CancellationToken.None).GetAwaiter().GetResult());
                check(FakeRadio.Mutations.Count == 0, model.Id + " rejects unsupported connection writes");
            }
        }
        check(HeadsetProfiles.Detect("Karthy的FreeClip 2")?.Id == "freeclip2"
            && HeadsetProfiles.Detect("FreeClip 2 S") == null && HeadsetProfiles.Detect("FreeBuds 7i") == null
            && HeadsetProfiles.Detect("HUAWEI FreeBuds Pro 4")?.Id == "pro4",
            "custom-name prefix is accepted without guessing unknown or adjacent models");

        string checkRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".scratch"));
        string directory = Path.Combine(checkRoot, "profile-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "headsets.json");
            const ulong second = 0x112233445566;
            var store = new HeadsetSelectionStore(file);
            store.Save(BluetoothDiscovery.Headset, "freeclip2");
            store.Save(second, "honor2lite");
            var loaded = new HeadsetSelectionStore(file);
            check(loaded.SelectedAddress == second && loaded.ProfileFor(second) == "honor2lite"
                && loaded.ProfileFor(BluetoothDiscovery.Headset) == "freeclip2", "reloading keeps per-address model mappings and selected earphone");
            FakeRadio.Reset();
            FakeRadio.Devices = new() { new(second, "我的耳机", true, 0, true) };
            using (var source = new HuaweiBluetoothDataSource(0, loaded))
            {
                var choices = await source.ListHeadsetsAsync(CancellationToken.None);
                check(choices.Single().ProfileId == "honor2lite", "renamed earphone uses saved model by address");
                var snapshot = await source.ReadAsync(CancellationToken.None);
                check(snapshot.Name == "我的耳机" && FakeRadio.LastAddress == second && FakeRadio.LastChannel == 16,
                    "restart reconnects to renamed device with the saved protocol");
                invalid(() => source.SelectHeadsetAsync(0xAABBCCDDEEFF, "freeclip2", CancellationToken.None).GetAwaiter().GetResult());
                invalid(() => source.SelectHeadsetAsync(second, "unknown", CancellationToken.None).GetAwaiter().GetResult());
                check(source.SelectedAddress == second && source.SelectedProfileId == "honor2lite", "invalid selection cannot replace current target");
            }
            File.WriteAllText(file, "invalid json");
            new HeadsetSelectionStore(file).Save(second, "studio");
            check(Directory.GetFiles(directory, "*.invalid-*").Length == 1 && new HeadsetSelectionStore(file).ProfileFor(second) == "studio",
                "corrupt preferences are backed up before recovery");
            // A failed save must leave the active earphone unchanged.
            string blocker = Path.Combine(directory, "file-not-directory");
            File.WriteAllText(blocker, "block");
            FakeRadio.Reset();
            using (var source = new HuaweiBluetoothDataSource(0, new HeadsetSelectionStore(Path.Combine(blocker, "headsets.json"))))
            {
                try { await source.SelectHeadsetAsync(BluetoothDiscovery.Headset, "studio", CancellationToken.None); throw new Exception("save should fail"); }
                catch (IOException) { }
                check(source.SelectedAddress == null && FakeRadio.Mutations.Count == 0, "save failure does not switch target or send device writes");
            }
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(checkRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped its scratch directory.");
            Directory.Delete(directory, true);
        }

        FakeRadio.Reset();
        using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0)))
        {
            await vm.RefreshConnectedAsync();
            check(vm.CopyBrand == HeadsetBrand.Huawei, "Huawei snapshot selects Huawei copy library");
            int brandNotifications = 0;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(PanelViewModel.CopyBrand)) brandNotifications++; };
            var oldRow = vm.Devices.First();
            await vm.SelectHeadsetAsync(new(BluetoothDiscovery.Headset, "测试耳机", true, "studio"), HeadsetProfiles.Find("studio"));
            check(vm.Devices.Count == 0 && vm.GlobalBatteryVisibility == Visibility.Visible && vm.SplitBatteryVisibility == Visibility.Collapsed
                && vm.DeviceManagementVisibility == Visibility.Collapsed && vm.TrayToolTip == "FreeBuds Studio（80%）" && vm.ConnectionLabel == "已连接",
                "switching to aggregate-battery model clears device rows and changes UI and tray");
            await vm.SetConnectionLockedAsync(oldRow, true);
            await vm.ToggleAsync(oldRow);
            check(FakeRadio.Mutations.Count == 0, "stale controls from previous earphone cannot issue commands");
            FakeRadio.SystemConnected = false;
            int sessions = FakeRadio.Connections;
            await vm.SelectHeadsetAsync(new(BluetoothDiscovery.Headset, "测试耳机", false, "honor2lite"), HeadsetProfiles.Find("honor2lite"));
            check(vm.CopyBrand == HeadsetBrand.Honor && brandNotifications > 0, "renamed Honor selection notifies copy library immediately even when offline");
            check(FakeRadio.Connections == sessions && vm.IsConnectionNoticeVisible && vm.TrayToolTip == "Earbuds 2 Lite（--%）"
                && vm.LeftBattery.Level == null && vm.GlobalBattery.Level == null,
                "selecting disconnected device clears old battery and never connects in background");
            await vm.SelectHeadsetAsync(new(BluetoothDiscovery.Headset, "荣耀昵称", false, "freeclip2"), HeadsetProfiles.Find("freeclip2"));
            check(vm.CopyBrand == HeadsetBrand.Huawei && FakeRadio.Connections == sessions, "switching back restores Huawei copy regardless of alias without connecting");
        }
        FakeRadio.Reset();
        FakeRadio.Devices = new() { new(BluetoothDiscovery.Headset, "HUAWEI FreeClip 2", true, 0, true), new(0x112233445566, "HUAWEI FreeBuds Pro 4", true, 0, true) };
        using (var source = new HuaweiBluetoothDataSource(0))
        {
            invalid(() => source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult());
            check(FakeRadio.Connections == 0, "multiple connected earphones require explicit choice instead of arbitrary control");
        }
        FakeRadio.Reset();
    }
}
