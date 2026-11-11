using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;

namespace LwCargoManager.Tests;

public static class LcdTagTests
{
    public static void CargoViews()
    {
        Assert.Equal(new LcdView(LcdViewKind.Summary, Category.None), TagParser.Parse("LCD 1 [Cargo]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Ore), TagParser.Parse("LCD [cargo ore]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Parts), TagParser.Parse("LCD [Cargo Parts]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Alerts, Category.None), TagParser.Parse("LCD [Cargo Alerts]").LcdViews[0]);
    }

    public static void CargoTagIsNotAContainerTag()
    {
        var t = TagParser.Parse("LCD [Cargo Ore]");
        Assert.True(t.IsLcd);
        Assert.False(t.HasAnyCategory, "[Cargo Ore] must not make the block an [Ore] destination");
    }

    public static void SeveralViewsInOrder()
    {
        var t = TagParser.Parse("Cockpit [Cargo] [Cargo Ore]");
        Assert.Equal(2, t.LcdViews.Count);
        Assert.Equal(LcdViewKind.Summary, t.LcdViews[0].Kind);
        Assert.Equal(Category.Ore, t.LcdViews[1].Category);
    }

    public static void UnknownCargoWordIgnored()
    {
        Assert.False(TagParser.Parse("LCD [Cargo Banana]").IsLcd);
    }
}

public static class CargoSummaryTests
{
    private static int id;

    private static StoreInfo Box(string name, double max, double used, bool slotsFull = false, BlockRole role = BlockRole.Storage) =>
        new()
        { Id = ++id, Role = role, Tags = TagParser.Parse(name), MaxMass = max, CurrentMass = used, IsEmpty = used == 0, SlotsFull = slotsFull };

    public static void CapacityIsSumOfTaggedContainers()
    {
        // Example from the design: 20/20 + 10/20 + 0/10 = 30 / 50 t = 60 %
        var stores = new[] { Box("Cargo 1 [Ore P1]", 20000, 20000), Box("Cargo 2 [Ore]", 20000, 10000), Box("Cargo 3 [Ore]", 10000, 0) };
        var ore = CargoSummary.Build(stores).First(r => r.Category == Category.Ore);
        Assert.Equal(30000.0, ore.Used);
        Assert.Equal(50000.0, ore.Max);
        Assert.Equal(3, ore.Containers);
        Assert.False(ore.Full);
        Assert.Equal("60%", LcdFormatter.Percent(ore));
    }

    public static void RowsInCategoryOrderThenUntaggedThenTotal()
    {
        var rows = CargoSummary.Build(new[] { Box("[Parts]", 100, 10), Box("[Ore]", 100, 50), Box("Cargo", 100, 5) });
        Assert.Equal("Ore", rows[0].Label);
        Assert.Equal("Parts", rows[1].Label);
        Assert.Equal("Untagged", rows[2].Label);
        Assert.Equal("Total", rows[3].Label);
        Assert.Equal(300.0, rows[3].Max);
    }

    public static void LockedAndNonStorageNotCounted()
    {
        var rows = CargoSummary.Build(new[] { Box("[Ore]", 100, 50), Box("[Ore] [Lock]", 1000, 0), Box("Refinery", 500, 0, role: BlockRole.Producer) });
        var ore = rows.First(r => r.Category == Category.Ore);
        Assert.Equal(100.0, ore.Max);
        Assert.Equal(1, ore.Containers);
        Assert.Equal(100.0, rows.Last().Max, "total");
    }

    public static void FullBySlots()
    {
        var rows = CargoSummary.Build(new[] { Box("[Parts]", 20000, 12000, slotsFull: true) });
        var comp = rows[0];
        Assert.True(comp.Full);
        Assert.Equal("FULL", LcdFormatter.Percent(comp));
    }

    public static void NotFullIfOneContainerHasRoom()
    {
        var rows = CargoSummary.Build(new[] { Box("[Ore]", 100, 100), Box("[Ore]", 100, 20) });
        Assert.False(rows[0].Full);
    }

    public static void NoUntaggedRowWhenAllTagged()
    {
        var rows = CargoSummary.Build(new[] { Box("[Ore]", 100, 10) });
        Assert.False(rows.Any(r => r.Label == "Untagged"));
    }
}

public static class LcdFormatterTests
{
    public static void Bars()
    {
        Assert.Equal("..........", LcdFormatter.Bar(0));
        Assert.Equal("#####.....", LcdFormatter.Bar(0.5));
        Assert.Equal("##########", LcdFormatter.Bar(1.2), "clamped");
        Assert.Equal("######....", LcdFormatter.Bar(0.6));
    }

    public static void MassUnits()
    {
        Assert.Equal("850 kg", LcdFormatter.Mass(850));
        Assert.Equal("24.3 t", LcdFormatter.Mass(24300));
        Assert.Equal("1.2 kt", LcdFormatter.Mass(1_200_000));
    }

    public static void CountsAndRates()
    {
        Assert.Equal("1 250", LcdFormatter.Count(1250));
        Assert.Equal("+1.2 t/min", LcdFormatter.Rate(1200, true));
        Assert.Equal("-500 kg/min", LcdFormatter.Rate(-500, true));
        Assert.Equal("+40/min", LcdFormatter.Rate(40, false));
    }

    public static void HeaderFitsWidth()
    {
        var h = LcdFormatter.Header("LW CARGO - A very long base name here", "12:41", 30);
        Assert.Equal(30, h.Length);
        Assert.True(h.EndsWith(" 12:41"));
    }

    public static void SummaryScreen()
    {
        var rows = new List<CategorySummary>
        {
            new() { Label = "Ore", Category = Category.Ore, Used = 30000, Max = 50000, Containers = 3 },
            new() { Label = "Parts", Category = Category.Parts, Used = 12000, Max = 20000, Containers = 1, Full = true },
            new() { Label = "Total", Used = 42000, Max = 70000, Containers = 4 },
        };
        var text = LcdFormatter.Summary("Mining Base", rows, new[] { "Dock A: unloading" }, "12:41", 60);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal(5, lines.Length);
        Assert.True(lines[0].StartsWith("LW CARGO - Mining Base"));
        Assert.Equal("Ore        ######....  60%  30.0 t / 50.0 t (3)", lines[1]);
        Assert.Equal("Parts      ######.... FULL  12.0 t / 20.0 t (1)", lines[2]);
        Assert.Equal("Total 42.0 t / 70.0 t (60%)", lines[3]);
        Assert.Equal("Dock A: unloading", lines[4]);
    }

    public static void CategoryScreenSortedAndLimited()
    {
        var cap = new CategorySummary { Label = "Ore", Category = Category.Ore, Used = 41000, Max = 50000, Containers = 3 };
        var items = new List<ItemLine>
        {
            new("Stone", 3000, true, null),
            new("Iron", 24300, true, 1200),
            new("Nickel", 8100, true, null),
            new("Gold", 0, true, null),
            new("Silicon", 5600, true, -500),
        };
        var text = LcdFormatter.CategoryDetail("Mining Base", cap, items, "", 60, maxLines: 3);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("ORE 1/2 - Mining Base", lines[0]);
        Assert.True(lines[1].Contains("82%"), lines[1]);
        Assert.True(lines[2].StartsWith("Iron ") && lines[2].EndsWith("24.3 t  +1.2 t/min"), lines[2]);
        Assert.True(lines[3].StartsWith("Nickel ") && lines[3].EndsWith("8.1 t"), lines[3]);
        Assert.True(lines[4].EndsWith("5.6 t  -500 kg/min"), lines[4]);
        Assert.Equal("... +1 more (page 2)", lines[5], "Stone hidden, Gold (0) skipped");
        var page2 = LcdFormatter.CategoryDetail("Mining Base", cap, items, "", 60, 3, page: 2).Split('\n');
        Assert.Equal("ORE 2/2 - Mining Base", page2[0].TrimEnd('\r'));
        Assert.True(page2[2].StartsWith("Stone"), page2[2]);
    }

    public static void CategoryScreenWithoutContainers()
    {
        var cap = new CategorySummary { Label = "Ore", Category = Category.Ore, Containers = 0 };
        var text = LcdFormatter.CategoryDetail("Ship", cap, new List<ItemLine>(), "", 40, 10);
        Assert.True(text.Contains("No [Ore] containers"));
        Assert.True(text.Contains("(empty)"));
    }

    public static void ComponentsAsCounts()
    {
        var cap = new CategorySummary { Label = "Parts", Category = Category.Parts, Used = 1, Max = 10, Containers = 1 };
        var text = LcdFormatter.CategoryDetail("Base", cap, new[] { new ItemLine("Steel Plate", 1250, false, null) }, "", 40, 10);
        Assert.True(text.Contains("Steel Plate") && text.Contains("1 250"), text);
    }

    public static void AlertsScreen()
    {
        var rows = new List<CategorySummary>
        {
            new() { Label = "Ore", Full = true, Containers = 2, Max = 1 },
            new() { Label = "Total", Full = true, Containers = 2, Max = 1 },
        };
        var text = LcdFormatter.Alerts("Base", rows, new[] { "Plugin stopped: test" }, new[] { "now     Unloaded Ore 1200" }, "", 40);
        Assert.True(text.Contains("! Plugin stopped: test"));
        Assert.True(text.Contains("! Ore storage full"));
        Assert.False(text.Contains("Total storage full"));
        Assert.True(text.Contains("-- recent --"));
        var ok = LcdFormatter.Alerts("Base", new List<CategorySummary>(), null, null, "", 40);
        Assert.True(ok.Contains("All good"));
    }
}

public static class RateTrackerTests
{
    public static void NeedsMinimumSpan()
    {
        var r = new RateTracker<string>();
        r.Record("iron", 1000, 0);
        r.Record("iron", 1500, 10);
        Assert.True(r.PerMinute("iron") == null, "10 s is too short");
        r.Record("iron", 2000, 60);
        Assert.Equal(1000.0, Math.Round(r.PerMinute("iron").Value, 6), "1000 kg in 60 s");
    }

    public static void SlidingWindow()
    {
        var r = new RateTracker<string> { WindowSeconds = 120 };
        r.Record("iron", 0, 0);
        r.Record("iron", 6000, 60);
        r.Record("iron", 6000, 180); // first sample now older than the window
        Assert.Equal(0.0, Math.Round(r.PerMinute("iron") ?? 0), "flat in the last window");
    }

    public static void DeadBand()
    {
        var r = new RateTracker<string> { DeadBandPerMinute = 1 };
        r.Record("x", 100, 0);
        r.Record("x", 100.2, 60);
        Assert.True(r.PerMinute("x") == null);
    }

    public static void UnknownKey()
    {
        Assert.True(new RateTracker<string>().PerMinute("nope") == null);
    }
}

public static class AlertLogTests
{
    public static void NewestFirstWithAges()
    {
        var log = new AlertLog();
        log.Add("first", 0);
        log.Add("second", 100);
        var recent = log.Recent(130, 5);
        Assert.Equal(2, recent.Count);
        Assert.True(recent[0].StartsWith("now") && recent[0].EndsWith("second"), recent[0]);
        Assert.True(recent[1].StartsWith("2m ago") && recent[1].EndsWith("first"), recent[1]);
    }

    public static void RepeatRefreshes()
    {
        var log = new AlertLog();
        log.Add("full", 0);
        log.Add("full", 50);
        Assert.Equal(1, log.Recent(50, 5).Count);
    }

    public static void CapacityAndAge()
    {
        var log = new AlertLog { Capacity = 2, MaxAgeSeconds = 100 };
        log.Add("a", 0); log.Add("b", 1); log.Add("c", 2);
        Assert.Equal(2, log.Recent(2, 5).Count);
        Assert.Equal(0, log.Recent(500, 5).Count, "too old");
    }
}

public static class StripTagsTests
{
    public static void Strips()
    {
        Assert.Equal("Base LCD", TagParser.StripTags("Base LCD [Cargo]"));
        Assert.Equal("Dock A", TagParser.StripTags("Dock [x] A [Unload]"));
        Assert.Equal("Grid", TagParser.StripTags("[Cargo Ore]", "Grid"));
        Assert.Equal("", TagParser.StripTags(null));
    }
}

public static class DeliveryTests
{
    public static void PassesMergeIntoOneDelivery()
    {
        var t = new DeliveryTracker<int, string>();
        t.Add(1, "Dock A", "iron", 1000, 0);
        t.Add(1, "Dock A", "iron", 500, 1);
        t.Add(1, "Dock A", "nickel", 300, 2);
        var d = t.Recent(1, 10);
        Assert.Equal(1500.0, d.Items["iron"]);
        Assert.Equal(300.0, d.Items["nickel"]);
        Assert.Equal("Dock A", d.Dock);
    }

    public static void GapStartsNewDelivery()
    {
        var t = new DeliveryTracker<int, string>();
        t.Add(1, "Dock A", "iron", 1000, 0);
        t.Add(1, "Dock A", "iron", 200, 100); // > 30 s later: new ship
        Assert.Equal(200.0, t.Recent(1, 100).Items["iron"]);
    }

    public static void OtherDockStartsNewDelivery()
    {
        var t = new DeliveryTracker<int, string>();
        t.Add(1, "Dock A", "iron", 1000, 0);
        t.Add(1, "Dock B", "stone", 50, 5);
        var d = t.Recent(1, 5);
        Assert.Equal("Dock B", d.Dock);
        Assert.False(d.Items.ContainsKey("iron"));
    }

    public static void HiddenAfterOneHour()
    {
        var t = new DeliveryTracker<int, string>();
        t.Add(1, "Dock A", "iron", 1000, 0);
        Assert.True(t.Recent(1, 3600) != null);
        Assert.True(t.Recent(1, 3601) == null);
        Assert.True(t.Recent(2, 0) == null, "other grid");
    }

    public static void IgnoresZero()
    {
        var t = new DeliveryTracker<int, string>();
        t.Add(1, "Dock A", "iron", 0, 0);
        Assert.True(t.Recent(1, 0) == null);
    }

    public static void DeliveredColumnAndFooter()
    {
        var cap = new CategorySummary { Label = "Ore", Category = Category.Ore, Used = 41000, Max = 50000, Containers = 3 };
        var items = new List<ItemLine>
        {
            new("Iron", 24300, true, null, 3100),
            new("Silicon", 5600, true, null, null),
        };
        var footer = LcdFormatter.DeliveryLine(5200, true, 240, "Dock A");
        var text = LcdFormatter.CategoryDetail("Mining Base", cap, items, "", 40, 10, footer);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.True(lines[2].StartsWith("Iron ") && lines[2].EndsWith("24.3 t  +3.1 t"), lines[2]);
        Assert.True(lines[3].StartsWith("Silicon ") && lines[3].EndsWith("5.6 t"), lines[3]);
        Assert.True(lines.All(l => l.Length <= 40));
        Assert.Equal("Last delivery: 5.2 t, 4m ago (Dock A)", lines[4]);
    }
}

public static class RulerTests
{
    public static void Pattern()
    {
        var lines = LcdFormatter.Ruler(20, 4).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("----+----1----+----2", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Equal("02", lines[1]);
        Assert.Equal("04", lines[3]);
        Assert.Equal(new LcdView(LcdViewKind.Ruler, Category.None), TagParser.Parse("Test [Cargo Ruler]").LcdViews[0]);
    }
}

public static class CompactLcdTests
{
    public static void NarrowScreenDropsMax()
    {
        var rows = new List<CategorySummary>
        {
            new() { Label = "Ore", Category = Category.Ore, Used = 1900, Max = 10_800_000, Containers = 1 },
            new() { Label = "Total", Used = 10300, Max = 344_800_000, Containers = 36 },
        };
        var lines = LcdFormatter.Summary("LCD", rows, null, "15:17", 36).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("Ore       ..........   0% 1.9 t (1)", lines[1]);
        Assert.Equal("Total 10.3 t (0%)", lines[2]);
    }

    public static void EveryLineFitsWidth()
    {
        var rows = new List<CategorySummary>
        {
            new() { Label = "Ore", Category = Category.Ore, Used = 10_260, Max = 10_750_000, Containers = 1 },
            new() { Label = "Consumables", Category = Category.Consumables, Used = 999_000_000, Max = 999_000_000, Containers = 12 },
            new() { Label = "Untagged", Used = 0, Max = 334_000_000, Containers = 35 },
            new() { Label = "Total", Used = 10_300, Max = 344_800_000, Containers = 36 },
        };
        foreach (var width in new[] { 20, 24, 30, 31, 36 })
        {
            var text = LcdFormatter.Summary("LCD", rows, null, "15:43", width);
            foreach (var line in text.Split('\n'))
                Assert.True(line.TrimEnd('\r').Length <= width, $"width {width}: '{line}'");
        }
        var thirty = LcdFormatter.Summary("LCD", rows, null, "15:43", 30).Split('\n');
        Assert.Equal("Ore       ....   0% 10.3 t (1)", thirty[1].TrimEnd('\r'));
    }

    public static void WidthInName()
    {
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Ore, 54), TagParser.Parse("LCD [Cargo Ore 54]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Summary, Category.None, 100), TagParser.Parse("Wide [cargo 100]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Summary, Category.None, 0), TagParser.Parse("[Cargo]").LcdViews[0]);
        Assert.Equal(0, TagParser.Parse("[Cargo Ore 54]").Items.Count, "not an item tag");
    }
}

public static class StatusTests
{
    public static void StatusTags()
    {
        Assert.Equal(new LcdView(LcdViewKind.Power, Category.None, 0), TagParser.Parse("LCD [Power]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Power, Category.None, 0), TagParser.Parse("LCD [Status Power]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.PowerSources, Category.None, 54), TagParser.Parse("[Power Sources 54]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.PowerUsers, Category.None, 0), TagParser.Parse("[power users]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Damage, Category.None, 40), TagParser.Parse("[Damage 40]").LcdViews[0]);
        Assert.Equal(1, TagParser.Parse("Box [Power Module]").Items.Count, "item tag, not a screen");
        Assert.Equal(0, TagParser.Parse("Box [Power Module]").LcdViews.Count);
    }

