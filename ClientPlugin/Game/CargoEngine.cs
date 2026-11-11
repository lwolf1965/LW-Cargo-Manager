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
    /// <summary>From the last full scan: any connector that can unload here. False = unload-only ticks are skipped.</summary>
    public bool HasUnloadDocks = true;
    /// <summary>Scan timing for the 30 s report (ms).</summary>
    public double ScanMsTotal, ScanMsMax;
    public int ScanCount;
    /// <summary>Dock lines for [Cargo] screens, per receiving grid. Rebuilt by every unload pass.</summary>
    public readonly Dictionary<object, List<string>> DockStatus = new();
    public int SortCursor;
    /// <summary>Dock direction decided at lock time, kept until unlock (mass changes while unloading).</summary>
    public readonly Dictionary<(Entity, Entity), DockReceiver> DockDirections = new();
    /// <summary>Both connector names when the direction was decided: a rename while docked decides again.</summary>
    public readonly Dictionary<(Entity, Entity), string> DockNames = new();
    public readonly HashSet<(Entity, Entity)> TieNotified = new();
    /// <summary>"Storage full" was already shown for this receiver/giver grid pair.</summary>
    public readonly HashSet<(object, object)> FullNotified = new();
}

/// <summary>
/// Runs the jobs from the Session.Update hook. Only on the in-process game server
/// (single player / self-hosted), only on the DCS thread, never while paused or saving.
/// Any exception stops the plugin until "Reset error stop" or the next world load (a new session clears it).
/// </summary>
internal static class CargoEngine
{
    private static readonly ConditionalWeakTable<Session, RunState> States = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly GraceTracker<InventoryComponent, ItemDefinition> Grace =
        new(() => Clock.Elapsed.TotalSeconds);

    /// <summary>Last delivery per receiving grid, for [Cargo Ore] style screens.</summary>
    public static readonly DeliveryTracker<object, ItemDefinition> Deliveries = new();

    /// <summary>Recent messages for [Cargo Alerts] screens.</summary>
    public static readonly AlertLog Alerts = new();

    public static double NowSeconds => Clock.Elapsed.TotalSeconds;

    /// <summary>Entity captured from InputGameComponent.Init, used to reach the scene.</summary>
    public static Entity Host;

    private static string failure;
    private static WeakReference<Session> failedSession;
    private static bool remoteWarned;
    private static BlockRoles roles;
    private static string rolesSource;

    public static bool Failed => failure != null;

    private static Session currentSession;

    public static void Disable(string reason) => Disable(reason, currentSession);

    private static void Disable(string reason, Session session)
    {
        failure = reason;
        failedSession = session != null ? new WeakReference<Session>(session) : null;
        PluginLog.Error("Stopped: " + reason + ". Use 'Reset error stop' in the settings or load the world again.");
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
        if (!config.Enabled || !updateLifetime) return;

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

            if (Failed)
            {
                // An error stop belongs to the world it happened in: a newly loaded world starts clean.
                Session stoppedIn = null;
                if (failedSession == null || (failedSession.TryGetTarget(out stoppedIn) && ReferenceEquals(stoppedIn, session)))
                    return;
                failure = null;
                failedSession = null;
                PluginLog.Info("New world loaded: error stop cleared.");
            }
            currentSession = session;

            var client = scene.GameClient?.TryGet<WorldSessionComponent>();
            if (client == null || world.Pause.IsPaused || client.Pause.IsPaused || world.IsSaving) return;

            var now = Stopwatch.GetTimestamp();
            var state = States.GetValue(session, _ => new RunState());

            var unloadDue = now >= state.NextUnload;
            var sortDue = config.SortEnabled && now >= state.NextSort;
            var tidyDue = (config.TidyConsolidate || config.TidyMergeExperimental) && now >= state.NextTidy;
            var lcdDue = config.LcdEnabled && now >= state.NextLcd;
            var discoveryDue = Discovery.Requested;
            // Unload-only tick with no unload dock in the last full scan: nothing to do, skip the scan.
            if (unloadDue && !state.HasUnloadDocks && !sortDue && !tidyDue && !lcdDue && !discoveryDue)
            {
                state.NextUnload = now + Ticks(config.UnloadIntervalSeconds);
                return;
            }
            if (!unloadDue && !sortDue && !tidyDue && !lcdDue && !discoveryDue) return;

            // Never write from an arbitrary thread.
            if (!session.Scene.IsCurrentThreadDCS())
                throw new InvalidOperationException("Session.Update ran outside the DCS thread");

            if (discoveryDue)
                Discovery.Write(session);

            var players = client.OwnedSession?.SessionComponents?.TryGet<ClientPlayersSessionComponent>();
            if (players == null || !players.LocalPlayerIdentity.IsValid()) return;
            IdentityId me = players.LocalPlayerIdentity;

            var scanStart = Stopwatch.GetTimestamp();
            var scan = WorldScan.Build(session, me, Roles(config), config.DefaultPriority);
            var scanMs = (Stopwatch.GetTimestamp() - scanStart) * 1000.0 / Stopwatch.Frequency;
            state.ScanMsTotal += scanMs;
            state.ScanMsMax = Math.Max(state.ScanMsMax, scanMs);
            state.ScanCount++;
            state.HasUnloadDocks = config.UnloadMode == UnloadMode.AllStationConnectors || scan.HasUnloadTags;
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
                AmmoJob.Run(scan, config, NowSeconds); // turret ammo stock; same slow rhythm as tidy
            }

            // LCDs last, so they show the result of this pass. Not limited by the transfer budget.
            if (lcdDue)
            {
                state.NextLcd = now + Ticks(config.LcdIntervalSeconds);
                scan.RefreshAll();
                try
                {
                    LcdJob.Run(scan, state, config, NowSeconds);
                }
                catch (Exception e)
                {
                    // Screens only read; a screen bug must not stop unloading and sorting. Logged, not stopped.
                    if (LcdErrors.Add(e.GetType().Name + e.TargetSite?.Name))
                        PluginLog.Warn("LCD update failed, plugin keeps running: " + e);
                }
            }

            if (now >= state.NextReport)
            {
                state.NextReport = now + Ticks(30);
                Grace.Prune();
                PluginLog.Info($"Scan: grids={scan.Grids.Count}, inventories={scan.Stores.Count}, connectorApi={(ConnectorAdapter.Supported ? "probed" : "fallback")}, grace={Grace.Count}, " +
                               $"scan avg {(state.ScanCount > 0 ? state.ScanMsTotal / state.ScanCount : 0):0.00} ms / max {state.ScanMsMax:0.00} ms over {state.ScanCount} passes, unloadDocks={state.HasUnloadDocks}");
                state.ScanMsTotal = state.ScanMsMax = 0;
                state.ScanCount = 0;
            }

            if (TransferService.MovesThisPass > 0)
                PluginLog.Verbose($"Pass done: moves={TransferService.MovesThisPass}, attempts={budget.Attempts}, checks={budget.Checks}");
        }
        catch (Exception e)
        {
            Disable(e.GetType().Name + ": " + e.Message, session);
            PluginLog.Error(e.ToString());
        }
    }

    private static readonly HashSet<string> LcdErrors = new();

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
