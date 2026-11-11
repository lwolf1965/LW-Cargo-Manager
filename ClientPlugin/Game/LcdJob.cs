using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.DCS.Components;

namespace ClientPlugin.Game;

/// <summary>
/// Writes cargo screens to LCDs tagged [Cargo], [Cargo Ore], [Cargo Components] ..., [Cargo Alerts].
/// Each tag in the block name goes to the next text surface (cockpits have several).
/// Text is only written when it changed, to avoid network traffic.
/// </summary>
internal static class LcdJob
{
    private static readonly Dictionary<(object, int), string> LastText = new();

    /// <summary>Forget the LCDs of the previous world (called on world load).</summary>
    public static void ResetWorld()
    {
        LastText.Clear();
        Styled.Clear();
    }
    private static readonly HashSet<LcdViewKind> ScreenErrors = new();
    /// <summary>Block name + settings each surface was last styled for; a change restyles it.</summary>
    private static readonly Dictionary<(object, int), string> Styled = new();
    private static readonly RateTracker<(CubeGridComponent, ItemDefinition)> Rates = new();
    private static bool unsupportedWarned;

    public static void Run(WorldScan scan, RunState state, Config config, double now)
    {
        var clock = DateTime.Now.ToString("HH:mm");
        CargoEngine.Deliveries.ShowForSeconds = config.DeliveryShowMinutes * 60.0;
        var wrote = 0;

        foreach (var grid in scan.Grids.Values)
        {
            var lcds = grid.Blocks.Where(b => b.Tags.ScreenViews.Count > 0).ToList(); // blocks without LCD surfaces are skipped below
            if (lcds.Count == 0) continue;

            var infos = grid.Stores.Select(s => s.Info).ToList();
            var delivery = CargoEngine.Deliveries.Recent(grid.Grid, now);
            var rows = CargoSummary.Build(infos);
            var totals = config.LcdShowRates || lcds.Any(l => l.Tags.ScreenViews.Any(v => v.Kind == LcdViewKind.Category))
                ? ItemTotals(grid, now, config.LcdShowRates)
                : null;

            PowerSnapshot power = null; // read once per grid, only if a power screen needs it
            foreach (var lcd in lcds)
            {
                var surfaces = LcdAdapter.Surfaces(lcd.Entity);
                if (surfaces.Count == 0) continue;
                var title = TitleOf(lcd, grid.Grid);
                foreach (var s in surfaces) LcdAdapter.LearnFont(s);

                for (var i = 0; i < lcd.Tags.ScreenViews.Count && i < surfaces.Count; i++)
                {
                    var view = lcd.Tags.ScreenViews[i];
                    if (view.Kind == LcdViewKind.Skip) continue; // screen left to the game
                    var width = view.Width > 0 ? view.Width : config.LcdWidth; // [Cargo Ore 54] overrides the setting
                    string text;
                    try
                    {
                    text = view.Kind switch
                    {
                        LcdViewKind.Summary => LcdFormatter.Summary(title, rows, StatusLines(state, grid, delivery, now), clock, width),
                        LcdViewKind.Category => LcdFormatter.CategoryDetail(title, CargoSummary.For(infos, view.Category),
                            Lines(totals, view.Category, grid, config.LcdShowRates, delivery, config.LowOreTons * 1000), clock, width, config.LcdMaxLines,
                            view.Page == 1 ? Footer(delivery, view.Category, now, view.Category == Category.Ice ? IceExtras(grid, width) : null) : null, view.Page),
                        LcdViewKind.Ruler => LcdFormatter.Ruler(160, 60),
                        LcdViewKind.Power => StatusFormatter.PowerSummary(title, power ??= StatusReader.ReadPower(grid), clock, width),
                        LcdViewKind.PowerSources => StatusFormatter.PowerSources(title, power ??= StatusReader.ReadPower(grid), clock, width, config.LcdMaxLines),
                        LcdViewKind.PowerUsers => StatusFormatter.PowerUsers(title, power ??= StatusReader.ReadPower(grid), clock, width, config.LcdMaxLines),
                        LcdViewKind.Damage => StatusFormatter.Damage(title, StatusReader.ReadDamage(grid, now), clock, width, config.LcdMaxLines),
                        LcdViewKind.Turrets => AmmoPlanner.Screen(title, AmmoJob.Status.TryGetValue(grid.Grid, out var ammo) ? ammo : new List<AmmoStatus>(), clock, width),
                        _ => LcdFormatter.Alerts(title, rows, Problems(grid, now), CargoEngine.Alerts.Recent(now, config.LcdMaxLines), clock, width),
                    };
                    }
                    catch (Exception e)
                    {
                        // A broken screen must never stop sorting and unloading: show the error on that screen only.
                        if (ScreenErrors.Add(view.Kind))
                            PluginLog.Warn($"LCD '{lcd.Name}' {view.Kind} screen failed (shown on the screen, plugin keeps running): {e}");
                        text = $"{view.Kind.ToString().ToUpperInvariant()} - screen error\n{e.GetType().Name}\nsee game log [LwCargoManager]";
                    }

                    var surface = surfaces[i];
                    var key = (surface.Panel, surface.Index);

                    // Auto-style when the tag first shows up, the name changes or the settings change.
                    if (config.LcdAutoStyle)
                    {
                        var signature = $"{lcd.Name}|{config.LcdFontSize}|{LcdAdapter.HasMonoFont}";
                        if (!Styled.TryGetValue(key, out var styled) || styled != signature)
                        {
                            if (LcdAdapter.TryApplyStyle(surface, config.LcdFontSize, padding: 2f))
                                PluginLog.Info($"LCD '{lcd.Name}' surface {surface.Index}: style applied (size {config.LcdFontSize})");
                            Styled[key] = signature;
                        }
                    }
                    var firstWrite = !LastText.TryGetValue(key, out var last);
                    if (!firstWrite && last == text) continue;
                    // LCDs are written in dry run too: showing text moves no items.
                    if (LcdAdapter.TryWrite(surface, text, setContentMode: firstWrite))
                    {
                        LastText[key] = text;
                        wrote++;
                    }
                    else if (!unsupportedWarned)
                    {
                        unsupportedWarned = true;
                        PluginLog.Warn($"LCD '{lcd.Name}': LCD text API not found on {surface.Panel.GetType().FullName}. See the discovery report.");
                    }
                }
            }
        }

        if (wrote > 0) PluginLog.Verbose($"LCD: updated {wrote} surface(s)");
        if (LastText.Count > 2000) LastText.Clear(); // removed blocks; cheap reset
        if (Styled.Count > 2000) Styled.Clear();
        Rates.Prune(now);
    }

