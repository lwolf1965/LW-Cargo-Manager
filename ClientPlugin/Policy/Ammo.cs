using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClientPlugin.Policy;

/// <summary>
/// Ammo stock for one ammo item on a grid (turrets and fixed weapons). Weapons reload by themselves from any
/// conveyor-connected inventory (game code), so the plugin only keeps the stock up.
/// Capacity = items all weapons take for one reload. PerMinute = items they use per minute when all fire
/// (RoundsPerMinute × ConsumedAmmoPerRound ÷ rounds per item, from the game's definitions).
/// Target = one reload for every weapon + PerMinute × reserve minutes (or × reserve multiplier when the rate is unknown).
/// </summary>
public sealed class AmmoStatus
{
    public string Name { get; init; }
    public int Turrets { get; set; }
    public double Capacity { get; set; }
    /// <summary>Items per minute when every weapon fires; 0 = unknown.</summary>
    public double PerMinute { get; set; }
    public double Stock { get; set; }
    public double Target { get; set; }
    /// <summary>"making 10 (Gearforge 2.5 m)", "short: Steel Plate", "no [Make Ammo] Gearforge" ... or null.</summary>
    public string Production { get; set; }
    public bool Short => Stock < Target;
}

public static class AmmoPlanner
{
    public static double Target(double capacity, double reserveMultiplier) => capacity * (Math.Max(0, reserveMultiplier) + 1);

    /// <summary>One reload for every weapon + enough for <paramref name="minutes"/> of everything firing.</summary>
    public static double Target(double capacity, double perMinute, double minutes, double fallbackMultiplier) =>
        perMinute > 0 ? Math.Ceiling(capacity + perMinute * Math.Max(0, minutes)) : Target(capacity, fallbackMultiplier);

    /// <summary>Items per minute for one weapon firing nonstop; 0 when a value is missing.</summary>
    public static double ItemsPerMinute(double roundsPerMinute, int ammoPerRound, int roundsPerItem) =>
        roundsPerMinute > 0 && roundsPerItem > 0 ? roundsPerMinute * Math.Max(1, ammoPerRound) / roundsPerItem : 0;

    /// <summary>
    /// How many recipe batches to queue: enough to reach the target, no more than the materials allow,
    /// no more than one batch limit at a time. 0 = nothing to do (or nothing possible).
    /// </summary>
    public static int Batches(double stock, double target, double perBatch, int affordable, int maxBatches)
    {
        if (stock >= target || perBatch <= 0 || affordable <= 0 || maxBatches <= 0) return 0;
        var needed = (int)Math.Ceiling((target - stock) / perBatch);
        return Math.Min(needed, Math.Min(affordable, maxBatches));
    }

    /// <summary>[Turrets]: per ammo type the turrets, stock vs target and what the Gearforge is doing.</summary>
    public static string Screen(string title, IReadOnlyList<AmmoStatus> ammo, string clock, int width)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(LcdFormatter.Header("TURRETS - " + title, clock, width));
        if (ammo.Count == 0) sb.AppendLine("No turrets");
        foreach (var a in ammo.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"{a.Name}  {a.Turrets} weapon{(a.Turrets == 1 ? "" : "s")}");
            var fraction = a.Target > 0 ? a.Stock / a.Target : 1;
            var state = a.Short ? "LOW" : "OK";
            sb.AppendLine($"  {LcdFormatter.Bar(fraction, 8)} {LcdFormatter.Count(a.Stock)} / {LcdFormatter.Count(a.Target)} {state}");
            if (a.PerMinute > 0) sb.AppendLine($"  uses {Rate(a.PerMinute)} all firing");
            if (!string.IsNullOrEmpty(a.Production)) sb.AppendLine("  " + a.Production);
        }
        return LcdFormatter.Clip(sb.ToString().TrimEnd(), width);
    }

    private static string Rate(double perMinute) =>
        perMinute >= 10 ? LcdFormatter.Count(perMinute) + "/min" : perMinute.ToString("0.0", CultureInfo.InvariantCulture) + "/min";
}
