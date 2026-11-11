using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Policy;

namespace LwCargoManager.Tests;

public static class TagParserTests
{
    public static void PlainCategory()
    {
        var t = TagParser.Parse("Large Cargo [Ore]");
        Assert.Equal(1, t.Categories.Count);
        Assert.Equal(Category.Ore, t.Categories[0].Category);
        Assert.Equal(5, t.Categories[0].Priority, "default priority");
    }

    public static void PriorityInsideBracket()
    {
        var t = TagParser.Parse("Cargo 3 [ore p1]");
        Assert.Equal(new CategoryTag(Category.Ore, 1), t.Categories[0]);
        Assert.Equal(1, t.PriorityFor(Category.Ore));
        Assert.Equal(int.MaxValue, t.PriorityFor(Category.Parts));
    }

    public static void SeveralTags()
    {
        var t = TagParser.Parse("Big Box [Ore P1] [NoTidy] [Parts P3]");
        Assert.True(t.Accepts(Category.Ore));
        Assert.True(t.Accepts(Category.Parts));
        Assert.True(t.NoTidy);
        Assert.False(t.Lock);
    }

    public static void PluralAndSingularWords()
    {
        Assert.True(TagParser.Parse("[PARTS]").Accepts(Category.Parts));
        Assert.True(TagParser.Parse("[Parts]").Accepts(Category.Parts));
        Assert.True(TagParser.Parse("[Part]").Accepts(Category.Parts));
        foreach (var old in new[] { "[Ingots]", "[Materials]", "[Components]" })
            Assert.False(TagParser.Parse(old).Accepts(Category.Parts), old + " removed: only [Parts]");
        Assert.True(TagParser.Parse("[Tool]").Accepts(Category.Tools));
        Assert.True(TagParser.Parse("[Consumables]").Accepts(Category.Consumables));
        Assert.True(TagParser.Parse("[Ice]").Accepts(Category.Ice));
        Assert.True(TagParser.Parse("[Ammo]").Accepts(Category.Ammo));
        Assert.True(TagParser.Parse("[Misc]").Accepts(Category.Misc));
    }

    public static void UnknownWordsIgnored()
    {
        var main = TagParser.Parse("Cargo [Main]");
        Assert.False(main.HasAnyCategory, "[Main] is no category");
        Assert.Equal(1, main.Items.Count, "unknown words become item tags that match nothing");
        Assert.True(ReferenceEquals(BlockTags.Empty, TagParser.Parse("No brackets here")));
        Assert.True(ReferenceEquals(BlockTags.Empty, TagParser.Parse(null)));
        Assert.False(TagParser.Parse("[Ore Main]").HasAnyCategory, "bad modifier");
        Assert.False(TagParser.Parse("[Ore P0]").HasAnyCategory, "P0 invalid");
        Assert.False(TagParser.Parse("[Ore P10]").HasAnyCategory, "P10 invalid");
    }

    public static void UnloadTags()
    {
        var a = TagParser.Parse("Dock A [Unload]");
        Assert.True(a.Unload); Assert.False(a.UnloadAll);
        var b = TagParser.Parse("Dock B [unload all]");
        Assert.True(b.Unload); Assert.True(b.UnloadAll);
        var c = TagParser.Parse("Fuel Dock [NoUnload]");
        Assert.True(c.NoUnload); Assert.False(c.Unload);
        Assert.False(TagParser.Parse("[Unload P1]").Unload, "unknown modifier");
    }

    public static void LockAndAliases()
    {
        Assert.True(TagParser.Parse("Spare parts [Lock]").Lock);
        Assert.True(TagParser.Parse("Box [NoSort]").Lock);
        Assert.True(TagParser.Parse("Box [Не сортировать]").Lock);
    }

    public static void CustomDefaultPriority()
    {
        Assert.Equal(7, TagParser.Parse("[Ore]", defaultPriority: 7).Categories[0].Priority);
    }
}

public static class ItemClassifierTests
{
    public static void BasicTypes()
    {
        Assert.Equal(Category.Ore, ItemClassifier.Classify(ItemTraits.Ore));
        Assert.Equal(Category.Parts, ItemClassifier.Classify(ItemTraits.Material));
        Assert.Equal(Category.Parts, ItemClassifier.Classify(ItemTraits.Component), "components are parts");
        Assert.Equal(Category.Misc, ItemClassifier.Classify(ItemTraits.None));
    }

    public static void StableSingleCategory()
    {
        Assert.Equal(Category.Ice, ItemClassifier.Classify(ItemTraits.Ore | ItemTraits.Ice));
        Assert.Equal(Category.Tools, ItemClassifier.Classify(ItemTraits.Tool | ItemTraits.Component));
        Assert.Equal(Category.Ammo, ItemClassifier.Classify(ItemTraits.Ammo | ItemTraits.Component));
    }

