using System;
using System.Collections.Generic;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.GameSystems.Ownership;
using Keen.Game2.Simulation.Utils;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.Physicss;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Localization;
using Keen.VRage.Physics.Data;

namespace ClientPlugin.Game;

/// <summary>One inventory found in the world, with everything the jobs need to know about it.</summary>
internal sealed class Store
{
    public InventoryComponent Inventory;
    public Entity BlockEntity;
    public FunctionalBlockComponent Block;
    public CubeGridComponent Grid;
    public string Name;
    public Guid? BlockKind;
    public StoreInfo Info;          // policy view (tags, role, mass, ...)
}

/// <summary>A block that may be a connector (any functional block, checked by ConnectorAdapter).</summary>
internal sealed class BlockRef
{
    public Entity Entity;
    public FunctionalBlockComponent Block;
    public CubeGridComponent Grid;
    public string Name;
    public BlockTags Tags;
    public Guid? BlockKind;
}

internal sealed class GridInfo
{
    public CubeGridComponent Grid;
    public int Id;
    public bool IsStatic;
    public int FunctionalBlockCount;
    public readonly List<Store> Stores = new();
    public readonly List<BlockRef> Blocks = new();
}

/// <summary>Snapshot of all inventories the local player can use. Rebuilt every pass that has work to do
/// (unload-only ticks are skipped when the last scan found no unload dock).</summary>
internal sealed class WorldScan
{
    public readonly Dictionary<CubeGridComponent, GridInfo> Grids = new();
    public readonly Dictionary<InventoryComponent, Store> Stores = new();
    /// <summary>Any owned block tagged [Unload] or [Unload All] (lets the engine skip unload-only ticks).</summary>
    public bool HasUnloadTags;

    /// <summary>Parsed tags per block name and default priority. BlockTags is immutable (init-only), so sharing is safe.</summary>
    private static readonly Dictionary<(string, int), BlockTags> TagCache = new();

    private static BlockTags ParseCached(string name, int defaultPriority)
    {
        if (TagCache.TryGetValue((name, defaultPriority), out var tags)) return tags;
        if (TagCache.Count > 20000) TagCache.Clear(); // renames add entries; keep it bounded
        tags = TagParser.Parse(name, defaultPriority);
        TagCache[(name, defaultPriority)] = tags;
        return tags;
    }

    public static WorldScan Build(Session session, IdentityId player, BlockRoles roles, int defaultPriority)
    {
        var scan = new WorldScan();
        var nextStoreId = 0;

        foreach (var entity in session.GetEntitiesOfType<FunctionalBlockComponent>())
        {
            var block = entity.TryGet<FunctionalBlockComponent>();
            var grid = block?.Grid;
            if (grid == null || !block.Functional)
                continue;

            if (!AccessCheck.CanUse(entity, grid, player))
                continue;

            if (!scan.Grids.TryGetValue(grid, out var gridInfo))
            {
                var physics = grid.Entity.TryGet<GridPhysicsComponent>();
                gridInfo = new GridInfo
                {
                    Grid = grid,
                    Id = scan.Grids.Count + 1,
                    IsStatic = physics != null && physics.GridMotionType == BodyArgs.Motion.Static,
                };
                scan.Grids.Add(grid, gridInfo);
            }
            gridInfo.FunctionalBlockCount++;

            var name = block.DisplayName.EvaluateText() ?? "";
            var kind = block.Definition.BlockKind?.Guid;
            var tags = ParseCached(name, defaultPriority);
            if (tags.Unload || tags.UnloadAll) scan.HasUnloadTags = true;
            gridInfo.Blocks.Add(new BlockRef { Entity = entity, Block = block, Grid = grid, Name = name, Tags = tags, BlockKind = kind });

            var role = roles.RoleOf(kind);
            if (role == BlockRole.Consumer && !roles.IsKnown(kind))
                role = AutoRole(entity, kind, name);
            if (role == BlockRole.Consumer)
                continue;

            // Producer output (discovery report: refinery, smelter, assembler, Gearforge have 2 inventories,
            // drill and grinder have 1). Rule: single inventory → it's the output; otherwise InventoryIndex 0
            // is the input and index >= 1 is the output. TODO: confirm in game that index 1 holds the products.
            var inventoryCount = 0;
            foreach (var component in entity.Components)
                if (component is InventoryComponent) inventoryCount++;

            foreach (var component in entity.Components)
            {
                if (component is not InventoryComponent inventory || inventory.ConveyorSystem == null)
                    continue;

                var isOutput = role == BlockRole.Storage || inventoryCount == 1 || inventory.InventoryIndex >= 1;
                var store = new Store
                {
                    Inventory = inventory,
                    BlockEntity = entity,
                    Block = block,
                    Grid = grid,
                    Name = name,
                    BlockKind = kind,
                    Info = Describe(inventory, ++nextStoreId, gridInfo, role, isOutput, tags),
                };
                gridInfo.Stores.Add(store);
                scan.Stores[inventory] = store;
            }
        }

        return scan;
    }

