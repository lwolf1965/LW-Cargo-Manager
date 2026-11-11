using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Keen.Game2.Game.SessionComponents;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.Physicss;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Localization;

namespace ClientPlugin.Game;

/// <summary>
/// M1 helper: writes discovery.txt with what the game actually has, so the open API items
/// can be filled in without guessing. Triggered by the "Write discovery report" button.
/// Sections: block kinds (GUID, names, components), connector members, physics members,
/// ItemTypes flags, item definitions seen in inventories.
/// </summary>
internal static class Discovery
{
    public static volatile bool Requested;

    public static void Write(Session session)
    {
        Requested = false;
        var sb = new StringBuilder();
        sb.AppendLine($"LW Cargo Manager discovery report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Game assembly: {typeof(InventoryComponent).Assembly.GetName().Version}");
        sb.AppendLine();

        var kinds = new Dictionary<Guid, KindInfo>();
        var interesting = new Dictionary<Type, string>();
        var powerSamples = new Dictionary<string, (object Component, string Block)>();
        var items = new Dictionary<ItemDefinition, int>();

        foreach (var entity in session.GetEntitiesOfType<FunctionalBlockComponent>())
        {
            var block = entity.TryGet<FunctionalBlockComponent>();
            if (block == null) continue;
            var guid = block.Definition.BlockKind?.Guid ?? Guid.Empty;
            if (!kinds.TryGetValue(guid, out var info))
                kinds[guid] = info = new KindInfo { DefinitionType = block.Definition.GetType().FullName };
            info.Count++;
            var name = block.DisplayName.EvaluateText();
            if (info.Names.Count < 3 && !info.Names.Contains(name)) info.Names.Add(name);

            var inventories = 0;
            foreach (var component in entity.Components)
            {
                var type = component.GetType();
                if (PowerSampleNames.Contains(type.Name) && !powerSamples.ContainsKey(type.Name))
                    powerSamples[type.Name] = (component, name);
                info.Components.Add(type.FullName);
                if (component is InventoryComponent inv)
                {
                    inventories++;
                    var stacks = inv.Items;
                    for (var i = 0; i < stacks.Length; i++)
                    {
                        items.TryGetValue(stacks[i].Definition, out var n);
                        items[stacks[i].Definition] = n + 1;
                    }
                }
                if (ConnectorAdapter.IsConnectorComponentType(type)) interesting[type] = "Connector-like component";
                if (LcdAdapter.IsSurfaceType(type)) interesting[type] = "LCD / text surface component";
                var tn = type.Name;
                if (tn.Contains("Refinery") || tn.Contains("Assembler") || tn.Contains("Production") || tn.Contains("Drill") || tn.Contains("Grinder"))
                    interesting[type] = "Production-like component";
                // M9 [Power] / M10 [Gas]: power producers, consumers, batteries, tanks.
                if (tn.Contains("Powerable") || tn.Contains("Generator") || tn.Contains("WindTurbine") || tn.Contains("Battery")
                    || tn.Contains("ResourceConverter") || tn.Contains("ResourceContainer") || tn.Contains("Power") || tn.Contains("Reactor"))
                    interesting[type] = "Power / resource component";
                // M11 [Damage]: integrity, build progress (unfinished blocks).
                if (tn.Contains("Breakable") || tn.Contains("Integrity") || tn.Contains("Damage") || tn.Contains("Construction")
                    || tn.Contains("Buildable") || tn.Contains("BuildProgress") || tn.Contains("Deformation"))
                    interesting[type] = "Damage / build state component";
            }
            info.MaxInventories = Math.Max(info.MaxInventories, inventories);

            var physics = block.Grid?.Entity.TryGet<GridPhysicsComponent>();
            if (physics != null) interesting[physics.GetType()] = "Grid physics";
            if (block.Grid != null)
            {
                interesting[block.Grid.GetType()] = "Grid component";
                foreach (var gridComponent in block.Grid.Entity.Components)
                    interesting[gridComponent.GetType()] = "Grid entity component (name, power network ...)";
            }
        }

        sb.AppendLine("== Block kinds (BlockKind GUID → role overrides use these) ==");
        foreach (var pair in kinds.OrderByDescending(p => p.Value.Count))
        {
            sb.AppendLine($"{pair.Key}  count={pair.Value.Count}  inventories={pair.Value.MaxInventories}  def={pair.Value.DefinitionType}");
            sb.AppendLine($"    names: {string.Join(" | ", pair.Value.Names)}");
            sb.AppendLine($"    components: {string.Join(", ", pair.Value.Components.OrderBy(x => x))}");
        }
        sb.AppendLine();

        sb.AppendLine("== Members of interesting component types ==");
        foreach (var pair in interesting)
        {
            sb.AppendLine($"-- {pair.Value}: {pair.Key.FullName}");
            DumpMembers(sb, pair.Key);
        }
        sb.AppendLine();

        DumpPowerData(sb, powerSamples, interesting.Keys);

        sb.AppendLine("== ItemTypes flags ==");
        foreach (var name in Enum.GetNames(typeof(ItemTypes)))
            sb.AppendLine($"{name} = {Convert.ToInt64(Enum.Parse(typeof(ItemTypes), name))}");
        sb.AppendLine();

        sb.AppendLine("== Item definitions seen in inventories ==");
        foreach (var pair in items.OrderBy(p => p.Key.Type.ToString()))
        {
            var def = pair.Key;
            sb.AppendLine($"{def.Guid}  type={def.Type}  stackable={def.IsStackable}  category={ItemTraitsMapper.CategoryOf(def)}  stacks={pair.Value}  text={def}");
        }
        sb.AppendLine();

        sb.AppendLine("== Item definition members ==");
        DumpMembers(sb, typeof(ItemDefinition));
        sb.AppendLine("== InventoryComponent members ==");
        DumpMembers(sb, typeof(InventoryComponent));

        var path = Path.Combine(Plugin.Instance.DataDir, "discovery.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        PluginLog.Info("Discovery report written: " + path);
        Notifier.Hud("Discovery report written to discovery.txt");
    }

    private static readonly HashSet<string> PowerSampleNames = new()
    {
        "SolarGeneratorComponent", "WindTurbineComponent", "ItemToResourceConverterComponent",
        "ResourceContainerInventoryComponent", "PowerableBlockComponent",
    };

    /// <summary>
    /// M9b: per-generator output lives in entity data structs (SolarGeneratorData, WindTurbineResourceData ...),
    /// not in component members. Lists those struct types and their fields only (type metadata, no game data).
    /// </summary>
    private static void DumpPowerData(StringBuilder sb, Dictionary<string, (object Component, string Block)> samples, IEnumerable<Type> knownTypes)
    {
        sb.AppendLine("== Power data structs (per-generator output) ==");
        var rx = new System.Text.RegularExpressions.Regex(
            @"(Solar|Wind|Resource|Converter|Conversion|Production|Fuel|Power|Battery|Generator)\w*(Data|Flow|Node|Statistics|Stats|Rate|Buffer|Generators)$");
        var assemblies = knownTypes.Select(t => t.Assembly).Concat(samples.Values.Select(v => v.Component.GetType().Assembly)).Distinct().ToList();
        var structs = new List<Type>();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            structs.AddRange(types.Where(t => t.IsValueType && !t.IsEnum && !t.IsGenericTypeDefinition && rx.IsMatch(t.Name)));
        }
        foreach (var t in structs.OrderBy(t => t.FullName))
        {
            sb.AppendLine($"-- struct {t.FullName}");
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                sb.AppendLine($"     field {f.FieldType.Name} {f.Name}");
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                sb.AppendLine($"     prop  {p.PropertyType.Name} {p.Name}");
        }
        sb.AppendLine();

        // No live TryGetData<T> probing here: asking the game's entity data store for a struct type the entity
        // doesn't have crashed the game (native, no managed exception). Live reads come later, with exact types only.
        sb.AppendLine();
    }

    private static void DumpMembers(StringBuilder sb, Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            sb.AppendLine($"   [{t.FullName}]");
            foreach (var p in t.GetProperties(flags)) sb.AppendLine($"     prop  {p.PropertyType.Name} {p.Name}");
            foreach (var f in t.GetFields(flags)) sb.AppendLine($"     field {f.FieldType.Name} {f.Name}");
            foreach (var m in t.GetMethods(flags).Where(m => !m.IsSpecialName))
                sb.AppendLine($"     meth  {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name))})");
            foreach (var e in t.GetEvents(flags)) sb.AppendLine($"     event {e.EventHandlerType?.Name} {e.Name}");
        }
    }

    private sealed class KindInfo
    {
        public int Count;
        public int MaxInventories;
        public string DefinitionType;
        public readonly List<string> Names = new();
        public readonly HashSet<string> Components = new();
    }
}
