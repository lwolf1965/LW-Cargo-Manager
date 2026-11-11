using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Policy;

/// <summary>
/// Finds tagged blocks that are not on the grid's main conveyor network, the most common reason for
/// "why doesn't it sort?". Blocks are grouped by conveyor reach (either direction links two blocks);
/// the group with the most blocks is the main network, every block outside it is reported.
/// </summary>
public static class ConveyorCheck
{
    /// <param name="nodes">Tagged blocks: Id and display name.</param>
    /// <param name="reach">Ids reachable from a block through conveyors (may contain ids that are not in <paramref name="nodes"/>).</param>
    /// <returns>Names of blocks outside the main network, in input order. Empty when all are linked.</returns>
    public static IReadOnlyList<string> Unlinked(IReadOnlyList<(int Id, string Name)> nodes, Func<int, ICollection<int>> reach)
    {
        if (nodes.Count < 2) return Array.Empty<string>();
        var parent = nodes.ToDictionary(n => n.Id, n => n.Id);

        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }

        foreach (var node in nodes)
        {
            var reachable = reach(node.Id);
            if (reachable == null) continue;
            foreach (var other in reachable)
                if (other != node.Id && parent.ContainsKey(other))
                    parent[Find(other)] = Find(node.Id);
        }

        var sizes = nodes.GroupBy(n => Find(n.Id)).Select(g => (Root: g.Key, Count: g.Count())).ToList();
        if (sizes.Count < 2) return Array.Empty<string>();
        var main = sizes.OrderByDescending(s => s.Count).First().Root; // stable: first group wins a tie
        return nodes.Where(n => Find(n.Id) != main).Select(n => n.Name).ToList();
    }
}
