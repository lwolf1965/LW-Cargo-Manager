using System;

namespace ClientPlugin.Policy;

/// <summary>
/// Game-independent description of an item. The game adapter fills this from
/// ItemDefinition.Type (ItemTypes flags), ToolsConfiguration and item names.
/// </summary>
[Flags]
public enum ItemTraits
{
    None = 0,
    Ore = 1 << 0,
    Ice = 1 << 1,
    Material = 1 << 2,   // refined metals
    Component = 1 << 3,
    Ammo = 1 << 4,
    Tool = 1 << 5,       // hand tools and hand weapons (ToolsConfiguration)
    Consumable = 1 << 6, // ItemTypes.Consumable (suit battery, jetpack fuel, O2 recharge, medkit)
    Cell = 1 << 7,       // power cells / battery items
}

public static class ItemClassifier
{
    /// <summary>
    /// Maps traits to exactly one category. Order matters: an item with several flags
    /// gets one stable category, so it can't bounce between containers.
    /// </summary>
    public static Category Classify(ItemTraits traits)
    {
        if ((traits & ItemTraits.Tool) != 0) return Category.Tools;
        if ((traits & ItemTraits.Consumable) != 0) return Category.Consumables;
        if ((traits & ItemTraits.Cell) != 0) return Category.Cells;
        if ((traits & ItemTraits.Ammo) != 0) return Category.Ammo;
        if ((traits & ItemTraits.Ice) != 0) return Category.Ice;
        if ((traits & ItemTraits.Ore) != 0) return Category.Ore;
        // Materials and components are both "Parts" (one shorter word, one container type).
        if ((traits & (ItemTraits.Material | ItemTraits.Component)) != 0) return Category.Parts;
        return Category.Misc;
    }

    /// <summary>Order used by TidyOrder.CategoryThenName.</summary>
    public static int SortRank(Category category) => category switch
    {
        Category.Ore => 0,
        Category.Ice => 1,
        Category.Parts => 2,
        Category.Ammo => 4,
        Category.Tools => 5,
        Category.Consumables => 6,
        Category.Cells => 7,
        Category.Misc => 8,
        _ => 9,
    };
}
