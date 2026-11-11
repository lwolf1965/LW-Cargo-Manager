using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClientPlugin.Policy;

/// <summary>Fill state of all containers tagged for one category on a grid.</summary>
public sealed class CategorySummary
{
    public string Label { get; init; }
    public Category Category { get; init; }
    public double Used { get; init; }
    public double Max { get; init; }
    public int Containers { get; init; }
    /// <summary>No container in the group can take more (mass or stack slots).</summary>
    public bool Full { get; init; }

    public double Fraction => Max <= 0 ? 0 : Math.Clamp(Used / Max, 0, 1);
}

/// <summary>One item row on a category screen.</summary>
/// <summary>One item row on a category screen. Delivered = amount of this item in the last delivery (if recent).</summary>
/// <summary>Low: the amount is below the "low" warning level (ores: under 1 t by default), shown as "(Low)".</summary>
public readonly record struct ItemLine(string Name, double Amount, bool IsMass, double? RatePerMinute, double? Delivered = null, bool Low = false);

public static class CargoSummary
{
    public const string UntaggedLabel = "Untagged";
    public const string TotalLabel = "Total";

    /// <summary>
    /// One row per category that has at least one tagged container (in sort order),
    /// then one row per item tag ([Si]), then "Untagged" (storage without tags, if any), then "Total".
    /// [Lock] containers and non-Storage inventories are not counted.
    /// </summary>
    public static List<CategorySummary> Build(IEnumerable<StoreInfo> stores)
    {
        var storage = stores.Where(s => s.Role == BlockRole.Storage && !s.Tags.Lock).ToList();
        var rows = new List<CategorySummary>();

        // Categories tagged directly; categories that only come from a group ([Player]) get one group row instead.
        var categories = storage.SelectMany(s => s.Tags.Categories).Where(t => t.Group == null).Select(t => t.Category).Distinct()
            .OrderBy(ItemClassifier.SortRank);
        foreach (var category in categories)
            rows.Add(Row(Label(category), category, storage.Where(s => s.Tags.Categories.Any(t => t.Category == category && t.Group == null))));

        foreach (var group in storage.SelectMany(s => s.Tags.Categories).Where(t => t.Group != null).Select(t => t.Group).Distinct())
            rows.Add(Row(group, Category.None, storage.Where(s => s.Tags.Categories.Any(t => t.Group == group))));

        // Item-tagged containers ([Si]) without a category tag: one row per tag text.
        foreach (var group in storage.Where(s => !s.Tags.HasAnyCategory && s.Tags.HasAnyItemTag)
                     .GroupBy(s => s.Tags.Items[0].Text, StringComparer.OrdinalIgnoreCase))
            rows.Add(Row(group.Key, Category.None, group));

        var untagged = storage.Where(s => !s.Tags.HasAnyStorageTag).ToList();
        if (untagged.Count > 0)
            rows.Add(Row(UntaggedLabel, Category.None, untagged));

        rows.Add(Row(TotalLabel, Category.None, storage));
        return rows;
    }

    public static CategorySummary For(IEnumerable<StoreInfo> stores, Category category) =>
        Row(Label(category), category, stores.Where(s => s.Role == BlockRole.Storage && !s.Tags.Lock && s.Tags.Accepts(category)));

    private static CategorySummary Row(string label, Category category, IEnumerable<StoreInfo> group)
    {
        var list = group.ToList();
        return new CategorySummary
        {
            Label = label,
            Category = category,
            Used = list.Sum(s => s.CurrentMass),
            Max = list.Sum(s => s.MaxMass),
            Containers = list.Count,
            Full = list.Count > 0 && list.All(s => s.FreeMass <= 0 || s.SlotsFull),
        };
    }

    public static string Label(Category category) => category switch
    {
        Category.Ore => "Ore",
        Category.Ice => "Ice",
        Category.Parts => "Parts",
        Category.Ammo => "Ammo",
        Category.Tools => "Tools",
        Category.Consumables => "Consumables",
        Category.Cells => "Cells",
        Category.Misc => "Misc",
        _ => "-",
    };

    /// <summary>Ore, ice and parts are listed by mass; everything else by count. TODO: check if SE2 counts parts in pieces.</summary>
    public static bool IsMassCategory(Category category) =>
        category == Category.Ore || category == Category.Ice;
}

