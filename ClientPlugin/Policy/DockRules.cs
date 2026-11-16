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

    /// <summary>
    /// Why nothing moved from a ship docked at an unload dock. <paramref name="eligible"/>: stacks the dock may take.
    /// Short text for the dock line on [Cargo] screens; <see cref="IdleDetail"/> is the log sentence.
    /// </summary>
    public static string IdleStatus(int eligible, bool noPath, bool receiverFull)
    {
        if (eligible == 0) return "ship docked";
        if (noPath) return "no conveyor path";
        if (receiverFull) return "storage FULL";
        return "cannot move";
    }

    public static string IdleDetail(int eligible, bool noPath, bool receiverFull, bool unloadAll)
    {
        if (eligible == 0)
            return unloadAll
                ? "nothing to unload (the ship is empty or holds only what [Unload All] keeps)"
                : "nothing to unload ([Unload] takes ore and ice only; use [Unload All] for the rest)";
        if (noPath) return $"{eligible} stack(s) wait, but no conveyor path leads from the ship to a container on the receiving grid";
        if (receiverFull) return $"{eligible} stack(s) wait, but no container on the receiving grid has room for them";
        return $"{eligible} stack(s) wait, but the game refused the move (no free slot in the destination?)";
    }

    /// <summary>A station never feeds a ship automatically.</summary>
    private static bool Allowed(DockSide receiver, DockSide giver) => !(giver.GridIsStatic && !receiver.GridIsStatic);
}
