using System.Globalization;
using System.Text.RegularExpressions;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

public enum CleanupKind { Names, Dates, Places }

/// <summary>
/// One proposed clean-up: every place <see cref="Before"/> appears becomes <see cref="After"/>.
/// <see cref="Touched"/> lists the records it changes (for undo); <see cref="Apply"/> makes the change
/// and returns how many values it changed. Apply right after finding, before anything else edits the tree.
/// </summary>
public sealed class CleanupFix
{
    public required CleanupKind Kind { get; init; }
    public required string Before { get; init; }
    public required string After { get; init; }
    /// <summary>Why, or what is unsure ("could be 3 April or 4 March").</summary>
    public string Note { get; init; } = "";
    public required int Count { get; init; }
    /// <summary>A few of the people or records involved, for the preview.</summary>
    public required string Where { get; init; }
    public required Func<IEnumerable<Entity>> Touched { get; init; }
    public required Func<int> Apply { get; init; }
}

/// <summary>
/// The clean-up wizard's checks: names typed in ALL CAPITALS (or all small letters) or with stray spaces,
/// dates in formats the program can't read ("12/03/1842", "March 12, 1842", "abt. 1850"), and place
/// names that are the same place written differently or have untidy commas and spaces. Nothing is
/// changed here: each finding is a <see cref="CleanupFix"/> the user can preview and pick.
/// </summary>
public static class Cleanup
{
    public static List<CleanupFix> Find(FamilyTree tree, bool dayFirst) =>
        Names(tree).Concat(Dates(tree, dayFirst)).Concat(Places(tree)).ToList();

