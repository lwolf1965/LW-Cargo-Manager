using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ClientPlugin.Policy;
using Keen.Game2.Client.GameSystems.PlayerControl;
using Keen.Game2.Game.SessionComponents;
using Keen.Game2.Simulation.GameSystems.Ownership;
using Keen.Game2.Simulation.RuntimeSystems.CoreScenes;
using Keen.Game2.Simulation.WorldObjects.Items;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;

namespace ClientPlugin.Game;

/// <summary>Work limits for one pass, shared by all jobs (Unload > Sort > Tidy).</summary>
internal sealed class Budget
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly int maxAttempts, maxChecks, maxMs;
    public int Attempts, Checks;

    public Budget(Config config)
    {
        maxAttempts = config.MaxTransferAttempts;
        maxChecks = config.MaxItemChecks;
        maxMs = config.SoftTimeBudgetMs;
    }

    public bool Exhausted => Attempts >= maxAttempts || Checks >= maxChecks || watch.ElapsedMilliseconds >= maxMs;
}

/// <summary>State kept per world session.</summary>
internal sealed class RunState
{
    public long NextUnload, NextSort, NextTidy, NextLcd, NextReport;
    /// <summary>Dock lines for [Cargo] screens, per receiving grid. Rebuilt by every unload pass.</summary>
    public readonly Dictionary<object, List<string>> DockStatus = new Dictionary<object, List<string>>();
    public int SortCursor;
    /// <summary>Dock direction decided at lock time, kept until unlock (mass changes while unloading).</summary>
    public readonly Dictionary<(Entity, Entity), DockReceiver> DockDirections = new Dictionary<(Entity, Entity), DockReceiver>();
    public readonly HashSet<(Entity, Entity)> TieNotified = new HashSet<(Entity, Entity)>();
    /// <summary>"Storage full" was already shown for this receiver/giver grid pair.</summary>
    public readonly HashSet<(object, object)> FullNotified = new HashSet<(object, object)>();
}

/// <summary>
/// Runs the jobs from the Session.Update hook. Only on the in-process game server
/// (single player / self-hosted), only on the DCS thread, never while paused or saving.
/// Any exception stops the plugin until "Reset error stop" or a world reload.
/// </summary>
internal static class CargoEngine
{
    private static readonly ConditionalWeakTable<Session, RunState> States = new ConditionalWeakTable<Session, RunState>();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly GraceTracker<InventoryComponent, ItemDefinition> Grace =
        new GraceTracker<InventoryComponent, ItemDefinition>(() => Clock.Elapsed.TotalSeconds);

    /// <summary>Last delivery per receiving grid, for [Cargo Ore] style screens.</summary>
    public static readonly DeliveryTracker<object, ItemDefinition> Deliveries = new DeliveryTracker<object, ItemDefinition>();

    /// <summary>Recent messages for [Cargo Alerts] screens.</summary>
    public static readonly AlertLog Alerts = new AlertLog();

    public static double NowSeconds => Clock.Elapsed.TotalSeconds;

    /// <summary>Entity captured from InputGameComponent.Init, used to reach the scene.</summary>
    public static Entity Host;

    private static string failure;
    private static bool remoteWarned;
    private static BlockRoles roles;
    private static string rolesSource;

    public static bool Failed => failure != null;

    public static void Disable(string reason)
    {
        failure = reason;
        PluginLog.Error("Stopped: " + reason + ". Use 'Reset error stop' in the settings or reload the world.");
        Notifier.Hud("LW Cargo Manager stopped: " + reason);
    }

    public static void ResetFailure()
    {
        failure = null;
        PluginLog.Info("Error stop reset.");
    }

    /// <summary>Called by the TransferByIdx patch for transfers not made by the plugin.</summary>
    public static void MarkPlayerMove(InventoryComponent destination, ItemDefinition item)
    {
        Grace.DelaySeconds = Config.Current.GraceDelaySeconds;
        Grace.Mark(destination, item);
    }

