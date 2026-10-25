using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace FreeClipControl.Shell;

// The XAML DesktopAcrylicBackdrop wrapper does not expose Kind in this SDK.
// Use its system controller to select the standard, more opaque Base variant.
public sealed class PanelAcrylicBackdrop : SystemBackdrop
{
    private sealed record TargetState(DesktopAcrylicController Controller,
        SystemBackdropConfiguration Configuration, SystemBackdropConfiguration Defaults);
    private readonly Dictionary<ICompositionSupportsSystemBackdrop, TargetState> targets = new();
    private bool hostInputActive;

    internal void DetachForRetirement(SystemBackdropElement owner)
    {
        if (!DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("The panel backdrop must be retired on its UI thread.");

        // Keep the element's native links alive through OnTargetDisconnected and
        // the removal of its placement visual. Only then close them on this thread.
        // Releasing the controller alone leaves their last COM release to the .NET
        // finalizer, where ContentExternalBackdropLink's destructor fails fast with
        // RPC_E_WRONG_THREAD. This is terminal retirement, not a backdrop replacement.
        var retiringTargets = targets.Keys.ToArray();
        try { owner.SystemBackdrop = null; }
        finally
        {
            foreach (var target in retiringTargets)
            {
                UiCleanup.Run("panel backdrop link", () =>
                {
                    target.As<IDisposable>().Dispose();
                    RuntimeLog.Write("Panel backdrop link closed on UI thread.");
                });
            }
        }
    }

    internal void SetHostInputActive(bool value)
    {
        if (hostInputActive == value) return;
        hostInputActive = value;
        foreach (var state in targets.Values) UpdateConfiguration(state);
        RuntimeLog.Write("Panel acrylic host input active: " + value);
    }

    private void UpdateConfiguration(TargetState state)
    {
        // An element backdrop's default input policy can remain inactive in a
        // transparent popup. Own that field, but keep the system's theme and
        // accessibility policy, as in the documented custom configuration pattern.
        state.Configuration.Theme = state.Defaults.Theme;
        state.Configuration.IsHighContrast = state.Defaults.IsHighContrast;
        state.Configuration.IsInputActive = hostInputActive;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (targets.ContainsKey(connectedTarget)) return;
        var defaults = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        var configuration = new SystemBackdropConfiguration();
        var controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        var state = new TargetState(controller, configuration, defaults);
        UpdateConfiguration(state);
        controller.SetSystemBackdropConfiguration(configuration);
        bool added = controller.AddSystemBackdropTarget(connectedTarget);
        targets.Add(connectedTarget, state);
        controller.StateChanged += (_, _) => RuntimeLog.Write("Panel acrylic state: " + controller.State);
        RuntimeLog.Write($"Panel acrylic attached; kind={controller.Kind}; targetAdded={added}; state={controller.State}; inputActive={configuration.IsInputActive}; defaultInputActive={defaults.IsInputActive}; highContrast={configuration.IsHighContrast}; theme={configuration.Theme}");
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        if (targets.TryGetValue(target, out var state)) UpdateConfiguration(state);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (targets.Remove(disconnectedTarget, out var state))
        {
            state.Controller.RemoveSystemBackdropTarget(disconnectedTarget);
            state.Controller.Dispose();
        }
        base.OnTargetDisconnected(disconnectedTarget);
    }
}
