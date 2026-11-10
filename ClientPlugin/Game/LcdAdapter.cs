using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClientPlugin.Policy;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.DCS.Components;

namespace ClientPlugin.Game;

/// <summary>One text surface of a block: the block's LCD panel component + the surface index.</summary>
internal readonly struct LcdSurface
{
    public readonly object Panel;
    public readonly int Index;
    public LcdSurface(object panel, int index) { Panel = panel; Index = index; }
}

/// <summary>
/// Writing text to LCD surfaces. From the discovery report: every LCD-capable block has one
/// Keen.Game2.Simulation.WorldObjects.CubeBlocks.Lcd.LcdMultiPanelComponent with
///   int SurfaceCount, SetSurfaceText(int, string), SetSurfaceContent(int, LcdPanelContent).
/// Called through reflection (cached) so a renamed member disables LCDs with a log line instead of a crash.
/// TODO: check in a self-hosted world that other players see the text (replication).
/// </summary>
internal static class LcdAdapter
{
    private const string PanelTypeName = "LcdMultiPanelComponent";

    private sealed class Api
    {
        public PropertyInfo SurfaceCount;
        public MethodInfo SetText;
        public MethodInfo SetContent;
        public object TextContentValue;
        public MethodInfo SetFontSize, SetFont, SetHAlign, SetVAlign, SetPadding;
        public object LeftValue, TopValue;
        public PropertyInfo SurfaceStates;
        public FieldInfo FontField; // ResourceHandle<Font> inside LcdPanelSurfaceState
        public bool Usable => SurfaceCount != null && SetText != null;
    }

    /// <summary>A monospace font handle seen on any LCD (set by the player once), reused for auto-style.</summary>
    private static object monoFont;
    private static bool stateLogged;

    private static readonly Dictionary<Type, Api> Apis = new Dictionary<Type, Api>();

    public static bool Supported { get; private set; }