    private static PowerSnapshot Sample()
    {
        var p = new PowerSnapshot();
        p.Sources.Add(new PowerSource("Reactor 2.5 m", PowerSourceKind.Reactor, true));
        p.Sources.Add(new PowerSource("Solar Panel 5 m", PowerSourceKind.Solar, true));
        p.Sources.Add(new PowerSource("Solar Panel 5 m", PowerSourceKind.Solar, false));
        p.Sources.Add(new PowerSource("Battery 2.5 m", PowerSourceKind.Battery, true, 3, 4, 0.5));
        p.Sources.Add(new PowerSource("Battery 2.5 m", PowerSourceKind.Battery, true, 1, 4, 0.5));
        p.Users.Add(new PowerUser("Refinery 7.5 m", 1.8, true));
        p.Users.Add(new PowerUser("Assembler 5 m", 0.3, true));
        p.Users.Add(new PowerUser("Assembler 5 m", 0.3, true));
        return p;
    }

    public static void EnergyBalance()
    {
        var p = Sample();
        Assert.Equal(2.4, Math.Round(p.UseMW, 3));
        Assert.Equal(1.0, Math.Round(p.BatteryFlowMW, 3));
        Assert.Equal(3.4, Math.Round(p.MakeMW, 3), "make = use + charging");
        Assert.Equal(0.5, p.BatteryStoredMWh / p.BatteryMaxMWh);
    }