    public static void SortRankOrder()
    {
        var order = new[] { Category.Misc, Category.Ore, Category.Ammo, Category.Parts }
            .OrderBy(ItemClassifier.SortRank).ToArray();
        Assert.Equal(Category.Ore, order[0]);
        Assert.Equal(Category.Parts, order[1]);
        Assert.Equal(Category.Ammo, order[2]);
        Assert.Equal(Category.Misc, order[3]);
    }
}

public static class BlockRolesTests
{
    public static void CargoIsStorage()
    {
        Assert.Equal(BlockRole.Storage, new BlockRoles().RoleOf(BlockRoles.CargoContainerKind));
    }

    public static void UnknownIsConsumer()
    {
        var roles = new BlockRoles();
        Assert.Equal(BlockRole.Consumer, roles.RoleOf(Guid.NewGuid()));
        Assert.Equal(BlockRole.Consumer, roles.RoleOf(null));
    }

    public static void OverridesFromText()
    {
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid();
        var errors = new List<string>();
        var parsed = BlockRoles.ParseOverrides($"{g1}=Producer; {g2}=storage\nnonsense; {Guid.NewGuid()}=Wrong", errors);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(2, errors.Count);
        var roles = new BlockRoles(parsed);
        Assert.Equal(BlockRole.Producer, roles.RoleOf(g1));
        Assert.Equal(BlockRole.Storage, roles.RoleOf(g2));
    }

    public static void OverrideCanDisableCargo()
    {
        var roles = new BlockRoles(BlockRoles.ParseOverrides($"{BlockRoles.CargoContainerKind}=Consumer"));
        Assert.Equal(BlockRole.Consumer, roles.RoleOf(BlockRoles.CargoContainerKind));
    }
}

/// <summary>The direction test table from the design doc (section 5).</summary>
public static class DockRulesTests
{
    private static readonly BlockTags Unload = TagParser.Parse("[Unload]");
    private static readonly BlockTags NoUnload = TagParser.Parse("[NoUnload]");
    private static readonly BlockTags None = BlockTags.Empty;

    private static DockSide Station(BlockTags t) => new(t, true, 1_000_000);
    private static DockSide Ship(BlockTags t, double tonnes) => new(t, false, tonnes * 1000);

    public static void StationTaggedShipUntagged() =>
        Assert.Equal(DockReceiver.A, DockRules.Receiver(Station(Unload), Ship(None, 60), UnloadMode.Tagged));

    public static void NothingTaggedInTaggedMode() =>
        Assert.Equal(DockReceiver.None, DockRules.Receiver(Station(None), Ship(None, 60), UnloadMode.Tagged));

    public static void AllStationConnectorsMode() =>
        Assert.Equal(DockReceiver.A, DockRules.Receiver(Station(None), Ship(None, 60), UnloadMode.AllStationConnectors));

    public static void BothTaggedStationWins()
    {
        Assert.Equal(DockReceiver.A, DockRules.Receiver(Station(Unload), Ship(Unload, 60), UnloadMode.Tagged));
        Assert.Equal(DockReceiver.B, DockRules.Receiver(Ship(Unload, 60), Station(Unload), UnloadMode.Tagged));
    }

    public static void BothShipsHeavierWins()
    {
        Assert.Equal(DockReceiver.A, DockRules.Receiver(Ship(Unload, 400), Ship(Unload, 60), UnloadMode.Tagged));
        Assert.Equal(DockReceiver.B, DockRules.Receiver(Ship(Unload, 60), Ship(Unload, 400), UnloadMode.Tagged));
    }

    public static void EqualShipsTie() =>
        Assert.Equal(DockReceiver.Tie, DockRules.Receiver(Ship(Unload, 60), Ship(Unload, 60), UnloadMode.Tagged));

    public static void TwoStationsTie() =>
        Assert.Equal(DockReceiver.Tie, DockRules.Receiver(Station(Unload), Station(Unload), UnloadMode.Tagged));

    public static void NoUnloadBlocksAllStationConnectors() =>
        Assert.Equal(DockReceiver.None, DockRules.Receiver(Station(NoUnload), Ship(None, 60), UnloadMode.AllStationConnectors));

    public static void NoUnloadOnEitherSideBlocks() =>
        Assert.Equal(DockReceiver.None, DockRules.Receiver(Station(Unload), Ship(NoUnload, 60), UnloadMode.Tagged));

