using System.Globalization;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>
/// What person B is to person A, e.g. "2nd great-grandmother" or "first cousin once removed".
/// <see cref="Path"/> runs from A to B through the people that connect them (up to the common ancestor and down
/// again, plus a spouse step for in-laws) so the UI can show the chain. <see cref="CommonAncestors"/> are the
/// closest shared ancestors (usually a couple) for blood relatives.
/// </summary>
public sealed record RelationshipResult(string Text, IReadOnlyList<long> Path, IReadOnlyList<long> CommonAncestors, bool Related)
{
    /// <summary>True when the connection goes through a marriage: spouses, in-laws and step-relatives.</summary>
    public bool ByMarriage { get; init; }
}

/// <summary>
/// The relationship calculator. Blood relationships come from the closest common ancestor, found by walking up
/// every parent family of both people (breadth-first, so loops in the data can't trap it); the generations on each
/// side give the name. If there is no blood link it tries one marriage step: spouse, in-laws, step-parents and
/// step-children, the spouse's relatives ("wife's aunt") and relatives' spouses ("first cousin's husband").
/// Blood always wins when both exist, as it does when cousins marry.
/// </summary>
public static class Relationship
{
    public const string SamePerson = "Same person";
    public const string NotRelated = "Not related by blood or marriage that Lineage Roots can find";

    private static readonly IReadOnlyList<long> None = Array.Empty<long>();

    public static RelationshipResult Describe(FamilyTree tree, Person a, Person b)
    {
        if (a.Id == b.Id) return new RelationshipResult(SamePerson, new[] { a.Id }, None, true);

        var upA = Up(tree, a);
        var upB = Up(tree, b);
        if (FindBlood(upA, upB) is { } blood)
            return new RelationshipResult(BloodText(tree, blood, a, b), blood.Path, blood.Common, true);

        var spousesA = Spouses(tree, a);
        if (spousesA.Any(s => s.Id == b.Id))
            return new RelationshipResult(Pick(b.Sex, "husband", "wife", "spouse"), new[] { a.Id, b.Id }, None, true) { ByMarriage = true };

        RelationshipResult? best = null;
        var bestDistance = int.MaxValue;
        // B is a blood relative of A's spouse.
        foreach (var s in spousesA)
            if (FindBlood(Up(tree, s), upB) is { } r && r.Up + r.Down + 1 < bestDistance)
            {
                bestDistance = r.Up + r.Down + 1;
                best = new RelationshipResult(SpouseRelativeText(r, s, b), Prepend(a.Id, r.Path), r.Common, true) { ByMarriage = true };
            }
        // B is the spouse of A's blood relative.
        foreach (var t in Spouses(tree, b))
            if (FindBlood(upA, Up(tree, t)) is { } r && r.Up + r.Down + 1 < bestDistance)
            {
                bestDistance = r.Up + r.Down + 1;
                best = new RelationshipResult(RelativeSpouseText(r, t, b), Append(r.Path, b.Id), r.Common, true) { ByMarriage = true };
            }
        // B is the child of A's step-parent.
        if (bestDistance > 3 && StepSiblingPath(tree, a, b) is { } path)
            best = new RelationshipResult(Pick(b.Sex, "step-brother", "step-sister", "step-sibling"), path, None, true) { ByMarriage = true };

        return best ?? new RelationshipResult(NotRelated, None, None, false);
    }

    // ---- search ----

    /// <summary>Everyone above one person: generations up, the child each was reached from, and the families passed.</summary>
    private sealed class Ancestry
    {
        public readonly Dictionary<long, int> Gen = new();
        public readonly Dictionary<long, long> Via = new();
        public readonly Dictionary<long, int> FamilyGen = new();
    }

    private sealed record Blood(int Up, int Down, IReadOnlyList<long> Path, IReadOnlyList<long> Common, bool Half);