    public static void PowerScreens()
    {
        var p = Sample();
        var summary = StatusFormatter.PowerSummary("Base", p, "18:00", 36);
        Assert.True(summary.Contains("Make  3.4 MW"), summary);
        Assert.True(summary.Contains("charging 1.0 MW"), summary);
        Assert.True(summary.Contains("! 1 generator(s) off"), summary);

        var sources = StatusFormatter.PowerSources("Base", p, "", 40, 10);
        Assert.True(sources.Contains("Solar Panel 5 m x2"), sources);
        Assert.True(sources.Contains("1/2 on"), sources);
        Assert.True(sources.Contains("50%"), sources);

        var users = StatusFormatter.PowerUsers("Base", p, "", 40, 10).Split('\n');
        Assert.True(users[2].StartsWith("Refinery 7.5 m"), "biggest first");
        Assert.True(users[3].StartsWith("Assembler 5 m x2"));
        foreach (var w in new[] { 24, 30, 54 })
            foreach (var line in StatusFormatter.PowerSources("Base", p, "18:00", w, 10).Split('\n'))
                Assert.True(line.Length <= w);
    }

    public static void Units()
    {
        Assert.Equal("850 kW", StatusFormatter.Power(0.85));
        Assert.Equal("4.2 MW", StatusFormatter.Power(4.2));
        Assert.Equal("12.0 MWh", StatusFormatter.Energy(12));
        Assert.Equal("2h 30m", StatusFormatter.Duration(2.5));
        Assert.Equal("-", StatusFormatter.Duration(double.PositiveInfinity));
    }

