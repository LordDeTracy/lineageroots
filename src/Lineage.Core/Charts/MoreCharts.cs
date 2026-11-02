using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.Charts;

/// <summary>One life on the lifelines chart: who, how related, and the years (an open end is estimated).</summary>
public sealed record LifeRow(long PersonId, string Name, string Relation, int From, int To, bool FromEstimated, bool ToEstimated, bool IsRoot);

/// <summary>One person on a relationship chart, placed by generation (Y) along the path (X), with how they connect to the one before.</summary>
public sealed record PathStep(long PersonId, int X, int Y, string Link);

/// <summary>A count or average for one bar of a statistics chart.</summary>
public sealed record StatBar(string Label, double Value, int Count);

public sealed record TreeStatistics(IReadOnlyList<StatBar> BirthsByDecade, IReadOnlyList<StatBar> LifespanByCentury,
                                    IReadOnlyList<StatBar> Surnames, IReadOnlyList<StatBar> Places, int People);

/// <summary>The data behind the lifelines, relationship and statistics charts.</summary>
public static class MoreCharts
{
    /// <summary>A life with no recorded end is drawn to this age (or today), faded.</summary>
    public const int AssumedLife = 70;

    /// <summary>
    /// The person's family across time: great-grandparents to grandchildren, with siblings and spouses, each a bar
    /// from birth to death, earliest first. People with no dates at all are left out.
    /// </summary>
    public static List<LifeRow> Lifelines(FamilyTree tree, long rootId, int? currentYear = null)
    {
        if (tree.Person(rootId) is not { } root) return new();
        var now = currentYear ?? DateTime.Today.Year;
        var people = new Dictionary<long, Person> { [root.Id] = root };
        void Add(Person? p) { if (p != null) people.TryAdd(p.Id, p); }
        foreach (var a in tree.Ancestors(root, 4).Values) Add(a);
        foreach (var parent in tree.ParentsOf(root))
            foreach (var (_, _, children) in tree.FamiliesOf(parent))
                foreach (var sibling in children) Add(sibling);
        foreach (var (_, spouse, children) in tree.FamiliesOf(root))
        {
            Add(spouse);
            foreach (var child in children)
            {
                Add(child);
                foreach (var (_, _, grandchildren) in tree.FamiliesOf(child))
                    foreach (var g in grandchildren) Add(g);
            }
        }

        var rows = new List<LifeRow>();
        foreach (var p in people.Values)
        {
            var born = p.Birth?.Date.Year ?? 0;
            var died = p.Death?.Date.Year ?? 0;
            if (born == 0 && died == 0) continue;
            var (from, to) = (born > 0 ? born : died - AssumedLife, died > 0 ? died : Math.Min(born + AssumedLife, now));
            if (to < from) to = from;
            var relation = p.Id == root.Id ? "" : Relationship.Describe(tree, root, p) is { Related: true } r ? r.Text : "";
            rows.Add(new LifeRow(p.Id, p.DisplayName, relation.Length > 0 ? char.ToUpperInvariant(relation[0]) + relation[1..] : "",
                                 from, to, born == 0, died == 0, p.Id == root.Id));
        }
        return rows.OrderBy(r => r.From).ThenBy(r => r.Name).ToList();
    }

    /// <summary>
    /// The path from one person to another, as a relationship chart lays it out: a step up for each parent, down for
    /// each child, across for a marriage — so cousins meet at their common ancestors at the top.
    /// </summary>
    public static List<PathStep> RelationshipPath(FamilyTree tree, long fromId, long toId)
    {
        if (tree.Person(fromId) is not { } from || tree.Person(toId) is not { } to || from.Id == to.Id) return new();
        var result = Relationship.Describe(tree, from, to);
        if (!result.Related || result.Path.Count < 2) return new();
        var steps = new List<PathStep>();
        var (x, y) = (0, 0);
        Person? previous = null;
        foreach (var id in result.Path)
        {
            if (tree.Person(id) is not { } p) continue;
            var link = "";
            if (previous != null)
            {
                x++;
                if (tree.ParentsOf(previous).Any(q => q.Id == p.Id)) { y--; link = Pick(p.Sex, "father", "mother", "parent"); }
                else if (tree.ParentsOf(p).Any(q => q.Id == previous.Id)) { y++; link = Pick(p.Sex, "son", "daughter", "child"); }
                else link = Pick(p.Sex, "husband", "wife", "spouse");
            }
            steps.Add(new PathStep(p.Id, x, y, link));
            previous = p;
        }
        var top = steps.Min(s => s.Y);
        return steps.Select(s => s with { Y = s.Y - top }).ToList();
    }

    public static TreeStatistics Statistics(FamilyTree tree)
    {
        var people = tree.People.Values.ToList();
        var births = people.Select(p => p.Birth?.Date.Year ?? 0).Where(y => y > 0).ToList();
        var byDecade = births.GroupBy(y => y / 10 * 10).OrderBy(g => g.Key)
            .Select(g => new StatBar($"{g.Key}s", g.Count(), g.Count())).ToList();
        var lifespans = people
            .Select(p => (Born: p.Birth?.Date.Year ?? 0, Died: p.Death?.Date.Year ?? 0))
            .Where(x => x.Born > 0 && x.Died >= x.Born && x.Died - x.Born <= 120)
            .GroupBy(x => x.Born / 100).OrderBy(g => g.Key)
            .Select(g => new StatBar(Century(g.Key), Math.Round(g.Average(x => x.Died - x.Born), 1), g.Count())).ToList();
        var surnames = people.Select(p => p.PrimaryName.Surname.Trim()).Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(10)
            .Select(g => new StatBar(g.First(), g.Count(), g.Count())).ToList();
        var places = people.Select(p => (p.Birth?.Place ?? "").Split(',')[0].Trim()).Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(10)
            .Select(g => new StatBar(g.First(), g.Count(), g.Count())).ToList();
        return new TreeStatistics(byDecade, lifespans, surnames, places, people.Count);
    }

    private static string Century(int c)
    {
        var n = c + 1;
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix} century";
    }

    private static string Pick(Sex sex, string male, string female, string other) =>
        sex switch { Sex.Male => male, Sex.Female => female, _ => other };
}