    // ---- names ----

    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase)
        { "van", "von", "der", "den", "de", "del", "della", "la", "le", "du", "da", "dos", "das", "ten", "ter", "y" };

    private static readonly HashSet<string> Numerals = new(StringComparer.OrdinalIgnoreCase)
        { "ii", "iii", "iv", "vi", "vii", "viii" };

    private static List<CleanupFix> Names(FamilyTree tree)
    {
        var hits = new List<(Person Person, Name Name, bool Surname, string Before, string After)>();
        foreach (var p in tree.People.Values)
            foreach (var n in p.Names)
            {
                if (TidyName(n.Given, surname: false) is { } g) hits.Add((p, n, false, n.Given, g));
                if (TidyName(n.Surname, surname: true) is { } s) hits.Add((p, n, true, n.Surname, s));
            }
        return hits.GroupBy(h => (h.Before, h.After, h.Surname)).Select(g =>
        {
            var list = g.ToList();
            return new CleanupFix
            {
                Kind = CleanupKind.Names, Before = g.Key.Before, After = g.Key.After, Count = list.Count,
                Note = g.Key.Before.Trim() == g.Key.Before && !g.Key.Before.Contains("  ")
                    ? (g.Key.Surname ? "Surname in capitals" : "Given name in capitals")
                    : "Extra spaces",
                Where = Sample(list.Select(h => h.Person.DisplayName)),
                Touched = () => list.Select(h => (Entity)h.Person).Distinct(),
                Apply = () =>
                {
                    var changed = 0;
                    foreach (var h in list)
                    {
                        // The person may have been replaced by an undo since; change the one in the tree.
                        if (tree.Person(h.Person.Id) is not { } p) continue;
                        foreach (var n in p.Names)
                        {
                            if (g.Key.Surname && n.Surname == h.Before) { n.Surname = h.After; changed++; }
                            if (!g.Key.Surname && n.Given == h.Before) { n.Given = h.After; changed++; }
                        }
                    }
                    return changed;
                },
            };
        }).OrderBy(f => f.Before, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The name in ordinary capitals with single spaces, or null when it is fine as it is.</summary>
    public static string? TidyName(string name, bool surname)
    {
        var spaced = Regex.Replace(name.Trim(), @"\s+", " ");
        var letters = spaced.Where(char.IsLetter).ToList();
        var shouting = letters.Count >= 2 && (letters.All(char.IsUpper) || letters.All(char.IsLower));
        var result = shouting ? Capitalise(spaced, surname) : spaced;
        return result == name ? null : result;
    }

    private static string Capitalise(string name, bool surname)
    {
        var words = name.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (Numerals.Contains(w)) { words[i] = w.ToUpperInvariant(); continue; }
            // "de la Cruz", "van der Berg" — but a particle that starts the whole name stays capitalised.
            if (surname && i < words.Length - 1 && i > 0 && Particles.Contains(w)) { words[i] = w.ToLowerInvariant(); continue; }
            words[i] = Regex.Replace(w.ToLowerInvariant(), @"[\p{L}]+", m =>
            {
                var part = m.Value;
                var cap = char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..];
                if (surname && part.Length > 2 && part.StartsWith("mc", StringComparison.Ordinal))
                    cap = "Mc" + char.ToUpper(part[2], CultureInfo.InvariantCulture) + part[3..];
                return cap;
            });
        }
        return string.Join(' ', words);
    }

    // ---- dates ----

    private static List<CleanupFix> Dates(FamilyTree tree, bool dayFirst)
    {
        var hits = new List<(Entity Owner, Event Event, string Before, string After, string Note)>();
        void Check(Entity owner, Event e)
        {
            if (e.Date.Kind != DateKind.Text || e.Date.Text.Length == 0) return;
            if (RepairDate(e.Date.Text, dayFirst, out var note) is { } fixedText)
                hits.Add((owner, e, e.Date.Text, fixedText, note));
        }
        foreach (var p in tree.People.Values)
            foreach (var e in p.Events) Check(p, e);
        foreach (var f in tree.Families.Values)
            foreach (var e in f.Events) Check(f, e);

        return hits.GroupBy(h => (h.Before, h.After)).Select(g =>
        {
            var list = g.ToList();
            return new CleanupFix
            {
                Kind = CleanupKind.Dates, Before = g.Key.Before, After = g.Key.After, Count = list.Count,
                Note = list[0].Note, Where = Sample(list.Select(h => OwnerName(tree, h.Owner))),
                Touched = () => list.Select(h => h.Owner).Distinct(),
                Apply = () =>
                {
                    var date = GenDate.Parse(g.Key.After);
                    var changed = 0;
                    foreach (var h in list)
                    {
                        var events = h.Owner is Person ? tree.Person(h.Owner.Id)?.Events : tree.Family(h.Owner.Id)?.Events;
                        foreach (var e in events ?? new List<Event>())
                            if (e.Date.Kind == DateKind.Text && e.Date.Text == g.Key.Before) { e.Date = date; changed++; }
                    }
                    return changed;
                },
            };
        }).OrderBy(f => f.Before, StringComparer.OrdinalIgnoreCase).ToList();
    }


    private static readonly (Regex Rx, string Gedcom)[] Qualifiers =
    {
        (new Regex(@"^(?:abt|about|abo|circa|cir|ca|c|approx|approximately|around)\b\.?\s*", RegexOptions.IgnoreCase), "ABT"),
        (new Regex(@"^(?:bef|before|bfr|by)\b\.?\s*", RegexOptions.IgnoreCase), "BEF"),
        (new Regex(@"^(?:aft|after)\b\.?\s*", RegexOptions.IgnoreCase), "AFT"),
        (new Regex(@"^(?:est|estimated)\b\.?\s*", RegexOptions.IgnoreCase), "EST"),
        (new Regex(@"^(?:cal|calc|calculated)\b\.?\s*", RegexOptions.IgnoreCase), "CAL"),
    };

    private static readonly Regex Between = new(@"^(?:bet|betw|between)\b\.?\s+(.+?)\s+(?:and|&|-)\s+(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex FromTo = new(@"^(?:from)\s+(.+?)\s+(?:to|until|-)\s+(.+)$", RegexOptions.IgnoreCase);
    private static readonly Regex Iso = new(@"^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$");
    private static readonly Regex Numeric = new(@"^(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})$");
    private static readonly Regex MonthFirst = new(@"^([A-Za-z]{3,9})\.?\s+(\d{1,2})\s+(\d{3,4})$");
    private static readonly string[] Months = { "", "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    /// <summary>
    /// A date the program can't read, rewritten in the standard form ("12 MAR 1842", "ABT 1850"),
    /// or null when it can't be worked out. <paramref name="note"/> says when the reading is a guess.
    /// </summary>
    public static string? RepairDate(string text, bool dayFirst, out string note)
    {
        note = "";
        var s = Regex.Replace(text.Trim(), @"\s+", " ");
        if (Between.Match(s) is { Success: true } bet)
            return Core(bet.Groups[1].Value, dayFirst, ref note) is { } a && Core(bet.Groups[2].Value, dayFirst, ref note) is { } b
                ? Checked($"BET {a} AND {b}") : null;
        if (FromTo.Match(s) is { Success: true } ft)
            return Core(ft.Groups[1].Value, dayFirst, ref note) is { } a && Core(ft.Groups[2].Value, dayFirst, ref note) is { } b
                ? Checked($"FROM {a} TO {b}") : null;
        foreach (var (rx, gedcom) in Qualifiers)
        {
            var m = rx.Match(s);
            if (!m.Success || m.Length == s.Length) continue;
            return Core(s[m.Length..], dayFirst, ref note) is { } d ? Checked($"{gedcom} {d}") : null;
        }
        return Core(s, dayFirst, ref note) is { } plain ? Checked(plain) : null;
    }

    private static string? Checked(string gedcom) => GenDate.Parse(gedcom).Kind == DateKind.Text ? null : gedcom;

    private static string? Core(string s, bool dayFirst, ref string note)
    {
        s = Regex.Replace(s.Trim(), @"(\d{1,2})(?:st|nd|rd|th)\b", "$1", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bof\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s.Replace(',', ' '), @"\s+", " ").Trim();
        if (Iso.Match(s) is { Success: true } iso)
            return Dmy(int.Parse(iso.Groups[3].Value), int.Parse(iso.Groups[2].Value), iso.Groups[1].Value);
        if (Numeric.Match(s) is { Success: true } num)
        {
            int a = int.Parse(num.Groups[1].Value), b = int.Parse(num.Groups[2].Value);
            var y = num.Groups[3].Value;
            if (a > 12 && b <= 12) return Dmy(a, b, y);
            if (b > 12 && a <= 12) return Dmy(b, a, y);
            if (a == b) return Dmy(a, b, y);
            var reading = dayFirst ? Dmy(a, b, y) : Dmy(b, a, y);
            var other = dayFirst ? Dmy(b, a, y) : Dmy(a, b, y);
            if (reading != null && other != null)
            {
                var d = GenDate.Parse(other);
                note = $"Could also be {d.Day} {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(d.Month)} {d.Year}; check the date order setting";
            }
            return reading;
        }
        // "12-Mar-1842", "Mar.12.1842"
        if (Regex.IsMatch(s, @"[A-Za-z]") && Regex.IsMatch(s, @"[-/.]")) s = Regex.Replace(s, @"[-/.]+", " ").Trim();
        if (MonthFirst.Match(s) is { Success: true } mf) s = $"{mf.Groups[2].Value} {mf.Groups[1].Value} {mf.Groups[3].Value}";
        var parsed = GenDate.Parse(s);
        return parsed.Kind == DateKind.Text ? null : parsed.ToGedcom();
    }

    private static string? Dmy(int day, int month, string year) =>
        month is >= 1 and <= 12 && day is >= 1 and <= 31 && day <= DateTime.DaysInMonth(2000, month)
            ? $"{day} {Months[month]} {year}" : null;

    // ---- places ----

    private static List<CleanupFix> Places(FamilyTree tree)
    {
        var entries = PlaceIndex.Build(tree);
        var fixes = new List<CleanupFix>();
        var merged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in PlaceIndex.Suggest(entries))
            foreach (var variant in s.Variants)
            {
                merged.Add(variant);
                fixes.Add(PlaceFix(tree, entries, variant, s.Standard, s.Reason));
            }
        foreach (var e in entries)
            if (!merged.Contains(e.Name) && TidyPlace(e.Name) is { } tidy)
                fixes.Add(PlaceFix(tree, entries, e.Name, tidy, "Untidy commas or spaces"));
        return fixes.OrderBy(f => f.After, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Before, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static CleanupFix PlaceFix(FamilyTree tree, List<PlaceEntry> entries, string from, string to, string why) => new()
    {
        Kind = CleanupKind.Places, Before = from, After = to, Note = why,
        Count = entries.FirstOrDefault(e => e.Name == from)?.EventCount ?? 0,
        Where = Sample(PlaceIndex.Touched(tree, new[] { from }).Select(o => OwnerName(tree, o))),
        Touched = () => PlaceIndex.Touched(tree, new[] { from, to }),
        Apply = () => PlaceIndex.Rename(tree, from, to),
    };

    /// <summary>The place with one space after each comma and no empty parts, or null when it is already tidy.</summary>
    public static string? TidyPlace(string place)
    {
        var parts = place.Split(',').Select(p => Regex.Replace(p.Trim(), @"\s+", " ")).Where(p => p.Length > 0);
        var tidy = string.Join(", ", parts);
        return tidy.Length == 0 || tidy == place ? null : tidy;
    }

    // ---- shared ----

    private static string OwnerName(FamilyTree tree, Entity owner) => owner switch
    {
        Person p => p.DisplayName,
        Family f => string.Join(" & ", f.PartnerIds.Select(id => tree.Person(id)?.DisplayName).OfType<string>()) is { Length: > 0 } n ? n : "a family",
        _ => "",
    };

    private static string Sample(IEnumerable<string> names)
    {
        var all = names.Distinct().ToList();
        return all.Count <= 3 ? string.Join(", ", all) : $"{string.Join(", ", all.Take(3))} and {all.Count - 3:N0} more";
    }
}
