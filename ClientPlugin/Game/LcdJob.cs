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
    private static readonly Dictionary<(object, int), string> LastText = new Dictionary<(object, int), string>();
    /// <summary>Block name + settings each surface was last styled for; a change restyles it.</summary>
    private static readonly Dictionary<(object, int), string> Styled = new Dictionary<(object, int), string>();
    private static readonly RateTracker<(CubeGridComponent, ItemDefinition)> Rates = new RateTracker<(CubeGridComponent, ItemDefinition)>();
    private static bool unsupportedWarned;

    public static void Run(WorldScan scan, RunState state, Config config, double now)
    {
        var clock = DateTime.Now.ToString("HH:mm");
        CargoEngine.Deliveries.ShowForSeconds = config.DeliveryShowMinutes * 60.0;
        var wrote = 0;

        foreach (var grid in scan.Grids.Values)
        {
            var lcds = grid.Blocks.Where(b => b.Tags.IsLcd).ToList();
            if (lcds.Count == 0) continue;

            var infos = grid.Stores.Select(s => s.Info).ToList();
            var delivery = CargoEngine.Deliveries.Recent(grid.Grid, now);
            var rows = CargoSummary.Build(infos);
            var totals = config.LcdShowRates || lcds.Any(l => l.Tags.LcdViews.Any(v => v.Kind == LcdViewKind.Category))
                ? ItemTotals(grid, now, config.LcdShowRates)
                : null;

            foreach (var lcd in lcds)
            {
                var surfaces = LcdAdapter.Surfaces(lcd.Entity);
                if (surfaces.Count == 0) continue;
                var title = TitleOf(lcd);
                foreach (var s in surfaces) LcdAdapter.LearnFont(s);

                for (var i = 0; i < lcd.Tags.LcdViews.Count && i < surfaces.Count; i++)
                {
                    var view = lcd.Tags.LcdViews[i];
                    var width = view.Width > 0 ? view.Width : config.LcdWidth; // [Cargo Ore 54] overrides the setting
                    var text = view.Kind switch
                    {
                        LcdViewKind.Summary => LcdFormatter.Summary(title, rows, StatusLines(state, grid, delivery, now), clock, width),
                        LcdViewKind.Category => LcdFormatter.CategoryDetail(title, CargoSummary.For(infos, view.Category),
                            Lines(totals, view.Category, grid, config.LcdShowRates, delivery), clock, width, config.LcdMaxLines,
                            Footer(delivery, view.Category, now)),
                        LcdViewKind.Ruler => LcdFormatter.Ruler(160, 60),
                        _ => LcdFormatter.Alerts(title, rows, Problems(), CargoEngine.Alerts.Recent(now, config.LcdMaxLines), clock, width),
                    };

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

    private static List<ItemLine> Lines(Dictionary<ItemDefinition, double> totals, Category category, GridInfo grid, bool rates, Delivery<ItemDefinition> delivery)
    {
        var lines = new List<ItemLine>();
        if (totals == null) return lines;
        foreach (var pair in totals)
        {
            if (ItemTraitsMapper.CategoryOf(pair.Key) != category) continue;
            // Ore, ice and materials (ingots) in kg: amount × MassPerUnit. Components in pieces.
            var isMass = IsMassItem(pair.Key, category);
            var unit = isMass ? KgPerUnit(pair.Key) : 1.0;
            var rate = rates ? Rates.PerMinute((grid.Grid, pair.Key)) * unit : null;
            double? delivered = delivery != null && delivery.Items.TryGetValue(pair.Key, out var d) ? d * unit : null;
            lines.Add(new ItemLine(ItemNames.Of(pair.Key), pair.Value * unit, isMass, rate, delivered));
        }
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
    private static string Footer(Delivery<ItemDefinition> delivery, Category category, double now)
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

    private static IEnumerable<string> Problems()
    {
        if (!ConnectorAdapter.Supported) yield return "Connector API unknown (fallback mode)";
    }

    /// <summary>LCD name without tags, e.g. "Base LCD [Cargo]" → "Base LCD".</summary>
    private static string TitleOf(BlockRef lcd) => TagParser.StripTags(lcd.Name, "Grid");
}