    public static void StationNeverFeedsShip() =>
        Assert.Equal(DockReceiver.None, DockRules.Receiver(Ship(Unload, 60), Station(None), UnloadMode.Tagged));

    public static void OneTaggedShipReceivesFromOtherShip() =>
        Assert.Equal(DockReceiver.A, DockRules.Receiver(Ship(Unload, 60), Ship(None, 400), UnloadMode.Tagged));

    public static void NullTagsAreSafe() =>
        Assert.Equal(DockReceiver.None, DockRules.Receiver(new DockSide(null, true, 0), new DockSide(null, false, 0), UnloadMode.Tagged));
}

public static class RoutingTests
{
    private static readonly RoutingOptions Defaults = new();
    private static int nextId;

    private static StoreInfo Store(string name, double max = 1000, double current = 0, BlockRole role = BlockRole.Storage,
        bool output = true, Category content = Category.None, int grid = 1) =>
        new()
        {
            Id = ++nextId, GridId = grid, Role = role, IsOutput = output, Tags = TagParser.Parse(name),
            MaxMass = max, CurrentMass = current, IsEmpty = current == 0, ContentCategory = content,
        };

    public static void TaggedByPriorityThenSize()
    {
        var p5Big = Store("[Ore]", max: 5000);
        var p1Small = Store("[Ore P1]", max: 100);
        var p5Small = Store("[Ore]", max: 200);
        var parts = Store("[Parts P1]");
        var d = Routing.Destinations(new[] { p5Small, parts, p5Big, p1Small }, Category.Ore, Defaults, false);
        Assert.Equal(p1Small.Id, d[0].Id, "P1 first");
        Assert.Equal(p5Big.Id, d[1].Id, "then bigger");
        Assert.Equal(p5Small.Id, d[2].Id);
        Assert.False(d.Contains(parts), "wrong category excluded");
    }

    public static void FullAndLockedSkipped()
    {
        var full = Store("[Ore P1]", max: 100, current: 100);
        var locked = Store("[Ore P1] [Lock]");
        var ok = Store("[Ore]");
        var d = Routing.Destinations(new[] { full, locked, ok }, Category.Ore, Defaults, false);
        Assert.Equal(1, d.Count);
        Assert.Equal(ok.Id, d[0].Id);
    }

    public static void MiscThenEmptyOverflow()
    {
        var ore = Store("[Ore]", max: 100, current: 100); // full
        var misc = Store("[Misc]");
        var empty = Store("Cargo 9");
        var mixed = Store("Cargo 10", current: 50);
        var d = Routing.Destinations(new[] { mixed, empty, misc, ore }, Category.Ore, Defaults, false);
        Assert.Equal(misc.Id, d[0].Id);
        Assert.Equal(empty.Id, d[1].Id, "empty overflow");
        Assert.False(d.Contains(mixed), "mixed never gets overflow when sorting");
    }

    public static void NoOverflowWithoutTaggedDestination()
    {
        var empty = Store("Cargo 9");
        var d = Routing.Destinations(new[] { empty }, Category.Ore, Defaults, false);
        Assert.Equal(0, d.Count);
    }

    public static void UnloadFallsBackToUntagged()
    {
        var mixed = Store("Cargo 10", current: 50);
        var d = Routing.Destinations(new[] { mixed }, Category.Ore, Defaults, forUnload: true);
        Assert.Equal(1, d.Count);
        var off = new RoutingOptions { FallbackToUntaggedCargo = false };
        Assert.Equal(0, Routing.Destinations(new[] { mixed }, Category.Ore, off, forUnload: true).Count);
    }

    public static void ConsumersAndProducersNeverDestinations()
    {
        var turret = Store("[Ammo]", role: BlockRole.Consumer);
        var refinery = Store("[Parts]", role: BlockRole.Producer);
        Assert.Equal(0, Routing.Destinations(new[] { turret }, Category.Ammo, Defaults, true).Count);
        Assert.Equal(0, Routing.Destinations(new[] { refinery }, Category.Parts, Defaults, true).Count);
    }

    public static void SortSources()
    {
        Assert.True(Routing.ShouldSortOut(Store("Cargo"), Category.Ore, Defaults), "untagged");
        Assert.True(Routing.ShouldSortOut(Store("[Parts]"), Category.Ore, Defaults), "wrong tag");
        Assert.False(Routing.ShouldSortOut(Store("[Ore]"), Category.Ore, Defaults), "already right: no loop");
        Assert.False(Routing.ShouldSortOut(Store("[Lock]"), Category.Ore, Defaults), "locked");
        Assert.False(Routing.ShouldSortOut(Store("Turret", role: BlockRole.Consumer), Category.Ammo, Defaults), "consumer");
        Assert.True(Routing.ShouldSortOut(Store("Refinery", role: BlockRole.Producer, output: true), Category.Parts, Defaults), "producer output");
        Assert.False(Routing.ShouldSortOut(Store("Refinery", role: BlockRole.Producer, output: false), Category.Ore, Defaults), "producer input");
        var noDrain = new RoutingOptions { DrainProducerOutputs = false };
        Assert.False(Routing.ShouldSortOut(Store("Refinery", role: BlockRole.Producer), Category.Parts, noDrain));
    }

