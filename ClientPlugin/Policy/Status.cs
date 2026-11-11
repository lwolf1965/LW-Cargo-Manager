using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClientPlugin.Policy;

/// <summary>Kind of power block, for the [Power Sources] list.</summary>
public enum PowerSourceKind
{
    Reactor,
    Engine,
    Solar,
    Wind,
    Battery,
}

/// <summary>One power producer or battery as read from the game.</summary>
/// <summary>OutputMW / OutputMaxMW: current and possible output, when the game reports them (null = unknown).</summary>
/// <summary>OutputNowMaxMW: what it could give right now (sun angle, wind, fuel) — "CurrentMax" in the game.</summary>
public readonly record struct PowerSource(string Name, PowerSourceKind Kind, bool Working, double StoredMWh = 0, double MaxMWh = 0, double FlowMW = 0,
    double? OutputMW = null, double? OutputMaxMW = null, double? OutputNowMaxMW = null);

/// <summary>One power consumer: name, current draw and max draw (everything running) in MW.</summary>
public readonly record struct PowerUser(string Name, double DrawMW, bool Working, double MaxMW = 0);

/// <summary>Power state of one grid.</summary>
public sealed class PowerSnapshot
{
    public List<PowerSource> Sources { get; } = new();
    public List<PowerUser> Users { get; } = new();
    /// <summary>Full-load planning for power (null if the game didn't report resource nodes).</summary>
    public ResourceBalance Balance { get; set; }

    public double UseMW => Users.Sum(u => u.DrawMW);
    public double BatteryStoredMWh => Sources.Where(s => s.Kind == PowerSourceKind.Battery).Sum(s => s.StoredMWh);
    public double BatteryMaxMWh => Sources.Where(s => s.Kind == PowerSourceKind.Battery).Sum(s => s.MaxMWh);
    /// <summary>Positive = batteries charging, negative = discharging.</summary>
    public double BatteryFlowMW => Sources.Where(s => s.Kind == PowerSourceKind.Battery).Sum(s => s.FlowMW);
    /// <summary>Generation by energy balance: what is used plus what goes into the batteries.</summary>
    public double MakeMW => Math.Max(0, UseMW + BatteryFlowMW);
    public int BatteryCount => Sources.Count(s => s.Kind == PowerSourceKind.Battery);
    /// <summary>Generators that report numbers: what they could give right now / their nameplate max.</summary>
    public double? AvailableNowMW => Sources.Any(s => s.OutputNowMaxMW.HasValue) ? Sources.Sum(s => s.OutputNowMaxMW ?? 0) : null;
    public double? NameplateMW => Sources.Any(s => s.OutputMaxMW.HasValue) ? Sources.Sum(s => s.OutputMaxMW ?? 0) : null;
}

/// <summary>Gas kind of a tank.</summary>
public enum GasKind { Hydrogen, Oxygen }

/// <summary>One gas tank as read from the game (amounts in the game's own unit; only the ratio is shown).</summary>
public readonly record struct GasTank(string Name, GasKind Gas, double Stored, double Max);

/// <summary>One damaged or unfinished block.</summary>
public readonly record struct DamagedBlock(string Name, double Integrity, double Build);

/// <summary>Damage state of one grid.</summary>
public sealed class DamageSnapshot
{
    public int Blocks { get; set; }
    public List<DamagedBlock> Problems { get; } = new();
    public int Damaged => Problems.Count(p => p.Integrity < DamageRules.Full);
    public int Unfinished => Problems.Count(p => p.Build < DamageRules.Full);
}

public static class DamageRules
{
    /// <summary>Values at or above this count as 100 % (float noise).</summary>
    public const double Full = 0.999;

    public static bool IsProblem(double integrity, double build) => integrity < Full || build < Full;
}