    /// <summary>Amount per item on this grid's storage (all Storage inventories, also [Lock] ones).</summary>
    private static Dictionary<ItemDefinition, double> ItemTotals(GridInfo grid, double now, bool trackRates)
    {
        var totals = new Dictionary<ItemDefinition, double>();
        foreach (var store in grid.Stores)
        {
            if (store.Info.Role != BlockRole.Storage) continue;
            var items = store.Inventory.Items;
            for (var i = 0; i < items.Length; i++)
            {
                totals.TryGetValue(items[i].Definition, out var sum);
                totals[items[i].Definition] = sum + Fx.ToDouble(items[i].Amount);
            }
        }

        if (trackRates)
        {
            foreach (var pair in totals)
                Rates.Record((grid.Grid, pair.Key), pair.Value, now);
            // Items that ran out must drop to 0 in the rate history.
            foreach (var key in Rates.Keys.Where(k => ReferenceEquals(k.Item1, grid.Grid) && !totals.ContainsKey(k.Item2)).ToList())
                Rates.Record(key, 0, now);
        }
        return totals;
    }

    private static List<ItemLine> Lines(Dictionary<ItemDefinition, double> totals, Category category, GridInfo grid, bool rates, Delivery<ItemDefinition> delivery, double lowOreKg = 0)
    {
        var lines = new List<ItemLine>();
        if (totals == null) return lines;
        foreach (var pair in totals)
        {
            if (ItemTraitsMapper.CategoryOf(pair.Key) != category) continue;
            // Ore, ice and refined metals in kg: amount × MassPerUnit. Components in pieces.
            var isMass = IsMassItem(pair.Key, category);
            var unit = isMass ? KgPerUnit(pair.Key) : 1.0;
            var rate = rates ? Rates.PerMinute((grid.Grid, pair.Key)) * unit : null;
            double? delivered = delivery != null && delivery.Items.TryGetValue(pair.Key, out var d) ? d * unit : null;
            var amount = pair.Value * unit;
            var name = ItemNames.Of(pair.Key);
            var low = false;
            if (category == Category.Ore)
            {
                name = ItemPaths.ShortOreName(name);
                low = lowOreKg > 0 && amount < lowOreKg;
            }
            lines.Add(new ItemLine(name, amount, isMass, rate, delivered, low));
        }
        return lines;
    }