    public static void AutoCategories()
    {
        var oreOnly = Store("Cargo 1", current: 10, content: Category.Ore);
        Assert.False(Routing.Keeps(oreOnly, Category.Ore, Defaults), "off by default");
        var auto = new RoutingOptions { AutoCategories = true };
        Assert.True(Routing.Keeps(oreOnly, Category.Ore, auto));
        Assert.False(Routing.ShouldSortOut(oreOnly, Category.Ore, auto));
        var taggedParts = Store("[Parts]", current: 10, content: Category.Ore);
        Assert.False(Routing.Keeps(taggedParts, Category.Ore, auto), "tags win over content");
    }

    public static void GridOptIn()
    {
        Assert.False(Routing.GridOptedIn(new[] { Store("Cargo"), Store("Cargo 2") }, Defaults));
        Assert.True(Routing.GridOptedIn(new[] { Store("Cargo"), Store("[Ore]") }, Defaults));
        Assert.False(Routing.GridOptedIn(new[] { Store("[Ore] [Lock]") }, Defaults), "locked doesn't opt in");
    }

    public static void UnloadFilter()
    {
        var keeps = new HashSet<Category> { Category.Tools, Category.Consumables, Category.Ammo };
        Assert.True(Routing.ShouldUnload(Category.Ore, false, true, keeps));
        Assert.True(Routing.ShouldUnload(Category.Ice, false, true, keeps));
        Assert.False(Routing.ShouldUnload(Category.Ice, false, false, keeps));
        Assert.False(Routing.ShouldUnload(Category.Parts, false, true, keeps), "[Unload] = ore only");
        Assert.True(Routing.ShouldUnload(Category.Parts, true, true, keeps), "[Unload All]");
        Assert.False(Routing.ShouldUnload(Category.Ammo, true, true, keeps), "ship keeps ammo");
        Assert.False(Routing.ShouldUnload(Category.None, true, true, keeps));
    }

    public static void UnloadSources()
    {
        Assert.True(Routing.CanBeUnloadSource(Store("Cargo")));
        Assert.True(Routing.CanBeUnloadSource(Store("Drill", role: BlockRole.Producer)));
        Assert.False(Routing.CanBeUnloadSource(Store("Cargo [Lock]")));
        Assert.False(Routing.CanBeUnloadSource(Store("Reactor", role: BlockRole.Consumer)));
    }

    public static void ParseKeepList()
    {
        var errors = new List<string>();
        var set = Routing.ParseCategories("Tools, Bottles; ammo  Banana", errors);
        Assert.Equal(3, set.Count);
        Assert.True(set.Contains(Category.Ammo));
        Assert.Equal(1, errors.Count);
    }

    public static void RoundRobin()
    {
        Assert.Equal(0, Routing.Next(0, 0));
        Assert.Equal(1, Routing.Next(0, 3));
        Assert.Equal(0, Routing.Next(2, 3));
    }
}

public static class GraceTrackerTests
{
    public static void ProtectsForDelay()
    {
        double now = 0;
        var g = new GraceTracker<int, string>(() => now) { DelaySeconds = 60 };
        g.Mark(1, "iron");
        now = 59;
        Assert.True(g.IsProtected(1, "iron"));
        Assert.False(g.IsProtected(2, "iron"), "other inventory");
        Assert.False(g.IsProtected(1, "nickel"), "other item");
        now = 60;
        Assert.False(g.IsProtected(1, "iron"), "expired");
        Assert.Equal(0, g.Count, "expired entry removed");
    }

    public static void ZeroDelayDisables()
    {
        var g = new GraceTracker<int, string>(() => 0) { DelaySeconds = 0 };
        g.Mark(1, "iron");
        Assert.False(g.IsProtected(1, "iron"));
        Assert.Equal(0, g.Count);
    }

    public static void RemarkRestartsTimer()
    {
        double now = 0;
        var g = new GraceTracker<int, string>(() => now) { DelaySeconds = 10 };
        g.Mark(1, "iron");
        now = 8; g.Mark(1, "iron");
        now = 15;
        Assert.True(g.IsProtected(1, "iron"));
    }