/// <summary>Builds the text for LCD screens. Monospace font assumed.</summary>
public static class LcdFormatter
{
    public const int BarWidth = 10;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Bar(double fraction, int width = BarWidth)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * width, MidpointRounding.AwayFromZero);
        // ASCII on purpose: the SE2 LCD font has no block characters (they render as blanks).
        return new string('#', filled) + new string('.', width - filled);
    }

    public static string Percent(CategorySummary row) =>
        row.Full ? "FULL" : ((int)Math.Round(row.Fraction * 100, MidpointRounding.AwayFromZero)).ToString(Inv) + "%";

    /// <summary>850 kg, 24.3 t, 1.2 kt.</summary>
    public static string Mass(double kg)
    {
        var abs = Math.Abs(kg);
        if (abs < 1000) return Math.Round(kg).ToString("0", Inv) + " kg";
        if (abs < 1_000_000) return (kg / 1000).ToString("0.0", Inv) + " t";
        return (kg / 1_000_000).ToString("0.0", Inv) + " kt";
    }

    /// <summary>1 250 (space as thousands separator, easy to read on LCD).</summary>
    public static string Count(double amount) =>
        Math.Round(amount).ToString("#,0", Inv).Replace(',', ' ');

    public static string Amount(double amount, bool isMass) => isMass ? Mass(amount) : Count(amount);

    public static string Rate(double perMinute, bool isMass)
    {
        var sign = perMinute > 0 ? "+" : "-";
        var value = Math.Abs(perMinute);
        return sign + (isMass ? Mass(value) : Count(value)) + "/min";
    }

    public static string Header(string title, string clock, int width)
    {
        clock ??= "";
        var room = Math.Max(1, width - clock.Length - 1);
        var left = title.Length > room ? title.Substring(0, room) : title.PadRight(room);
        return clock.Length == 0 ? left.TrimEnd() : left + " " + clock;
    }

    /// <summary>[Cargo]: one bar per category.</summary>
    public static string Summary(string gridName, IReadOnlyList<CategorySummary> rows, IEnumerable<string> statusLines, string clock, int width)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header("LW CARGO - " + gridName, clock, width));
        foreach (var row in rows)
        {
            if (row.Label == CargoSummary.TotalLabel) continue;
            sb.AppendLine(width >= WideWidth ? SummaryLine(row) : CompactSummaryLine(row, width));
        }
        var total = rows.FirstOrDefault(r => r.Label == CargoSummary.TotalLabel);
        if (total != null)
            sb.AppendLine(width >= WideWidth
                ? $"Total {Mass(total.Used)} / {Mass(total.Max)} ({Percent(total)})"
                : $"Total {Mass(total.Used)} ({Percent(total)})");
        if (statusLines != null)
            foreach (var line in statusLines)
                sb.AppendLine(line);
        return Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>Screens narrower than this get the compact layout (no "/ max").</summary>
    public const int WideWidth = 46;

    /// <summary>
    /// Narrow screens: "Ore       ######....  60% 30.0 t (3)". The bar shrinks so the line fits
    /// <paramref name="width"/>; the container count goes first, then the bar.
    /// </summary>
    public static string CompactSummaryLine(CategorySummary row, int width = 38)
    {
        const int labelWidth = 10;
        var label = Fit(row.Label, labelWidth).PadRight(labelWidth);
        var tail = $" {Percent(row),4} {Mass(row.Used)}";
        var count = $" ({row.Containers})";

        var bar = width - labelWidth - tail.Length - count.Length;
        if (bar < 4) { count = ""; bar = width - labelWidth - tail.Length; }
        bar = Math.Min(bar, BarWidth);
        return bar >= 3 ? label + Bar(row.Fraction, bar) + tail + count : label + tail.TrimStart() + count;
    }

    public static string SummaryLine(CategorySummary row) =>
        $"{row.Label,-11}{Bar(row.Fraction)} {Percent(row),4}  {Mass(row.Used)} / {Mass(row.Max)} ({row.Containers})";

    /// <summary>[Cargo Ore] etc.: capacity line, then items, biggest first. Rates are shown when known.</summary>
    public static string CategoryDetail(string gridName, CategorySummary capacity, IReadOnlyList<ItemLine> items, string clock, int width, int maxLines,
        string footer = null, int page = 1, string label = null)
    {
        var sb = new StringBuilder();
        // Items in t/kg first (ore, materials), then items in pieces (components), each biggest first:
        // on [Parts] the two kinds stay together and pages simply continue.
        var sorted = items.Where(i => i.Amount > 0).OrderByDescending(i => i.IsMass).ThenByDescending(i => i.Amount)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var perPage = maxLines <= 0 ? Math.Max(1, sorted.Count) : maxLines;
        var pages = Math.Max(1, (sorted.Count + perPage - 1) / perPage);
        page = Math.Max(1, page);

        label = (label ?? capacity?.Label ?? "-").ToUpperInvariant();
        if (pages > 1 || page > 1) label += $" {page}/{pages}";
        sb.AppendLine(Header($"{label} - {gridName}", clock, width));
        if (capacity != null && capacity.Containers > 0)
            sb.AppendLine(width >= WideWidth
                ? $"{Bar(capacity.Fraction)} {Percent(capacity)}  {Mass(capacity.Used)} / {Mass(capacity.Max)} ({capacity.Containers})"
                : $"{Bar(capacity.Fraction)} {Percent(capacity)}  {Mass(capacity.Used)} ({capacity.Containers})");
        else
            sb.AppendLine($"No [{capacity?.Label}] containers - showing all storage");

        if (sorted.Count == 0)
            sb.AppendLine("(empty)");

        // Names get all the room the screen has: width minus the amount column and the +delivery / rate column.
        var extra = (sorted.Any(i => i.RatePerMinute.HasValue) ? 14 : sorted.Any(i => i.Delivered is > 0) ? 10 : 0)
                    + (sorted.Any(i => i.Low) ? 6 : 0);
        var nameWidth = Math.Clamp(width - 11 - extra, 12, 32);

        var first = (page - 1) * perPage;
        var last = Math.Min(sorted.Count, first + perPage);
        if (first >= sorted.Count && sorted.Count > 0)
            sb.AppendLine($"(list ends on page {pages})");
        for (var i = first; i < last; i++)
        {
            var item = sorted[i];
            var line = Fit(item.Name, nameWidth).PadRight(nameWidth) + " " + Amount(item.Amount, item.IsMass).PadLeft(10);
            if (item.Low)
                line += " (Low)";
            if (item.Delivered.HasValue && item.Delivered.Value > 0)
                line += $"  +{Amount(item.Delivered.Value, item.IsMass)}";
            if (item.RatePerMinute.HasValue)
                line += "  " + Rate(item.RatePerMinute.Value, item.IsMass);
            sb.AppendLine(line);
        }
        if (last < sorted.Count)
            sb.AppendLine($"... +{sorted.Count - last} more (page {page + 1})");
        if (!string.IsNullOrEmpty(footer))
            sb.AppendLine(footer);

        return Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>[Cargo Alerts]: full categories, plugin state, recent messages.</summary>
    public static string Alerts(string gridName, IReadOnlyList<CategorySummary> rows, IEnumerable<string> problems, IEnumerable<string> recent, string clock, int width)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header("ALERTS - " + gridName, clock, width));
        var any = false;
        if (problems != null)
            foreach (var p in problems) { sb.AppendLine("! " + p); any = true; }
        foreach (var row in rows.Where(r => r.Full && r.Label != CargoSummary.TotalLabel))
        {
            sb.AppendLine($"! {row.Label} storage full");
            any = true;
        }
        if (!any) sb.AppendLine("All good");

        var recentList = recent?.ToList() ?? new List<string>();
        if (recentList.Count > 0)
        {
            sb.AppendLine("-- recent --");
            foreach (var line in recentList) sb.AppendLine(line);
        }
        return Clip(sb.ToString().TrimEnd(), width);
    }

    /// <summary>"Last delivery: 5.2 t, 4m ago (Dock A)" or the count form for components.</summary>
    public static string DeliveryLine(double amount, bool isMass, double ageSeconds, string dock) =>
        $"Last delivery: {Amount(amount, isMass)}, {Age(ageSeconds)} ({dock})";

    public static string Age(double seconds) =>
        seconds < 60 ? "now" : seconds < 3600 ? $"{(int)(seconds / 60)}m ago" : $"{(int)(seconds / 3600)}h ago";

    /// <summary>
    /// [Cargo Ruler]: line 1 is a column ruler (digit = tens, '+' = fives), lines 2.. start with
    /// their number. Read the last visible column and line to know what fits.
    /// </summary>
    public static string Ruler(int columns, int lines)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= columns; i++)
            sb.Append(i % 10 == 0 ? (char)('0' + (i / 10) % 10) : i % 5 == 0 ? '+' : '-');
        sb.AppendLine();
        for (var n = 2; n <= lines; n++)
            sb.AppendLine(n.ToString("00", Inv));
        return sb.ToString().TrimEnd();
    }

    /// <summary>Cuts every line to <paramref name="width"/> so the LCD never wraps.</summary>
    public static string Clip(string text, int width)
    {
        if (width <= 0 || string.IsNullOrEmpty(text)) return text;
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = Fit(lines[i], width);
        return string.Join("\n", lines);
    }

    private static string Fit(string text, int width)
    {
        text ??= "?";
        return text.Length <= width ? text : text.Substring(0, width - 1) + "~";
    }
}