    private static Ancestry Up(FamilyTree tree, Person start)
    {
        var a = new Ancestry();
        a.Gen[start.Id] = 0;
        var queue = new Queue<Person>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var g = a.Gen[p.Id] + 1;
            foreach (var fid in p.ParentFamilyIds)
            {
                if (tree.Family(fid) is not { } f) continue;
                a.FamilyGen.TryAdd(f.Id, g);
                foreach (var pid in f.PartnerIds)
                {
                    if (a.Gen.ContainsKey(pid) || tree.Person(pid) is not { } parent) continue;
                    a.Gen[pid] = g;
                    a.Via[pid] = p.Id;
                    queue.Enqueue(parent);
                }
            }
        }
        return a;
    }

    /// <summary>
    /// The closest common ancestor of X and Y: fewest generations in all, then a direct line, then the most even split.
    /// Up is the generations from X, Down from Y. Half when the two lines meet in one person but not in a shared family.
    /// </summary>
    private static Blood? FindBlood(Ancestry x, Ancestry y)
    {
        int bestUp = -1, bestDown = -1;
        foreach (var (id, up) in x.Gen)
        {
            if (!y.Gen.TryGetValue(id, out var down)) continue;
            if (bestUp < 0 || Closer(up, down, bestUp, bestDown)) (bestUp, bestDown) = (up, down);
        }
        if (bestUp < 0) return null;

        var common = new List<long>();
        foreach (var (id, up) in x.Gen)
            if (up == bestUp && y.Gen.TryGetValue(id, out var down) && down == bestDown) common.Add(id);
        var half = bestUp > 0 && bestDown > 0 &&
            !x.FamilyGen.Any(kv => kv.Value == bestUp && y.FamilyGen.TryGetValue(kv.Key, out var g) && g == bestDown);
        return new Blood(bestUp, bestDown, PathThrough(x, y, common[0]), common, half);
    }

    private static bool Closer(int up, int down, int bestUp, int bestDown)
    {
        if (up + down != bestUp + bestDown) return up + down < bestUp + bestDown;
        var direct = up == 0 || down == 0;
        var bestDirect = bestUp == 0 || bestDown == 0;
        if (direct != bestDirect) return direct;
        return Math.Abs(up - down) < Math.Abs(bestUp - bestDown);
    }

    private static List<long> PathThrough(Ancestry x, Ancestry y, long ancestor)
    {
        var path = Chain(x, ancestor);   // ancestor ... X
        path.Reverse();                  // X ... ancestor
        path.AddRange(Chain(y, ancestor).Skip(1));
        return path;
    }

    private static List<long> Chain(Ancestry a, long from)
    {
        var chain = new List<long> { from };
        while (a.Via.TryGetValue(chain[^1], out var next)) chain.Add(next);
        return chain;
    }

    private static List<Person> Spouses(FamilyTree tree, Person p)
    {
        var list = new List<Person>();
        foreach (var fid in p.SpouseFamilyIds)
            if (tree.Family(fid) is { } f && tree.SpouseIn(f, p) is { } s && s.Id != p.Id && list.All(x => x.Id != s.Id))
                list.Add(s);
        return list;
    }

    private static IReadOnlyList<long>? StepSiblingPath(FamilyTree tree, Person a, Person b)
    {
        var parentsA = tree.ParentsOf(a).Select(p => p.Id).ToHashSet();
        var parentsB = tree.ParentsOf(b).Select(p => p.Id).ToHashSet();
        foreach (var pa in tree.ParentsOf(a))
            foreach (var q in Spouses(tree, pa))
                if (!parentsA.Contains(q.Id) && parentsB.Contains(q.Id))
                    return new[] { a.Id, pa.Id, q.Id, b.Id };
        return null;
    }

    private static IReadOnlyList<long> Prepend(long id, IReadOnlyList<long> path) => path.Prepend(id).ToArray();
    private static IReadOnlyList<long> Append(IReadOnlyList<long> path, long id) => path.Append(id).ToArray();

    // ---- wording ----

    private static string BloodText(FamilyTree tree, Blood r, Person a, Person b)
    {
        // A direct parent or child may be adoptive, step or foster rather than by birth.
        if (r.Up == 1 && r.Down == 0)
            return LinkBetween(tree, child: a, parent: b) switch
            {
                ParentRelation.Adopted => "adoptive " + Pick(b.Sex, "father", "mother", "parent"),
                ParentRelation.Step => Pick(b.Sex, "step-father", "step-mother", "step-parent"),
                ParentRelation.Foster => "foster " + Pick(b.Sex, "father", "mother", "parent"),
                ParentRelation.Guardian => "guardian",
                _ => Pick(b.Sex, "father", "mother", "parent"),
            };
        if (r.Up == 0 && r.Down == 1)
            return LinkBetween(tree, child: b, parent: a) switch
            {
                ParentRelation.Adopted => "adopted " + Pick(b.Sex, "son", "daughter", "child"),
                ParentRelation.Step => Pick(b.Sex, "step-son", "step-daughter", "step-child"),
                ParentRelation.Foster => "foster " + Pick(b.Sex, "son", "daughter", "child"),
                ParentRelation.Guardian => "ward",
                _ => Pick(b.Sex, "son", "daughter", "child"),
            };
        return Text(r.Up, r.Down, b.Sex, r.Half);
    }

    /// <summary>How a child is linked to a parent; a birth link anywhere wins over adoptive or step links.</summary>
    private static ParentRelation LinkBetween(FamilyTree tree, Person child, Person parent)
    {
        ParentRelation? found = null;
        foreach (var fid in child.ParentFamilyIds)
        {
            if (tree.Family(fid) is not { } f) continue;
            var link = f.Children.FirstOrDefault(c => c.ChildId == child.Id);
            if (link == null) continue;
            ParentRelation? rel = f.Partner1Id == parent.Id ? link.ToFather
                : f.Partner2Id == parent.Id ? link.ToMother : (ParentRelation?)null;
            if (rel is ParentRelation.Birth or ParentRelation.Sealing) return ParentRelation.Birth;
            found ??= rel;
        }
        return found ?? ParentRelation.Birth;
    }

    /// <summary>B is a blood relative (r) of A's spouse S.</summary>
    private static string SpouseRelativeText(Blood r, Person s, Person b)
    {
        if (r.Up == 1 && r.Down == 0) return Pick(b.Sex, "father-in-law", "mother-in-law", "parent-in-law");
        if (r.Up == 1 && r.Down == 1) return Pick(b.Sex, "brother-in-law", "sister-in-law", "sibling-in-law");
        if (r.Up == 0) return "step-" + Text(0, r.Down, b.Sex, false);
        return $"{Pick(s.Sex, "husband", "wife", "spouse")}'s {Text(r.Up, r.Down, b.Sex, r.Half)}";
    }

    /// <summary>B is the spouse of T, who is A's blood relative (r).</summary>
    private static string RelativeSpouseText(Blood r, Person t, Person b)
    {
        if (r.Down == 0) return "step-" + Text(r.Up, 0, b.Sex, false);
        if (r.Up == 0) return Text(0, r.Down, b.Sex, false) + "-in-law";
        if (r.Up == 1 && r.Down == 1) return Pick(b.Sex, "brother-in-law", "sister-in-law", "sibling-in-law");
        return $"{Text(r.Up, r.Down, t.Sex, r.Half)}'s {Pick(b.Sex, "husband", "wife", "spouse")}";
    }

    /// <summary>The name for someone <paramref name="down"/> generations below the common ancestor that is <paramref name="up"/> above A.</summary>
    private static string Text(int up, int down, Sex sex, bool half)
    {
        if (down == 0) return Generations(up, Pick(sex, "father", "mother", "parent"));
        if (up == 0) return Generations(down, Pick(sex, "son", "daughter", "child"));
        if (up == 1 && down == 1) return Half(half, Pick(sex, "brother", "sister", "sibling"));
        if (down == 1) return Half(half, Greats(up - 2) + Pick(sex, "uncle", "aunt", "uncle or aunt"));
        if (up == 1) return Half(half, Greats(down - 2) + Pick(sex, "nephew", "niece", "nephew or niece"));
        var removed = Math.Abs(up - down);
        var cousin = $"{CousinDegree(Math.Min(up, down) - 1)} cousin" + (removed == 0 ? "" : " " + Removed(removed));
        return half ? "half " + cousin : cousin;
    }

    private static string Generations(int n, string word) => n == 1 ? word : Greats(n - 2) + "grand" + word;

    private static string Greats(int n) => n <= 0 ? "" : n == 1 ? "great-" : Ordinal(n) + " great-";

    private static string Half(bool half, string term) =>
        !half ? term : term.Contains(' ') || term.Contains('-') ? "half " + term : "half-" + term;

    private static readonly string[] Degrees =
        { "", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth" };

    private static readonly string[] Numbers =
        { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };

    private static string CousinDegree(int n) => n < Degrees.Length ? Degrees[n] : Ordinal(n);

    private static string Removed(int n) => n switch
    {
        1 => "once removed",
        2 => "twice removed",
        _ => (n < Numbers.Length ? Numbers[n] : n.ToString(CultureInfo.InvariantCulture)) + " times removed",
    };

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static string Pick(Sex sex, string male, string female, string unknown) =>
        sex switch { Sex.Male => male, Sex.Female => female, _ => unknown };
}
