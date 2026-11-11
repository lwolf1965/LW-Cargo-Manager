using System;
using System.Collections.Generic;
using System.Reflection;
using Keen.Game2.Simulation.WorldObjects.CubeGrids.Physicss;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Connector lock state and partner — NOT confirmed yet (M1, item 3).
///
/// This adapter PROBES the connector component by reflection, looking for typical member names
/// (IsConnected / IsLocked and Other / OtherConnector / ConnectedConnector ...). If the probe
/// finds them, unload docks work per connector. If not, UnloadJob uses a fallback that detects
/// docked grids through conveyor reachability (see UnloadJob).
///
/// Once the real API is known (run "Write discovery report" and look at the Connector section),
/// replace TryGetPartner with direct calls and delete the probing code.
/// </summary>
internal static class ConnectorAdapter
{
    private static readonly string[] LockedNames = { "IsConnected", "Connected", "IsLocked", "Locked", "IsDocked" };
    // Discovery report: MechanicalBlockComponent has IsConnected and GetConnectedComponent() (→ the other connector).
    private static readonly string[] PartnerNames = { "GetConnectedComponent", "OtherConnector", "ConnectedConnector", "Other", "Partner" };

    private sealed class Probe
    {
        public MemberInfo Locked;
        public MemberInfo Partner;
        public bool Usable => Locked != null && Partner != null;
    }

    private static readonly Dictionary<Type, Probe> Probes = new();

    /// <summary>True once any connector type was probed successfully.</summary>
    public static bool Supported { get; private set; }

    /// <summary>True once at least one connector-like component was seen.</summary>
    public static bool SeenAnyConnector { get; private set; }

    public static bool IsConnectorComponentType(Type type)
    {
        var name = type.Name;
        return name.IndexOf("Connector", StringComparison.OrdinalIgnoreCase) >= 0
               && name.IndexOf("Conveyor", StringComparison.OrdinalIgnoreCase) < 0;
    }

    /// <summary>Is this block entity a connector (has a connector-like component)?</summary>
    public static bool IsConnector(Entity entity) => FindConnectorComponent(entity) != null;

    /// <summary>If the connector on <paramref name="entity"/> is locked, returns the partner connector entity.</summary>
    public static bool TryGetPartner(Entity entity, out Entity partner)
    {
        partner = null;
        var component = FindConnectorComponent(entity);
        if (component == null) return false;
        SeenAnyConnector = true;

        var probe = GetProbe(component.GetType());
        if (!probe.Usable) return false;
        Supported = true;

        if (!(Read(probe.Locked, component) is bool locked) || !locked) return false;

        var value = Read(probe.Partner, component);
        partner = value switch
        {
            Entity e => e,
            Component c => c.Entity,
            _ => null,
        };
        return partner != null && !ReferenceEquals(partner, entity);
    }

    private static object FindConnectorComponent(Entity entity)
    {
        foreach (var component in entity.Components)
            if (IsConnectorComponentType(component.GetType()))
                return component;
        return null;
    }

    private static Probe GetProbe(Type type)
    {
        if (Probes.TryGetValue(type, out var probe)) return probe;
        probe = new Probe
        {
            Locked = FindMember(type, LockedNames, t => t == typeof(bool)),
            Partner = FindMember(type, PartnerNames, t => typeof(Entity).IsAssignableFrom(t) || typeof(Component).IsAssignableFrom(t)),
        };
        Probes[type] = probe;
        PluginLog.Info($"Connector probe {type.FullName}: locked={probe.Locked?.Name ?? "-"}, partner={probe.Partner?.Name ?? "-"}");
        return probe;
    }

    private static MemberInfo FindMember(Type type, string[] names, Func<Type, bool> typeOk)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in names)
        {
            var property = Reflect.Property(type, name);
            if (property != null && property.GetIndexParameters().Length == 0 && typeOk(property.PropertyType)) return property;
            var field = type.GetField(name, flags) ?? type.GetField("_" + char.ToLowerInvariant(name[0]) + name.Substring(1), flags);
            if (field != null && typeOk(field.FieldType)) return field;
            var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (method != null && typeOk(method.ReturnType)) return method;
        }
        return null;
    }

    private static object Read(MemberInfo member, object target)
    {
        try
        {
            return member switch
            {
                PropertyInfo p => p.GetValue(target),
                FieldInfo f => f.GetValue(target),
                MethodInfo m => m.Invoke(target, null),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Grid mass for the dock direction rule — NOT confirmed yet (M1, item 1).
/// Probes GridPhysicsComponent for a "Mass" member; if missing, the caller uses the
/// number of functional blocks as a rough size instead.
/// </summary>
internal static class GridMass
{
    private static MemberInfo massMember;
    private static bool probed;

    public static double Of(GridInfo grid)
    {
        var physics = grid.Grid.Entity.TryGet<GridPhysicsComponent>();
        if (physics != null)
        {
            if (!probed)
            {
                probed = true;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                massMember = Reflect.Member(physics.GetType(), "Mass");
                PluginLog.Info($"Grid mass probe: {(massMember == null ? "not found, using block count" : massMember.Name)}");
            }

            if (massMember != null)
            {
                try
                {
                    var value = massMember is PropertyInfo p ? p.GetValue(physics) : ((FieldInfo)massMember).GetValue(physics);
                    if (value is FixedPoint fp) return Fx.ToDouble(fp);
                    if (value is IConvertible c) return c.ToDouble(null);
                }
                catch
                {
                    // fall through
                }
            }
        }

        // Fallback: block count. Heavier ships almost always have more blocks.
        return grid.FunctionalBlockCount;
    }
}