/// <summary>
/// Amount change per minute, from samples over a sliding window.
/// Returns null until the window covers at least <see cref="MinSpanSeconds"/>.
/// </summary>
public sealed class RateTracker<TKey>
{
    private readonly Dictionary<TKey, Queue<(double Time, double Amount)>> samples = new();

    public double WindowSeconds { get; init; } = 120;
    public double MinSpanSeconds { get; init; } = 30;
    /// <summary>Rates smaller than this (per minute) are reported as null (no change).</summary>
    public double DeadBandPerMinute { get; init; } = 0.5;

    public void Record(TKey key, double amount, double now)
    {
        if (!samples.TryGetValue(key, out var queue))
            samples[key] = queue = new Queue<(double, double)>();
        queue.Enqueue((now, amount));
        while (queue.Count > 2 && now - queue.Peek().Time > WindowSeconds)
            queue.Dequeue();
    }

    public double? PerMinute(TKey key)
    {
        if (!samples.TryGetValue(key, out var queue) || queue.Count < 2) return null;
        var first = queue.Peek();
        var last = queue.Last();
        var span = last.Time - first.Time;
        if (span < MinSpanSeconds) return null;
        var rate = (last.Amount - first.Amount) / span * 60;
        return Math.Abs(rate) < DeadBandPerMinute ? null : rate;
    }