    public static void Prune()
    {
        double now = 0;
        var g = new GraceTracker<int, string>(() => now) { DelaySeconds = 10 };
        g.Mark(1, "a"); g.Mark(2, "b");
        now = 5; g.Mark(3, "c");
        now = 12; g.Prune();
        Assert.Equal(1, g.Count);
    }
}

public static class CellTests
{
    public static void CellTags()
    {
        Assert.True(TagParser.Parse("Cell Store [Cells]").Accepts(Category.Cells));
        Assert.True(TagParser.Parse("[Batteries P1]").Accepts(Category.Cells));
        Assert.True(TagParser.Parse("[PowerCells]").Accepts(Category.Cells));
        Assert.Equal(new LcdView(LcdViewKind.Category, Category.Cells), TagParser.Parse("LCD [Cargo Cells]").LcdViews[0]);
    }

    public static void CellItemsClassified()
    {
        Assert.Equal(Category.Cells, ItemClassifier.Classify(ItemTraits.Cell));
        Assert.Equal(Category.Cells, ItemClassifier.Classify(ItemTraits.Cell | ItemTraits.Component), "cell wins over component");
        Assert.Equal("Cells", CargoSummary.Label(Category.Cells));
        Assert.False(CargoSummary.IsMassCategory(Category.Cells), "cells are counted");
    }

    public static void ShipKeepsCellsAtUnloadAll()
    {
        var keeps = Routing.ParseCategories("Tools, Bottles, Ammo, Cells");
        Assert.False(Routing.ShouldUnload(Category.Cells, true, true, keeps));
    }
}

/// <summary>Real paths from Lone's discovery report (SE2, 2026-11-10).</summary>
public static class ItemPathTests
{
    public static void Ammo()
    {
        Assert.Equal(ItemTraits.Ammo, ItemPaths.TraitsFromPath(@"a74caed5-d6ff-4b3d-9c6b-2f55bfb16c31 : Vanilla\Items\Ammo\GatlingAmmoItem\ItemGatlingAmmo.def"));
        Assert.Equal(ItemTraits.Ammo, ItemPaths.TraitsFromPath(@"x : Vanilla\Items\Ammo\Missile\RocketLauncherAmmo.def"));
        Assert.Equal(Category.Ammo, ItemClassifier.Classify(ItemPaths.TraitsFromPath(@"x : Vanilla\Items\Ammo\RifleMagazine\RifleMagazineItem.def")));
    }

    public static void Ice()
    {
        var traits = ItemTraits.Ore | ItemPaths.TraitsFromPath(@"6ae64849 : Vanilla\Items\Ores\IceOre\IceOreItem.def");
        Assert.Equal(Category.Ice, ItemClassifier.Classify(traits));
        Assert.Equal(Category.Ore, ItemClassifier.Classify(ItemTraits.Ore | ItemPaths.TraitsFromPath(@"x : Vanilla\Items\Ores\IronOre\IronOre_Item.def")));
    }

    public static void PowerModuleIsCell()
    {
        var traits = ItemTraits.Component | ItemPaths.TraitsFromPath(@"c8f857ea : Vanilla\Components\Complex\PowerModule\PowerModuleItem Base_Client_FloatingObjectItem.def");
        Assert.Equal(Category.Cells, ItemClassifier.Classify(traits));
    }

    public static void Consumables()
    {
        Assert.Equal(Category.Consumables, ItemClassifier.Classify(ItemTraits.Consumable));
        Assert.True(TagParser.Parse("[Consumables]").Accepts(Category.Consumables));
        Assert.True(TagParser.Parse("[Bottles]").Accepts(Category.Consumables), "old name");
    }

    public static void Names()
    {
        Assert.Equal("Iron Ore", ItemPaths.NameFromPath(@"cb651320 : Vanilla\Items\Ores\IronOre\IronOre_Item.def"));
        Assert.Equal("Steel Plate", ItemPaths.NameFromPath(@"x : Vanilla\Components\Simple\SteelPlate\ItemSteelPlate.def"));
        Assert.Equal("Heavy-duty Plate", ItemPaths.NameFromPath(@"x : Vanilla\Components\Complex\Heavy-dutyPlate\Heavy-dutyPlateItem Base_Client_FloatingObjectItem.def"));
        Assert.Equal("Radio-comm Module", ItemPaths.NameFromPath(@"x : Vanilla\Components\Complex\Radio-commModule\x.def"));
        Assert.Equal("Gatling Ammo", ItemPaths.NameFromPath(@"x : Vanilla\Items\Ammo\GatlingAmmoItem\ItemGatlingAmmo.def"));
        Assert.Equal("Power Module", ItemPaths.NameFromPath(@"x : Vanilla\Components\Complex\PowerModule\PowerModuleItem.def"));
        Assert.Equal("Suit Battery", ItemPaths.NameFromPath(@"x : Vanilla\Items\Consumables\SuitBattery\ConsumableSuitBattery.def"));
        Assert.True(ItemPaths.NameFromPath("no path") == null);
        Assert.True(ItemPaths.NameFromPath(null) == null);
    }
}

