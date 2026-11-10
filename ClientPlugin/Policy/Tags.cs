using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ClientPlugin.Policy;

/// <summary>A category tag with its priority, e.g. [Ore P1] → (Ore, 1).</summary>
public readonly record struct CategoryTag(Category Category, int Priority);

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
}

/// <summary>Width: characters per line from the name ([Cargo Ore 54]); 0 = the plugin setting.</summary>
public readonly record struct LcdView(LcdViewKind Kind, Category Category, int Width = 0);

/// <summary>All tags found in one block name.</summary>
public sealed class BlockTags
{
    public static readonly BlockTags Empty = new BlockTags();

    public bool Unload { get; init; }
    public bool UnloadAll { get; init; }
    public bool NoUnload { get; init; }
    public bool Lock { get; init; }
    public bool NoTidy { get; init; }
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

    private static readonly Regex TagRx = new Regex(
        @"\[\s*([A-Za-z]+)(?:\s+([A-Za-z0-9]+))?\s*\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Dictionary<string, Category> CategoryWords = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase)
    {
        ["Ore"] = Category.Ore,
        ["Ores"] = Category.Ore,
        ["Ice"] = Category.Ice,
        ["Part"] = Category.Parts,
        ["Parts"] = Category.Parts,
        // Old names, kept so existing container names keep working:
        ["Ingot"] = Category.Parts,
        ["Ingots"] = Category.Parts,
        ["Material"] = Category.Parts,
        ["Materials"] = Category.Parts,
        ["Component"] = Category.Parts,
        ["Components"] = Category.Parts,
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

        bool unload = false, unloadAll = false, noUnload = false, locked = false, noTidy = false;
        var categories = new List<CategoryTag>();
        var lcdViews = new List<LcdView>();
        var items = new List<ItemTag>();

        foreach (Match bracket in AnyBracketRx.Matches(name))
        {
            var cargo = CargoRx.Match(bracket.Value);
            if (cargo.Success)
            {
                var kindWord = cargo.Groups[1].Success ? cargo.Groups[1].Value : null;
                var width = cargo.Groups[2].Success ? Math.Clamp(int.Parse(cargo.Groups[2].Value), 20, 200) : 0;
                if (kindWord == null) lcdViews.Add(new LcdView(LcdViewKind.Summary, Category.None, width));
                else if (Is(kindWord, "Alerts") || Is(kindWord, "Alert")) lcdViews.Add(new LcdView(LcdViewKind.Alerts, Category.None, width));
                else if (Is(kindWord, "Ruler")) lcdViews.Add(new LcdView(LcdViewKind.Ruler, Category.None, width));
                else if (CategoryWords.TryGetValue(kindWord, out var lcdCategory)) lcdViews.Add(new LcdView(LcdViewKind.Category, lcdCategory, width));
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

            if (Is(word, "Unload"))
            {
                if (modifier == null) unload = true;
                else if (Is(modifier, "All")) { unload = true; unloadAll = true; }
                continue;
            }

            if (modifier == null)
            {
                if (Is(word, "NoUnload")) { noUnload = true; continue; }
                if (Is(word, "Lock") || Is(word, "NoSort")) { locked = true; continue; } // [NoSort] = AutoSorter alias
                if (Is(word, "NoTidy")) { noTidy = true; continue; }
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

        // AutoSorter's Russian alias for [NoSort]
        if (name.IndexOf("[Не сортировать]", StringComparison.OrdinalIgnoreCase) >= 0)
            locked = true;

        if (!unload && !noUnload && !locked && !noTidy && categories.Count == 0 && lcdViews.Count == 0 && items.Count == 0)
            return BlockTags.Empty;

        return new BlockTags
        {
            Unload = unload,
            UnloadAll = unloadAll,
            NoUnload = noUnload,
            Lock = locked,
            NoTidy = noTidy,
            Categories = categories,
            Items = items,
            LcdViews = lcdViews,
        };
    }

    /// <summary>[Cargo], [Cargo Ore], [Cargo 54], [Cargo Ore 54]: LCD view with optional width.</summary>
    private static readonly Regex CargoRx = new Regex(
        @"^\[\s*Cargo(?:\s+([A-Za-z]+))?(?:\s+(\d{2,3}))?\s*\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AnyBracketRx = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);

    /// <summary>"Base LCD [Cargo] [Lock]" → "Base LCD". Used for LCD titles and dock names.</summary>
    public static string StripTags(string name, string fallback = "")
    {
        var s = AnyBracketRx.Replace(name ?? "", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s.Length == 0 ? fallback : s;
    }

    public static bool TryCategoryWord(string word, out Category category) => CategoryWords.TryGetValue(word, out category);

    private static readonly Regex ItemTagRx = new Regex(
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
        foreach (var reserved in new[] { "Cargo", "Unload", "NoUnload", "Lock", "NoSort", "NoTidy" })
            if (text.StartsWith(reserved, StringComparison.OrdinalIgnoreCase)) return false;
        if (CategoryWords.ContainsKey(text.Split(' ')[0])) return false; // "[Ore Main]" stays invalid
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