    public static void DamageScreen()
    {
        var d = new DamageSnapshot { Blocks = 1245 };
        d.Problems.Add(new DamagedBlock("Battery 2.5 m", 0.23, 1));
        d.Problems.Add(new DamagedBlock("Connector 1.5 m", 1, 0.4));
        var text = StatusFormatter.Damage("Base", d, "", 40, 10);
        Assert.True(text.Contains("Blocks 1 245  damaged 1  unfinished 1"), text);
        var lines = text.Split('\n');
        Assert.True(lines[2].StartsWith("Battery 2.5 m"), "worst first");
        Assert.True(lines[3].Contains("build  40%"), lines[3]);

        var many = new DamageSnapshot { Blocks = 100 };
        foreach (var b in new[] { 0.01, 0.01, 0.02, 0.05, 0.10, 0.21, 0.71 })
            many.Problems.Add(new DamagedBlock("Light", 1, b));
        many.Problems.Add(new DamagedBlock("Light", 0.5, 1));
        var groups = StatusFormatter.GroupDamage(many.Problems);
        Assert.Equal(5, groups.Count, "1,1,2,5 | 10 | 21 | 71 build + one damaged");
        Assert.Equal(new StatusFormatter.DamageGroup("Light", true, 4, 1, 5, 0.01), groups[0]);
        var grouped = StatusFormatter.Damage("Base", many, "", 40, 10);
        Assert.True(grouped.Contains("Light x4"), grouped);
        Assert.True(grouped.Contains("build 1-5%"), grouped);
        Assert.True(StatusFormatter.Damage("Base", new DamageSnapshot { Blocks = 10 }, "", 40, 10).Contains("No damage"));
        Assert.False(DamageRules.IsProblem(0.9995, 1));
    }
}

public static class PartsScreenTests
{
    public static void TagsWithPageAndFilter()
    {
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Parts, 0, 2), TagParser.Parse("[Cargo Parts 2]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Parts, 54, 2), TagParser.Parse("[Cargo Parts 2 54]").LcdViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Parts, 54, 1), TagParser.Parse("[Cargo Parts 54]").LcdViews[0]);
        Assert.Equal(0, TagParser.Parse("LCD [Cargo Ingots]").LcdViews.Count, "old filtered views removed");
        Assert.Equal(0, TagParser.Parse("LCD [Components 2]").ScreenViews.Count, "old filtered views removed");
    }

