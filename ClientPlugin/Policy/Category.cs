// Pure policy code: no game types allowed in the Policy folder.
// It is compiled into the plugin AND into the test project (tests/).

namespace ClientPlugin.Policy;

/// <summary>Item category used for sorting. One item has exactly one category.</summary>
public enum Category
{
    None = 0,
    Ore,
    Ice,
    /// <summary>SE2 "Material" and "Component" items (refined metals, plates, frames ...). Tag [Parts].</summary>
    Parts,
    Ammo,
    Tools,
    /// <summary>SE2 Consumable items: suit batteries, jetpack fuel, oxygen recharges, medkits. Tag [Consumables] (old: [Bottles]).</summary>
    Consumables,
    /// <summary>Power cells / battery items (keep their charge metadata). Tag [Cells].</summary>
    Cells,
    Misc,
}

/// <summary>What the plugin may do with a block's inventory.</summary>
public enum BlockRole
{
    /// <summary>Never touched (turrets, reactors, generators, welders, unknown blocks).</summary>
    Consumer = 0,
    /// <summary>Sorted in and out, tidied (cargo containers, connectors).</summary>
    Storage,
    /// <summary>Only the output inventory may be emptied (refinery, assembler, drill, grinder).</summary>
    Producer,
}

/// <summary>When a connector counts as an unload dock.</summary>
public enum UnloadMode
{
    /// <summary>Only connectors tagged [Unload] or [Unload All].</summary>
    Tagged,
    /// <summary>Every connector on a static grid, unless tagged [NoUnload].</summary>
    AllStationConnectors,
}

/// <summary>How items are ordered inside a container by the tidy job.</summary>
public enum TidyOrder
{
    Off,
    CategoryThenName,
    Name,
    AmountDesc,
}