public static class ItemTagTests
{
    private static readonly RoutingOptions Defaults = new();
    private static readonly ItemKey Silicon = new(Category.Ore, "Silicon Ore");
    private static readonly ItemKey Nickel = new(Category.Ore, "Nickel Ore");
    private static readonly ItemKey SiliconParts = new(Category.Parts, "Silicon Wafer");
    private static readonly ItemKey Iron = new(Category.Ore, "Iron Ore");

    private static StoreInfo Store(string name, int id, double max = 1000, double used = 0) => new()
    {
        Id = id, Role = BlockRole.Storage, Tags = TagParser.Parse(name), MaxMass = max, CurrentMass = used, IsEmpty = used == 0,
    };

    public static void Parsing()
    {
        var t = TagParser.Parse("Box [Si] [Iron Ore P1] [Heavy-duty Plate P2]");
        Assert.Equal(3, t.Items.Count);
        Assert.Equal(new ItemTag("Si", TagParser.DefaultPriority), t.Items[0]);
        Assert.Equal(new ItemTag("Iron Ore", 1), t.Items[1]);
        Assert.Equal(new ItemTag("Heavy-duty Plate", 2), t.Items[2]);
        Assert.False(t.HasAnyCategory);
        Assert.True(t.HasAnyStorageTag);

        var mixed = TagParser.Parse("[Ore] [Silicon P1] [Unload] [Cargo Ore] [Ore Main]");
        Assert.Equal(1, mixed.Items.Count, "only Silicon is an item tag");
        Assert.Equal(new ItemTag("Silicon", 1), mixed.Items[0]);
        Assert.Equal(1, mixed.Categories.Count);
        Assert.True(TagParser.Parse("[Не сортировать]").Items.Count == 0, "non-latin never an item");
    }

    public static void Matching()
    {
        Assert.True(ItemMatch.Matches("Si", Silicon));
        Assert.True(ItemMatch.Matches("silicon", Silicon));
        Assert.True(ItemMatch.Matches("Silicon Ore", Silicon));
        Assert.True(ItemMatch.Matches("Si Ore", Silicon));
        Assert.True(ItemMatch.Matches("Si", SiliconParts), "symbol alone = all silicon items");
        Assert.False(ItemMatch.Matches("Si Ore", SiliconParts), "category word limits it");
        Assert.True(ItemMatch.Matches("Si Parts", SiliconParts));
        Assert.False(ItemMatch.Matches("Si", Nickel));
        Assert.False(ItemMatch.Matches("Ir", Iron), "whole words only");
        Assert.True(ItemMatch.Matches("Fe", Iron));
        Assert.False(ItemMatch.Matches("Main", Iron));
    }

    public static void ItemTagWinsOverCategory()
    {
        var ore = Store("[Ore]", 1, used: 100);
        var si = Store("[Si]", 2);
        Assert.True(Routing.ShouldSortOut(ore, Silicon, Defaults, itemTagElsewhere: true), "[Ore] gives Si to [Si]");
        Assert.False(Routing.ShouldSortOut(ore, Silicon, Defaults, itemTagElsewhere: false), "no [Si] room: stays");
        Assert.False(Routing.ShouldSortOut(ore, Nickel, Defaults), "nickel stays in [Ore]");
        Assert.False(Routing.ShouldSortOut(si, Silicon, Defaults, itemTagElsewhere: true), "already in [Si]");
        Assert.True(Routing.ShouldSortOut(si, Nickel, Defaults), "nickel leaves [Si]");

        var d = Routing.Destinations(new[] { ore, si }, Silicon, Defaults, forUnload: false);
        Assert.Equal(2, d[0].Id, "[Si] first");
        Assert.Equal(1, d[1].Id, "then [Ore]");
        var only = Routing.Destinations(new[] { ore, si }, Silicon, Defaults, false, itemTagsOnly: true);
        Assert.Equal(1, only.Count);
        Assert.Equal(1, Routing.Destinations(new[] { ore, si }, Nickel, Defaults, false).Count, "nickel never into [Si]");
    }