    public static void LongerNames()
    {
        var cap = new CategorySummary { Label = "Parts", Category = Category.Parts, Used = 1, Max = 10, Containers = 1 };
        var items = new[] { new ItemLine("Electronic Parts", 800, false, null), new ItemLine("Radiation Shielding", 102, false, null) };
        var text = LcdFormatter.CategoryDetail("Base", cap, items, "", 36, 10);
        Assert.True(text.Contains("Electronic Parts"), text);
        Assert.True(text.Contains("Radiation Shielding"), text);
        foreach (var line in text.Split('\n')) Assert.True(line.Length <= 36, line);
    }
}

public static class ShortScreenTests
{
    public static void WithoutCargoWord()
    {
        var t = TagParser.Parse("Wall [Parts 54]");
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Parts, 54, 1), t.ScreenViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Parts, 0, 2), TagParser.Parse("[Parts 2]").ScreenViews[0]);
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Ore, 0, 1), TagParser.Parse("[Ore]").ScreenViews[0]);
        Assert.True(TagParser.Parse("[Ore]").Accepts(Category.Ore), "still a storage tag on containers");
        Assert.Equal(0, TagParser.Parse("[Ore]").LcdViews.Count, "explicit list unchanged");

        var cockpit = TagParser.Parse("Cockpit [Cargo] [Ore 40] [Power]").ScreenViews;
        Assert.Equal(3, cockpit.Count);
        Assert.Equal(LcdViewKind.Summary, cockpit[0].Kind);
        Assert.Equal(LcdViewKind.Category, cockpit[1].Kind);
        Assert.Equal(LcdViewKind.Power, cockpit[2].Kind);
        Assert.Equal(0, TagParser.Parse("[Si] [Main] [Unload]").ScreenViews.Count);
    }
}

