namespace ClientPlugin.Policy;

/// <summary>One side of a locked connector pair.</summary>
public readonly record struct DockSide(BlockTags Tags, bool GridIsStatic, double GridMass);

public enum DockReceiver
{
    /// <summary>No unload (no dock, [NoUnload], or a tie).</summary>
    None,
    A,
    B,
    /// <summary>Both sides are unload docks and the rule can't decide. Show a HUD message.</summary>
    Tie,
}

/// <summary>Decides which grid receives items when two connectors lock.</summary>
public static class DockRules
{
    public static bool IsUnloadDock(BlockTags tags, bool gridIsStatic, UnloadMode mode)
    {
        tags ??= BlockTags.Empty;
        if (tags.NoUnload) return false;
        if (tags.Unload) return true;
        return mode == UnloadMode.AllStationConnectors && gridIsStatic;
    }

    /// <summary>
    /// 1. Only one side is a dock → that side receives.
    /// 2. Both are docks: static grid wins; both ships → heavier grid wins; otherwise Tie.
    /// [NoUnload] on either side → None.
    /// </summary>
    public static DockReceiver Receiver(DockSide a, DockSide b, UnloadMode mode, double massTolerance = 0.5)
    {
        var tagsA = a.Tags ?? BlockTags.Empty;
        var tagsB = b.Tags ?? BlockTags.Empty;
        if (tagsA.NoUnload || tagsB.NoUnload)
            return DockReceiver.None;

        var dockA = IsUnloadDock(tagsA, a.GridIsStatic, mode);
        var dockB = IsUnloadDock(tagsB, b.GridIsStatic, mode);

        if (dockA && !dockB) return Allowed(a, b) ? DockReceiver.A : DockReceiver.None;
        if (dockB && !dockA) return Allowed(b, a) ? DockReceiver.B : DockReceiver.None;
        if (!dockA) return DockReceiver.None;

        // Both sides are docks.
        if (a.GridIsStatic != b.GridIsStatic)
            return a.GridIsStatic ? DockReceiver.A : DockReceiver.B;
        if (a.GridIsStatic)
            return DockReceiver.Tie; // station to station

        var diff = a.GridMass - b.GridMass;
        if (diff > massTolerance) return DockReceiver.A;
        if (diff < -massTolerance) return DockReceiver.B;
        return DockReceiver.Tie;
    }

    /// <summary>A station never feeds a ship automatically.</summary>
    private static bool Allowed(DockSide receiver, DockSide giver) => !(giver.GridIsStatic && !receiver.GridIsStatic);
}
