using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Game;

/// <summary>
/// Current output of one generator (solar, wind, reactor) through the game's own
/// IResourceNode.GetResources(BufferReference&lt;ResourceNodeData&gt;), called exactly like the game's
/// terminal does it (Game2.Simulation, GridStatusModelServer.AnyResourceNodes):
///     var buf = Buffer.Inline4&lt;ResourceNodeData&gt;("..."); node.GetResources(buf /* op_Implicit(ref buf) */);
///     read buf; buf.Dispose();
/// BufferReference is a ref struct, so the call is a small method generated at runtime (no compile-time
/// reference to game-internal types). ResourceNodeData: Resource, Current, CurrentMax, TheoreticalMax, IsConsumer.
/// Values in kW → MW. Any failure: that block type shows on/off (logged once), the plugin keeps running.
/// </summary>
internal static class GeneratorOutput
{
    private const double KwToMw = 1.0 / 1000;
    private static readonly Dictionary<Type, Func<object, Array>> Callers = new();
    private static readonly HashSet<Type> Logged = new();

    /// <summary>One resource node of a block, raw game units (power: kW; gas: the game's unitless amount).</summary>
    public readonly record struct Node(string Resource, bool Consumer, double Current, double CurrentMax, double TheoreticalMax);

    /// <summary>All resource nodes a block component reports (empty if it doesn't implement IResourceNode or the call fails).</summary>
    public static List<Node> ReadNodes(object component)
    {
        var result = new List<Node>();
        if (component == null) return result;
        var type = component.GetType();
        if (!Callers.TryGetValue(type, out var caller))
        {
            try { caller = type.GetInterfaces().Any(i => i.Name == "IResourceNode") ? Build(type) : null; }
            catch (Exception e) { PluginLog.Warn($"Resource node probe {type.Name}: build failed: {e.Message}"); caller = null; }
            Callers[type] = caller;
        }
        if (caller == null) return result;
        try
        {
            foreach (var node in caller(component))
            {
                var t = node.GetType();
                result.Add(new Node(
                    Reflect.Get(Reflect.Member(t, "Resource"), node)?.ToString() ?? "",
                    Reflect.Get(Reflect.Member(t, "IsConsumer"), node) as bool? ?? false,
                    Number(Reflect.Get(Reflect.Member(t, "Current"), node)),
                    Number(Reflect.Get(Reflect.Member(t, "CurrentMax"), node)),
                    Number(Reflect.Get(Reflect.Member(t, "TheoreticalMax"), node))));
            }
        }
        catch (Exception e)
        {
            Callers[type] = null;
            PluginLog.Warn($"Resource node probe {type.Name} failed: {e.InnerException?.Message ?? e.Message}");
        }
        return result;
    }

