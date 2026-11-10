using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Tidy (M5):
///  - Consolidate: same item packed into as few same-tag containers as possible. Containers are
///    ranked (priority, size, id); items only move from a lower rank to a higher rank, so it can't loop.
///  - Merge (experimental, off by default): join split stacks inside one container.
///  - Order: TODO(M5), not implemented — needs a confirmed same-inventory move.
/// </summary>
internal static class TidyJob
{
    public static void Run(Session session, WorldScan scan, RunState state, Config config, RoutingOptions options, Budget budget)
    {
        foreach (var grid in scan.Grids.Values)
        {
            if (budget.Exhausted) return;
            if (!Routing.GridOptedIn(grid.Stores.Select(s => s.Info), options)) continue;

            if (config.TidyConsolidate)
                Consolidate(session, scan, grid, config, budget);

            if (config.TidyMergeExperimental && !config.DryRun)
                foreach (var store in grid.Stores)
                    if (!budget.Exhausted && store.Info.Role == BlockRole.Storage && !store.Info.Tags.Lock && !store.Info.Tags.NoTidy)
                        Merge(store, budget);
        }
    }

    private static void Consolidate(Session session, WorldScan scan, GridInfo grid, Config config, Budget budget)
    {
        var categories = grid.Stores.SelectMany(s => s.Info.Tags.Categories).Select(t => t.Category).Distinct();
        foreach (var category in categories)
        {
            var ranked = grid.Stores
                .Where(s => s.Info.Role == BlockRole.Storage && !s.Info.Tags.Lock && !s.Info.Tags.NoTidy && s.Info.Tags.Accepts(category))
                .OrderBy(s => s.Info.Tags.PriorityFor(category))
                .ThenByDescending(s => s.Info.MaxMass)
                .ThenBy(s => s.Info.Id)
                .ToList();
            if (ranked.Count < 2) continue;

            // Walk from the lowest-ranked container up, pushing its items to better-ranked ones.
            for (var r = ranked.Count - 1; r > 0 && !budget.Exhausted; r--)
            {
                var source = ranked[r];
                for (var index = source.Inventory.Items.Length - 1; index >= 0 && !budget.Exhausted; index--)
                {
                    if (index >= source.Inventory.Items.Length) continue;
                    var item = source.Inventory.Items[index].Definition;
                    budget.Checks++;
                    if (ItemTraitsMapper.CategoryOf(item) != category) continue;
                    if (CargoEngine.Grace.IsProtected(source.Inventory, item)) continue;

                    var reachable = TransferService.ReachableSet(source.Inventory, item);

                    for (var t = 0; t < r && !budget.Exhausted; t++)
                    {
                        var target = ranked[t];
                        if (!reachable.Contains(target.Inventory) || target.Info.FreeMass <= 0) continue;
                        if (!session.IsEntityInScene(target.Inventory.Entity)) continue;
                        budget.Attempts++;
                        FixedPoint moved = TransferService.MoveStack(source.Inventory, target.Inventory, index, config.DryRun, $"consolidate {category} '{source.Name}' -> '{target.Name}'");
                        if (config.DryRun) break; // dry run: report only the first (real) target
                        if (moved > 0)
                        {
                            scan.Refresh(target);
                            scan.Refresh(source);
                        }
                        if (index >= source.Inventory.Items.Length || source.Inventory.Items[index].Definition != item)
                            break; // whole stack moved
                    }
                }
            }
        }
    }

    private static void Merge(Store store, Budget budget)
    {
        var inventory = store.Inventory;
        for (var index = inventory.Items.Length - 1; index > 0 && !budget.Exhausted; index--)
        {
            if (index >= inventory.Items.Length) continue;
            var stack = inventory.Items[index];
            if (!stack.Definition.IsStackable) continue;
            budget.Checks++;
            for (var target = 0; target < index; target++)
            {
                var other = inventory.Items[target];
                if (other.Definition != stack.Definition) continue;
                if (!(other.MetaData?.Equals(stack.MetaData) ?? stack.MetaData == null)) continue;
                budget.Attempts++;
                TransferService.MergeInto(inventory, index, target);
                break;
            }
        }
    }
}
