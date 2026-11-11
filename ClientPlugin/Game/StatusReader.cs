using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Reads power and damage state for [Power], [Power Sources], [Power Users], [Damage].
/// Member names come from the discovery report (game 4.0.0.3165):
///   PowerableBlockComponent._appliedConsumption (FixedPoint), FunctionalBlockComponent.Working,
///   ResourceContainerComponent.CurrentChargeValue / MaxChargeValue / CurrentChargeFlowRate (batteries: ResourceContainerInventoryComponent),
///   SolarGeneratorComponent, WindTurbineComponent, ItemToResourceConverterComponent (reactor; O2/H2 generator too),
///   CubeBlockComponent.EffectiveIntegrity / EffectiveBuildProgress, CubeGridComponent.VisitAllBlocksWithComponent&lt;T&gt;(Action&lt;T&gt;, bool).
/// Everything is read through cached reflection, read only. A missing member disables that part with one log line.
/// Units (from the power sample, Ore Detector = 250): power in kW, battery energy in kJ (kW·s); converted to MW / MWh here.
/// </summary>
internal static class StatusReader
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<(Type, string), MemberInfo> Members = new();
    private static readonly HashSet<string> Warned = new();
    private static bool powerSampleLogged;
    private const double KwToMw = 1.0 / 1000;
    private const double KjToMwh = 1.0 / 3_600_000;

    // ---------------- power ----------------

    public static PowerSnapshot ReadPower(GridInfo grid)
    {
        var snapshot = new PowerSnapshot();
        foreach (var block in grid.Blocks)
        {
            try { AddPowerBlock(snapshot, block); }
            catch (Exception e) { WarnOnce("power:" + e.GetType().Name, $"Power read failed on '{block.Name}': {e.Message}"); }
        }
        snapshot.Balance = PowerBalance(snapshot);
        LogSample(snapshot);
        return snapshot;
    }

    private static void AddPowerBlock(PowerSnapshot snapshot, BlockRef block)
    {
        object powerable = null, container = null, generator = null;
        bool solar = false, wind = false, converter = false, battery = false;
        foreach (var component in block.Entity.Components)
        {
            var n = component.GetType().Name;
            if (n == "PowerableBlockComponent") powerable = component;
            else if (n == "ResourceContainerInventoryComponent") { container = component; battery = true; }
            else if (n == "SolarGeneratorComponent") { solar = true; generator = component; }
            else if (n == "WindTurbineComponent") { wind = true; generator = component; }
            else if (n == "ItemToResourceConverterComponent") { converter = true; generator = component; }
            else if (n == "ResourceContainerComponent" && container == null) container = component;
        }

        var name = TagParser.StripTags(block.Name, block.Name);
        var working = ReadBool(block.Block, "Working") ?? true;

        if (battery && container != null)
        {
            snapshot.Sources.Add(new PowerSource(name, PowerSourceKind.Battery, working,
                (ReadNumber(container, "CurrentChargeValue") ?? 0) * KjToMwh,
                (ReadNumber(container, "MaxChargeValue") ?? 0) * KjToMwh,
                (ReadNumber(container, "CurrentChargeFlowRate") ?? 0) * KwToMw));
            return;
        }
        if (solar || wind || (converter && IsGenerator(name)))
        {
            var kind = solar ? PowerSourceKind.Solar
                : wind ? PowerSourceKind.Wind
                : name.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0 ? PowerSourceKind.Engine : PowerSourceKind.Reactor;
            var (output, max, nowMax) = GeneratorOutput.Read(generator, name);
            snapshot.Sources.Add(new PowerSource(name, kind, working, OutputMW: output, OutputMaxMW: max, OutputNowMaxMW: nowMax));
            return;
        }
        if (powerable != null)
        {
            // The block's resource node says what it consumes and its max (nameplate). Hydrogen thrusters are
            // "powerable" too but consume hydrogen: they are not power users.
            var nodes = GeneratorOutput.ReadNodes(powerable);
            var electric = nodes.Where(n => n.Consumer && GeneratorOutput.IsElectricity(n.Resource)).ToList();
            if (nodes.Count > 0 && electric.Count == 0) return;
            var draw = (ReadNumber(powerable, "_appliedConsumption") ?? 0) * KwToMw;
            var max = electric.Sum(n => n.TheoreticalMax > 0 ? n.TheoreticalMax : n.CurrentMax) * KwToMw;
            snapshot.Users.Add(new PowerUser(name, Math.Max(0, draw), working, max));
        }
    }

    /// <summary>Full-load power planning from what ReadPower collected.</summary>
    private static ResourceBalance PowerBalance(PowerSnapshot p)
    {
        var balance = new ResourceBalance { Kind = ResourceKind.Power, UseNow = p.UseMW, UseMax = p.Users.Sum(u => u.MaxMW) };
        foreach (var s in p.Sources)
        {
            if (s.Kind == PowerSourceKind.Battery || !s.OutputMaxMW.HasValue) continue;
            var variable = s.Kind == PowerSourceKind.Solar || s.Kind == PowerSourceKind.Wind;
            balance.Producers.Add(new Producer(s.Name, variable, s.OutputMaxMW.Value, s.OutputNowMaxMW ?? 0, s.OutputMW ?? 0));
        }
        return balance.UseMax > 0 || balance.Producers.Count > 0 ? balance : null;
    }

    /// <summary>
    /// Hydrogen / oxygen supply and demand: producers (O2/H2 generators) and consumers (hydrogen thrusters,
    /// engines ...) from every block's resource nodes. Tanks are storage and are skipped.
    /// </summary>
    public static (ResourceBalance Hydrogen, ResourceBalance Oxygen) ReadGasBalance(GridInfo grid)
    {
        var h2 = new ResourceBalance { Kind = ResourceKind.Hydrogen };
        var o2 = new ResourceBalance { Kind = ResourceKind.Oxygen };
        foreach (var block in grid.Blocks)
        {
            try
            {
                var name = TagParser.StripTags(block.Name, block.Name);
                foreach (var component in block.Entity.Components)
                {
                    var typeName = component.GetType().Name;
                    if (typeName.StartsWith("ResourceContainer", StringComparison.Ordinal)) continue; // tanks, batteries
                    foreach (var n in GeneratorOutput.ReadNodes(component))
                    {
                        var target = n.Resource.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0 ? h2
                            : n.Resource.IndexOf("Oxygen", StringComparison.OrdinalIgnoreCase) >= 0 ? o2 : null;
                        if (target == null) continue;
                        var max = n.TheoreticalMax > 0 ? n.TheoreticalMax : n.CurrentMax;
                        if (n.Consumer) { target.UseNow += n.Current; target.UseMax += max; }
                        else target.Producers.Add(new Producer(name, false, max, n.CurrentMax, n.Current));
                    }
                }
            }
            catch (Exception e) { WarnOnce("gasflow:" + e.GetType().Name, $"Gas flow read failed on '{block.Name}': {e.Message}"); }
        }
        return (h2.UseMax > 0 || h2.Producers.Count > 0 ? h2 : null, o2.UseMax > 0 || o2.Producers.Count > 0 ? o2 : null);
    }

    /// <summary>Item→resource converters that make power (reactor, hydrogen engine); the O2/H2 generator makes gas.</summary>
    private static bool IsGenerator(string name) =>
        name.IndexOf("Reactor", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void LogSample(PowerSnapshot p)
    {
        if (powerSampleLogged || (p.Users.Count == 0 && p.BatteryCount == 0)) return;
        powerSampleLogged = true;
        var battery = p.Sources.FirstOrDefault(s => s.Kind == PowerSourceKind.Battery);
        var user = p.Users.OrderByDescending(u => u.DrawMW).FirstOrDefault();
        PluginLog.Info($"Power sample (raw values, compare units with the terminal): battery '{battery.Name}' stored={battery.StoredMWh} max={battery.MaxMWh} flow={battery.FlowMW}; " +
                       $"biggest user '{user.Name}' consumption={user.DrawMW}; total use={p.UseMW}, batteries={p.BatteryCount}, sources={p.Sources.Count}");
    }

    // ---------------- gas ----------------

    private static bool gasSampleLogged;

    /// <summary>H2 / O2 tanks of the grid: ResourceContainerComponent (not the battery variant), gas from ResourceType.</summary>
    public static List<GasTank> ReadGas(GridInfo grid)
    {
        var tanks = new List<GasTank>();
        foreach (var block in grid.Blocks)
        {
            try
            {
                foreach (var component in block.Entity.Components)
                {
                    if (component.GetType().Name != "ResourceContainerComponent") continue;
                    var resource = Read(component, "ResourceType")?.ToString() ?? "";
                    GasKind gas;
                    if (resource.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0) gas = GasKind.Hydrogen;
                    else if (resource.IndexOf("Oxygen", StringComparison.OrdinalIgnoreCase) >= 0) gas = GasKind.Oxygen;
                    else continue;
                    var stored = ReadNumber(component, "CurrentChargeValue") ?? 0;
                    var max = ReadNumber(component, "MaxChargeValue") ?? 0;
                    tanks.Add(new GasTank(TagParser.StripTags(block.Name, block.Name), gas, stored, max));
                    if (!gasSampleLogged)
                    {
                        gasSampleLogged = true;
                        PluginLog.Info($"Gas sample: '{block.Name}' resource='{resource}' stored={stored} max={max}");
                    }
                }
            }
            catch (Exception e) { WarnOnce("gas:" + e.GetType().Name, $"Gas read failed on '{block.Name}': {e.Message}"); }
        }
        return tanks;
    }

    // ---------------- damage ----------------

    private static readonly Dictionary<CubeGridComponent, (double Time, DamageSnapshot Snapshot)> DamageCache = new();

    public static void ResetWorld() => DamageCache.Clear();
    private static MethodInfo visitMethod;
    private static Type cubeBlockType;
    private static bool visitProbed;

    /// <summary>All blocks of the grid (armor too) with integrity and build progress. Cached for 10 s per grid.</summary>
    public static DamageSnapshot ReadDamage(GridInfo grid, double now)
    {
        if (DamageCache.TryGetValue(grid.Grid, out var cached) && now - cached.Time < 10)
            return cached.Snapshot;
        if (DamageCache.Count > 200) DamageCache.Clear();

        var snapshot = new DamageSnapshot();
        var blocks = AllBlocks(grid);
        foreach (var block in blocks)
        {
            snapshot.Blocks++;
            var integrity = ReadNumber(block, "EffectiveIntegrity") ?? 1;
            var build = ReadNumber(block, "EffectiveBuildProgress") ?? 1;
            if (DamageRules.IsProblem(integrity, build))
                snapshot.Problems.Add(new DamagedBlock(BlockName(block), Clamp01(integrity), Clamp01(build)));
        }
        DamageCache[grid.Grid] = (now, snapshot);
        return snapshot;
    }

    private static List<object> AllBlocks(GridInfo grid)
    {
        if (!visitProbed)
        {
            visitProbed = true;
            cubeBlockType = typeof(FunctionalBlockComponent);
            while (cubeBlockType != null && cubeBlockType.Name != "CubeBlockComponent") cubeBlockType = cubeBlockType.BaseType;
            visitMethod = grid.Grid.GetType().GetMethods(Flags).FirstOrDefault(m =>
                m.Name == "VisitAllBlocksWithComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType.IsGenericType
                && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Action<>));
            PluginLog.Info($"Damage probe: cubeBlock={(cubeBlockType != null)}, visit={(visitMethod != null)}");
        }

        if (cubeBlockType != null && visitMethod != null)
        {
            try
            {
                var collectorType = typeof(Collector<>).MakeGenericType(cubeBlockType);
                var collector = (ICollector)Activator.CreateInstance(collectorType);
                var action = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(cubeBlockType), collector, collectorType.GetMethod(nameof(Collector<object>.Add)));
                visitMethod.MakeGenericMethod(cubeBlockType).Invoke(grid.Grid, new object[] { action, false });
                return collector.Items;
            }
            catch (Exception e)
            {
                visitMethod = null;
                PluginLog.Warn("Damage: VisitAllBlocksWithComponent failed, counting functional blocks only: " + (e.InnerException?.Message ?? e.Message));
            }
        }
        // Fallback: functional blocks found by the scan (no armor, no damaged ones the scan skipped).
        return grid.Blocks.Select(b => (object)b.Block).ToList();
    }

    private interface ICollector { List<object> Items { get; } }

    private sealed class Collector<T> : ICollector
    {
        public List<object> Items { get; } = new();
        public void Add(T block) => Items.Add(block);
    }

    private static string BlockName(object block)
    {
        if (block is Component component)
        {
            var functional = component.Entity?.Components.OfType<FunctionalBlockComponent>().FirstOrDefault();
            if (functional != null)
            {
                var text = TagParser.StripTags(functional.DisplayName.EvaluateText() ?? "", "");
                if (text.Length > 0) return text; // a name made only of tags ("[Ice]") falls through to the block type
            }
        }
        // Armor and other non-terminal blocks: name from the definition path.
        var definition = Read(block, "Definition");
        return ItemPaths.NameFromPath(definition?.ToString()) ?? "Block";
    }

    private static double Clamp01(double v) => v > 1.5 ? 1 : Math.Clamp(v, 0, 1); // > 1.5: not a ratio, treat as healthy

    // ---------------- reflection helpers ----------------

    private static object Read(object target, string member)
    {
        if (target == null) return null;
        var key = (target.GetType(), member);
        if (!Members.TryGetValue(key, out var info))
        {
            info = Reflect.Member(target.GetType(), member);
            Members[key] = info;
            if (info == null) WarnOnce(key.ToString(), $"Status: member {target.GetType().Name}.{member} not found");
        }
        return info switch
        {
            PropertyInfo p => p.GetValue(target),
            FieldInfo f => f.GetValue(target),
            _ => null,
        };
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        return null;
    }

    private static double? ReadNumber(object target, string member)
    {
        var value = Read(target, member);
        return value switch
        {
            null => null,
            FixedPoint fp => Fx.ToDouble(fp),
            float f => f,
            double d => d,
            int i => i,
            _ => double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
        };
    }

    private static bool? ReadBool(object target, string member) => Read(target, member) as bool?;

    private static void WarnOnce(string key, string message)
    {
        if (Warned.Add(key)) PluginLog.Warn(message);
    }
}