    public static bool IsElectricity(string resource) => resource.IndexOf("Electric", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>(current, nameplate max, max possible right now) in MW; nulls when unknown.</summary>
    public static (double? Output, double? Max, double? NowMax) Read(object generator, string blockName)
    {
        if (generator == null) return (null, null, null);
        var type = generator.GetType();
        if (!Callers.TryGetValue(type, out var caller))
        {
            try { caller = Build(type); }
            catch (Exception e) { PluginLog.Warn($"Generator output probe {type.Name}: build failed: {e.Message}"); caller = null; }
            Callers[type] = caller;
        }
        if (caller == null) return (null, null, null);

        try
        {
            var nodes = caller(generator);
            double current = 0, max = 0, nowMax = 0;
            var any = false;
            var description = new List<string>();
            foreach (var node in nodes)
            {
                var nodeType = node.GetType();
                var resource = Reflect.Get(Reflect.Member(nodeType, "Resource"), node);
                var consumer = Reflect.Get(Reflect.Member(nodeType, "IsConsumer"), node) as bool? ?? false;
                var cur = Number(Reflect.Get(Reflect.Member(nodeType, "Current"), node));
                var curMax = Number(Reflect.Get(Reflect.Member(nodeType, "CurrentMax"), node));
                var theory = Number(Reflect.Get(Reflect.Member(nodeType, "TheoreticalMax"), node));
                var resourceText = resource?.ToString() ?? "";
                description.Add($"[{resourceText}] current={cur} currentMax={curMax} theoreticalMax={theory} consumer={consumer}");
                if (resource == null || consumer || IsGas(resourceText)) continue;
                any = true;
                current += cur;
                max += theory > 0 ? theory : curMax;
                nowMax += curMax;
            }
            if (Logged.Add(type))
                PluginLog.Info($"Generator output probe {type.Name} on '{blockName}': {nodes.Length} node(s) {string.Join("; ", description)}");
            // No power node at all (wind turbine without wind): known, producing nothing.
            if (!any) return nodes.Length == 0 ? (0, 0, 0) : (null, null, null);
            return (current * KwToMw, max * KwToMw, nowMax * KwToMw);
        }
        catch (Exception e)
        {
            Callers[type] = null;
            PluginLog.Warn($"Generator output probe {type.Name} failed, showing on/off: {e.InnerException?.Message ?? e.Message}");
            return (null, null, null);
        }
    }

    /// <summary>
    /// Generates:
    ///   object Call(object component) {
    ///     var buf = Buffer.Inline4&lt;T&gt;("LwcmGeneratorOutput");
    ///     try { ((IResourceNode)component).GetResources(Buffer&lt;T,Inline4&lt;T&gt;&gt;.op_Implicit(ref buf)); return buf.ToArray(); }
    ///     finally { buf.Dispose(); }
    ///   }
    /// </summary>
    private static Func<object, Array> Build(Type type)
    {
        var iface = type.GetInterfaces().FirstOrDefault(i => i.Name == "IResourceNode");
        var getResources = iface?.GetMethod("GetResources");
        if (getResources == null || getResources.GetParameters().Length != 1) { Note(type, "no IResourceNode.GetResources"); return null; }

        var referenceType = getResources.GetParameters()[0].ParameterType;   // BufferReference<ResourceNodeData>
        if (!referenceType.IsGenericType) { Note(type, "unexpected parameter " + referenceType.FullName); return null; }
        var nodeType = referenceType.GetGenericArguments()[0];                 // ResourceNodeData

        var bufferStatics = referenceType.Assembly.GetType("Keen.VRage.Library.Memory.Buffer");
        var inline4 = bufferStatics?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "Inline4" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1
                                 && m.GetParameters()[0].ParameterType == typeof(string));
        if (inline4 == null) { Note(type, "Buffer.Inline4<T>(string) not found"); return null; }
        inline4 = inline4.MakeGenericMethod(nodeType);
        var bufferType = inline4.ReturnType;                                   // Buffer<T, Inline4<T>>

        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
        var toReference = bufferType.GetMethods(BindingFlags.Static | any).FirstOrDefault(m =>
            m.Name == "op_Implicit" && m.ReturnType == referenceType && m.GetParameters().Length == 1
            && m.GetParameters()[0].ParameterType == bufferType.MakeByRefType());
        var toArray = bufferType.GetMethod("ToArray", BindingFlags.Instance | any, null, Type.EmptyTypes, null);
        var dispose = bufferType.GetMethod("Dispose", BindingFlags.Instance | any, null, Type.EmptyTypes, null);
        if (toReference == null || toArray == null || dispose == null)
        {
            Note(type, $"buffer methods missing: op_Implicit={toReference != null} ToArray={toArray != null} Dispose={dispose != null}");
            return null;
        }

        var dm = new DynamicMethod("LwcmGetResources_" + type.Name, typeof(Array), new[] { typeof(object) }, typeof(GeneratorOutput).Module, skipVisibility: true);
        var il = dm.GetILGenerator();
        var buf = il.DeclareLocal(bufferType);
        var result = il.DeclareLocal(typeof(Array));
        il.Emit(OpCodes.Ldstr, "LwcmGeneratorOutput");
        il.Emit(OpCodes.Call, inline4);
        il.Emit(OpCodes.Stloc, buf);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, iface);
        il.Emit(OpCodes.Ldloca, buf);
        il.Emit(OpCodes.Call, toReference);
        il.Emit(OpCodes.Callvirt, getResources);
        il.Emit(OpCodes.Ldloca, buf);
        il.Emit(OpCodes.Call, toArray);
        il.Emit(OpCodes.Stloc, result);
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloca, buf);
        il.Emit(OpCodes.Call, dispose);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        Note(type, "call ready (Buffer.Inline4 pattern)");
        return (Func<object, Array>)dm.CreateDelegate(typeof(Func<object, Array>));
    }

    private static bool IsGas(string resource) =>
        resource.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0 || resource.IndexOf("Oxygen", StringComparison.OrdinalIgnoreCase) >= 0;

    private static double Number(object value) => value switch
    {
        FixedPoint fp => Fx.ToDouble(fp),
        float f => f,
        double d => d,
        int i => i,
        _ => 0,
    };

    private static void Note(Type type, string message) => PluginLog.Info($"Generator output probe {type.Name}: {message}");
}