    private static readonly Dictionary<Guid, BlockRole> AutoRoles = new();

    /// <summary>Modded / unknown block kinds: role from the components (cached per kind, logged once).</summary>
    private static BlockRole AutoRole(Entity entity, Guid? kind, string name)
    {
        if (!kind.HasValue) return BlockRole.Consumer;
        if (AutoRoles.TryGetValue(kind.Value, out var cached)) return cached;
        var names = new HashSet<string>();
        foreach (var component in entity.Components) names.Add(component.GetType().Name);
        var role = BlockRoles.AutoRole(names);
        AutoRoles[kind.Value] = role;
        if (role != BlockRole.Consumer)
            PluginLog.Info($"Unknown block kind {kind.Value} ('{name}') treated as {role} (from its components)");
        return role;
    }

    /// <summary>Fresh policy view of one inventory (mass and contents change after transfers).</summary>
    public static StoreInfo Describe(InventoryComponent inventory, int id, GridInfo grid, BlockRole role, bool isOutput, BlockTags tags)
    {
        var items = inventory.Items;
        var content = Category.None;
        var mixed = false;
        for (var i = 0; i < items.Length; i++)
        {
            var category = ItemTraitsMapper.CategoryOf(items[i].Definition);
            if (content == Category.None) content = category;
            else if (content != category) mixed = true;
        }

        return new StoreInfo
        {
            Id = id,
            GridId = grid.Id,
            GridIsStatic = grid.IsStatic,
            Role = role,
            IsOutput = isOutput,
            Tags = tags,
            MaxMass = Fx.ToDouble(inventory.Mass.Max),
            CurrentMass = Fx.ToDouble(inventory.Mass.Current),
            IsEmpty = items.Length == 0,
            SlotsFull = inventory.Definition.MaxStacks.HasValue && items.Length >= inventory.Definition.MaxStacks.Value,
            ContentCategory = mixed ? Category.None : content,
        };
    }

    public void RefreshAll()
    {
        foreach (var store in Stores.Values) Refresh(store);
    }

    public void Refresh(Store store)
    {
        var grid = Grids[store.Grid];
        var old = store.Info;
        store.Info = Describe(store.Inventory, old.Id, grid, old.Role, old.IsOutput, old.Tags);
    }
}

/// <summary>
/// Who may the plugin act for? Design: "whatever the game lets the player open".
/// TODO(M1, item 6): use the game's real access rule (faction / share settings).
/// For now: owned by the local player.
/// </summary>
internal static class AccessCheck
{
    public static bool CanUse(Entity blockEntity, CubeGridComponent grid, IdentityId player)
    {
        var owner = blockEntity.TryGetOwner() ?? grid?.Entity.TryGetOwner();
        return owner.HasValue && owner.Value.Equals(player);
    }
}
