using System;
using System.Collections.Generic;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.Characters;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Library.Definitions;
using Keen.VRage.Library.Utils;

namespace ClientPlugin.Game;

/// <summary>
/// Turns a game ItemDefinition into game-independent ItemTraits.
/// Ore / Material / Component come from ItemTypes (confirmed in game).
/// SE2 has no Ammo / Ice flags: those (and power cells) are recognised from the item's content path
/// (ItemPaths). Consumable is matched by enum name.
/// </summary>
internal static class ItemTraitsMapper
{
    private static readonly Dictionary<ItemDefinition, Category> Cache = new();
    private static HashSet<Guid> toolItems;
    private static List<(ItemTypes Flag, ItemTraits Trait)> namedFlags;

    public static void ResetCache()
    {
        Cache.Clear();
        toolItems = null;
    }

    public static Category CategoryOf(ItemDefinition definition)
    {
        if (definition == null) return Category.None;
        if (Cache.TryGetValue(definition, out var cached)) return cached;
        var category = ItemClassifier.Classify(TraitsOf(definition));
        Cache[definition] = category;
        return category;
    }

    public static ItemTraits TraitsOf(ItemDefinition definition)
    {
        var traits = ItemTraits.None;
        var type = definition.Type;
        if ((type & ItemTypes.Ore) != 0) traits |= ItemTraits.Ore;
        if ((type & ItemTypes.Material) != 0) traits |= ItemTraits.Material;
        if ((type & ItemTypes.Component) != 0) traits |= ItemTraits.Component;

        foreach (var (flag, trait) in NamedFlags())
            if ((type & flag) != 0)
                traits |= trait;

        // Ammo, ice and power cells are recognised from the content path ("guid : Vanilla\Items\Ammo\...").
        traits |= ItemPaths.TraitsFromPath(definition.ToString());

        if (ToolItems().Contains(definition.Guid))
            traits |= ItemTraits.Tool;

        return traits;
    }

    /// <summary>Hand tools and hand weapons. These are never auto-moved off a character.</summary>
    public static HashSet<Guid> ToolItems()
    {
        if (toolItems != null) return toolItems;
        var result = new HashSet<Guid>();
        try
        {
            var tools = Singleton<DefinitionManager>.Instance.GetConfiguration<ToolsConfiguration>();
            foreach (var item in tools.ItemPrefabs.Values) result.Add(item.Guid);
            foreach (var item in tools.GunItems.Values) result.Add(item.Guid);
        }
        catch (Exception e)
        {
            PluginLog.Warn("Could not read ToolsConfiguration, tools will be treated as Misc: " + e.Message);
        }
        toolItems = result;
        return result;
    }

    private static List<(ItemTypes, ItemTraits)> NamedFlags()
    {
        if (namedFlags != null) return namedFlags;
        namedFlags = new List<(ItemTypes, ItemTraits)>();
        // From the discovery report: Ore, Material, Component, Item, Consumable, Datapad, Quest, BlockSchematic.
        // Only Consumable is matched by name here (no Ammo/Ice flags exist: those come from the item path).
        foreach (var name in Enum.GetNames(typeof(ItemTypes)))
        {
            if (!name.Equals("Consumable", StringComparison.OrdinalIgnoreCase)) continue;
            var flag = (ItemTypes)Enum.Parse(typeof(ItemTypes), name);
            if (Convert.ToInt64(flag) != 0)
                namedFlags.Add((flag, ItemTraits.Consumable));
        }
        return namedFlags;
    }
}
