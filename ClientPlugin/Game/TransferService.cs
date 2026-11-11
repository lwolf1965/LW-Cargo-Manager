using System;
using System.Collections.Generic;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// The ONLY code that moves items.
/// Contains code adapted from MIT-licensed work, see THIRD-PARTY-NOTICES.txt. Safety rules:
///  - always TransferByIdx with an explicit target slot (never TransferByDef: it can duplicate
///    items with mixed battery metadata),
///  - check slot capacity before moving, non-stackable items move one at a time,
///  - conservation check after each move; a mismatch stops the plugin for the session.
/// </summary>
internal static class TransferService
{
    /// <summary>True while the plugin itself is moving items (the TransferByIdx patch ignores those for grace tracking).</summary>
    [ThreadStatic] public static bool IsOwnMove;

    public static int MovesThisPass;

    /// <summary>Is <paramref name="destination"/> reachable from <paramref name="source"/> through conveyors for this item?</summary>
    public static bool Reachable(InventoryComponent source, InventoryComponent destination, ItemDefinition item)
    {
        foreach (var reachable in ConveyorSystemComponent.IterateReachableInventories(source, item, followEdgeDirection: true))
            if (ReferenceEquals(reachable, destination))
                return true;
        return false;
    }

    /// <summary>
    /// All inventories reachable from <paramref name="source"/> through conveyors for this item.
    /// The game returns its own struct enumerator (ReachableInventoriesEnumerator), so copy it with foreach.
    /// </summary>
    public static HashSet<InventoryComponent> ReachableSet(InventoryComponent source, ItemDefinition item)
    {
        var set = new HashSet<InventoryComponent>();
        foreach (var reachable in ConveyorSystemComponent.IterateReachableInventories(source, item, followEdgeDirection: true))
            set.Add(reachable);
        return set;
    }

    /// <summary>Find a matching stack with equal metadata, or a new slot if one is free.</summary>
    public static bool TryTarget(ReadOnlySpan<ItemStack> items, int? maxStacks, ItemStack stack, out int target)
    {
        if (stack.Definition.IsStackable)
            for (var i = 0; i < items.Length; i++)
                if (items[i].Definition == stack.Definition &&
                    (items[i].MetaData?.Equals(stack.MetaData) ?? stack.MetaData == null))
                {
                    target = i;
                    return true;
                }
        target = items.Length;
        return !maxStacks.HasValue || items.Length < maxStacks.Value;
    }

    /// <summary>Move the stack at <paramref name="index"/> (or as much as fits). Returns the amount moved.</summary>
    public static FixedPoint MoveStack(InventoryComponent source, InventoryComponent destination, int index, bool dryRun, string reason)
    {
        if (ReferenceEquals(source, destination)) return 0;
        var stack = source.Items[index];
        if (!TryTarget(destination.Items, destination.Definition.MaxStacks, stack, out var target)) return 0;
        if (destination.Mass.Current >= destination.Mass.Max) return 0;

        var requested = stack.Definition.IsStackable ? stack.Amount : (FixedPoint)1;
        if (dryRun)
        {
            PluginLog.Info($"DRY RUN {reason}: {requested} x {stack.Definition.Guid}");
            return 0;
        }

        var beforeSource = source.CountItem(stack.Definition);
        var beforeDestination = destination.CountItem(stack.Definition);

        FixedPoint moved;
        IsOwnMove = true;
        try
        {
            moved = source.TransferByIdx(destination, index, target, requested, true);
        }
        finally
        {
            IsOwnMove = false;
        }

        if (beforeSource - source.CountItem(stack.Definition) != moved ||
            destination.CountItem(stack.Definition) - beforeDestination != moved)
            throw new InvalidOperationException("Inventory conservation check failed; plugin stopped. Item=" + stack.Definition.Guid);

        if (moved > 0)
        {
            MovesThisPass++;
            PluginLog.Verbose($"{reason}: moved {moved} x {stack.Definition.Guid}");
        }
        return moved;
    }

    /// <summary>
    /// EXPERIMENTAL (M5): merge the stack at <paramref name="index"/> into an earlier stack of the
    /// same item and metadata inside the same inventory. Unverified whether the game allows a
    /// same-inventory TransferByIdx; the conservation check stops the plugin if it misbehaves.
    /// </summary>
    public static bool MergeInto(InventoryComponent inventory, int index, int target)
    {
        var stack = inventory.Items[index];
        var before = inventory.CountItem(stack.Definition);
        var stacksBefore = inventory.Items.Length;

        IsOwnMove = true;
        try
        {
            inventory.TransferByIdx(inventory, index, target, stack.Amount, true);
        }
        finally
        {
            IsOwnMove = false;
        }

        if (inventory.CountItem(stack.Definition) != before)
            throw new InvalidOperationException("Merge changed the item count; plugin stopped. Item=" + stack.Definition.Guid);
        return inventory.Items.Length < stacksBefore;
    }
}
