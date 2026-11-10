using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Moves items into the container tagged for their category, on the same grid,
/// only through conveyors. Round-robin over (inventory, item) work so big bases are
/// handled a slice per pass.
/// </summary>
internal static class SortJob
{
    private readonly struct Work
    {
        public readonly Store Source;
        public readonly ItemDefinition Item;
        public readonly ItemKey Key;
        /// <summary>Leaving a container that keeps it by category, for an item-tagged one ([Ore] → [Si]).</summary>
        public readonly bool ToItemTag;
        public Work(Store source, ItemDefinition item, ItemKey key, bool toItemTag) { Source = source; Item = item; Key = key; ToItemTag = toItemTag; }
    }

    /// <summary>Items already reported as "no destination", so the log says it once per session.</summary>
    private static readonly HashSet<(ItemDefinition, bool)> NoDestinationLogged = new HashSet<(ItemDefinition, bool)>();

    public static void Run(Session session, WorldScan scan, RunState state, Config config, RoutingOptions options, Budget budget)
    {
        var work = new List<Work>();
        foreach (var grid in scan.Grids.Values)
        {
            if (!Routing.GridOptedIn(grid.Stores.Select(s => s.Info), options)) continue;
            foreach (var store in grid.Stores)
            {
                var seen = new HashSet<ItemDefinition>();
                var items = store.Inventory.Items;
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i].Definition;
                    if (!seen.Add(item)) continue;
                    var key = new ItemKey(ItemTraitsMapper.CategoryOf(item), ItemNames.Of(item));
                    var itemTagElsewhere = grid.Stores.Any(s => !ReferenceEquals(s, store) && s.Info.FreeMass > 0 && Routing.KeepsByItemTag(s.Info, key));
                    if (!Routing.ShouldSortOut(store.Info, key, options, itemTagElsewhere)) continue;
                    if (CargoEngine.Grace.IsProtected(store.Inventory, item)) continue;
                    var toItemTag = itemTagElsewhere && Routing.Keeps(store.Info, key, options);
                    work.Add(new Work(store, item, key, toItemTag));
                }
            }
        }

        if (work.Count == 0) { state.SortCursor = 0; return; }
        state.SortCursor %= work.Count;

        for (var n = 0; n < work.Count && !budget.Exhausted; n++)
        {
            var task = work[state.SortCursor];
            state.SortCursor = Routing.Next(state.SortCursor, work.Count);
            budget.Checks++;

            var source = task.Source;
            if (!session.IsEntityInScene(source.Inventory.Entity)) continue;
            if (source.Inventory.CountItem(task.Item) <= 0) continue;

            var grid = scan.Grids[source.Grid];
            var reachable = TransferService.ReachableSet(source.Inventory, task.Item);
            var candidates = grid.Stores.Where(s => !ReferenceEquals(s, source) && reachable.Contains(s.Inventory)).ToList();
            var byId = candidates.ToDictionary(s => s.Info.Id);

            var destinations = Routing.Destinations(candidates.Select(s => s.Info), task.Key, options, forUnload: false, itemTagsOnly: task.ToItemTag);
            if (destinations.Count == 0)
            {
                if (NoDestinationLogged.Add((task.Item, task.ToItemTag)))
                    PluginLog.Info($"sort: no destination for {task.Key.Name} ({task.Key.Category}) in '{source.Name}' - add a [{CargoSummary.Label(task.Key.Category)}] container or check conveyors");
                continue;
            }

            foreach (var info in destinations)
            {
                if (budget.Exhausted) return;
                var destination = byId[info.Id];
                if (!session.IsEntityInScene(destination.Inventory.Entity)) continue;

                var movedAny = false;
                for (var index = source.Inventory.Items.Length - 1; index >= 0 && !budget.Exhausted; index--)
                {
                    if (source.Inventory.Items[index].Definition != task.Item) continue;
                    budget.Attempts++;
                    FixedPoint moved = TransferService.MoveStack(source.Inventory, destination.Inventory, index, config.DryRun, $"sort {task.Key.Name ?? task.Key.Category.ToString()} '{source.Name}' -> '{destination.Name}'");
                    if (moved > 0) movedAny = true;
                }
                if (movedAny)
                {
                    scan.Refresh(destination);
                    scan.Refresh(source);
                }
                if (config.DryRun) break; // dry run: report only the first (real) destination
                if (source.Inventory.CountItem(task.Item) <= 0) break;
            }
        }
    }
}
