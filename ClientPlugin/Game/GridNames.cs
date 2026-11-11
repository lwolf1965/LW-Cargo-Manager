using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;

namespace ClientPlugin.Game;

/// <summary>
/// The player-given grid name ("Black Widow"). NOT confirmed: the discovery report doesn't show the member yet.
/// Probes the grid component and every component on the grid entity for a name-like member
/// (DisplayName, GridName, CustomName, Name). A localized text object is turned into a string with
/// EvaluateText(). The member found is logged once; null if nothing is found.
/// </summary>
internal static class GridNames
{
    private static readonly string[] Members = { "DisplayName", "GridName", "CustomName", "DisplayNameText", "Name" };
    private static readonly Dictionary<Type, MemberInfo> Found = new();
    private static readonly HashSet<Type> Logged = new();
    private static readonly HashSet<Type> RawLogged = new();
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static string Of(CubeGridComponent grid)
    {
        if (grid == null) return null;
        try
        {
            var name = Read(grid);
            if (name != null) return name;
            foreach (var component in grid.Entity.Components)
            {
                if (ReferenceEquals(component, grid)) continue;
                name = Read(component);
                if (name != null) return name;
            }
            LogMissing(grid);
        }
        catch (Exception e)
        {
            if (Logged.Add(typeof(GridNames))) PluginLog.Warn("Grid name probe failed: " + e.Message);
        }
        return null;
    }

    private static string Read(object component)
    {
        var type = component.GetType();
        if (!Found.TryGetValue(type, out var member))
        {
            member = Members.Select(n => Reflect.Member(type, n)).FirstOrDefault(m => m != null);
            Found[type] = member;
            if (member != null) PluginLog.Info($"Grid name probe: {type.Name}.{member.Name}");
        }
        if (member == null) return null;
        var value = member is PropertyInfo p ? p.GetValue(component) : ((FieldInfo)member).GetValue(component);
        var text = value as string;
        if (text == null && value != null)
        {
            var evaluate = value.GetType().GetMethod("EvaluateText", Type.EmptyTypes);
            text = evaluate != null ? evaluate.Invoke(value, null) as string : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                // Fallback: the text object's own ToString(), unless that is just its type name.
                var raw = value.ToString();
                if (!string.IsNullOrWhiteSpace(raw) && raw != value.GetType().FullName && raw != value.GetType().Name) text = raw;
                if (RawLogged.Add(type))
                    PluginLog.Info($"Grid name raw: {type.Name}.{member.Name} type={value.GetType().FullName} evaluate='{text}' toString='{raw}'");
            }
        }
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static void LogMissing(CubeGridComponent grid)
    {
        if (!Logged.Add(typeof(CubeGridComponent))) return;
        var types = string.Join(", ", grid.Entity.Components.Select(c => c.GetType().Name));
        PluginLog.Info("Grid name probe: no name member found. Grid components: " + types);
    }
}
