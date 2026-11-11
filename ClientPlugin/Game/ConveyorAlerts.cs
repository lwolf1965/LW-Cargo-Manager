using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.DCS.Components;

namespace ClientPlugin.Game;

/// <summary>
/// [Alerts]: tagged blocks (storage tags, [Unload], [Make Ammo]) that are not on the grid's main conveyor network.
/// Uses the same game call as every transfer (ConveyorSystemComponent.IterateReachableInventories), read only,
/// cached for 30 s per grid. The probe item is any item found on the grid; a sorter that blocks exactly that item
/// could give a false warning, so the text says "no conveyor link" and the log names the probe item.
/// </summary>
internal static class ConveyorAlerts
{
    private const double CacheSeconds = 30;
    private static readonly Dictionary<CubeGridComponent, (double Time, List<string> Lines)> Cache = new();
    private static readonly HashSet<string> Logged = new();

    public static void ResetWorld() => Cache.Clear();

    public static IReadOnlyList<string> For(GridInfo grid, double now)
    {
        if (Cache.TryGetValue(grid.Grid, out var cached) && now - cached.Time < CacheSeconds) return cached.Lines;
        var lines = new List<string>();
        try { lines = Check(grid); }
        catch (Exception e)
        {
            if (Logged.Add("error:" + e.GetType().Name))
                PluginLog.Warn("Conveyor check failed (plugin keeps running): " + e.Message);
        }
        if (Cache.Count > 200) Cache.Clear();
        Cache[grid.Grid] = (now, lines);
        return lines;
    }

    private static List<string> Check(GridInfo grid)
    {
        // One entry per tagged block (producers have an input and an output inventory: use the output one).
        var blocks = new List<(int Id, string Name, Store Store)>();
        var blockIds = new Dictionary<Entity, int>();
        foreach (var store in grid.Stores)
        {
            if (!store.Info.IsOutput || store.BlockEntity == null || blockIds.ContainsKey(store.BlockEntity)) continue;
            var tags = store.Info.Tags;
            if (tags.Lock || !(tags.HasAnyStorageTag || tags.Unload || tags.UnloadAll || tags.MakeAmmo)) continue;
            blockIds[store.BlockEntity] = blocks.Count;
            blocks.Add((blocks.Count, store.Name, store));
        }
        if (blocks.Count < 2) return new List<string>();

        var probe = ProbeItem(grid);
        if (probe == null) return new List<string>();

        var inventoryToBlock = new Dictionary<InventoryComponent, int>();
        foreach (var store in grid.Stores)
            if (store.BlockEntity != null && blockIds.TryGetValue(store.BlockEntity, out var id))
                inventoryToBlock[store.Inventory] = id;

        var unlinked = ConveyorCheck.Unlinked(blocks.Select(b => (b.Id, b.Name)).ToList(), id =>
        {
            var reached = new HashSet<int>();
            foreach (var inventory in TransferService.ReachableSet(blocks[id].Store.Inventory, probe))
                if (inventoryToBlock.TryGetValue(inventory, out var other)) reached.Add(other);
            return reached;
        });

        foreach (var name in unlinked)
            if (Logged.Add("block:" + name))
                PluginLog.Warn($"Conveyor check: '{name}' is not linked to the main conveyor network (probe item {ItemNames.Of(probe)})");
        return unlinked.Select(n => n + ": no conveyor link").ToList();
    }

    /// <summary>Any item on the grid; reachability is checked per item in the game.</summary>
    private static ItemDefinition ProbeItem(GridInfo grid)
    {
        foreach (var store in grid.Stores)
        {
            var items = store.Inventory.Items;
            if (items.Length > 0) return items[0].Definition;
        }
        return null;
    }
}
