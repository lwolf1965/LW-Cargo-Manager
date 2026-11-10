using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.ResourceDistribution.Conveyors;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Unload docks: when a connector pair is locked, move items from the giving grid to the
/// receiving grid (direction from DockRules). Two ways to find docked pairs:
///  1. Connector API (ConnectorAdapter probe) — per connector, all rules work.
///  2. Fallback while the connector API is unknown: a grid with an [Unload]-tagged block
///     receives from any other grid whose inventories are conveyor-reachable from it.
///     Limits: only [Unload]/[Unload All] tags (no AllStationConnectors mode), and a ship
///     docked at a [NoUnload] connector on the same grid would still be unloaded.
/// </summary>
internal static class UnloadJob
{
    private static bool fallbackWarned;

    private readonly struct Pair
    {
        public readonly GridInfo Receiver, Giver;
        public readonly BlockTags DockTags;
        public readonly string DockName;
        public Pair(GridInfo receiver, GridInfo giver, BlockTags dockTags, string dockName)
        { Receiver = receiver; Giver = giver; DockTags = dockTags; DockName = TagParser.StripTags(dockName, "Dock"); }
    }

    public static void Run(Session session, WorldScan scan, RunState state, Config config, RoutingOptions options, Budget budget)
    {
        var pairs = ConnectorPairs(scan, state, config);
        if (!ConnectorAdapter.Supported)
        {
            if (!fallbackWarned)
            {
                fallbackWarned = true;
                PluginLog.Warn(ConnectorAdapter.SeenAnyConnector
                    ? "Connector API not recognised; using reachability fallback for unload docks (see ConnectorAdapter)."
                    : "No connector component seen yet; using reachability fallback for unload docks.");
            }
            pairs = FallbackPairs(scan, config);
        }

        // Forget "storage full" for pairs that undocked, so the message shows again next time.
        var current = new HashSet<(object, object)>(pairs.Select(p => ((object)p.Receiver.Grid, (object)p.Giver.Grid)));
        state.FullNotified.RemoveWhere(k => !current.Contains(k));

        state.DockStatus.Clear();
        var keeps = config.UnloadAllKeepSet;
        foreach (var pair in pairs)
        {
            if (budget.Exhausted) return;
            var totals = new Dictionary<Category, double>();
            var receiverFull = false;

            foreach (var source in pair.Giver.Stores)
            {
                if (budget.Exhausted) break;
                if (!Routing.CanBeUnloadSource(source.Info)) continue;
                UnloadStore(session, scan, source, pair, config, options, keeps, budget, totals, ref receiverFull);
            }

            var pairKey = ((object)pair.Receiver.Grid, (object)pair.Giver.Grid);
            var status = totals.Count > 0 ? "unloading" : receiverFull ? "storage FULL" : "ship docked";
            if (!state.DockStatus.TryGetValue(pair.Receiver.Grid, out var dockLines))
                state.DockStatus[pair.Receiver.Grid] = dockLines = new List<string>();
            dockLines.Add($"{pair.DockName}: {status}");
            if (totals.Count > 0)
            {
                state.FullNotified.Remove(pairKey);
                var text = string.Join(", ", totals.Select(t => $"{t.Key} {t.Value:0}"));
                Notifier.Hud($"Unloaded {text}");
            }
            else if (receiverFull && state.FullNotified.Add(pairKey))
            {
                Notifier.Hud("Mother grid storage full!");
            }
        }
    }

    private static void UnloadStore(Session session, WorldScan scan, Store source, Pair pair, Config config, RoutingOptions options,
        HashSet<Category> keeps, Budget budget, Dictionary<Category, double> totals, ref bool receiverFull)
    {
        // Descending indices stay valid when a whole stack is removed.
        for (var index = source.Inventory.Items.Length - 1; index >= 0; index--)
        {
            if (budget.Exhausted) return;
            budget.Checks++;
            if (index >= source.Inventory.Items.Length) continue;
            var stack = source.Inventory.Items[index];
            var item = stack.Definition;
            var category = ItemTraitsMapper.CategoryOf(item);
            if (!Routing.ShouldUnload(category, pair.DockTags.UnloadAll, config.UnloadIce, keeps)) continue;
            if (!session.IsEntityInScene(source.Inventory.Entity)) return;

            var reachable = TransferService.ReachableSet(source.Inventory, item);
            var candidates = pair.Receiver.Stores.Where(s => reachable.Contains(s.Inventory)).ToList();
            var byId = candidates.ToDictionary(s => s.Info.Id);
            var destinations = Routing.Destinations(candidates.Select(s => s.Info), new ItemKey(category, ItemNames.Of(item)), options, forUnload: true);
            if (destinations.Count == 0) { receiverFull = true; continue; }

            foreach (var info in destinations)
            {
                if (budget.Exhausted) return;
                var destination = byId[info.Id];
                budget.Attempts++;
                FixedPoint moved = TransferService.MoveStack(source.Inventory, destination.Inventory, index, config.DryRun,
                    $"unload {category} '{source.Name}' -> '{destination.Name}' ({pair.DockName})");
                if (config.DryRun) break; // dry run: report only the first (real) destination
                if (moved > 0)
                {
                    CargoEngine.Deliveries.Add(pair.Receiver.Grid, pair.DockName, item, Fx.ToDouble(moved), CargoEngine.NowSeconds);
                    totals.TryGetValue(category, out var sum);
                    totals[category] = sum + Fx.ToDouble(moved);
                    scan.Refresh(destination);
                    scan.Refresh(source);
                }
                if (index >= source.Inventory.Items.Length || source.Inventory.Items[index].Definition != item)
                    break; // whole stack moved
            }
        }
    }

