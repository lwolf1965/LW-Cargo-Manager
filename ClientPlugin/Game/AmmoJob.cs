using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// M13: keeps weapon ammo (turrets, fixed guns, rocket launchers) in stock by queuing it in Gearforges tagged [Make Ammo].
/// Game facts (Game2.Simulation 4.0.0.3169):
///  - turrets reload themselves from any conveyor-reachable inventory (ProjectileWeaponComponent.TryGetReloadProjectileItem),
///    so the plugin never moves ammo into turrets — it only keeps the stock up;
///  - ProjectileWeaponComponent._definition: AmmoTypes (ImmutableArray&lt;ItemDefinition&gt;), MaxCapacity (int, items per reload),
///    RoundsPerMinute (float), ConsumedAmmoPerRound (int); a reload removes MaxCapacity items and loads
///    items × AmmoDefinition.Capacity rounds; AmmoDefinition comes from LoadedAmmoType or _ammoDefinitions.AmmoTypes[item];
///  - rockets use the same ProjectileWeaponComponent, so they are counted the same way;
///  - ItemConverterComponent: CanProduceItem(ItemDefinition) → ItemRecipeDefinition, TryEnqueueRecipe(recipe, int),
///    Crafting (bool); ItemRecipeDefinition.Inputs / Outputs: ImmutableArray&lt;ItemAmount&gt; (fields Item, Amount).
/// Opt-in per Gearforge ([Make Ammo]) and only when that Gearforge is idle. With "Order ammo without materials" the order
/// goes in anyway and the game requests the missing parts from connected producers (it is vital); otherwise never more
/// than the materials on the grid allow. Read-only through reflection except TryEnqueueRecipe, the same call as the production screen.
/// </summary>
internal static class AmmoJob
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Last status per grid, for the [Turrets] screen.</summary>
    public static readonly Dictionary<CubeGridComponent, List<AmmoStatus>> Status = new();

    /// <summary>(Gearforge, ammo) → time of our last order, so one order isn't repeated before Crafting turns on.</summary>
    private static readonly Dictionary<(object, ItemDefinition), double> LastOrder = new();

    public static void ResetWorld()
    {
        Status.Clear();
        LastOrder.Clear();
    }
    private static readonly HashSet<string> Warned = new();

    public static void Run(WorldScan scan, Config config, double now)
    {
        foreach (var grid in scan.Grids.Values)
        {
            try { RunGrid(grid, config, now); }
            catch (Exception e)
            {
                if (Warned.Add("grid:" + e.GetType().Name))
                    PluginLog.Warn("Ammo job failed (plugin keeps running): " + e);
            }
        }
        if (Status.Count > 200) Status.Clear();
        if (LastOrder.Count > 500) LastOrder.Clear();
    }

    private static void RunGrid(GridInfo grid, Config config, double now)
    {
        // 1. Turrets: ammo item and capacity.
        var needs = new Dictionary<ItemDefinition, AmmoStatus>();
        foreach (var block in grid.Blocks)
        {
            foreach (var component in block.Entity.Components)
            {
                if (component.GetType().Name != "ProjectileWeaponComponent") continue;
                var definition = Reflect.Get(Reflect.Field(component.GetType(), "_definition"), component);
                var ammo = Reflect.Get(Reflect.Member(component.GetType(), "LoadedAmmoItem"), component) as ItemDefinition
                           ?? FirstItem(Reflect.Get(Reflect.Member(definition?.GetType(), "AmmoTypes"), definition));
                var capacity = Reflect.Get(Reflect.Member(definition?.GetType(), "MaxCapacity"), definition) is int c ? c : 0;
                if (ammo == null || capacity <= 0) continue;
                if (!needs.TryGetValue(ammo, out var status))
                    needs[ammo] = status = new AmmoStatus { Name = ItemNames.Of(ammo) };
                status.Turrets++;
                status.Capacity += capacity;
                status.PerMinute += PerMinute(component, definition, ammo);
            }
        }
        if (needs.Count == 0) { Status.Remove(grid.Grid); return; }

        // 2. Stock in storage (turret inventories don't count: that ammo is already in use).
        var stock = new Dictionary<ItemDefinition, double>();
        foreach (var store in grid.Stores)
        {
            if (store.Info.Role != BlockRole.Storage) continue;
            var items = store.Inventory.Items;
            for (var i = 0; i < items.Length; i++)
            {
                stock.TryGetValue(items[i].Definition, out var sum);
                stock[items[i].Definition] = sum + Fx.ToDouble(items[i].Amount);
            }
        }
        foreach (var pair in needs)
        {
            pair.Value.Stock = stock.TryGetValue(pair.Key, out var s) ? s : 0;
            pair.Value.Target = AmmoPlanner.Target(pair.Value.Capacity, pair.Value.PerMinute, config.AmmoReserveMinutes, config.AmmoReserveMultiplier);
        }

        // 3. Production in [Make Ammo] Gearforges.
        var forges = grid.Blocks.Where(b => b.Tags.MakeAmmo)
            .Select(b => (Block: b, Converter: (object)b.Entity.Components.FirstOrDefault(c => c.GetType().Name == "ItemConverterComponent")))
            .Where(f => f.Converter != null).ToList();

        foreach (var pair in needs)
        {
            var status = pair.Value;
            if (!status.Short) continue;
            if (!config.AutoAmmoProduction) { status.Production = "auto production off"; continue; }
            if (forges.Count == 0) { status.Production = "no [Make Ammo] Gearforge"; continue; }
            status.Production = Order(pair.Key, status, forges, stock, config, now);
        }

        Status[grid.Grid] = needs.Values.ToList();
    }

    private static string Order(ItemDefinition ammo, AmmoStatus status, List<(BlockRef Block, object Converter)> forges,
        Dictionary<ItemDefinition, double> stock, Config config, double now)
    {
        string lastProblem = "no Gearforge can make it";
        foreach (var (blockRef, converter) in forges)
        {
            var block = (Name: TagParser.StripTags(blockRef.Name, "Gearforge"), Ref: blockRef);
            var type = converter.GetType();
            var recipe = Call(type, "CanProduceItem", converter, ammo);
            if (recipe == null) continue;

            if (Reflect.Get(Reflect.Member(type, "Crafting"), converter) is true)
                return $"making (busy: {block.Name})";
            if (LastOrder.TryGetValue((converter, ammo), out var t) && now - t < 30)
                return $"ordered ({block.Name})";

            var perBatch = Amounts(Reflect.Get(Reflect.Member(recipe.GetType(), "Outputs"), recipe))
                .Where(a => a.Item == ammo).Sum(a => a.Amount);
            var affordable = int.MaxValue;
            string missing = null;
            foreach (var input in Amounts(Reflect.Get(Reflect.Member(recipe.GetType(), "Inputs"), recipe)))
            {
                var have = stock.TryGetValue(input.Item, out var h) ? h : 0;
                var can = input.Amount > 0 ? (int)(have / input.Amount) : int.MaxValue;
                if (can < affordable) { affordable = can; if (can == 0) missing = ItemNames.Of(input.Item); }
            }

            var onHand = affordable;
            if (config.AmmoOrderWithoutMaterials) affordable = int.MaxValue;
            var batches = AmmoPlanner.Batches(status.Stock, status.Target, perBatch, affordable, config.AmmoBatchSize);
            var requested = batches > onHand;
            if (batches <= 0)
            {
                lastProblem = missing != null ? $"short: {missing}" : "waiting";
                continue;
            }

            if (config.DryRun)
            {
                PluginLog.Info($"DRY RUN ammo: would queue {batches} x {status.Name} in '{block.Name}'");
                return $"would make {batches * perBatch:0} (dry run)";
            }

            var ok = Call(type, "TryEnqueueRecipe", converter, recipe, batches) is true;
            LastOrder[(converter, ammo)] = now;
            PluginLog.Info($"Ammo: queued {batches} x {status.Name} recipe in '{block.Name}' ({(ok ? "ok" : "refused")}), stock {status.Stock:0}/{status.Target:0}");
            if (ok) return requested && config.AmmoOrderWithoutMaterials
                ? $"making {batches * perBatch:0} ({block.Name}, parts requested{(missing != null ? ": " + missing : "")})"
                : $"making {batches * perBatch:0} ({block.Name})";
            lastProblem = "Gearforge refused the order";
        }
        return lastProblem;
    }

    /// <summary>Items per minute for one weapon firing nonstop, from the game's definitions; 0 when unknown.</summary>
    private static double PerMinute(object weapon, object definition, ItemDefinition ammo)
    {
        try
        {
            var dt = definition?.GetType();
            var rpm = Reflect.Get(Reflect.Member(dt, "RoundsPerMinute"), definition) is float f ? f : 0;
            var perRound = Reflect.Get(Reflect.Member(dt, "ConsumedAmmoPerRound"), definition) is int p ? p : 1;
            var ammoDef = Reflect.Get(Reflect.Member(weapon.GetType(), "LoadedAmmoType"), weapon);
            if (ammoDef == null || !Equals(ItemOf(ammoDef, weapon), ammo)) ammoDef = AmmoDefinitionFor(weapon, ammo) ?? ammoDef;
            var rounds = ammoDef != null && Reflect.Get(Reflect.Member(ammoDef.GetType(), "Capacity"), ammoDef) is int r ? r : 0;
            return AmmoPlanner.ItemsPerMinute(rpm, perRound, rounds);
        }
        catch (Exception e)
        {
            if (Warned.Add("rate:" + e.GetType().Name)) PluginLog.Warn("Ammo: fire rate not readable, using reserve fallback: " + e.Message);
            return 0;
        }
    }

    /// <summary>The item whose AmmoDefinition this is, via _ammoDefinitions; null when not found.</summary>
    private static object ItemOf(object ammoDef, object weapon)
    {
        foreach (var (item, def) in AmmoTypes(weapon))
            if (ReferenceEquals(def, ammoDef)) return item;
        return null;
    }

    private static object AmmoDefinitionFor(object weapon, ItemDefinition ammo)
    {
        foreach (var (item, def) in AmmoTypes(weapon))
            if (Equals(item, ammo)) return def;
        return null;
    }

    /// <summary>ProjectileWeaponBaseComponent._ammoDefinitions.AmmoTypes as (item, AmmoDefinition) pairs.</summary>
    private static List<(object Item, object Def)> AmmoTypes(object weapon)
    {
        var result = new List<(object, object)>();
        var config = Reflect.Get(Reflect.Field(weapon.GetType(), "_ammoDefinitions"), weapon);
        var map = Reflect.Get(Reflect.Member(config?.GetType(), "AmmoTypes"), config);
        if (map is not IEnumerable list) return result;
        try
        {
            foreach (var pair in list)
            {
                var t = pair.GetType();
                result.Add((Reflect.Get(Reflect.Member(t, "Key"), pair), Reflect.Get(Reflect.Member(t, "Value"), pair)));
            }
        }
        catch (Exception) { /* default/empty collection */ }
        return result;
    }

    private static object Call(Type type, string name, object target, params object[] args)
    {
        var method = type.GetMethods(Any).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length
            && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i]) || p.ParameterType == args[i].GetType()).All(x => x));
        if (method == null)
        {
            if (Warned.Add(type.Name + "." + name)) PluginLog.Warn($"Ammo: {type.Name}.{name} not found");
            return null;
        }
        return method.Invoke(target, args);
    }

    private static ItemDefinition FirstItem(object immutableArray)
    {
        try
        {
            if (immutableArray is IEnumerable list)
                foreach (var item in list)
                    if (item is ItemDefinition definition) return definition;
        }
        catch (Exception) { /* default ImmutableArray throws when enumerated */ }
        return null;
    }

    private static IEnumerable<(ItemDefinition Item, double Amount)> Amounts(object immutableArray)
    {
        var result = new List<(ItemDefinition, double)>();
        try
        {
            if (immutableArray is IEnumerable list)
                foreach (var amount in list)
                {
                    var t = amount.GetType();
                    if (Reflect.Get(Reflect.Member(t, "Item"), amount) is ItemDefinition item
                        && Reflect.Get(Reflect.Member(t, "Amount"), amount) is FixedPoint fp)
                        result.Add((item, Fx.ToDouble(fp)));
                }
        }
        catch (Exception) { /* empty */ }
        return result;
    }
}
