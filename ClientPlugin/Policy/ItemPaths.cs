using System;
using System.Text;

namespace ClientPlugin.Policy;

/// <summary>
/// SE2 item definitions carry their content path, e.g.
/// "cb651320-... : Vanilla\Items\Ores\IronOre\IronOre_Item.def" (seen in the discovery report).
/// ItemTypes only has Ore / Material / Component / Item / Consumable / Datapad / Quest / BlockSchematic,
/// so ammo, ice and power cells are recognised from this path.
/// </summary>
public static class ItemPaths
{
    /// <summary>Extra traits that ItemTypes can't tell: ammo, ice, power cells.</summary>
    public static ItemTraits TraitsFromPath(string text)
    {
        if (string.IsNullOrEmpty(text)) return ItemTraits.None;
        var traits = ItemTraits.None;
        if (Has(text, @"\Ammo\") || Has(text, "/Ammo/")) traits |= ItemTraits.Ammo;
        if (Has(text, "IceOre")) traits |= ItemTraits.Ice;
        // SE2's battery cell is the non-stackable "Power Module" component (found in battery slots).
        if (Has(text, "PowerModule") || Has(text, "PowerCell")) traits |= ItemTraits.Cell;
        return traits;
    }

    /// <summary>
    /// Readable name from the path: the folder that holds the .def file, CamelCase split,
    /// trailing "Item" removed. "...\Ores\IronOre\IronOre_Item.def" → "Iron Ore",
    /// "...\Complex\Heavy-dutyPlate\..." → "Heavy-duty Plate", "...\Ammo\GatlingAmmoItem\..." → "Gatling Ammo".
    /// Returns null if the text doesn't look like a path.
    /// </summary>
    public static string NameFromPath(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var path = text;
        var colon = path.IndexOf(" : ", StringComparison.Ordinal);
        if (colon >= 0) path = path.Substring(colon + 3);
        path = path.Replace('/', '\\').Trim();
        var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var folder = parts[parts.Length - 2];
        if (folder.EndsWith("Item", StringComparison.Ordinal) && folder.Length > 4)
            folder = folder.Substring(0, folder.Length - 4);
        var name = SplitCamel(folder).Trim();
        return name.Length == 0 ? null : name;
    }

    public static string SplitCamel(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || char.IsDigit(s[i - 1])) && s[i - 1] != '-')
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Replace("  ", " ");
    }

    private static bool Has(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
}