    /// <summary>Items that disappeared completely must be recorded as 0 by the caller; this drops stale keys.</summary>
    public void Prune(double now)
    {
        var stale = samples.Where(p => p.Value.Count == 0 || now - p.Value.Last().Time > WindowSeconds * 2).Select(p => p.Key).ToList();
        foreach (var key in stale) samples.Remove(key);
    }

    public IEnumerable<TKey> Keys => samples.Keys;
}

/// <summary>Recent plugin messages for [Cargo Alerts] screens (also the HUD fallback).</summary>
public sealed class AlertLog
{
    private readonly LinkedList<(double Time, string Text)> entries = new();
    public int Capacity { get; init; } = 8;
    public double MaxAgeSeconds { get; init; } = 600;

    public void Add(string text, double now)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (entries.First != null && entries.First.Value.Text == text)
            entries.RemoveFirst(); // same message again: refresh instead of duplicate
        entries.AddFirst((now, text));
        while (entries.Count > Capacity) entries.RemoveLast();
    }

    /// <summary>Newest first, formatted "5m ago  text".</summary>
    public List<string> Recent(double now, int max)
    {
        var result = new List<string>();
        foreach (var (time, text) in entries)
        {
            var age = now - time;
            if (age > MaxAgeSeconds || result.Count >= max) break;
            result.Add($"{LcdFormatter.Age(age),-7} {text}");
        }
        return result;
    }
}

/// <summary>One delivery to a grid: everything moved by consecutive unload passes from one dock.</summary>
public sealed class Delivery<TItem>
{
    public string Dock { get; init; }
    public double Start { get; init; }
    public double Last { get; set; }
    public Dictionary<TItem, double> Items { get; } = new Dictionary<TItem, double>();
}

/// <summary>
/// Remembers the last delivery per receiving grid. Unload passes run every second; passes from the
/// same dock with less than <see cref="MergeGapSeconds"/> between them belong to one delivery.
/// A delivery is shown only while it is younger than <see cref="ShowForSeconds"/> (default 1 hour).
/// </summary>
public sealed class DeliveryTracker<TGrid, TItem>
{
    private readonly Dictionary<TGrid, Delivery<TItem>> last = new();

    public double MergeGapSeconds { get; init; } = 30;
    public double ShowForSeconds { get; set; } = 3600;

    public void Add(TGrid grid, string dock, TItem item, double amount, double now)
    {
        if (amount <= 0) return;
        if (!last.TryGetValue(grid, out var d) || d.Dock != dock || now - d.Last > MergeGapSeconds)
            last[grid] = d = new Delivery<TItem> { Dock = dock, Start = now, Last = now };
        d.Last = now;
        d.Items.TryGetValue(item, out var sum);
        d.Items[item] = sum + amount;
    }

    /// <summary>Last delivery to this grid, or null if none or older than the show window.</summary>
    public Delivery<TItem> Recent(TGrid grid, double now) =>
        last.TryGetValue(grid, out var d) && now - d.Last <= ShowForSeconds ? d : null;
}
