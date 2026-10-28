using Lineage.Core.Model;

namespace Lineage.Core.Dna;

/// <summary>Which of the tester's parents a painted segment came through.</summary>
public enum PaintSide { Father, Mother, Unknown }

/// <summary>A segment shared with a linked match, attributed to the ancestors the tester and the match have in common.</summary>
public sealed record PaintedSegment(DnaMatch Match, DnaSegment Segment, PaintSide Side, int Group);

/// <summary>An ancestor (or couple) that painted segments came from, numbered for its colour.</summary>
public sealed record PaintGroup(int Number, IReadOnlyList<long> Ancestors, string Label, PaintSide Side,
                                int Segments, double Cm, IReadOnlyList<string> Matches);

public sealed record Painting(IReadOnlyList<PaintedSegment> Segments, IReadOnlyList<PaintGroup> Groups,
                              int Unlinked, int Unrelated)
{
    /// <summary>How much of the tester's genome (by base pairs, chromosomes 1–22) is painted on one side.</summary>
    public double Coverage(PaintSide side)
    {
        long covered = 0;
        foreach (var chr in Segments.Where(s => s.Side == side && s.Segment.Chromosome < ChromosomePainter.X).GroupBy(s => s.Segment.Chromosome))
        {
            long end = 0;
            foreach (var s in chr.Select(s => s.Segment).OrderBy(s => s.Start))
            {
                var from = Math.Max(s.Start, end);
                if (s.End > from) covered += s.End - from;
                end = Math.Max(end, s.End);
            }
        }
        return (double)covered / ChromosomePainter.AutosomalLength;
    }
}

/// <summary>
/// The chromosome painter: each segment the tester shares with a match who is linked to the tree is painted with the
/// ancestors they have in common, on the father's or mother's side — the side those ancestors are on. Overlapping
/// segments on one side from different ancestors on different lines are worth a second look: one of the links may be
/// wrong.
/// </summary>
public static class ChromosomePainter
{
    public const int X = 23;

    /// <summary>Chromosome lengths in base pairs (GRCh37, the build the companies' downloads use): 1–22, then X.</summary>
    public static readonly long[] Lengths =
    {
        249250621, 243199373, 198022430, 191154276, 180915260, 171115067, 159138663, 146364022, 141213431, 135534747,
        135006516, 133851895, 115169878, 107349540, 102531392, 90354753, 81195210, 78077248, 59128983, 63025520,
        48129895, 51304566, 155270560,
    };

    public static readonly long AutosomalLength = Lengths.Take(22).Sum();

    public static long Length(int chromosome) => Lengths[Math.Clamp(chromosome, 1, X) - 1];
    public static string Name(int chromosome) => chromosome == X ? "X" : chromosome.ToString();

    public static Painting Paint(FamilyTree tree, Person tester)
    {
        var (fatherSide, motherSide) = Sides(tree, tester);
        var groups = new Dictionary<string, (List<long> Ancestors, PaintSide Side, List<PaintedSegment> Segments, HashSet<string> Matches)>();
        int unlinked = 0, unrelated = 0;
        var painted = new List<(DnaMatch Match, DnaSegment Segment, PaintSide Side, string Key)>();
        foreach (var m in tester.DnaMatches.Where(m => m.ChromosomeSegments.Count > 0))
        {
            if (tree.Person(m.PersonId) is not { } p || p.Id == tester.Id) { unlinked++; continue; }
            if (Placement.Link(tree, tester, p) is not { } link) { unrelated++; continue; }
            var ancestors = link.CommonAncestors.OrderBy(id => id).ToList();
            var side = ancestors.All(fatherSide.Contains) ? PaintSide.Father
                     : ancestors.All(motherSide.Contains) ? PaintSide.Mother
                     : PaintSide.Unknown;   // the tester's own descendants, or full siblings (both parents)
            var key = string.Join(",", ancestors);
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = (ancestors, side, new(), new());
            g.Matches.Add(m.Name);
            foreach (var s in m.ChromosomeSegments) painted.Add((m, s, side, key));
        }

        // Number the groups father's side first, then mother's, nearest ancestors (most DNA) first.
        var ordered = groups.OrderBy(kv => kv.Value.Side)
            .ThenByDescending(kv => painted.Where(x => x.Key == kv.Key).Sum(x => x.Segment.Cm))
            .Select((kv, i) => (kv.Key, Number: i + 1, kv.Value)).ToList();
        var numberOf = ordered.ToDictionary(x => x.Key, x => x.Number);
        var segments = painted
            .OrderBy(x => x.Segment.Chromosome).ThenBy(x => x.Segment.Start)
            .Select(x => new PaintedSegment(x.Match, x.Segment, x.Side, numberOf[x.Key])).ToList();
        var paintGroups = ordered.Select(x =>
        {
            var mine = segments.Where(s => s.Group == x.Number).ToList();
            return new PaintGroup(x.Number, x.Value.Ancestors, Label(tree, x.Value.Ancestors), x.Value.Side,
                                  mine.Count, mine.Sum(s => s.Segment.Cm), x.Value.Matches.Order(StringComparer.OrdinalIgnoreCase).ToList());
        }).ToList();
        return new Painting(segments, paintGroups, unlinked, unrelated);
    }

    /// <summary>The ancestors (and the parent) on each side: through the birth father, and through the birth mother.</summary>
    private static (HashSet<long> Father, HashSet<long> Mother) Sides(FamilyTree tree, Person tester)
    {
        HashSet<long> father = new(), mother = new();
        foreach (var fid in tester.ParentFamilyIds)
        {
            if (tree.Family(fid) is not { } f) continue;
            var link = f.Children.FirstOrDefault(c => c.ChildId == tester.Id);
            if (link?.ToFather is null or ParentRelation.Birth && tree.Person(f.Partner1Id) is { } dad && father.Count == 0)
                father.UnionWith(Placement.Ancestors(tree, dad).Keys);
            if (link?.ToMother is null or ParentRelation.Birth && tree.Person(f.Partner2Id) is { } mum && mother.Count == 0)
                mother.UnionWith(Placement.Ancestors(tree, mum).Keys);
        }
        return (father, mother);
    }

    private static string Label(FamilyTree tree, IReadOnlyList<long> ancestors) =>
        string.Join(" & ", ancestors.Select(id => tree.Person(id)?.DisplayName ?? "?"));
}