    /// <summary>Ice screen extras: tank fill, then hydrogen / oxygen full-load planning.</summary>
    private static List<string> IceExtras(GridInfo grid, int width)
    {
        var tanks = StatusReader.ReadGas(grid);
        var lines = StatusFormatter.GasLines(tanks, width);
        var (h2, o2) = StatusReader.ReadGasBalance(grid);
        if (h2 != null) lines.AddRange(Planner.Advice(h2, tanks.Where(t => t.Gas == GasKind.Hydrogen).Sum(t => t.Stored), StatusFormatter.Volume, "tanks", width));
        if (o2 != null) lines.AddRange(Planner.Advice(o2, tanks.Where(t => t.Gas == GasKind.Oxygen).Sum(t => t.Stored), StatusFormatter.Volume, "tanks", width));
        return lines;
    }

    private static IEnumerable<string> StatusLines(RunState state, GridInfo grid, Delivery<ItemDefinition> delivery, double now)
    {
        if (state.DockStatus.TryGetValue(grid.Grid, out var lines))
            foreach (var line in lines) yield return line;
        if (delivery != null)
            yield return $"Last delivery {LcdFormatter.Age(now - delivery.Last)} ({delivery.Dock})";
    }

    /// <summary>"Last delivery: 5.2 t, 4m ago (Dock A)" for the items of this category, or null.</summary>
    private static string Footer(Delivery<ItemDefinition> delivery, Category category, double now, List<string> extra = null)
    {
        var tail = extra != null && extra.Count > 0 ? string.Join("\n", extra) : null;
        var last = LastDelivery(delivery, category, now);
        if (last == null) return tail;
        return tail == null ? last : last + "\n" + tail;
    }

    private static string LastDelivery(Delivery<ItemDefinition> delivery, Category category, double now)
    {
        if (delivery == null) return null;
        // Always kg here: a delivery can mix materials and components.
        var amount = delivery.Items.Where(p => ItemTraitsMapper.CategoryOf(p.Key) == category)
            .Sum(p => p.Value * KgPerUnit(p.Key));
        return amount > 0 ? LcdFormatter.DeliveryLine(amount, true, now - delivery.Last, delivery.Dock) : null;
    }

    private static bool IsMassItem(ItemDefinition item, Category category) =>
        CargoSummary.IsMassCategory(category)
        || (category == Category.Parts && (ItemTraitsMapper.TraitsOf(item) & ItemTraits.Material) != 0);

    /// <summary>kg per unit of an item (ItemDefinition.MassPerUnit). 1 if unknown or zero.</summary>
    private static double KgPerUnit(ItemDefinition definition)
    {
        var mpu = Fx.ToDouble(definition.MassPerUnit);
        return mpu > 0 ? mpu : 1.0;
    }

    private static IEnumerable<string> Problems(GridInfo grid, double now)
    {
        if (!ConnectorAdapter.Supported) yield return "Connector API unknown (fallback mode)";
        foreach (var line in ConveyorAlerts.For(grid, now)) yield return line;
    }

    /// <summary>
    /// LCD name without tags ("Base LCD [Cargo]" → "Base LCD"). An LCD named only with tags shows the
    /// grid's name ("Black Widow"), else "Grid".
    /// </summary>
    private static string TitleOf(BlockRef lcd, CubeGridComponent grid)
    {
        var own = TagParser.StripTags(lcd.Name, "");
        return own.Length > 0 ? own : GridNames.Of(grid) ?? "Grid";
    }
}