    public static void SummaryRow()
    {
        var rows = CargoSummary.Build(new[] { Store("[Ore]", 1, used: 5), Store("[Si]", 2, used: 7), Store("Box", 3) });
        Assert.Equal("Ore", rows[0].Label);
        Assert.Equal("Si", rows[1].Label);
        Assert.Equal("Untagged", rows[2].Label);
        Assert.Equal(1, rows[2].Containers);
    }
}

public static class PlayerTagTests
{
    public static void PlayerGroup()
    {
        var t = TagParser.Parse("Locker [Player P2] [Rifle]");
        Assert.True(t.Accepts(Category.Tools));
        Assert.True(t.Accepts(Category.Consumables));
        Assert.False(t.Accepts(Category.Ammo), "ammo is separate");
        Assert.Equal(2, t.PriorityFor(Category.Consumables));
        Assert.Equal(1, t.Items.Count, "[Rifle] item tag for the magazines");
        Assert.Equal(0, TagParser.Parse("[Player]").Items.Count, "not an item tag");

        var box = new StoreInfo { Id = 1, Role = BlockRole.Storage, Tags = TagParser.Parse("[Player]"), MaxMass = 100 };
        var tools = new StoreInfo { Id = 2, Role = BlockRole.Storage, Tags = TagParser.Parse("[Tools]"), MaxMass = 100 };
        var rows = CargoSummary.Build(new[] { box, tools });
        Assert.Equal("Tools", rows[0].Label);
        Assert.Equal(1, rows[0].Containers, "Player locker not counted under Tools");
        Assert.Equal("Player", rows[1].Label);
        Assert.Equal("Total", rows[2].Label);
    }
}

public static class ToolNameTests
{
    public static void HandToolNames()
    {
        Assert.Equal("Rifle", ItemPaths.NameFromPath(@"x : Vanilla\CharacterTools\Rifle\Items\Rifle_ItemDefinition.def"));
        Assert.Equal("Pistol", ItemPaths.NameFromPath(@"x : Vanilla\CharacterTools\Pistol\Item\Pistol_ItemDefinition.def"));
        Assert.Equal("Rocket Launcher", ItemPaths.NameFromPath(@"x : Vanilla\CharacterTools\RocketLauncher\Item\RocketLauncherItemDefinition.def"));
        Assert.Equal("Drill Mk3", ItemPaths.NameFromPath(@"x : Vanilla\CharacterTools\HandDrills\DrillMk3\Items\ItemDrillMk3.def"));
        Assert.Equal("Area Welder Mk1", ItemPaths.NameFromPath(@"x : Vanilla\CharacterTools\HandWelders\AreaWelderMk1\Items\ItemAreaWelderMk1.def"));
        Assert.Equal("Rifle Magazine", ItemPaths.NameFromPath(@"x : Vanilla\Items\Ammo\RifleMagazine\RifleMagazineItem.def"));
    }
}

public static class ProducerRoleTests
{
    public static void AllProductionBlocksAreProducers()
    {
        var roles = new BlockRoles();
        foreach (var kind in new[] { BlockRoles.RefineryKind, BlockRoles.SmelterKind, BlockRoles.AssemblerKind,
                     BlockRoles.GearforgeKind, BlockRoles.FabricatorKind, BlockRoles.DrillKind, BlockRoles.MiningLaserKind })
            Assert.Equal(BlockRole.Producer, roles.RoleOf(kind), kind.ToString());
    }

    public static void ProducerOutputFallsBackToUntaggedCargo()
    {
        var plain = new StoreInfo { Id = 1, Role = BlockRole.Storage, Tags = BlockTags.Empty, MaxMass = 100 };
        var d = Routing.Destinations(new[] { plain }, Category.Parts, new RoutingOptions(), forUnload: true);
        Assert.Equal(1, d.Count, "untagged cargo takes producer output");
        Assert.Equal(0, Routing.Destinations(new[] { plain }, Category.Parts, new RoutingOptions(), forUnload: false).Count, "normal sort doesn't");
    }
}

public static class AutoRoleTests
{
    private static BlockRole Of(params string[] c) => BlockRoles.AutoRole(new HashSet<string>(c));

