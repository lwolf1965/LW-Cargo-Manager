using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Policy;

/// <summary>Resources the planner balances.</summary>
public enum ResourceKind { Power, Hydrogen, Oxygen }

/// <summary>
/// One producing block. Variable = depends on sun/wind (solar, wind): only what it can give right now counts
/// for planning. Steady producers (reactor, engine, O2/H2 generator) count with their nameplate max.
/// </summary>
public readonly record struct Producer(string Name, bool Variable, double UnitMax, double NowMax, double Current);

/// <summary>
/// Supply and demand of one resource on a grid, from the game's own resource nodes:
/// consumers with their max draw (everything running), producers with their max output.
/// Power in MW; gas in the game's unitless amount per second.
/// </summary>
public sealed class ResourceBalance
{
    public ResourceKind Kind { get; init; }
    public List<Producer> Producers { get; } = new();
    public double UseNow { get; set; }
    /// <summary>Everything running at once (machines busy, thrusters full).</summary>
    public double UseMax { get; set; }

    public double MakeNow => Producers.Sum(p => p.Current);
    /// <summary>What producers can deliver now: steady ones at max, solar/wind as the sun/wind allows.</summary>
    public double CanNow => Producers.Sum(p => p.Variable ? p.NowMax : p.UnitMax);
    public double Spare => CanNow - UseMax;
}

public static class Planner
{
    /// <summary>
    /// Advice for full load: "OK, 148 MW spare" or "SHORT 52 MW" + how long storage covers it + what to add.
    /// The block to add is the steady producer with the biggest unit already on the grid (you know you can build it).
    /// </summary>
    public static List<string> Advice(ResourceBalance b, double stored, Func<double, string> amount, string storageName, int width)
    {
        var lines = new List<string>();
        if (b.UseMax <= 0 && b.Producers.Count == 0) return lines;

        var head = b.Kind switch { ResourceKind.Power => "Peak ", ResourceKind.Hydrogen => "H2   ", _ => "O2   " };
        lines.Add($"{head} use {amount(b.UseMax)} max / can {amount(b.CanNow)}");
        if (b.Spare >= 0)
        {
            lines.Add($"      OK, {amount(b.Spare)} spare at full load");
            return lines;
        }

        var shortBy = -b.Spare;
        lines.Add($"      SHORT {amount(shortBy)} at full load");
        if (stored > 0 && b.Kind == ResourceKind.Power)
            lines.Add($"      {storageName} cover {StatusFormatter.Duration(stored / shortBy)}");

        var best = b.Producers.Where(p => !p.Variable && p.UnitMax > 0).OrderByDescending(p => p.UnitMax).FirstOrDefault();
        if (best.UnitMax > 0)
        {
            var count = (int)Math.Ceiling(shortBy / best.UnitMax);
            lines.Add($"      add {count}x {best.Name} ({amount(best.UnitMax)})");
        }
        else
        {
            lines.Add("      add producers (none steady yet)");
        }
        return lines;
    }
}
