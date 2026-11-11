using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ClientPlugin.Policy;

/// <summary>A category tag with its priority, e.g. [Ore P1] → (Ore, 1).</summary>
/// <summary>A category tag with its priority, e.g. [Ore P1] → (Ore, 1). Group: added by a group tag like [Player].</summary>
public readonly record struct CategoryTag(Category Category, int Priority, string Group = null);

/// <summary>What an LCD tagged [Cargo ...] shows.</summary>
public enum LcdViewKind
{
    /// <summary>[Cargo]: fill bars for every category of the grid.</summary>
    Summary,
    /// <summary>[Cargo Ore], [Cargo Components] ...: item list for one category.</summary>
    Category,
    /// <summary>[Cargo Alerts]: problems and recent messages.</summary>
    Alerts,
    /// <summary>[Cargo Ruler]: test pattern to see how many lines and characters fit.</summary>
    Ruler,
    /// <summary>[Power]: generation, use, batteries.</summary>
    Power,
    /// <summary>[Power Sources]: reactors, solar, wind, batteries.</summary>
    PowerSources,
    /// <summary>[Power Users]: consumers, biggest draw first.</summary>
    PowerUsers,
    /// <summary>[Damage]: block count, damaged and unfinished blocks.</summary>
    Damage,
    /// <summary>[Skip]: leave this screen to the game (cockpits: keep one screen, use the next).</summary>
    Skip,
    /// <summary>[Turrets]: ammo per turret type, stock vs target, Gearforge production.</summary>
    Turrets,
}


/// <summary>
/// Width: characters per line from the name ([Cargo Ore 54]); 0 = the plugin setting.
/// Page: [Cargo Parts 2] continues the list where page 1 stopped (one digit = page, 2-3 digits = width).
/// </summary>
public readonly record struct LcdView(LcdViewKind Kind, Category Category, int Width = 0, int Page = 1);

/// <summary>All tags found in one block name.</summary>
public sealed class BlockTags
{
    public static readonly BlockTags Empty = new();

    public bool Unload { get; init; }
    public bool UnloadAll { get; init; }
    public bool NoUnload { get; init; }
    public bool Lock { get; init; }
    public bool NoTidy { get; init; }
    /// <summary>[Make Ammo] on a Gearforge: the plugin may queue ammo there to keep the turret reserve.</summary>
    public bool MakeAmmo { get; init; }
    public IReadOnlyList<CategoryTag> Categories { get; init; } = Array.Empty<CategoryTag>();
    /// <summary>Single-item tags: [Si], [Iron Ore P1]. They win over category tags.</summary>
    public IReadOnlyList<ItemTag> Items { get; init; } = Array.Empty<ItemTag>();
    /// <summary>LCD views, in name order. The first one is used for the block's first text surface.</summary>
    public IReadOnlyList<LcdView> LcdViews { get; init; } = Array.Empty<LcdView>();

    public bool HasAnyCategory => Categories.Count > 0;
    public bool HasAnyItemTag => Items.Count > 0;
    /// <summary>Has a category or an item tag (a sort destination of some kind).</summary>
    public bool HasAnyStorageTag => Categories.Count > 0 || Items.Count > 0;

    /// <summary>Best (lowest) priority of an item tag that matches, int.MaxValue if none.</summary>
    public int ItemPriorityFor(ItemKey item)
    {
        var best = int.MaxValue;
        foreach (var tag in Items)
            if (tag.Priority < best && ItemMatch.Matches(tag.Text, item))
                best = tag.Priority;
        return best;
    }

    public bool AcceptsItem(ItemKey item) => ItemPriorityFor(item) != int.MaxValue;
    public bool IsLcd => LcdViews.Count > 0;

    /// <summary>
    /// Screens in name order, including the short forms without "Cargo" ([Parts 54], [Ore], [Parts 2]).
    /// Used for LCD blocks (they have no storage, so a category tag there can only mean a screen).
    /// </summary>
    public IReadOnlyList<LcdView> ScreenViews { get; init; } = Array.Empty<LcdView>();

