using System;
using System.Collections.Generic;

namespace ClientPlugin.Policy;

/// <summary>
/// Remembers when a player (not the plugin) put an item into an inventory.
/// The sorter leaves such items alone for the grace delay.
/// Keys are opaque: the game adapter passes an inventory key and an item key.
/// </summary>
public sealed class GraceTracker<TInventory, TItem>
{
    private readonly Dictionary<(TInventory, TItem), double> marks = new Dictionary<(TInventory, TItem), double>();
    private readonly Func<double> clockSeconds;

    public GraceTracker(Func<double> clockSeconds)
    {
        this.clockSeconds = clockSeconds ?? throw new ArgumentNullException(nameof(clockSeconds));
    }

    public double DelaySeconds { get; set; } = 60;

    public int Count => marks.Count;

    public void Mark(TInventory inventory, TItem item)
    {
        if (DelaySeconds <= 0) return;
        marks[(inventory, item)] = clockSeconds();
    }

    public bool IsProtected(TInventory inventory, TItem item)
    {
        if (DelaySeconds <= 0) return false;
        if (!marks.TryGetValue((inventory, item), out var since)) return false;
        if (clockSeconds() - since < DelaySeconds) return true;
        marks.Remove((inventory, item));
        return false;
    }

    /// <summary>Drops expired entries; call now and then to keep memory small.</summary>
    public void Prune()
    {
        if (marks.Count == 0) return;
        var now = clockSeconds();
        var expired = new List<(TInventory, TItem)>();
        foreach (var pair in marks)
            if (DelaySeconds <= 0 || now - pair.Value >= DelaySeconds)
                expired.Add(pair.Key);
        foreach (var key in expired)
            marks.Remove(key);
    }

    public void Clear() => marks.Clear();
}
