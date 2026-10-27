using Lineage.Core.Model;

namespace Lineage.Core.Dna;

/// <summary>A group of matches who mostly match each other, so probably descend from the same ancestral couple.</summary>
public sealed record MatchCluster(int Number, IReadOnlyList<DnaMatch> Members);

/// <summary>
/// The clustering: matches in display order (cluster by cluster, then the unclustered ones), the clusters, and
/// <see cref="Shares"/> for drawing the grid.
/// </summary>
public sealed class ClusterResult
{
    public required IReadOnlyList<DnaMatch> Order { get; init; }
    public required IReadOnlyList<MatchCluster> Clusters { get; init; }
    public required IReadOnlyList<DnaMatch> Unclustered { get; init; }
    /// <summary>For each match in <see cref="Order"/>, its cluster number (0 = none).</summary>
    public required IReadOnlyList<int> ClusterOf { get; init; }
    /// <summary>Whether the matches at these two positions in <see cref="Order"/> share DNA with each other.</summary>
    public required Func<int, int, bool> Shares { get; init; }
    /// <summary>Matches left out because there were too many (the smallest are dropped first).</summary>
    public int Dropped { get; init; }
}

/// <summary>
/// Groups a tester's matches by their shared matches, like the Leeds method and "autoclusters": two matches
/// look alike when they share many of the same matches (and each other). Clusters are built by joining the most
/// alike groups (average linkage) until no two groups are alike enough. Very close relatives match nearly
/// everyone and very distant ones are noise, so only matches between <c>minCm</c> and <c>maxCm</c> are used.
/// </summary>
public static class Clusters
{
    public const double DefaultMinCm = 20, DefaultMaxCm = 400;
    public const int MaxMatches = 500;
    /// <summary>How alike (0–1) two groups must be to join.</summary>
    public const double JoinThreshold = 0.25;

    public static ClusterResult Build(IEnumerable<DnaMatch> matches, double minCm = DefaultMinCm, double maxCm = DefaultMaxCm)
    {
        var inRange = matches.Where(m => m.SharedCm >= minCm && m.SharedCm <= maxCm)
                             .OrderByDescending(m => m.SharedCm).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var dropped = Math.Max(0, inRange.Count - MaxMatches);
        var list = inRange.Take(MaxMatches).ToList();
        var n = list.Count;

        var index = list.Select((m, i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var adjacent = new bool[n, n];
        for (var i = 0; i < n; i++)
            foreach (var other in list[i].SharedWith)
                if (index.TryGetValue(other, out var j) && j != i) adjacent[i, j] = adjacent[j, i] = true;

        // Closed neighbourhoods and their overlap (Jaccard).
        var hood = new List<HashSet<int>>();
        for (var i = 0; i < n; i++)
        {
            var set = new HashSet<int> { i };
            for (var j = 0; j < n; j++) if (adjacent[i, j]) set.Add(j);
            hood.Add(set);
        }
        var sim = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                if (hood[i].Count == 1 || hood[j].Count == 1) continue;
                var common = hood[i].Count(hood[j].Contains);
                sim[i, j] = sim[j, i] = common / (double)(hood[i].Count + hood[j].Count - common);
            }

        // Average-linkage joining (Lance–Williams update).
        var groups = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();
        var alive = Enumerable.Repeat(true, n).ToArray();
        while (true)
        {
            int a = -1, b = -1;
            var best = JoinThreshold;
            for (var i = 0; i < n; i++)
            {
                if (!alive[i]) continue;
                for (var j = i + 1; j < n; j++)
                    if (alive[j] && sim[i, j] >= best && (a < 0 || sim[i, j] > best)) { best = sim[i, j]; a = i; b = j; }
            }
            if (a < 0) break;
            double sa = groups[a].Count, sb = groups[b].Count;
            for (var k = 0; k < n; k++)
            {
                if (!alive[k] || k == a || k == b) continue;
                sim[a, k] = sim[k, a] = (sa * sim[a, k] + sb * sim[b, k]) / (sa + sb);
            }
            groups[a].AddRange(groups[b]);
            alive[b] = false;
        }

        var clustered = Enumerable.Range(0, n).Where(i => alive[i] && groups[i].Count > 1)
            .Select(i => groups[i].OrderByDescending(x => list[x].SharedCm).ToList())
            .OrderByDescending(g => g.Count).ThenByDescending(g => list[g[0]].SharedCm)
            .ToList();
        var order = new List<int>();
        var clusterOf = new List<int>();
        var clusters = new List<MatchCluster>();
        for (var c = 0; c < clustered.Count; c++)
        {
            order.AddRange(clustered[c]);
            clusterOf.AddRange(clustered[c].Select(_ => c + 1));
            clusters.Add(new MatchCluster(c + 1, clustered[c].Select(i => list[i]).ToList()));
        }
        var single = Enumerable.Range(0, n).Where(i => !order.Contains(i)).ToList();
        order.AddRange(single);
        clusterOf.AddRange(single.Select(_ => 0));

        return new ClusterResult
        {
            Order = order.Select(i => list[i]).ToList(),
            Clusters = clusters,
            Unclustered = single.Select(i => list[i]).ToList(),
            ClusterOf = clusterOf,
            Shares = (x, y) => adjacent[order[x], order[y]],
            Dropped = dropped,
        };
    }
}