    public static void OnUpdate(Session session, bool updateLifetime)
    {
        var config = Config.Current;
        if (!config.Enabled || Failed || !updateLifetime) return;

        try
        {
            var scene = Host?.Scene?.UserObject as GameCoreScene;
            if (scene == null) return;

            var world = scene.GameServer?.TryGet<WorldSessionComponent>();
            if (world == null)
            {
                if (!remoteWarned && scene.GameClient != null)
                {
                    remoteWarned = true;
                    PluginLog.Warn("No in-process game server: remote/dedicated servers are not supported yet. Plugin idle.");
                }
                return;
            }
            if (!ReferenceEquals(world.OwnedSession, session) || session.IsUnloading) return;

            var client = scene.GameClient?.TryGet<WorldSessionComponent>();
            if (client == null || world.Pause.IsPaused || client.Pause.IsPaused || world.IsSaving) return;

            var now = Stopwatch.GetTimestamp();
            var state = States.GetValue(session, _ => new RunState());

            var unloadDue = now >= state.NextUnload;
            var sortDue = config.SortEnabled && now >= state.NextSort;
            var tidyDue = (config.TidyConsolidate || config.TidyMergeExperimental) && now >= state.NextTidy;
            var lcdDue = config.LcdEnabled && now >= state.NextLcd;
            var discoveryDue = Discovery.Requested;
            if (!unloadDue && !sortDue && !tidyDue && !lcdDue && !discoveryDue) return;

            // Never write from an arbitrary thread.
            if (!session.Scene.IsCurrentThreadDCS())
                throw new InvalidOperationException("Session.Update ran outside the DCS thread");

            if (discoveryDue)
                Discovery.Write(session);

            var players = client.OwnedSession?.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
            if (players == null || !players.LocalPlayerIdentity.IsValid()) return;
            IdentityId me = players.LocalPlayerIdentity;

            var scan = WorldScan.Build(session, me, Roles(config), config.DefaultPriority);
            var budget = new Budget(config);
            var options = config.ToRoutingOptions();
            Grace.DelaySeconds = config.GraceDelaySeconds;
            TransferService.MovesThisPass = 0;

            if (unloadDue)
            {
                state.NextUnload = now + Ticks(config.UnloadIntervalSeconds);
                UnloadJob.Run(session, scan, state, config, options, budget);
            }

            if (sortDue && !budget.Exhausted)
            {
                state.NextSort = now + Ticks(config.SortIntervalSeconds);
                SortJob.Run(session, scan, state, config, options, budget);
            }

            if (tidyDue && !budget.Exhausted)
            {
                state.NextTidy = now + Ticks(config.TidyIntervalSeconds);
                TidyJob.Run(session, scan, state, config, options, budget);
            }

            // LCDs last, so they show the result of this pass. Not limited by the transfer budget.
            if (lcdDue)
            {
                state.NextLcd = now + Ticks(config.LcdIntervalSeconds);
                scan.RefreshAll();
                LcdJob.Run(scan, state, config, NowSeconds);
            }

            if (now >= state.NextReport)
            {
                state.NextReport = now + Ticks(30);
                Grace.Prune();
                PluginLog.Info($"Scan: grids={scan.Grids.Count}, inventories={scan.Stores.Count}, connectorApi={(ConnectorAdapter.Supported ? "probed" : "fallback")}, grace={Grace.Count}");
            }

            if (TransferService.MovesThisPass > 0)
                PluginLog.Verbose($"Pass done: moves={TransferService.MovesThisPass}, attempts={budget.Attempts}, checks={budget.Checks}");
        }
        catch (Exception e)
        {
            Disable(e.GetType().Name + ": " + e.Message);
            PluginLog.Error(e.ToString());
        }
    }

    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    private static BlockRoles Roles(Config config)
    {
        if (roles != null && rolesSource == config.BlockRoleOverrides) return roles;
        var errors = new List<string>();
        var overrides = BlockRoles.ParseOverrides(config.BlockRoleOverrides, errors);
        foreach (var error in errors) PluginLog.Warn("Ignored block role override: " + error);
        roles = new BlockRoles(overrides);
        rolesSource = config.BlockRoleOverrides;
        return roles;
    }
}