    /// <summary>Locked connector pairs found through ConnectorAdapter, with the direction rule applied.</summary>
    private static List<Pair> ConnectorPairs(WorldScan scan, RunState state, Config config)
    {
        var result = new List<Pair>();
        var byEntity = new Dictionary<Entity, (BlockRef Block, GridInfo Grid)>();
        foreach (var grid in scan.Grids.Values)
            foreach (var block in grid.Blocks)
                byEntity[block.Entity] = (block, grid);

        var seenPairs = new HashSet<(Entity, Entity)>();
        foreach (var (entity, (block, grid)) in byEntity)
        {
            if (!ConnectorAdapter.TryGetPartner(entity, out var partnerEntity)) continue;
            if (!byEntity.TryGetValue(partnerEntity, out var partner)) continue; // other side not accessible
            if (ReferenceEquals(partner.Grid, grid)) continue;

            var key = KeyOf(entity, partnerEntity);
            seenPairs.Add(key);

            // Stored direction is relative to the key order: A = key.Item1.
            var thisIsFirst = ReferenceEquals(key.Item1, entity);
            if (!state.DockDirections.TryGetValue(key, out var stored))
            {
                var mine = new DockSide(block.Tags, grid.IsStatic, GridMass.Of(grid));
                var theirs = new DockSide(partner.Block.Tags, partner.Grid.IsStatic, GridMass.Of(partner.Grid));
                var relativeToThis = DockRules.Receiver(mine, theirs, config.UnloadMode);
                stored = thisIsFirst ? relativeToThis : Flip(relativeToThis);
                state.DockDirections[key] = stored; // decided once per lock
                PluginLog.Verbose($"Dock '{block.Name}' <-> '{partner.Block.Name}': receiver={relativeToThis} (A = '{block.Name}')");
            }
            var receiver = thisIsFirst ? stored : Flip(stored);

            if (receiver == DockReceiver.Tie && state.TieNotified.Add(key))
                Notifier.Hud($"{block.Name}: both sides are unload docks, skipped");

            // Handle each pair once, from the receiving side.
            if (receiver == DockReceiver.A)
                result.Add(new Pair(grid, partner.Grid, DockTagsOf(block.Tags, partner.Block.Tags), block.Name));
        }

        // Forget pairs that are no longer locked, so the direction is decided again next time.
        foreach (var key in state.DockDirections.Keys.Where(k => !seenPairs.Contains(k)).ToList())
        {
            state.DockDirections.Remove(key);
            state.TieNotified.Remove(key);
        }
        return result;
    }

    /// <summary>Receiving dock's tags decide [Unload] vs [Unload All]; AllStationConnectors docks act as [Unload].</summary>
    private static BlockTags DockTagsOf(BlockTags receiverTags, BlockTags giverTags) => receiverTags.Unload ? receiverTags : BlockTags.Empty;

    private static (Entity, Entity) KeyOf(Entity a, Entity b) =>
        a.GetHashCode() <= b.GetHashCode() ? (a, b) : (b, a);

    private static DockReceiver Flip(DockReceiver r) => r switch
    {
        DockReceiver.A => DockReceiver.B,
        DockReceiver.B => DockReceiver.A,
        _ => r,
    };

    /// <summary>Fallback: grids with an [Unload] block receive from conveyor-connected grids.</summary>
    private static List<Pair> FallbackPairs(WorldScan scan, Config config)
    {
        var result = new List<Pair>();
        foreach (var receiver in scan.Grids.Values)
        {
            var dock = receiver.Blocks.FirstOrDefault(b => b.Tags.Unload && !b.Tags.NoUnload);
            if (dock == null || receiver.Stores.Count == 0) continue;

            foreach (var giver in scan.Grids.Values)
            {
                if (ReferenceEquals(giver, receiver)) continue;
                // Station never feeds a ship; ship-to-ship only if the receiver is the heavier one.
                var direction = DockRules.Receiver(
                    new DockSide(dock.Tags, receiver.IsStatic, GridMass.Of(receiver)),
                    new DockSide(giver.Blocks.FirstOrDefault(b => b.Tags.Unload)?.Tags ?? BlockTags.Empty, giver.IsStatic, GridMass.Of(giver)),
                    UnloadMode.Tagged);
                if (direction != DockReceiver.A) continue;
                if (!Connected(giver, receiver)) continue;
                result.Add(new Pair(receiver, giver, dock.Tags, dock.Name));
            }
        }
        return result;
    }

    private static bool Connected(GridInfo giver, GridInfo receiver)
    {
        var targets = new HashSet<InventoryComponent>(receiver.Stores.Select(s => s.Inventory));
        foreach (var store in giver.Stores)
        {
            var items = store.Inventory.Items;
            if (items.Length == 0) continue;
            foreach (var reachable in ConveyorSystemComponent.IterateReachableInventories(store.Inventory, items[0].Definition, followEdgeDirection: true))
                if (targets.Contains(reachable))
                    return true;
        }
        return false;
    }
}
