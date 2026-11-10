using System;
using System.Collections.Generic;

namespace ClientPlugin.Policy;

/// <summary>
/// Maps a block kind (BlockDefinition.BlockKind.Guid) to a role.
/// Unknown kinds are Consumers, so new blocks from game updates are never touched.
/// </summary>
public sealed class BlockRoles
{
    /// <summary>Cargo container kind, confirmed in AutoSorter 0.2.4.</summary>
    public static readonly Guid CargoContainerKind = new Guid("39699122-1fe4-4ed4-a9e2-9f2797067398");

    // Block kinds from Lone's discovery report (SE2 game assembly 4.0.0.x, 2026-11-10).
    public static readonly Guid ConnectorKind = new Guid("2a10a870-50cd-403c-ae16-810d35a9cad8");
    public static readonly Guid CrateKind = new Guid("e2588836-5b6f-4513-9559-b36c9e488527");
    public static readonly Guid LockerKind = new Guid("1102666b-35bf-4d55-8da4-25527b51eeaa");
    public static readonly Guid RefineryKind = new Guid("7487a403-543f-445f-b9d6-d06d8ed27a47");
    public static readonly Guid SmelterKind = new Guid("781de383-e433-417d-a0f7-7880072f3263");
    public static readonly Guid AssemblerKind = new Guid("a35d3e9c-a93b-4d95-b711-357ca1c66beb");
    public static readonly Guid GearforgeKind = new Guid("dc3b0fbc-fc22-45fd-b950-2233a38eea47");
    public static readonly Guid DrillKind = new Guid("e210cab6-1b79-4c6f-b656-6530db566866");
    public static readonly Guid GrinderKind = new Guid("472c87a0-e59a-4335-b27b-a6edb177dc09");

    // Everything else (batteries, reactors, O2/H2 generators, turrets, cockpits, terminals ...) stays Consumer.
    private static readonly Dictionary<Guid, BlockRole> Defaults = new Dictionary<Guid, BlockRole>
    {
        [CargoContainerKind] = BlockRole.Storage,
        [ConnectorKind] = BlockRole.Storage,
        [CrateKind] = BlockRole.Storage,
        [LockerKind] = BlockRole.Storage,
        [RefineryKind] = BlockRole.Producer,
        [SmelterKind] = BlockRole.Producer,
        [AssemblerKind] = BlockRole.Producer,
        [GearforgeKind] = BlockRole.Producer,
        [DrillKind] = BlockRole.Producer,
        [GrinderKind] = BlockRole.Producer,
    };

    private readonly Dictionary<Guid, BlockRole> roles;

    public BlockRoles(IEnumerable<KeyValuePair<Guid, BlockRole>> overrides = null)
    {
        roles = new Dictionary<Guid, BlockRole>(Defaults);
        if (overrides != null)
            foreach (var pair in overrides)
                roles[pair.Key] = pair.Value;
    }

    public BlockRole RoleOf(Guid? blockKind)
    {
        if (blockKind.HasValue && roles.TryGetValue(blockKind.Value, out var role))
            return role;
        return BlockRole.Consumer;
    }

    /// <summary>
    /// Parses overrides from config text, one per line or separated by ';':
    /// "GUID=Storage; GUID=Producer". Invalid entries are reported and skipped.
    /// </summary>
    public static List<KeyValuePair<Guid, BlockRole>> ParseOverrides(string text, List<string> errors = null)
    {
        var result = new List<KeyValuePair<Guid, BlockRole>>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var raw in text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            var eq = entry.IndexOf('=');
            if (eq <= 0
                || !Guid.TryParse(entry.Substring(0, eq).Trim(), out var guid)
                || !Enum.TryParse(entry.Substring(eq + 1).Trim(), true, out BlockRole role)
                || !Enum.IsDefined(typeof(BlockRole), role))
            {
                errors?.Add(entry);
                continue;
            }
            result.Add(new KeyValuePair<Guid, BlockRole>(guid, role));
        }
        return result;
    }
}
