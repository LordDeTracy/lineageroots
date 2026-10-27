using Lineage.Core.Model;

namespace Lineage.Core.Dna;

/// <summary>How two people are related genetically according to the tree, and through which ancestors.</summary>
public sealed record GeneticLink(Kinship Kinship, IReadOnlyList<long> CommonAncestors);

/// <summary>Someone in the tree who shares this much DNA with the person being placed (0 = tested but no match).</summary>
public sealed record Observation(Person Person, double Cm);

/// <summary>"The person being placed is a child of <see cref="Parent"/> (and <see cref="OtherParent"/>)."</summary>
public sealed record Hypothesis(Person Parent, Person? OtherParent)
{
    public string Label => OtherParent == null
        ? $"Child of {Parent.DisplayName}"
        : $"Child of {Parent.DisplayName} and {OtherParent.DisplayName}";
}

/// <summary>How one observation fits a hypothesis: the relationship it implies (null = not related) and its likelihood.</summary>
public sealed record ObservationFit(Observation Observation, Kinship? Kinship, double Likelihood)
{
    public string Relationship => Kinship?.Label ?? "not related";
}

/// <summary>
/// A hypothesis scored against the DNA. <see cref="Probability"/> assumes the hypotheses are equally likely
/// beforehand and that one of them is right; <see cref="TimesLessLikely"/> compares it with the best.
/// </summary>
public sealed record HypothesisResult(Hypothesis Hypothesis, double Probability, double TimesLessLikely, IReadOnlyList<ObservationFit> Fits);

/// <summary>
/// "What are the odds": where in the tree does someone fit, given how much DNA they share with people already
/// placed? Each hypothesis puts them as a child of a person or couple; the shared cM with every tested
/// relative is then compared with what that relationship would give, and the hypotheses are ranked.
/// Only birth links count — adoptive, step and foster links pass on no DNA.
/// </summary>
public static class Placement
{
    /// <summary>The genetic relationship between two people in the tree, or null if the tree doesn't connect them by blood.</summary>
    public static GeneticLink? Link(FamilyTree tree, Person a, Person b) =>
        a.Id == b.Id ? null : Between(tree, Ancestors(tree, a), Ancestors(tree, b));

    /// <summary>The genetic relationship between a hypothetical child of the hypothesis's parents and someone in the tree.</summary>
    public static GeneticLink? LinkOfChild(FamilyTree tree, Hypothesis h, Person other)
    {
        var child = new Dictionary<long, int>();
        foreach (var parent in new[] { h.Parent, h.OtherParent })
        {
            if (parent == null) continue;
            foreach (var (id, gen) in Ancestors(tree, parent))
                if (!child.TryGetValue(id, out var g) || gen + 1 < g) child[id] = gen + 1;
        }
        return Between(tree, child, Ancestors(tree, other));
    }

    public static List<HypothesisResult> Evaluate(FamilyTree tree, IReadOnlyList<Observation> observations, IReadOnlyList<Hypothesis> hypotheses)
    {
        var scored = new List<(Hypothesis H, double LogL, List<ObservationFit> Fits)>();
        foreach (var h in hypotheses)
        {
            var fits = new List<ObservationFit>();
            var logL = 0.0;
            foreach (var o in observations)
            {
                var kinship = LinkOfChild(tree, h, o.Person)?.Kinship;
                var l = kinship is { } k ? CmModel.Likelihood(k, o.Cm) : Unrelated(o.Cm);
                fits.Add(new ObservationFit(o, kinship, l));
                logL += Math.Log(l);
            }
            scored.Add((h, logL, fits));
        }
        if (scored.Count == 0) return new List<HypothesisResult>();
        var best = scored.Max(s => s.LogL);
        var total = scored.Sum(s => Math.Exp(s.LogL - best));
        return scored
            .Select(s => new HypothesisResult(s.H, Math.Exp(s.LogL - best) / total, Math.Exp(best - s.LogL), s.Fits))
            .OrderByDescending(r => r.Probability)
            .ToList();
    }

    /// <summary>
    /// Sharing between people the tree doesn't connect: usually nothing, sometimes a small segment from far
    /// back. Modelled as a very distant cousin.
    /// </summary>
    private static double Unrelated(double cm) => CmModel.Likelihood(new Kinship(6, 6, true), cm);

    /// <summary>The ancestors the tester shares with each linked match in a group, most common first (for naming a cluster's line).</summary>
    public static List<(long AncestorId, int Count)> SharedAncestors(FamilyTree tree, Person tester, IEnumerable<DnaMatch> matches)
    {
        var mine = Ancestors(tree, tester);
        var counts = new Dictionary<long, int>();
        foreach (var m in matches)
            if (tree.Person(m.PersonId) is { } p && p.Id != tester.Id && Between(tree, mine, Ancestors(tree, p)) is { } link)
                foreach (var id in link.CommonAncestors)
                    counts[id] = counts.GetValueOrDefault(id) + 1;
        return counts.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList();
    }

    // ---- ancestry by birth ----

    /// <summary>The person (0) and their birth ancestors with the fewest generations up to each.</summary>
    private static Dictionary<long, int> Ancestors(FamilyTree tree, Person start)
    {
        var gen = new Dictionary<long, int> { [start.Id] = 0 };
        var queue = new Queue<Person>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var g = gen[p.Id] + 1;
            foreach (var fid in p.ParentFamilyIds)
            {
                if (tree.Family(fid) is not { } f) continue;
                var link = f.Children.FirstOrDefault(c => c.ChildId == p.Id);
                foreach (var (pid, relation) in new[] { (f.Partner1Id, link?.ToFather), (f.Partner2Id, link?.ToMother) })
                {
                    if (pid is not { } id || relation is not (null or ParentRelation.Birth)) continue;
                    if (gen.ContainsKey(id) || tree.Person(id) is not { } parent) continue;
                    gen[id] = g;
                    queue.Enqueue(parent);
                }
            }
        }
        return gen;
    }

    /// <summary>
    /// The closest common ancestors: fewest generations in all, then a direct line, then the most even split. Full
    /// when two of them at the same distance are a couple, half otherwise.
    /// </summary>
    private static GeneticLink? Between(FamilyTree tree, Dictionary<long, int> x, Dictionary<long, int> y)
    {
        int bestUp = -1, bestDown = -1;
        foreach (var (id, up) in x)
        {
            if (!y.TryGetValue(id, out var down)) continue;
            if (bestUp < 0 || Closer(up, down, bestUp, bestDown)) (bestUp, bestDown) = (up, down);
        }
        if (bestUp < 0 || bestUp + bestDown == 0) return null;
        var common = x.Where(kv => kv.Value == bestUp && y.TryGetValue(kv.Key, out var d) && d == bestDown).Select(kv => kv.Key).ToList();
        var couple = common.Count >= 2 && common.Any(a => tree.Person(a) is { } pa && pa.SpouseFamilyIds
            .Select(fid => tree.Family(fid)).OfType<Family>()
            .Any(f => f.PartnerIds.Any(pid => pid != a && common.Contains(pid))));
        return new GeneticLink(Kinship.Of(bestUp, bestDown, half: !couple), common);
    }

    private static bool Closer(int up, int down, int bestUp, int bestDown)
    {
        if (up + down != bestUp + bestDown) return up + down < bestUp + bestDown;
        var direct = up == 0 || down == 0;
        var bestDirect = bestUp == 0 || bestDown == 0;
        if (direct != bestDirect) return direct;
        return Math.Abs(up - down) < Math.Abs(bestUp - bestDown);
    }
}
