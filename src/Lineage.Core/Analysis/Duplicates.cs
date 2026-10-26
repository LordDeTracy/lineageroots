using System.Globalization;
using System.Text;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

public sealed record DuplicateMatch(Person A, Person B, int Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Finds records that probably describe the same person. People are grouped by the sound of their
/// surname and their first initial, so only plausible pairs are compared — fast even on large trees.
/// Each pair is scored on names, dates, places and parents; obvious non-matches (different sex,
/// birth years far apart, already related) are ruled out.
/// </summary>
public static class Duplicates
{
    public const int DefaultMinScore = 60;
    private const int MaxGroup = 400;

    public static List<DuplicateMatch> Find(FamilyTree tree, int minScore = DefaultMinScore)
    {
        var results = new List<DuplicateMatch>();
        var groups = tree.People.Values
            .Select(p => (p, key: BlockKey(p)))
            .Where(x => x.key != null)
            .GroupBy(x => x.key!, x => x.p);
        foreach (var g in groups)
        {
            var people = g.ToList();
            if (people.Count < 2 || people.Count > MaxGroup) continue;
            for (var i = 0; i < people.Count; i++)
                for (var j = i + 1; j < people.Count; j++)
                {
                    var (a, b) = people[i].Id < people[j].Id ? (people[i], people[j]) : (people[j], people[i]);
                    if (tree.NotDuplicates.Contains(FamilyTree.PairKey(a.Id, b.Id))) continue;
                    if (Score(tree, a, b) is { } m && m.Score >= minScore) results.Add(m);
                }
        }
        return results.OrderByDescending(m => m.Score).ThenBy(m => m.A.PrimaryName.SortKey).ToList();
    }

    public static DuplicateMatch? Score(FamilyTree tree, Person a, Person b)
    {
        if (a.Sex != Sex.Unknown && b.Sex != Sex.Unknown && a.Sex != b.Sex) return null;
        if (AreRelated(tree, a, b)) return null;

        var reasons = new List<string>();
        var score = 0;

        var ga = Words(a.PrimaryName.Given);
        var gb = Words(b.PrimaryName.Given);
        if (ga.Length == 0 || gb.Length == 0) return null;
        if (ga.SequenceEqual(gb)) { score += 30; reasons.Add("Same given names"); }
        else if (ga[0] == gb[0]) { score += 20; reasons.Add("Same first name"); }
        else if (ga[0][0] == gb[0][0] && (ga[0].Length == 1 || gb[0].Length == 1)) score += 5;
        else return null;

        var sa = Simplify(a.PrimaryName.Surname);
        var sb = Simplify(b.PrimaryName.Surname);
        if (sa == sb) { score += 20; reasons.Add("Same surname"); }
        else { score += 10; reasons.Add("Surnames sound alike"); }

        score += CompareYears(a.Birth?.Date, b.Birth?.Date, "birth", 25, reasons, out var birthConflict);
        if (birthConflict) return null;
        score += CompareYears(a.Death?.Date, b.Death?.Date, "death", 15, reasons, out var deathConflict);
        if (deathConflict) return null;

        var pa = Simplify(FirstPlacePart(a.Birth?.Place));
        var pb = Simplify(FirstPlacePart(b.Birth?.Place));
        if (pa.Length > 0 && pa == pb) { score += 10; reasons.Add("Same birthplace"); }

        var (fa, ma) = tree.PrimaryParents(a);
        var (fb, mb) = tree.PrimaryParents(b);
        if (SameFirstName(fa, fb)) { score += 10; reasons.Add("Fathers have the same name"); }
        if (SameFirstName(ma, mb)) { score += 10; reasons.Add("Mothers have the same name"); }
        if (SameFirstName(SpouseOf(tree, a), SpouseOf(tree, b))) { score += 15; reasons.Add("Spouses have the same name"); }

        // Siblings with the same name are usually a child who died young and a later namesake.
        if (a.ParentFamilyIds.Intersect(b.ParentFamilyIds).Any() && a.Birth?.Date.Year != b.Birth?.Date.Year)
            score -= 40;

        if (a.FamilySearchId.Length > 0 && a.FamilySearchId == b.FamilySearchId) { score += 50; reasons.Insert(0, "Same FamilySearch ID"); }

        return new DuplicateMatch(a, b, Math.Clamp(score, 0, 100), reasons);
    }

    private static int CompareYears(GenDate? a, GenDate? b, string what, int weight, List<string> reasons, out bool conflict)
    {
        conflict = false;
        if (a is not { Year: > 0 } || b is not { Year: > 0 }) return 0;
        var diff = Math.Abs(a.Year - b.Year);
        if (diff == 0)
        {
            var exact = a.Month > 0 && a.Month == b.Month && a.Day > 0 && a.Day == b.Day;
            reasons.Add(exact ? $"Same {what} date" : $"Same {what} year");
            return exact ? weight + 5 : weight;
        }
        if (diff <= 2) { reasons.Add($"{Cap(what)} years within {diff}"); return weight * 3 / 5; }
        if (diff <= 5) return weight / 5;
        if (diff > 10) conflict = true;
        return 0;
    }

    private static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static bool AreRelated(FamilyTree tree, Person a, Person b)
    {
        foreach (var f in a.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>())
            if (f.PartnerIds.Contains(b.Id) || f.Children.Any(c => c.ChildId == b.Id)) return true;
        foreach (var f in b.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>())
            if (f.Children.Any(c => c.ChildId == a.Id)) return true;
        return false;
    }

    private static Person? SpouseOf(FamilyTree tree, Person p) =>
        tree.Family(p.SpouseFamilyIds.FirstOrDefault()) is { } f ? tree.SpouseIn(f, p) : null;

    private static bool SameFirstName(Person? a, Person? b) =>
        a != null && b != null && Words(a.PrimaryName.Given) is [var x, ..] && Words(b.PrimaryName.Given) is [var y, ..] && x == y;

    private static string? BlockKey(Person p)
    {
        var given = Words(p.PrimaryName.Given);
        var sur = Soundex(p.PrimaryName.Surname);
        return given.Length == 0 || sur.Length == 0 ? null : sur + given[0][0];
    }

    private static string FirstPlacePart(string? place) => (place ?? "").Split(',')[0];

    private static string[] Words(string s) =>
        Simplify(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Lower case, no accents, letters and spaces only.</summary>
    public static string Simplify(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetter(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>American Soundex: names that sound alike (Smith, Smyth) share a code.</summary>
    public static string Soundex(string name)
    {
        var s = Simplify(name).Replace(" ", "");
        if (s.Length == 0) return "";
        static char Code(char c) => c switch
        {
            'b' or 'f' or 'p' or 'v' => '1',
            'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => '2',
            'd' or 't' => '3', 'l' => '4', 'm' or 'n' => '5', 'r' => '6',
            'h' or 'w' => '-', _ => '0',
        };
        var result = new StringBuilder().Append(char.ToUpperInvariant(s[0]));
        var last = Code(s[0]);
        foreach (var c in s.Skip(1))
        {
            var code = Code(c);
            if (code == '-') continue;            // h and w don't separate equal codes
            if (code != '0' && code != last) result.Append(code);
            last = code;
            if (result.Length == 4) break;
        }
        return result.ToString().PadRight(4, '0');
    }
}