    /// <summary>Used by the discovery report: LCD-like component types (not cargo occupancy lights).</summary>
    public static bool IsSurfaceType(Type type) =>
        type.Name.IndexOf("Lcd", StringComparison.OrdinalIgnoreCase) >= 0
        || type.Name.IndexOf("TextSurface", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Text surfaces of a block, surface 0, 1, ... (cockpits have several).</summary>
    public static List<LcdSurface> Surfaces(Entity entity)
    {
        var list = new List<LcdSurface>();
        foreach (var component in entity.Components)
        {
            var type = component.GetType();
            if (type.Name != PanelTypeName) continue;
            var api = GetApi(type);
            if (!api.Usable) continue;
            var count = api.SurfaceCount.GetValue(component) is int n ? n : 0;
            for (var i = 0; i < count; i++)
                list.Add(new LcdSurface(component, i));
        }
        return list;
    }

    /// <summary>Switches the surface to text content (once) and writes the text.</summary>
    public static bool TryWrite(LcdSurface surface, string text, bool setContentMode)
    {
        var api = GetApi(surface.Panel.GetType());
        if (!api.Usable) return false;
        Supported = true;
        if (setContentMode && api.SetContent != null && api.TextContentValue != null)
            api.SetContent.Invoke(surface.Panel, new[] { (object)surface.Index, api.TextContentValue });
        api.SetText.Invoke(surface.Panel, new object[] { surface.Index, text });
        return true;
    }

    /// <summary>
    /// Applies the plugin's LCD look: font size, top-left alignment, small padding, and a monospace
    /// font when one was seen on any LCD. Every step is optional; returns false if nothing could be set.
    /// </summary>
    public static bool TryApplyStyle(LcdSurface surface, float fontSize, float padding)
    {
        var api = GetApi(surface.Panel.GetType());
        if (!api.Usable) return false;
        var any = false;
        any |= Call(api.SetFontSize, surface, fontSize);
        any |= Call(api.SetPadding, surface, padding);
        any |= Call(api.SetHAlign, surface, api.LeftValue);
        any |= Call(api.SetVAlign, surface, api.TopValue);
        if (monoFont != null) any |= Call(api.SetFont, surface, monoFont);
        return any;
    }

    /// <summary>
    /// Looks at the font of every surface and remembers the first monospace one ("Mono" in its name).
    /// The game gives no way to look a font up by name, so a player sets RobotoMono on one LCD by hand.
    /// </summary>
    public static void LearnFont(LcdSurface surface)
    {
        if (monoFont != null) return;
        var api = GetApi(surface.Panel.GetType());
        if (api.SurfaceStates == null || api.FontField == null) return;
        try
        {
            if (api.SurfaceStates.GetValue(surface.Panel) is not Array states || surface.Index >= states.Length) return;
            var state = states.GetValue(surface.Index);
            if (!stateLogged)
            {
                stateLogged = true;
                var fields = state.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PluginLog.Info("LCD surface state: " + string.Join(", ", fields.Select(f => $"{f.Name}={Safe(f.GetValue(state))}")));
            }
            var font = api.FontField.GetValue(state);
            var name = Safe(font);
            if (name.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                monoFont = font;
                PluginLog.Info($"LCD: monospace font found ({name}), used for auto-style");
            }
        }
        catch (Exception e)
        {
            api.FontField = null;
            PluginLog.Warn("LCD: cannot read the surface font: " + e.Message);
        }
    }

    public static bool HasMonoFont => monoFont != null;

    private static bool Call(MethodInfo method, LcdSurface surface, object value)
    {
        if (method == null || value == null) return false;
        try
        {
            method.Invoke(surface.Panel, new[] { (object)surface.Index, value });
            return true;
        }
        catch (Exception e)
        {
            PluginLog.Warn($"LCD: {method.Name} failed: {e.InnerException?.Message ?? e.Message}");
            return false;
        }
    }

    private static (MethodInfo, object) EnumSetter(Type type, BindingFlags flags, string name, string valueName)
    {
        var method = type.GetMethods(flags).FirstOrDefault(m =>
            m.Name == name && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType.IsEnum);
        if (method == null) return (null, null);
        var enumType = method.GetParameters()[1].ParameterType;
        var match = Enum.GetNames(enumType).FirstOrDefault(n => n.Equals(valueName, StringComparison.OrdinalIgnoreCase));
        return match == null ? (null, null) : (method, Enum.Parse(enumType, match));
    }

    private static string Safe(object value)
    {
        try { return value?.ToString() ?? "null"; }
        catch { return "?"; }
    }

    private static Api GetApi(Type type)
    {
        if (Apis.TryGetValue(type, out var api)) return api;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        api = new Api
        {
            SurfaceCount = type.GetProperty("SurfaceCount", flags),
            SetText = type.GetMethod("SetSurfaceText", flags, null, new[] { typeof(int), typeof(string) }, null),
        };
        var setContent = type.GetMethods(flags).FirstOrDefault(m =>
            m.Name == "SetSurfaceContent" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType.IsEnum);
        if (setContent != null)
        {
            var enumType = setContent.GetParameters()[1].ParameterType;
            // Prefer a value that is text only, else one that contains "Text" (e.g. TextAndImage).
            var names = Enum.GetNames(enumType);
            var textName = names.FirstOrDefault(n => n.Equals("Text", StringComparison.OrdinalIgnoreCase))
                           ?? names.FirstOrDefault(n => n.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0);
            if (textName != null)
            {
                api.SetContent = setContent;
                api.TextContentValue = Enum.Parse(enumType, textName);
            }
        }
        api.SetFontSize = type.GetMethod("SetSurfaceFontSize", flags, null, new[] { typeof(int), typeof(float) }, null);
        api.SetPadding = type.GetMethod("SetSurfacePadding", flags, null, new[] { typeof(int), typeof(float) }, null);
        api.SetFont = type.GetMethods(flags).FirstOrDefault(m => m.Name == "SetSurfaceFont" && m.GetParameters().Length == 2);
        (api.SetHAlign, api.LeftValue) = EnumSetter(type, flags, "SetSurfaceHorizontalAlignment", "Left");
        (api.SetVAlign, api.TopValue) = EnumSetter(type, flags, "SetSurfaceVerticalAlignment", "Top");
        api.SurfaceStates = type.GetProperty("SurfaceStates", flags);
        var stateType = api.SurfaceStates?.PropertyType.GetElementType();
        api.FontField = stateType?.GetFields(flags).FirstOrDefault(f =>
            f.FieldType.Name.StartsWith("ResourceHandle", StringComparison.Ordinal)
            && f.FieldType.IsGenericType
            && f.FieldType.GetGenericArguments()[0].Name.IndexOf("Font", StringComparison.OrdinalIgnoreCase) >= 0);
        PluginLog.Info($"LCD style api: fontSize={(api.SetFontSize != null)}, font={(api.SetFont != null)}, fontField={api.FontField?.Name ?? "-"}, align={(api.SetHAlign != null && api.SetVAlign != null)}, padding={(api.SetPadding != null)}");

        Apis[type] = api;
        PluginLog.Info($"LCD api {type.FullName}: surfaces={(api.SurfaceCount != null)}, setText={(api.SetText != null)}, content={api.TextContentValue?.ToString() ?? "-"}");
        return api;
    }
}

/// <summary>
/// Readable item names for LCD lists — NOT confirmed (M1, item 9).
/// Probes ItemDefinition for a name member (DisplayName / Name / Id / SubtypeId); a localized text
/// object is turned into a string with its EvaluateText() method when it has one.
/// Falls back to the definition's ToString(), then to the first 8 characters of its GUID.
/// </summary>
internal static class ItemNames
{
    private static readonly string[] NameMembers = { "DisplayName", "DisplayNameText", "Name", "SubtypeId", "SubtypeName", "Id" };
    private static readonly Dictionary<ItemDefinition, string> Cache = new Dictionary<ItemDefinition, string>();
    private static MemberInfo member;
    private static bool probed;

    public static string Of(ItemDefinition definition)
    {
        if (definition == null) return "?";
        if (Cache.TryGetValue(definition, out var cached)) return cached;
        var name = ItemPaths.NameFromPath(definition.ToString()) ?? Clean(Read(definition)) ?? definition.Guid.ToString().Substring(0, 8);
        Cache[definition] = name;
        return name;
    }

    private static string Read(ItemDefinition definition)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        if (!probed)
        {
            probed = true;
            var type = definition.GetType();
            foreach (var n in NameMembers)
            {
                member = (MemberInfo)type.GetProperty(n, flags) ?? type.GetField(n, flags);
                if (member != null) break;
            }
            PluginLog.Info($"Item name probe: {(member == null ? "none, using ToString()" : member.Name)}");
        }
        if (member == null) return null;
        try
        {
            var value = member is PropertyInfo p ? p.GetValue(definition) : ((FieldInfo)member).GetValue(definition);
            if (value == null) return null;
            if (value is string s) return s;
            var evaluate = value.GetType().GetMethod("EvaluateText", Type.EmptyTypes);
            return evaluate != null ? evaluate.Invoke(value, null) as string : value.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Turns "OreIron", "Ore/Iron" or "Keen.Something.Iron_Ore" into a short readable name.</summary>
    private static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var cut = Math.Max(s.LastIndexOf('/'), s.LastIndexOf('.'));
        if (cut >= 0 && cut < s.Length - 1) s = s.Substring(cut + 1);
        s = s.Replace('_', ' ');
        return s.Length == 0 || s.Contains("ItemDefinition") ? null : s;
    }
}
