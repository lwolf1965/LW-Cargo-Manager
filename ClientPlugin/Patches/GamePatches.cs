using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.Game;
using HarmonyLib;
using Keen.Game2.Client.Input;
using Keen.Game2.Game.SessionComponents;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Library.Mathematics;

namespace ClientPlugin.Patches;

/// <summary>If an essential patch fails, the plugin disables itself instead of half-working.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class EssentialPatchAttribute : Attribute
{
}

/// <summary>Captures an entity whose Scene leads to GameCoreScene (same approach as AutoSorter).</summary>
[EssentialPatch]
[HarmonyPatch(typeof(InputGameComponent), "Init", new Type[0])]
internal static class InputInitPatch
{
    private static void Postfix(InputGameComponent __instance)
    {
        CargoEngine.Host = __instance.Entity;
        PluginLog.Info("Input host captured.");
    }
}

/// <summary>Main loop: runs the jobs after each Session.Update(bool updateLifetime).</summary>
[EssentialPatch]
[HarmonyPatch(typeof(Session), nameof(Session.Update), new[] { typeof(bool) })]
internal static class SessionUpdatePatch
{
    // __0 = updateLifetime (positional, so a renamed parameter doesn't break the patch)
    private static void Postfix(Session __instance, bool __0) => CargoEngine.OnUpdate(__instance, __0);
}

/// <summary>
/// InventoryComponent.TransferByIdx(to, index, targetIndex, amount, partial):
///  - Prefix: blocks hand tools/weapons during the vanilla quick deposit (ProtectQuickDeposit).
///  - Postfix: transfers NOT made by the plugin mark the destination for the grace delay.
/// Note: game-internal conveyor moves may also pass here; they then get a grace delay too (harmless).
/// </summary>
[HarmonyPatch(typeof(InventoryComponent), nameof(InventoryComponent.TransferByIdx),
    new[] { typeof(InventoryComponent), typeof(int), typeof(int?), typeof(FixedPoint?), typeof(bool) })]
internal static class TransferByIdxPatch
{
    private static bool Prefix(InventoryComponent __instance, int __1, ref FixedPoint __result, out ItemDefinition __state)
    {
        __state = null;
        try
        {
            __state = __instance.GetItemDefinition(__1);
        }
        catch
        {
            return true; // let the game handle bad indices as usual
        }

        if (QuickDepositPatch.Active && __state != null && QuickDepositPatch.Protected.Contains(__state.Guid))
        {
            __result = 0;
            return false;
        }
        return true;
    }

    private static void Postfix(InventoryComponent __0, FixedPoint __result, ItemDefinition __state)
    {
        if (TransferService.IsOwnMove || __state == null || __0 == null) return;
        if (__result > 0)
            CargoEngine.MarkPlayerMove(__0, __state);
    }
}

/// <summary>
/// Vanilla Alt + right-click "deposit all non-essential items": keep hand tools and weapons
/// on the character. Optional: skipped if the method doesn't exist in this game version.
/// </summary>
[HarmonyPatch]
internal static class QuickDepositPatch
{
    private const string TypeName = "Keen.Game2.Game.SessionComponents.InProcessGameServerSessionComponent";
    private const string MethodName = "TryDepositAllNonEssentialItemsImpl";

    [ThreadStatic] public static bool Active;
    public static HashSet<Guid> Protected = new HashSet<Guid>();

    private static MethodBase TargetMethod()
    {
        var type = typeof(WorldSessionComponent).Assembly.GetType(TypeName, false);
        return type == null ? null : AccessTools.Method(type, MethodName);
    }

    private static bool Prepare()
    {
        var found = TargetMethod() != null;
        if (!found) PluginLog.Warn($"Quick deposit protection unavailable: {TypeName}.{MethodName} not found.");
        return found;
    }

    private static void Prefix()
    {
        var config = Config.Current;
        if (!config.Enabled || config.DryRun || !config.ProtectQuickDeposit) return;
        Protected = ItemTraitsMapper.ToolItems();
        Active = Protected.Count > 0;
    }

    private static Exception Finalizer(Exception __exception)
    {
        Active = false;
        return __exception;
    }
}