public static class OreScreenTests
{
    public static void ShortNamesAndLow()
    {
        Assert.Equal("Titanium", ItemPaths.ShortOreName("Titanium Ore"));
        Assert.Equal("Stone", ItemPaths.ShortOreName("Stone"));
        Assert.Equal("Ice", ItemPaths.NameFromPath(@"x : Vanilla\Items\Ores\IceOre\IceOreItem.def"));
        Assert.Equal("Iron Ore", ItemPaths.NameFromPath(@"x : Vanilla\Items\Ores\IronOre\IronOre_Item.def"), "full name kept for item tags");

        var cap = new CategorySummary { Label = "Ore", Category = Category.Ore, Used = 1, Max = 10, Containers = 1 };
        var items = new[] { new ItemLine("Uranium", 430300, true, null), new ItemLine("Gold", 20, true, null, null, true) };
        var lines = LcdFormatter.CategoryDetail("Base", cap, items, "", 40, 10).Split('\n');
        Assert.True(lines[3].StartsWith("Gold") && lines[3].EndsWith("20 kg (Low)"), lines[3]);
        Assert.False(lines[2].Contains("(Low)"));
        Assert.True(lines.All(l => l.Length <= 40));
    }
}

public static class SkipScreenTests
{
    public static void SkipKeepsPosition()
    {
        var v = TagParser.Parse("Cockpit [Cargo] [Skip] [Power]").ScreenViews;
        Assert.Equal(3, v.Count);
        Assert.Equal(LcdViewKind.Skip, v[1].Kind);
        Assert.Equal(LcdViewKind.Power, v[2].Kind, "power goes to screen 3");
        Assert.Equal(0, TagParser.Parse("[Skip]").Items.Count, "not an item tag");
    }
}