    public static void ModdedBlocks()
    {
        Assert.Equal(BlockRole.Storage, Of("InventoryBlockDestructionExplosionComponent", "FunctionalBlockComponent", "ConveyorComponent", "InventoryComponent"), "modded cargo");
        Assert.Equal(BlockRole.Producer, Of("PowerableBlockComponent", "ItemConverterComponent", "InventoryComponent"), "modded assembler");
        Assert.Equal(BlockRole.Producer, Of("PowerableBlockComponent", "DrillComponent", "InventoryComponent"), "modded drill");
        Assert.Equal(BlockRole.Consumer, Of("PowerableBlockComponent", "ProjectileWeaponComponent", "InventoryComponent"), "turret");
        Assert.Equal(BlockRole.Consumer, Of("PowerableBlockComponent", "ResourceContainerInventoryComponent", "InventoryComponent"), "battery");
        Assert.Equal(BlockRole.Consumer, Of("FunctionalBlockComponent", "ConveyorComponent", "InventoryComponent"), "trash bin (no explosion component)");
        Assert.Equal(BlockRole.Consumer, Of("PowerableBlockComponent", "ItemToResourceConverterComponent", "InventoryComponent"), "reactor");
        Assert.Equal(BlockRole.Consumer, Of("SolarGeneratorComponent"), "no inventory");
        Assert.False(new BlockRoles().IsKnown(Guid.NewGuid()));
        Assert.True(new BlockRoles().IsKnown(BlockRoles.CargoContainerKind));
    }
}

public static class AmmoPlannerTests
{
    public static void Planning()
    {
        Assert.Equal(360.0, AmmoPlanner.Target(120, 2), "turrets refilled once + 2x reserve");
        Assert.Equal(0, AmmoPlanner.Batches(400, 360, 10, 50, 10), "enough");
        Assert.Equal(10, AmmoPlanner.Batches(0, 360, 10, 50, 10), "capped by batch limit");
        Assert.Equal(3, AmmoPlanner.Batches(0, 360, 10, 3, 10), "capped by materials");
        Assert.Equal(1, AmmoPlanner.Batches(355, 360, 10, 50, 10), "rounds up");
        Assert.Equal(0, AmmoPlanner.Batches(0, 360, 10, 0, 10), "no materials");

        // 600 rounds/min, 1 round per shot, 200 rounds per item → 3 items/min per weapon
        Assert.Equal(3.0, AmmoPlanner.ItemsPerMinute(600, 1, 200));
        Assert.Equal(0.0, AmmoPlanner.ItemsPerMinute(600, 1, 0), "unknown");
        // 31 weapons × 1 item reload + 31 × 3/min × 5 min
        Assert.Equal(31 + 465.0, AmmoPlanner.Target(31, 93, 5, 2));
        Assert.Equal(93.0, AmmoPlanner.Target(31, 0, 5, 2), "rate unknown: 2x reserve");
    }

    public static void Tags()
    {
        Assert.True(TagParser.Parse("Gearforge 2.5 m [Make Ammo]").MakeAmmo);
        Assert.Equal(0, TagParser.Parse("Gearforge [Make Ammo]").Items.Count, "not an item tag");
        Assert.Equal(LcdViewKind.Turrets, TagParser.Parse("LCD [Turrets 54]").ScreenViews[0].Kind);
        Assert.Equal(54, TagParser.Parse("LCD [Turrets 54]").ScreenViews[0].Width);
    }

    public static void Screen()
    {
        var text = AmmoPlanner.Screen("Base", new List<AmmoStatus>
        {
            new() { Name = "Gatling Ammo", Turrets = 31, Capacity = 1550, Stock = 2233, Target = 4650, Production = "making 10 (Gearforge 2.5 m)" },
            new() { Name = "Missile", Turrets = 4, Capacity = 120, Stock = 453, Target = 360 },
        }, "", 40);
        Assert.True(text.Contains("Gatling Ammo  31 weapons"), text);
        Assert.True(text.Contains("2 233 / 4 650 LOW"), text);
        Assert.True(text.Contains("making 10"), text);
        Assert.True(text.Contains("453 / 360 OK"), text);
    }

    public static void ConveyorCheckFindsUnlinked()
    {
        var nodes = new List<(int, string)> { (1, "Cargo A"), (2, "Cargo B"), (3, "Cargo C"), (4, "Lost Box") };
        // 1 → 2, 3 → 2 (one-way links still join), 4 alone
        var reach = new Dictionary<int, ICollection<int>> { [1] = new[] { 1, 2 }, [2] = new[] { 2 }, [3] = new[] { 2, 99 }, [4] = new[] { 4 } };
        var lost = ConveyorCheck.Unlinked(nodes, id => reach[id]);
        Assert.Equal(1, lost.Count);
        Assert.Equal("Lost Box", lost[0]);

        var all = new Dictionary<int, ICollection<int>> { [1] = new[] { 2, 3, 4 }, [2] = new int[0], [3] = new int[0], [4] = new int[0] };
        Assert.Equal(0, ConveyorCheck.Unlinked(nodes, id => all[id]).Count, "all linked");
        Assert.Equal(0, ConveyorCheck.Unlinked(new List<(int, string)> { (1, "One") }, id => new int[0]).Count, "single block");
    }
}
