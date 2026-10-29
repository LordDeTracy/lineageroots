using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lineage.Core.Model;

public enum DateQualifier { None, About, Before, After, Calculated, Estimated }

public enum DateKind { Simple, Range, Period, Text }

/// <summary>
/// A genealogical date: partial (year only, month+year, full), qualified (ABT/BEF/...),
/// dual-dated (1691/1692), a range (BET .. AND ..), a period (FROM .. TO ..), or free text.
/// Immutable; parse with <see cref="Parse"/>, render with <see cref="ToGedcom"/>.
/// </summary>
public sealed record GenDate
{
    public DateKind Kind { get; init; } = DateKind.Simple;
    public DateQualifier Qualifier { get; init; }
    public int Year { get; init; }
    public int Month { get; init; }
    public int Day { get; init; }
    public int DualYear { get; init; }   // 1692 in "1691/1692"
    public int DualDay { get; init; }    // 29 in "28/29 SEP 1764"
    public GenDate? End { get; init; }   // second date of a range or period
    public string Text { get; init; } = "";

    public static readonly GenDate Empty = new();

    private static readonly string[] Months =
        { "", "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    [JsonIgnore] public bool IsEmpty => Kind == DateKind.Text ? Text.Length == 0 : Year == 0 && Month == 0 && Day == 0;

    /// <summary>Approximate sort key (year*10000 + month*100 + day); 0 when unknown.</summary>
    [JsonIgnore] public int SortKey => Kind == DateKind.Text ? 0 : NewStyleYear * 10000 + Month * 100 + Day;

    /// <summary>The year as counted today: 1692 for "10 FEB 1691/92" (the year then began on 25 March).</summary>
    [JsonIgnore] public int NewStyleYear => DualYear > Year ? DualYear : Year;

    /// <summary>
    /// A date from 1 January to 24 March before 1752 written with one year: in England and its colonies the year then
    /// began on 25 March, so it may be the old-style year (one less than today's count) — or already the new one.
    /// </summary>
    public static bool MaybeOldStyle(GenDate d) =>
        d.Year is > 0 and < 1752 && (d.Month is 1 or 2 || (d.Month == 3 && d.Day is >= 0 and <= 24));

    public string ToGedcom()
    {
        if (Kind == DateKind.Text) return Text;
        if (IsEmpty) return "";
        return Kind switch
        {
            DateKind.Range => $"BET {Core()} AND {End?.Core()}",
            DateKind.Period => End is null ? $"FROM {Core()}" : $"FROM {Core()} TO {End.Core()}",
            _ => Qualifier switch
            {
                DateQualifier.About => "ABT " + Core(),
                DateQualifier.Before => "BEF " + Core(),
                DateQualifier.After => "AFT " + Core(),
                DateQualifier.Calculated => "CAL " + Core(),
                DateQualifier.Estimated => "EST " + Core(),
                _ => Core(),
            },
        };
    }

    public override string ToString() => ToGedcom();

    private string Core()
    {
        var parts = new List<string>(3);
        if (Day > 0) parts.Add(DualDay > 0 ? $"{Day}/{DualDay}" : Day.ToString(CultureInfo.InvariantCulture));
        if (Month is > 0 and <= 12) parts.Add(Months[Month]);
        if (Year > 0) parts.Add(DualYear > 0 ? $"{Year}/{DualYear}" : Year.ToString(CultureInfo.InvariantCulture));
        return string.Join(' ', parts);
    }

    private static readonly Regex CoreRx = new(
        @"^(?:(?<d>\d{1,2})(?:/(?<d2>\d{1,2}))?\s+)?(?:(?<m>[A-Za-z]{3,9})\.?\s+)?(?<y>\d{1,4})(?:/(?<y2>\d{1,4}))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> MonthLookup = BuildMonths();

    private static Dictionary<string, int> BuildMonths()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] full = { "", "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE", "JULY",
                          "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER" };
        for (var i = 1; i <= 12; i++) { d[Months[i]] = i; d[full[i]] = i; }
        d["SEPT"] = 9;
        return d;
    }

    /// <summary>Parse GEDCOM-style date text. Anything unrecognised becomes a Text date.</summary>
    public static GenDate Parse(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return Empty;
        var upper = s.ToUpperInvariant();

        foreach (var bet in new[] { "BET ", "BETWEEN " })
        {
            if (!upper.StartsWith(bet, StringComparison.Ordinal)) continue;
            var and = upper.IndexOf(" AND ", StringComparison.Ordinal);
            if (and > 0 && TryCore(s[bet.Length..and], out var a) && TryCore(s[(and + 5)..], out var b))
                return a with { Kind = DateKind.Range, End = b };
        }
        if (upper.StartsWith("FROM ", StringComparison.Ordinal))
        {
            var to = upper.IndexOf(" TO ", StringComparison.Ordinal);
            if (to > 0 && TryCore(s[5..to], out var a) && TryCore(s[(to + 4)..], out var b))
                return a with { Kind = DateKind.Period, End = b };
            if (to < 0 && TryCore(s[5..], out var only))
                return only with { Kind = DateKind.Period };
        }
        foreach (var (prefix, q) in new[] { ("ABT ", DateQualifier.About), ("ABOUT ", DateQualifier.About),
                     ("BEF ", DateQualifier.Before), ("BEFORE ", DateQualifier.Before),
                     ("AFT ", DateQualifier.After), ("AFTER ", DateQualifier.After),
                     ("CAL ", DateQualifier.Calculated), ("CALCULATED ", DateQualifier.Calculated),
                     ("EST ", DateQualifier.Estimated), ("ESTIMATED ", DateQualifier.Estimated) })
        {
            if (upper.StartsWith(prefix, StringComparison.Ordinal) && TryCore(s[prefix.Length..], out var d))
                return d with { Qualifier = q };
        }
        return TryCore(s, out var plain) ? plain : new GenDate { Kind = DateKind.Text, Text = s };
    }

    private static bool TryCore(string s, out GenDate date)
    {
        date = Empty;
        var m = CoreRx.Match(s.Trim());
        if (!m.Success) return false;
        var month = 0;
        if (m.Groups["m"].Success && !MonthLookup.TryGetValue(m.Groups["m"].Value, out month)) return false;
        int Num(string g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var day = Num("d");
        if (day > 31 || (day > 0 && month == 0)) return false;
        var year = Num("y");
        var dual = Num("y2");
        if (dual is > 0 and < 100) dual = year / 100 * 100 + dual; // "1691/92"
        date = new GenDate { Year = year, Month = month, Day = day, DualDay = Num("d2"), DualYear = dual };
        return true;
    }
}