/// <summary>Text for [Power], [Power Sources], [Power Users], [Damage].</summary>
public static class StatusFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"850 kW", "4.2 MW", "1.3 GW".</summary>
    public static string Power(double mw)
    {
        var abs = Math.Abs(mw);
        if (abs >= 1000) return (mw / 1000).ToString("0.0", Inv) + " GW";
        if (abs >= 1) return mw.ToString("0.0", Inv) + " MW";
        return Math.Round(mw * 1000).ToString("0", Inv) + " kW";
    }

    /// <summary>"12.4 MWh", "850 kWh".</summary>
    public static string Energy(double mwh)
    {
        var abs = Math.Abs(mwh);
        if (abs >= 1000) return (mwh / 1000).ToString("0.0", Inv) + " GWh";
        if (abs >= 1) return mwh.ToString("0.0", Inv) + " MWh";
        return Math.Round(mwh * 1000).ToString("0", Inv) + " kWh";
    }

    public static string Duration(double hours)
    {
        if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0) return "-";
        if (hours >= 48) return ((int)(hours / 24)).ToString(Inv) + "d";
        if (hours >= 1) return ((int)hours).ToString(Inv) + "h " + ((int)(hours % 1 * 60)).ToString(Inv) + "m";
        return Math.Max(1, (int)(hours * 60)).ToString(Inv) + "m";
    }

    /// <summary>[Power]: generation, use, net, batteries.</summary>
    public static string PowerSummary(string title, PowerSnapshot p, string clock, int width)
    {
        var sb = new StringBuilder();
        sb.AppendLine(LcdFormatter.Header("POWER - " + title, clock, width));
        var use = p.UseMW;
        var make = p.MakeMW;
        var load = make > 0 ? use / make : (use > 0 ? 1 : 0);
        sb.AppendLine($"Make  {Power(make)}");
        if (p.AvailableNowMW.HasValue)
            sb.AppendLine($"Can   {Power(p.AvailableNowMW.Value)} now / {Power(p.NameplateMW ?? 0)} max");
        sb.AppendLine($"Use   {LcdFormatter.Bar(load, BarFor(width))} {Power(use)}");
        var net = make - use;
        sb.AppendLine($"Net   {(net >= 0 ? "+" : "-")}{Power(Math.Abs(net))}");

        if (p.BatteryCount > 0)
        {
            var fraction = p.BatteryMaxMWh > 0 ? p.BatteryStoredMWh / p.BatteryMaxMWh : 0;
            sb.AppendLine($"Batt  {LcdFormatter.Bar(fraction, BarFor(width))} {(int)Math.Round(fraction * 100)}%  ({p.BatteryCount})");
            sb.AppendLine($"      {Energy(p.BatteryStoredMWh)} / {Energy(p.BatteryMaxMWh)}");
            var flow = p.BatteryFlowMW;
            if (flow > 0.0005)
                sb.AppendLine($"      charging {Power(flow)}  full in {Duration((p.BatteryMaxMWh - p.BatteryStoredMWh) / flow)}");
            else if (flow < -0.0005)
                sb.AppendLine($"      draining {Power(-flow)}  empty in {Duration(p.BatteryStoredMWh / -flow)}");
            else
                sb.AppendLine("      idle");
        }
        else
        {
            sb.AppendLine("No batteries");
        }

        if (p.Balance != null)
            foreach (var line in Planner.Advice(p.Balance, p.BatteryStoredMWh, Power, "batteries", width))
                sb.AppendLine(line);

        var off = p.Sources.Count(s => s.Kind != PowerSourceKind.Battery && !s.Working);
        if (off > 0) sb.AppendLine($"! {off} generator(s) off");
        if (p.BatteryCount > 0 && p.BatteryMaxMWh > 0 && p.BatteryStoredMWh / p.BatteryMaxMWh < 0.2)
            sb.AppendLine("! Batteries low");
        return LcdFormatter.Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>[Power Sources]: generators grouped by name (with count), then batteries.</summary>
    public static string PowerSources(string title, PowerSnapshot p, string clock, int width, int maxLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(LcdFormatter.Header("SOURCES - " + title, clock, width));
        var groups = p.Sources
            .GroupBy(s => (s.Name, s.Kind))
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Kind,
                Count = g.Count(),
                On = g.Count(s => s.Working),
                Stored = g.Sum(s => s.StoredMWh),
                Max = g.Sum(s => s.MaxMWh),
                Flow = g.Sum(s => s.FlowMW),
                Known = g.All(s => s.OutputMW.HasValue),
                Out = g.Sum(s => s.OutputMW ?? 0),
                OutMax = g.Sum(s => s.OutputMaxMW ?? 0),
            })
            .OrderBy(g => g.Kind).ThenByDescending(g => g.Count).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count == 0) sb.AppendLine("No power sources");

        var nameWidth = Math.Max(10, Math.Min(22, width - 21));
        var shown = maxLines <= 0 ? groups.Count : Math.Min(groups.Count, maxLines);
        for (var i = 0; i < shown; i++)
        {
            var g = groups[i];
            var name = Fit(g.Count > 1 ? $"{g.Name} x{g.Count}" : g.Name, nameWidth).PadRight(nameWidth);
            string state;
            if (g.Kind == PowerSourceKind.Battery)
            {
                var pct = g.Max > 0 ? (int)Math.Round(g.Stored / g.Max * 100) : 0;
                state = $"{pct,3}% " + (g.Flow > 0.0005 ? "+" : g.Flow < -0.0005 ? "-" : " ") + (Math.Abs(g.Flow) > 0.0005 ? Power(Math.Abs(g.Flow)) : "");
            }
            else if (g.Known && g.OutMax <= 0)
            {
                state = "no output";   // e.g. wind turbine without wind
            }
            else if (g.Known)
            {
                state = $"{Power(g.Out)} / {Power(g.OutMax)}" + (g.On < g.Count ? $"  {g.Count - g.On} off" : "");
            }
            else
            {
                state = g.On == g.Count ? "on" : g.On == 0 ? "OFF" : $"{g.On}/{g.Count} on";
            }
            sb.AppendLine(name + " " + state.TrimEnd());
        }
        if (shown < groups.Count) sb.AppendLine($"... +{groups.Count - shown} more");
        return LcdFormatter.Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>[Power Consumers] (also [Power Users]): consumers grouped by name, biggest draw first, then idle ones.</summary>
    public static string PowerUsers(string title, PowerSnapshot p, string clock, int width, int maxLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(LcdFormatter.Header("CONSUMERS - " + title, clock, width));
        var maxAll = p.Users.Sum(u => u.MaxMW);
        sb.AppendLine($"Now {Power(p.UseMW)}  max {Power(maxAll)}  ({p.Users.Count} blocks)");
        // Active first (biggest draw), then idle ones (thrusters not firing, machines off) so every consumer is listed.
        var groups = p.Users
            .GroupBy(u => u.Name)
            .Select(g => new { Name = g.Key, Count = g.Count(), Draw = g.Sum(u => u.DrawMW), Max = g.Sum(u => u.MaxMW) })
            .OrderByDescending(g => g.Draw > 0.0000005).ThenByDescending(g => g.Draw).ThenByDescending(g => g.Max).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count == 0) sb.AppendLine("No power consumers");

        var nameWidth = Math.Max(10, Math.Min(26, width - 21));
        var shown = maxLines <= 0 ? groups.Count : Math.Min(groups.Count, maxLines);
        for (var i = 0; i < shown; i++)
        {
            var g = groups[i];
            var name = Fit(g.Count > 1 ? $"{g.Name} x{g.Count}" : g.Name, nameWidth).PadRight(nameWidth);
            var now = g.Draw > 0.0000005 ? Power(g.Draw) : "idle";
            sb.AppendLine(g.Max > 0 ? $"{name} {now,9} /{Power(g.Max),9}" : $"{name} {now,9}");
        }
        if (shown < groups.Count) sb.AppendLine($"... +{groups.Count - shown} more");
        return LcdFormatter.Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>[Damage]: block count, damaged and unfinished blocks, worst first.</summary>
    public static string Damage(string title, DamageSnapshot d, string clock, int width, int maxLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(LcdFormatter.Header("DAMAGE - " + title, clock, width));
        sb.AppendLine($"Blocks {LcdFormatter.Count(d.Blocks)}  damaged {d.Damaged}  unfinished {d.Unfinished}");
        if (d.Problems.Count == 0)
        {
            sb.AppendLine("No damage");
            return LcdFormatter.Clip(sb.ToString().TrimEnd(), width);
        }

        var groups = GroupDamage(d.Problems);
        var nameWidth = Math.Max(10, Math.Min(24, width - 19));
        var shown = maxLines <= 0 ? groups.Count : Math.Min(groups.Count, maxLines);
        for (var i = 0; i < shown; i++)
        {
            var g = groups[i];
            var name = Fit(g.Count > 1 ? $"{g.Name} x{g.Count}" : g.Name, nameWidth).PadRight(nameWidth);
            var range = g.Min == g.Max ? $"{g.Max,3}%" : $"{g.Min}-{g.Max}%";
            var state = g.Build ? $"build {range}" : $"{LcdFormatter.Bar(g.Value, 6)} {range}";
            sb.AppendLine(name + " " + state);
        }
        if (shown < groups.Count) sb.AppendLine($"... +{groups.Count - shown} more");
        return LcdFormatter.Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>A group of same-name blocks with similar damage or build progress.</summary>
    public readonly record struct DamageGroup(string Name, bool Build, int Count, int Min, int Max, double Value);

    /// <summary>Within one group, all values are no more than this many percent apart.</summary>
    public const int GroupSpreadPercent = 5;

    /// <summary>
    /// Same name + same kind (damaged / unfinished) + percent within <see cref="GroupSpreadPercent"/> of the group's lowest
    /// → one line ("Light x6  build 1-5%"). Worst groups first.
    /// </summary>
    public static List<DamageGroup> GroupDamage(IEnumerable<DamagedBlock> problems)
    {
        var result = new List<DamageGroup>();
        var keyed = problems.Select(p =>
        {
            var build = p.Build < DamageRules.Full;
            var value = build ? p.Build : p.Integrity;
            return (p.Name, Build: build, Value: value, Pct: (int)Math.Round(value * 100));
        });
        foreach (var byName in keyed.GroupBy(p => (p.Name, p.Build)))
        {
            var sorted = byName.OrderBy(p => p.Pct).ToList();
            var start = 0;
            for (var i = 1; i <= sorted.Count; i++)
            {
                if (i < sorted.Count && sorted[i].Pct - sorted[start].Pct <= GroupSpreadPercent) continue;
                var part = sorted.GetRange(start, i - start);
                result.Add(new DamageGroup(byName.Key.Name, byName.Key.Build, part.Count, part[0].Pct, part[^1].Pct, part.Min(p => p.Value)));
                start = i;
            }
        }
        return result.OrderBy(g => g.Value).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Tank fill lines for the [Ice] screen: "Hydrogen ########.. 81% (12)". Ice is what refills them,
    /// so the ice stock and the tanks are shown together. Empty list when the grid has no tanks.
    /// </summary>
    public static List<string> GasLines(IReadOnlyList<GasTank> tanks, int width)
    {
        var lines = new List<string>();
        foreach (var gas in new[] { GasKind.Hydrogen, GasKind.Oxygen })
        {
            var group = tanks.Where(t => t.Gas == gas).ToList();
            if (group.Count == 0) continue;
            var max = group.Sum(t => t.Max);
            var fraction = max > 0 ? group.Sum(t => t.Stored) / max : 0;
            var label = gas == GasKind.Hydrogen ? "Hydrogen" : "Oxygen";
            var low = fraction < 0.25 ? "  LOW" : "";
            var stored = group.Sum(t => t.Stored);
            var pct = $"{(int)Math.Round(fraction * 100),3}%";
            // Wide screens: stored / capacity; narrow ones: stored only. Amounts are the game's own (litres).
            var amounts = width >= LcdFormatter.WideWidth ? $"{Volume(stored)} / {Volume(max)}" : Volume(stored);
            lines.Add($"{label,-9}{LcdFormatter.Bar(fraction, BarFor(width))} {pct}  {amounts} ({group.Count}){low}");
        }
        return lines;
    }

    /// <summary>Gas amount like the game shows it, without a unit (the game has none): "850", "64.0k", "1.8M".</summary>
    public static string Volume(double amount)
    {
        var abs = Math.Abs(amount);
        if (abs >= 1_000_000) return (amount / 1_000_000).ToString("0.0", Inv) + "M";
        if (abs >= 1000) return (amount / 1000).ToString("0.0", Inv) + "k";
        return Math.Round(amount).ToString("0", Inv);
    }

    private static int BarFor(int width) => width >= 40 ? 10 : 6;

    private static string Fit(string text, int width)
    {
        text ??= "?";
        return text.Length <= width ? text : text.Substring(0, width - 1) + "~";
    }
}