    public bool Accepts(Category category)
    {
        foreach (var tag in Categories)
            if (tag.Category == category)
                return true;
        return false;
    }

    /// <summary>Priority of this container for a category (1 = first). int.MaxValue if not tagged for it.</summary>
    public int PriorityFor(Category category)
    {
        var best = int.MaxValue;
        foreach (var tag in Categories)
            if (tag.Category == category && tag.Priority < best)
                best = tag.Priority;
        return best;
    }
}

/// <summary>
/// Parses tags from block names. Case-insensitive, one tag per bracket, optional modifier:
/// "Cargo 3 [Ore P1] [NoTidy]", "Dock A [Unload]", "Dock B [unload all]".
/// Any other bracket text is an item tag ([Si], [Iron Ore P1]); one that matches no item does nothing.
/// </summary>
public static class TagParser
{
    public const int DefaultPriority = 5;

    private static readonly Regex TagRx = new(
        @"\[\s*([A-Za-z]+)(?:\s+([A-Za-z0-9]+))?\s*\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Dictionary<string, Category> CategoryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ore"] = Category.Ore,
        ["Ores"] = Category.Ore,
        ["Ice"] = Category.Ice,
        ["Part"] = Category.Parts,
        ["Parts"] = Category.Parts,
        ["Ammo"] = Category.Ammo,
        ["Tool"] = Category.Tools,
        ["Tools"] = Category.Tools,
        ["Consumable"] = Category.Consumables,
        ["Consumables"] = Category.Consumables,
        ["Bottle"] = Category.Consumables,
        ["Bottles"] = Category.Consumables,
        ["Cell"] = Category.Cells,
        ["Cells"] = Category.Cells,
        ["PowerCells"] = Category.Cells,
        ["Battery"] = Category.Cells,
        ["Batteries"] = Category.Cells,
        ["Misc"] = Category.Misc,
    };

    public static BlockTags Parse(string name, int defaultPriority = DefaultPriority)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOf('[') < 0)
            return BlockTags.Empty;

        bool unload = false, unloadAll = false, noUnload = false, locked = false, noTidy = false, makeAmmo = false;
        var categories = new List<CategoryTag>();
        var lcdViews = new List<LcdView>();
        var screens = new List<LcdView>();
        var items = new List<ItemTag>();

        foreach (Match bracket in AnyBracketRx.Matches(name))
        {
            // Short screen form for LCDs: [Ore], [Parts 54], [Parts 2 54]. Still parsed as a category below.
            var shortScreen = ShortScreenRx.Match(bracket.Value);
            if (shortScreen.Success && !Is(shortScreen.Groups[1].Value, "Cargo")
                && TryCargoView(shortScreen.Groups[1].Value, shortScreen.Groups[2], shortScreen.Groups[3], out var shortView))
                screens.Add(shortView);

            var status = StatusRx.Match(bracket.Value);
            if (status.Success)
            {
                var statusWidth = status.Groups[3].Success ? Math.Clamp(int.Parse(status.Groups[3].Value), 20, 200) : 0;
                var sub = status.Groups[2].Success ? status.Groups[2].Value : null;
                LcdViewKind kind;
                if (Is(status.Groups[1].Value, "Damage")) kind = LcdViewKind.Damage;
                else if (sub == null) kind = LcdViewKind.Power;
                else if (Is(sub, "Sources") || Is(sub, "Generators")) kind = LcdViewKind.PowerSources;
                else kind = LcdViewKind.PowerUsers;
                lcdViews.Add(new LcdView(kind, Category.None, statusWidth));
                screens.Add(lcdViews[^1]);
                continue;
            }

            var cargo = CargoRx.Match(bracket.Value);
            if (cargo.Success)
            {
                if (!cargo.Groups[1].Success)
                {
                    var summaryWidth = cargo.Groups[3].Success ? Math.Clamp(int.Parse(cargo.Groups[3].Value), 20, 200) : 0;
                    lcdViews.Add(new LcdView(LcdViewKind.Summary, Category.None, summaryWidth));
                    screens.Add(lcdViews[^1]);
                }
                else if (TryCargoView(cargo.Groups[1].Value, cargo.Groups[2], cargo.Groups[3], out var cargoView))
                {
                    lcdViews.Add(cargoView);
                    screens.Add(cargoView);
                }
                continue;
            }

            var m = TagRx.Match(bracket.Value);
            if (!m.Success || m.Length != bracket.Length)
            {
                if (TryItemTag(bracket.Value, defaultPriority, out var itemTag)) items.Add(itemTag);
                continue;
            }
            var word = m.Groups[1].Value;
            var modifier = m.Groups[2].Success ? m.Groups[2].Value : null;

            if (Is(word, "Cargo"))
            {
                if (modifier == null) lcdViews.Add(new LcdView(LcdViewKind.Summary, Category.None));
                else if (Is(modifier, "Alerts") || Is(modifier, "Alert")) lcdViews.Add(new LcdView(LcdViewKind.Alerts, Category.None));
                else if (Is(modifier, "Ruler")) lcdViews.Add(new LcdView(LcdViewKind.Ruler, Category.None));
                else if (CategoryWords.TryGetValue(modifier, out var lcdCategory)) lcdViews.Add(new LcdView(LcdViewKind.Category, lcdCategory));
                continue;
            }

            if (Is(word, "Make") && modifier != null && Is(modifier, "Ammo"))
            {
                makeAmmo = true;
                continue;
            }

            if (Is(word, "Unload"))
            {
                if (modifier == null) unload = true;
                else if (Is(modifier, "All")) { unload = true; unloadAll = true; }
                continue;
            }

            if (modifier == null)
            {
                if (Is(word, "NoUnload")) { noUnload = true; continue; }
                if (Is(word, "Lock") || Is(word, "NoSort")) { locked = true; continue; } // [NoSort]: alias used by other sorters
                if (Is(word, "NoTidy")) { noTidy = true; continue; }
            }

            if (Groups.TryGetValue(word, out var group))
            {
                var groupPriority = defaultPriority;
                if (modifier != null && !TryParsePriority(modifier, out groupPriority))
                    continue;
                foreach (var member in group.Categories)
                    categories.Add(new CategoryTag(member, groupPriority, group.Label));
                continue;
            }

            if (CategoryWords.TryGetValue(word, out var category))
            {
                var priority = defaultPriority;
                if (modifier != null && !TryParsePriority(modifier, out priority))
                    continue; // "[Ore Main]" is not a valid tag
                categories.Add(new CategoryTag(category, priority));
                continue;
            }

            if (TryItemTag(bracket.Value, defaultPriority, out var item)) items.Add(item);
        }

        // Russian alias for [NoSort] used by other sorters
        if (name.IndexOf("[Не сортировать]", StringComparison.OrdinalIgnoreCase) >= 0)
            locked = true;

        if (!unload && !noUnload && !locked && !noTidy && !makeAmmo && categories.Count == 0 && lcdViews.Count == 0 && items.Count == 0 && screens.Count == 0)
            return BlockTags.Empty;

        return new BlockTags
        {
            Unload = unload,
            UnloadAll = unloadAll,
            NoUnload = noUnload,
            Lock = locked,
            NoTidy = noTidy,
            MakeAmmo = makeAmmo,
            Categories = categories,
            Items = items,
            LcdViews = lcdViews,
            ScreenViews = screens,
        };
    }

    /// <summary>[Cargo], [Cargo Ore], [Cargo 54], [Cargo Parts 2], [Cargo Parts 2 54]: view, optional page (1 digit), optional width (2-3 digits).</summary>
    private static readonly Regex CargoRx = new(
        @"^\[\s*Cargo(?:\s+([A-Za-z]+))?(?:\s+([1-9]))?(?:\s+(\d{2,3}))?\s*\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>[Power], [Power Sources 54], [Power Users], [Damage], also with "Status": [Status Power].</summary>
    private static readonly Regex StatusRx = new(
        @"^\[\s*(?:Status\s+)?(Power|Damage)(?:\s+(Sources|Generators|Users|Consumers))?(?:\s+(\d{2,3}))?\s*\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>[Ore], [Parts 54], [Parts 2 54]: a screen word with optional page and width (LCD blocks only).</summary>
    private static readonly Regex ShortScreenRx = new(
        @"^\[\s*([A-Za-z]+)(?:\s+([1-9]))?(?:\s+(\d{2,3}))?\s*\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>"Ore", "Parts", "Alerts", "Ruler" + page/width groups → a screen.</summary>
    private static bool TryCargoView(string word, Group pageGroup, Group widthGroup, out LcdView view)
    {
        var page = pageGroup.Success ? int.Parse(pageGroup.Value) : 1;
        var width = widthGroup.Success ? Math.Clamp(int.Parse(widthGroup.Value), 20, 200) : 0;
        if (Is(word, "Alerts") || Is(word, "Alert"))
            view = new LcdView(LcdViewKind.Alerts, Category.None, width);
        else if (Is(word, "Ruler"))
            view = new LcdView(LcdViewKind.Ruler, Category.None, width);
        else if (Is(word, "Skip"))
            view = new LcdView(LcdViewKind.Skip, Category.None);
        else if (Is(word, "Turrets") || Is(word, "Turret"))
            view = new LcdView(LcdViewKind.Turrets, Category.None, width);
        else if (CategoryWords.TryGetValue(word, out var category))
            view = new LcdView(LcdViewKind.Category, category, width, page);
        else
        {
            view = default;
            return false;
        }
        return true;
    }

    private static readonly Regex AnyBracketRx = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    /// <summary>"Base LCD [Cargo] [Lock]" → "Base LCD". Used for LCD titles and dock names.</summary>
    public static string StripTags(string name, string fallback = "")
    {
        var s = AnyBracketRx.Replace(name ?? "", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s.Length == 0 ? fallback : s;
    }

    /// <summary>Group tags: one word for several categories.</summary>
    private static readonly Dictionary<string, (string Label, Category[] Categories)> Groups =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Everything a character carries except ammo: hand weapons/tools + medkits, O2, H2, suit batteries.
            ["Player"] = ("Player", new[] { Category.Tools, Category.Consumables }),
        };

    public static bool TryCategoryWord(string word, out Category category) => CategoryWords.TryGetValue(word, out category);

    private static readonly Regex ItemTagRx = new(
        @"^\[\s*([A-Za-z][A-Za-z0-9\- ]*?)(?:\s+([Pp][1-9]))?\s*\]$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>[Si], [Iron Ore], [Heavy-duty Plate P2]. Latin letters, digits, '-' and spaces only.</summary>
    private static bool TryItemTag(string bracket, int defaultPriority, out ItemTag tag)
    {
        tag = default;
        var m = ItemTagRx.Match(bracket);
        if (!m.Success) return false;
        var text = m.Groups[1].Value.Trim();
        if (text.Length == 0) return false;
        // Words that are tags on their own are never items.
        foreach (var exact in new[] { "Skip", "Ruler", "Alerts", "Alert", "Power", "Damage", "Turrets", "Turret" })
            if (text.Equals(exact, StringComparison.OrdinalIgnoreCase)) return false; // screen words, not items
        foreach (var reserved in new[] { "Cargo", "Unload", "NoUnload", "Lock", "NoSort", "NoTidy", "Make Ammo" })
            if (text.StartsWith(reserved, StringComparison.OrdinalIgnoreCase)) return false;
        var first = text.Split(' ')[0];
        if (CategoryWords.ContainsKey(first) || Groups.ContainsKey(first)) return false; // "[Ore Main]" stays invalid
        var priority = defaultPriority;
        if (m.Groups[2].Success) priority = m.Groups[2].Value[1] - '0';
        tag = new ItemTag(text, priority);
        return true;
    }

    private static bool TryParsePriority(string modifier, out int priority)
    {
        priority = 0;
        if (modifier.Length != 2 || (modifier[0] != 'P' && modifier[0] != 'p'))
            return false;
        var digit = modifier[1] - '0';
        if (digit < 1 || digit > 9)
            return false;
        priority = digit;
        return true;
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