public static class GeneratorOutputTests
{
    public static void OutputShownWhenKnown()
    {
        var p = new PowerSnapshot();
        p.Sources.Add(new PowerSource("Reactor 2.5 m", PowerSourceKind.Reactor, true, OutputMW: 2.5, OutputMaxMW: 3));
        p.Sources.Add(new PowerSource("Reactor 2.5 m", PowerSourceKind.Reactor, true, OutputMW: 1.7, OutputMaxMW: 3));
        p.Sources.Add(new PowerSource("Wind Turbine 7.5 m", PowerSourceKind.Wind, true));
        var text = StatusFormatter.PowerSources("Base", p, "", 54, 10);
        Assert.True(text.Contains("Reactor 2.5 m x2") && text.Contains("4.2 MW / 6.0 MW"), text);
        Assert.True(text.Contains("Wind Turbine 7.5 m") && text.Contains(" on"), "unknown output: on/off");
    }
}

public static class AvailablePowerTests
{
    public static void CanLineAndNoOutput()
    {
        var p = new PowerSnapshot();
        p.Sources.Add(new PowerSource("Solar Panel 10 m", PowerSourceKind.Solar, true, OutputMW: 0.7, OutputMaxMW: 16, OutputNowMaxMW: 15));
        p.Sources.Add(new PowerSource("Reactor 2.5 m", PowerSourceKind.Reactor, true, OutputMW: 0, OutputMaxMW: 20, OutputNowMaxMW: 20));
        p.Sources.Add(new PowerSource("Wind Turbine 7.5 m", PowerSourceKind.Wind, true, OutputMW: 0, OutputMaxMW: 0, OutputNowMaxMW: 0));
        p.Users.Add(new PowerUser("Refinery", 0.7, true));
        var summary = StatusFormatter.PowerSummary("Base", p, "", 54);
        Assert.True(summary.Contains("Can   35.0 MW now / 36.0 MW max"), summary);
        var sources = StatusFormatter.PowerSources("Base", p, "", 54, 10);
        Assert.True(sources.Contains("no output"), sources);
    }
}

public static class GasAndConsumerTests
{
    public static void GasLines()
    {
        var tanks = new List<GasTank>
        {
            new("Hydrogen Tank 5 m", GasKind.Hydrogen, 80, 100),
            new("Hydrogen Tank 5 m", GasKind.Hydrogen, 82, 100),
            new("Oxygen Tank 1.5 m", GasKind.Oxygen, 10, 100),
        };
        var lines = StatusFormatter.GasLines(tanks, 40);
        Assert.Equal(2, lines.Count);
        Assert.True(lines[0].StartsWith("Hydrogen") && lines[0].Contains("81%  162 (2)"), lines[0]);
        var wide = StatusFormatter.GasLines(tanks, 54);
        Assert.True(wide[0].Contains("81%  162 / 200 (2)"), wide[0]);
        Assert.Equal("64.0k", StatusFormatter.Volume(64000));
        Assert.Equal("1.8M", StatusFormatter.Volume(1_792_000));
        Assert.True(lines[1].StartsWith("Oxygen") && lines[1].EndsWith("LOW"), lines[1]);
        Assert.Equal(0, StatusFormatter.GasLines(new List<GasTank>(), 40).Count);
    }

