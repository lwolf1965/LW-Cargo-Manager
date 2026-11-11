using System;
using System.Reflection;

namespace ClientPlugin.Game;

/// <summary>
/// Member lookup that never throws AmbiguousMatchException. Game classes re-declare members with "new"
/// (FunctionalBlockComponent.Definition hides CubeBlockComponent.Definition), and Type.GetProperty(name)
/// throws for those. These helpers walk from the most derived type up and return the first match.
/// </summary>
internal static class Reflect
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static PropertyInfo Property(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
            foreach (var p in t.GetProperties(Flags))
                if (p.Name == name && p.GetIndexParameters().Length == 0)
                    return p;
        return null;
    }

    public static FieldInfo Field(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, Flags);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>Property first, then field.</summary>
    public static MemberInfo Member(Type type, string name) => (MemberInfo)Property(type, name) ?? Field(type, name);

    public static object Get(MemberInfo member, object target) => member switch
    {
        PropertyInfo p => p.GetValue(target),
        FieldInfo f => f.GetValue(target),
        _ => null,
    };
}
