using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Policy;

/// <summary>An item tag with its priority: [Si] → ("Si", 5), [Iron Ore P1] → ("Iron Ore", 1).</summary>
public readonly record struct ItemTag(string Text, int Priority);

/// <summary>An item as the policy sees it: its category and its readable name ("Silicon Ore").</summary>
public readonly record struct ItemKey(Category Category, string Name)
{
    public static implicit operator ItemKey(Category category) => new ItemKey(category, null);
}

/// <summary>
/// Matches item tags to item names. Case and spaces don't matter.
///   [Silicon] or [Si]    → every item whose name starts with "Silicon" (ore and parts)
///   [Si Ore], [Iron Parts] → only that category ("Ore", "Parts", "Ice" ... as last word)
///   [Silicon Ore]        → exact name
/// Element symbols (Fe, Ni, Si, Co, Mg, Ag, Au, Pt, U, Cu) stand for the element name.
/// </summary>
public static class ItemMatch
{
    private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Fe"] = "Iron", ["Ni"] = "Nickel", ["Si"] = "Silicon", ["Co"] = "Cobalt", ["Mg"] = "Magnesium",
        ["Ag"] = "Silver", ["Au"] = "Gold", ["Pt"] = "Platinum", ["U"] = "Uranium", ["Cu"] = "Copper",
    };

    public static bool Matches(string tagText, ItemKey item)
    {
        if (string.IsNullOrWhiteSpace(tagText) || string.IsNullOrWhiteSpace(item.Name)) return false;
        var words = Words(tagText);
        if (words.Count == 0) return false;

        // Last word may be a category: [Si Ore], [Iron Parts].
        if (words.Count > 1 && TagParser.TryCategoryWord(words[^1], out var category))
        {
            if (category != item.Category) return false;
            // [Silicon Ore] is also the exact item name, so keep the word if the name has it.
            if (!Normalize(item.Name).Equals(Normalize(string.Join("", words)), StringComparison.Ordinal))
                words.RemoveAt(words.Count - 1);
        }

        if (Symbols.TryGetValue(words[0], out var element)) words[0] = element;
        var prefix = Normalize(string.Join("", words));
        var name = Normalize(item.Name);
        if (name == prefix) return true;
        // Whole leading words only: [Iron] matches "Iron Ore", not "Ironwood".
        var nameWords = Words(item.Name);
        var joined = "";
        foreach (var w in nameWords)
        {
            joined += Normalize(w);
            if (joined == prefix) return true;
            if (joined.Length >= prefix.Length) break;
        }
        return false;
    }

    private static List<string> Words(string text) =>
        text.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string Normalize(string text) =>
        new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
