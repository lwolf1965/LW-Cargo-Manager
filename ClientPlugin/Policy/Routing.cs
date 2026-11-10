using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Policy;

/// <summary>Game-independent view of one inventory, used to choose sources and destinations.</summary>
public sealed class StoreInfo
{
    public int Id { get; init; }
    public int GridId { get; init; }
    public bool GridIsStatic { get; init; }
    public BlockRole Role { get; init; }
    public bool IsOutput { get; init; } = true;   // false = producer input inventory
    public BlockTags Tags { get; init; } = BlockTags.Empty;
    public double MaxMass { get; init; }
    public double CurrentMass { get; init; }
    public bool IsEmpty { get; init; }
    /// <summary>No free stack slot left (MaxStacks reached).</summary>
    public bool SlotsFull { get; init; }
    /// <summary>Single category of all items inside, or None if empty/mixed. Used by AutoCategories.</summary>
    public Category ContentCategory { get; init; }

    public double FreeMass => Math.Max(0, MaxMass - CurrentMass);
}

public sealed class RoutingOptions
{
    public bool OverflowToEmpty { get; init; } = true;
    public bool AutoCategories { get; init; }
    public bool FallbackToUntaggedCargo { get; init; } = true;
    public bool DrainProducerOutputs { get; init; } = true;
}

public static class Routing
{
    /// <summary>
    /// Does this store want to hold the item? An item tag that matches ([Si]) or a category tag ([Ore]),
    /// or content-seeded categories when AutoCategories is on (only for stores without any tag).
    /// </summary>
    public static bool Keeps(StoreInfo store, ItemKey item, RoutingOptions options)
    {
        if (store.Tags.Lock) return false;
        if (store.Tags.AcceptsItem(item)) return true;
        if (store.Tags.HasAnyStorageTag) return store.Tags.Accepts(item.Category);
        return options.AutoCategories && store.ContentCategory != Category.None && store.ContentCategory == item.Category;
    }

    /// <summary>Kept because of an item tag ([Si]), the most specific kind of destination.</summary>
    public static bool KeepsByItemTag(StoreInfo store, ItemKey item) =>
        !store.Tags.Lock && store.Role == BlockRole.Storage && store.Tags.AcceptsItem(item);

    public static bool IsTaggedDestination(StoreInfo store, RoutingOptions options) =>
        !store.Tags.Lock && store.Role == BlockRole.Storage
        && (store.Tags.HasAnyStorageTag || (options.AutoCategories && store.ContentCategory != Category.None));

    /// <summary>Can items be taken out of this inventory by the sorter (same grid)?</summary>
    public static bool CanBeSortSource(StoreInfo store, RoutingOptions options)
    {
        if (store.Tags.Lock) return false;
        return store.Role switch
        {
            BlockRole.Storage => true,
            BlockRole.Producer => options.DrainProducerOutputs && store.IsOutput,
            _ => false,
        };
    }

    /// <summary>
    /// Should the sorter move this item out of this store?
    /// <paramref name="itemTagElsewhere"/>: another container on the grid has a matching item tag and room,
    /// so an [Ore] container gives its Silicon to the [Si] container.
    /// </summary>
    public static bool ShouldSortOut(StoreInfo source, ItemKey item, RoutingOptions options, bool itemTagElsewhere = false)
    {
        if (item.Category == Category.None || !CanBeSortSource(source, options)) return false;
        if (source.Tags.AcceptsItem(item)) return false;
        if (Keeps(source, item, options)) return itemTagElsewhere;
        return true;
    }

    /// <summary>Is the sorter active on a grid? Only if it has at least one destination.</summary>
    public static bool GridOptedIn(IEnumerable<StoreInfo> gridStores, RoutingOptions options) =>
        gridStores.Any(s => IsTaggedDestination(s, options));

    /// <summary>
    /// Destinations for an item, best first: item-tagged containers ([Si]) by priority, then
    /// category-tagged ones by priority then size, then [Misc], then empty overflow, then untagged
    /// fallback (unload only). <paramref name="itemTagsOnly"/>: only item-tagged containers
    /// (used when moving an item out of its category container, to avoid ping-pong).
    /// Only stores in <paramref name="candidates"/> are considered; the caller filters by
    /// conveyor reachability and grid.
    /// </summary>
    public static List<StoreInfo> Destinations(IEnumerable<StoreInfo> candidates, ItemKey item, RoutingOptions options, bool forUnload, bool itemTagsOnly = false)
    {
        var usable = candidates.Where(s => s.Role == BlockRole.Storage && !s.Tags.Lock).ToList();
        // Overflow is only allowed when a destination for this item exists, even if it is full.
        var seeded = usable.Any(s => Keeps(s, item, options));
        var all = usable.Where(s => s.FreeMass > 0).ToList();

        var byItem = all.Where(s => s.Tags.AcceptsItem(item))
            .OrderBy(s => s.Tags.ItemPriorityFor(item))
            .ThenByDescending(s => s.MaxMass)
            .ToList();
        var result = new List<StoreInfo>(byItem);
        if (itemTagsOnly) return result;

        result.AddRange(all.Where(s => !result.Contains(s) && Keeps(s, item, options))
            .OrderBy(s => s.Tags.HasAnyCategory ? s.Tags.PriorityFor(item.Category) : TagParser.DefaultPriority)
            .ThenByDescending(s => s.MaxMass));

        if (item.Category != Category.Misc)
            result.AddRange(all.Where(s => !result.Contains(s) && s.Tags.Accepts(Category.Misc))
                .OrderBy(s => s.Tags.PriorityFor(Category.Misc)).ThenByDescending(s => s.MaxMass));

        if (options.OverflowToEmpty && seeded)
            result.AddRange(all.Where(s => !result.Contains(s) && s.IsEmpty && !s.Tags.HasAnyStorageTag)
                .OrderByDescending(s => s.MaxMass));

        if (forUnload && options.FallbackToUntaggedCargo)
            result.AddRange(all.Where(s => !result.Contains(s) && !s.Tags.HasAnyStorageTag)
                .OrderByDescending(s => s.FreeMass));

        return result;
    }

    /// <summary>
    /// Unload: what may be taken from the docked (giving) grid.
    /// [Unload] takes ore and ice; [Unload All] takes everything except the keep list.
    /// </summary>
    public static bool ShouldUnload(Category item, bool unloadAll, bool unloadIce, ICollection<Category> unloadAllKeeps)
    {
        if (item == Category.None) return false;
        if (!unloadAll) return item == Category.Ore || (unloadIce && item == Category.Ice);
        if (item == Category.Ice && !unloadIce) return false;
        return unloadAllKeeps == null || !unloadAllKeeps.Contains(item);
    }

    public static bool CanBeUnloadSource(StoreInfo store) =>
        !store.Tags.Lock && (store.Role == BlockRole.Storage || (store.Role == BlockRole.Producer && store.IsOutput));

    /// <summary>Round-robin cursor helper.</summary>
    public static int Next(int cursor, int count) => count == 0 ? 0 : (cursor + 1) % count;

    /// <summary>Parses "Tools, Consumables, Ammo" into categories. Unknown words are reported.</summary>
    public static HashSet<Category> ParseCategories(string text, List<string> errors = null)
    {
        var result = new HashSet<Category>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var tags = TagParser.Parse("[" + raw.Trim() + "]");
            if (tags.Categories.Count == 1) result.Add(tags.Categories[0].Category);
            else errors?.Add(raw);
        }
        return result;
    }
}