    public static void IdleConsumersListed()
    {
        var p = new PowerSnapshot();
        p.Users.Add(new PowerUser("Refinery 7.5 m", 1.8, true));
        p.Users.Add(new PowerUser("Ion Thruster 5 m", 0, true));
        p.Users.Add(new PowerUser("Ion Thruster 5 m", 0, true));
        var lines = StatusFormatter.PowerUsers("Base", p, "", 40, 10).Split('\n');
        Assert.True(lines[0].StartsWith("CONSUMERS"));
        Assert.True(lines[2].StartsWith("Refinery"), "active first");
        Assert.True(lines[3].StartsWith("Ion Thruster 5 m x2") && lines[3].Contains("idle"), lines[3]);
    }
}

public static class PlanningTests
{
    private static string Mw(double v) => StatusFormatter.Power(v);

    public static void PowerShortAndOk()
    {
        var b = new ResourceBalance { Kind = ResourceKind.Power, UseMax = 254 };
        b.Producers.Add(new Producer("Reactor 7.5 m", false, 80, 80, 0));
        b.Producers.Add(new Producer("Reactor 2.5 m", false, 20, 20, 0));
        b.Producers.Add(new Producer("Solar Panel 10 m", true, 16, 15, 0.7));
        var lines = Planner.Advice(b, 47, Mw, "batteries", 40);
        Assert.True(lines[0].Contains("use 254.0 MW max / can 115.0 MW"), lines[0]);
        Assert.True(lines[1].Contains("SHORT 139.0 MW"), lines[1]);
        Assert.True(lines[2].Contains("batteries cover 20m"), lines[2]);
        Assert.True(lines[3].Contains("add 2x Reactor 7.5 m (80.0 MW)"), lines[3]);

        b.UseMax = 50;
        var ok = Planner.Advice(b, 47, Mw, "batteries", 40);
        Assert.True(ok[1].Contains("OK, 65.0 MW spare"), ok[1]);
    }

    public static void GasShort()
    {
        var b = new ResourceBalance { Kind = ResourceKind.Hydrogen, UseMax = 3000 };
        b.Producers.Add(new Producer("O2/H2 Generator 5 m", false, 1000, 1000, 0));
        var lines = Planner.Advice(b, 0, StatusFormatter.Volume, "tanks", 40);
        Assert.True(lines[0].StartsWith("H2") && lines[0].Contains("use 3.0k max / can 1.0k"), lines[0]);
        Assert.True(lines[2].Contains("add 2x O2/H2 Generator 5 m"), lines[2]);
    }

    public static void ConsumersShowMax()
    {
        var p = new PowerSnapshot();
        p.Users.Add(new PowerUser("Refinery 7.5 m", 1.8, true, 4));
        p.Users.Add(new PowerUser("Ion Thruster 5 m", 0, true, 12));
        var text = StatusFormatter.PowerUsers("Base", p, "", 54, 10);
        Assert.True(text.Contains("max 16.0 MW"), text);
        Assert.True(text.Contains("1.8 MW /   4.0 MW"), text);
        Assert.True(text.Contains("idle /  12.0 MW"), text);
    }
}

public static class PartsOrderTests
{
    public static void MassItemsFirstThenPieces()
    {
        var items = new List<ItemLine> { new("Steel Plate", 900, false, null), new("Iron", 50, true, null), new("Motor", 20, false, null), new("Copper", 4000, true, null) };
        var lines = LcdFormatter.CategoryDetail("Base", null, items, "", 40, 10).Split('\n');
        Assert.True(lines[2].StartsWith("Copper") && lines[3].StartsWith("Iron"), "t first, biggest first");
        Assert.True(lines[4].StartsWith("Steel Plate") && lines[5].StartsWith("Motor"), "then pieces");
    }
}

public static class WorldResetTests
{
    public static void ClearForgetsThePreviousWorld()
    {
        var alerts = new AlertLog();
        alerts.Add("Dock A: 10 t unloaded", 100);
        alerts.Clear();
        Assert.Equal(0, alerts.Recent(101, 10).Count, "alerts cleared");

        var deliveries = new DeliveryTracker<string, string>();
        deliveries.Add("Base", "Dock A", "Iron", 1000, 100);
        Assert.True(deliveries.Recent("Base", 101) != null, "delivery kept before clear");
        deliveries.Clear();
        Assert.True(deliveries.Recent("Base", 101) == null, "deliveries cleared");
    }
}
