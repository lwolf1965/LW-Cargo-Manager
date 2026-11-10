using Keen.VRage.Library.Diagnostics;

namespace ClientPlugin.Game;

/// <summary>All plugin log lines start with [LwCargoManager] so they are easy to find in the game log.</summary>
internal static class PluginLog
{
    public static void Info(string message) => Log.Default.WriteLine($"[{Plugin.Name}] {message}");

    public static void Warn(string message) => Log.Default.WriteLine(LogSeverity.Warning, $"[{Plugin.Name}] {message}");

    public static void Error(string message) => Log.Default.WriteLine(LogSeverity.Error, $"[{Plugin.Name}] {message}");

    public static void Verbose(string message)
    {
        if (Config.Current.VerboseLog)
            Info(message);
    }
}

/// <summary>
/// On-screen messages. TODO(M1, item 8): find the game's HUD notification API and call it here.
/// Until then messages go to the log, so nothing is lost.
/// </summary>
internal static class Notifier
{
    public static void Hud(string message)
    {
        CargoEngine.Alerts.Add(message, CargoEngine.NowSeconds);
        if (!Config.Current.HudMessages)
            return;
        // TODO(M1): replace with the HUD notification call once found.
        PluginLog.Info("HUD: " + message);
    }
}
