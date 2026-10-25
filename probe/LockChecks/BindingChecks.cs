using FreeClipControl.Shell;
using Microsoft.UI.Xaml;

internal static class BindingChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        string scratch = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".scratch"));
        string directory = Path.Combine(scratch, "binding-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const ulong second = 0x112233445566;
        void Paired()
        {
            FakeRadio.Reset();
            FakeRadio.Devices = new()
            {
                new(BluetoothDiscovery.Headset, "HUAWEI FreeClip 2", true, 0, true),
                new(second, "HONOR Earbuds 2 Lite", true, 0, true)
            };
        }
        try
        {
            string file = Path.Combine(directory, "headsets.json");
            var store = new HeadsetSelectionStore(file);
            store.Save(BluetoothDiscovery.Headset, "freeclip2", "HUAWEI FreeClip 2");
            store.Save(second, "honor2lite", "HONOR Earbuds 2 Lite", select: false);
            Paired();
            using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0, store)))
            {
                await vm.RefreshConnectedAsync();
                int connections = FakeRadio.Connections, queries = FakeRadio.Queries.Count;
                await vm.RemoveBoundHeadsetAsync(vm.BoundHeadsets.Single(d => d.Address == second));
                check(vm.SelectedHeadsetAddress == BluetoothDiscovery.Headset && vm.LeftBattery.Level == 80
                    && vm.BoundHeadsets.Count == 1 && FakeRadio.Connections == connections && FakeRadio.Queries.Count == queries,
                    "deleting another binding preserves the active earphone and does not access Bluetooth");
                var loaded = new HeadsetSelectionStore(file);
                check(loaded.ProfileFor(second) == null && loaded.NameFor(second) == null && loaded.IsRemoved(second)
                    && loaded.SelectedAddress == BluetoothDiscovery.Headset, "removed profile and name stay deleted on disk");
            }
            using (var source = new HuaweiBluetoothDataSource(0, new HeadsetSelectionStore(file)))
            {
                await source.GetAvailabilityAsync(CancellationToken.None);
                check((await source.ListBindingsAsync(CancellationToken.None)).All(d => d.Address != second),
                    "startup auto-configuration does not re-add a removed paired earphone");
            }

            // Keep another configured headset to ensure deleting the current one
            // does not silently select a different physical device.
            store.Save(second, "honor2lite", "HONOR Earbuds 2 Lite", select: false);
            Paired();
            using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0, store)))
            {
                await vm.RefreshConnectedAsync();
                var staleDevice = vm.Devices.First();
                int connections = FakeRadio.Connections, queries = FakeRadio.Queries.Count;
                await vm.RemoveBoundHeadsetAsync(vm.BoundHeadsets.Single(d => d.Selected));
                check(vm.SelectedHeadsetAddress == null && vm.SelectedHeadsetTitle == "选择蓝牙耳机"
                    && vm.SelectedHeadsetVisibility == Visibility.Collapsed && vm.TrayToolTip == "未选择耳机"
                    && vm.Devices.Count == 0 && vm.LeftBattery.Level == null && vm.GlobalBattery.Level == null
                    && !vm.CanConnectToHeadset && vm.NeedsConfiguration && vm.BoundHeadsets.Single().Address == second,
                    "deleting current binding clears old controls and battery and leaves explicit selection");
                await vm.RefreshTrayBatteryAsync();
                await vm.RefreshConnectedAsync();
                await vm.ToggleAsync(staleDevice);
                check(FakeRadio.Connections == connections && FakeRadio.Queries.Count == queries && FakeRadio.Mutations.Count == 0,
                    "background, passive refresh and stale actions cannot contact the removed earphone");
                using (var restarted = new HuaweiBluetoothDataSource(0, new HeadsetSelectionStore(file)))
                {
                    check(await restarted.GetAvailabilityAsync(CancellationToken.None) == BluetoothAvailability.MatchFailed
                        && restarted.SelectedAddress == null && (await restarted.ListBindingsAsync(CancellationToken.None)).Single().Address == second,
                        "restart with a remaining binding still waits for an explicit switch");
                }
                await vm.SelectBoundHeadsetAsync(vm.BoundHeadsets.Single());
                check(vm.SelectedHeadsetAddress == second && vm.CopyBrand == HeadsetBrand.Honor && vm.RightBattery.Level == 80
                    && vm.BoundHeadsets.Single().Selected && !store.RequiresSelection,
                    "explicitly switching remaining binding restores data, brand and active selection");
                await vm.RemoveBoundHeadsetAsync(vm.BoundHeadsets.Single());
                check(vm.BoundHeadsets.Count == 0 && vm.NoBindingsVisibility == Visibility.Visible,
                    "deleting the last binding produces the empty selector state");
            }
            Paired();
            using (var source = new HuaweiBluetoothDataSource(0, new HeadsetSelectionStore(file)))
            {
                check(await source.GetAvailabilityAsync(CancellationToken.None) == BluetoothAvailability.MatchFailed
                    && source.SelectedAddress == null && (await source.ListBindingsAsync(CancellationToken.None)).Length == 0
                    && FakeRadio.Connections == 0, "restart preserves deletion and never selects a removed paired device");
                bool blocked = false;
                try { await source.ReadAsync(CancellationToken.None, true); }
                catch (InvalidOperationException) { blocked = true; }
                check(blocked && FakeRadio.Connections == 0, "direct reads cannot bypass explicit selection after deletion");
                await source.SelectHeadsetAsync(BluetoothDiscovery.Headset, "freeclip2", CancellationToken.None);
                var restored = new HeadsetSelectionStore(file);
                check(!restored.IsRemoved(BluetoothDiscovery.Headset) && restored.IsRemoved(second)
                    && restored.SelectedAddress == BluetoothDiscovery.Headset && !restored.RequiresSelection,
                    "adding a deleted earphone explicitly restores only its own configuration");
            }

            var unselected = new HeadsetSelectionStore(Path.Combine(directory, "unselected.json"));
            unselected.Save(BluetoothDiscovery.Headset, "freeclip2", select: false);
            unselected.Remove(BluetoothDiscovery.Headset);
            check(unselected.RequiresSelection && unselected.Bindings.Length == 0,
                "deleting an unselected last binding also requires explicit reconfiguration");

            // A failed disk commit must preserve both the in-memory binding and UI.
            var failureStore = new HeadsetSelectionStore(file);
            Paired();
            using (var vm = new PanelViewModel(new HuaweiBluetoothDataSource(0, failureStore)))
            {
                await vm.RefreshConnectedAsync();
                File.Move(file, file + ".backup");
                Directory.CreateDirectory(file);
                bool rejected = false;
                int connections = FakeRadio.Connections;
                try { await vm.RemoveBoundHeadsetAsync(vm.BoundHeadsets.Single()); }
                catch (IOException) { rejected = true; }
                catch (UnauthorizedAccessException) { rejected = true; }
                check(rejected && failureStore.SelectedAddress == BluetoothDiscovery.Headset && !failureStore.IsRemoved(BluetoothDiscovery.Headset)
                    && vm.SelectedHeadsetAddress == BluetoothDiscovery.Headset && vm.LeftBattery.Level == 80
                    && vm.BoundHeadsets.Count == 1 && vm.CanSelectHeadset && FakeRadio.Connections == connections,
                    "failed deletion preserves active target, saved state and UI and restores action availability");
            }
        }
        finally
        {
            FakeRadio.Reset();
            if (!Path.GetFullPath(directory).StartsWith(scratch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped scratch directory.");
            Directory.Delete(directory, true);
        }
    }
}
