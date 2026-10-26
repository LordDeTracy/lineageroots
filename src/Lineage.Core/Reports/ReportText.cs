using System.Globalization;
using Lineage.Core.Model;

namespace Lineage.Core.Reports;

/// <summary>
/// Plain-English wording for reports: dates ("about 1850", "on 14 March 1842"), pronouns, ages,
/// roman numerals. Kept in Core so every report format and the web export word things the same way.
/// </summary>
public static class ReportText
{
    private static readonly string[] Months =
        { "", "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    public static string MonthName(int month) => month is >= 1 and <= 12 ? Months[month] : "";

    /// <summary>A date for tables and lists: "14 March 1842", "about 1850", "between 1932 and 1933".</summary>
    public static string Date(GenDate d)
    {
        if (d.IsEmpty) return "";
        if (d.Kind == DateKind.Text) return d.Text;
        var core = Core(d);
        return d.Kind switch
        {
            DateKind.Range => d.End is { IsEmpty: false } e ? $"between {core} and {Core(e)}" : core,
            DateKind.Period => d.End is { IsEmpty: false } e ? $"from {core} to {Core(e)}" : $"from {core}",
            _ => d.Qualifier switch
            {
                DateQualifier.About or DateQualifier.Calculated or DateQualifier.Estimated => "about " + core,
                DateQualifier.Before => "before " + core,
                DateQualifier.After => "after " + core,
                _ => core,
            },
        };
    }

    /// <summary>A date inside a sentence: "on 14 March 1842", "in March 1842", "in 1842", "about 1850".</summary>
    public static string DatePhrase(GenDate d)
    {
        if (d.IsEmpty) return "";
        if (d.Kind == DateKind.Text) return char.IsDigit(d.Text[0]) ? "on " + d.Text : d.Text;
        if (d.Kind == DateKind.Simple && d.Qualifier == DateQualifier.None)
            return (d.Day > 0 ? "on " : "in ") + Core(d);
        return Date(d);
    }

    /// <summary>The year of a date for "1842–1911" style ranges; 0 when unknown.</summary>
    public static int Year(GenDate d) => d.Kind == DateKind.Text ? 0 : d.Year;

    private static string Core(GenDate d)
    {
        var parts = new List<string>(3);
        if (d.Day > 0 && d.Month > 0) parts.Add(d.DualDay > 0 ? $"{d.Day}/{d.DualDay}" : d.Day.ToString(CultureInfo.InvariantCulture));
        if (d.Month is > 0 and <= 12) parts.Add(Months[d.Month]);
        if (d.Year > 0)
        {
            var year = d.Year.ToString(CultureInfo.InvariantCulture);
            if (d.DualYear > 0)
                year += "/" + (d.DualYear / 100 == d.Year / 100 ? (d.DualYear % 100).ToString("00", CultureInfo.InvariantCulture) : d.DualYear.ToString(CultureInfo.InvariantCulture));
            parts.Add(year);
        }
        return string.Join(' ', parts);
    }

    /// <summary>"aged 69", "aged about 69", "aged 3 months" — or "" when the dates don't allow it.</summary>
    public static string Age(Event? birth, GenDate death)
    {
        if (birth == null) return "";
        var b = birth.Date;
        if (b.Kind != DateKind.Simple || death.Kind != DateKind.Simple || b.Year == 0 || death.Year == 0) return "";
        if (b.Qualifier is DateQualifier.Before or DateQualifier.After || death.Qualifier is DateQualifier.Before or DateQualifier.After) return "";
        var approx = b.Qualifier != DateQualifier.None || death.Qualifier != DateQualifier.None || birth.Tag != "BIRT";
        var full = b.Month > 0 && b.Day > 0 && death.Month > 0 && death.Day > 0;
        var years = death.Year - b.Year;
        if (b.Month > 0 && death.Month > 0)
        {
            if (death.Month < b.Month || (death.Month == b.Month && b.Day > 0 && death.Day > 0 && death.Day < b.Day)) years--;
            if (!full && death.Month == b.Month) approx = true;
        }
        else approx = true;
        if (years < 0) return "";
        if (years > 0) return approx ? $"aged about {years}" : $"aged {years}";
        if (!full || approx) return "";
        try
        {
            var days = (new DateTime(death.Year, death.Month, death.Day) - new DateTime(b.Year, b.Month, b.Day)).Days;
            if (days < 0) return "";
            if (days < 31) return days == 1 ? "aged 1 day" : $"aged {days} days";
            var months = (death.Year * 12 + death.Month) - (b.Year * 12 + b.Month) - (death.Day < b.Day ? 1 : 0);
            return months <= 1 ? "aged 1 month" : $"aged {months} months";
        }
        catch (ArgumentOutOfRangeException) { return ""; }
    }

    public static string Subject(Sex sex) => sex switch { Sex.Male => "He", Sex.Female => "She", _ => "They" };
    public static string Possessive(Sex sex) => sex switch { Sex.Male => "his", Sex.Female => "her", _ => "their" };
    public static string ChildWord(Sex sex) => sex switch { Sex.Male => "son", Sex.Female => "daughter", _ => "child" };

    /// <summary>Lower-case roman numerals (i, ii, iii, iv, ...) as used to list children.</summary>
    public static string Roman(int n)
    {
        if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < values.Length; i++)
            while (n >= values[i]) { sb.Append(symbols[i]); n -= values[i]; }
        return sb.ToString();
    }

    /// <summary>"first", "second", ... for counting marriages.</summary>
    public static string OrdinalWord(int n) => n switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth",
        6 => "sixth", 7 => "seventh", 8 => "eighth", 9 => "ninth", 10 => "tenth", _ => Ordinal(n),
    };

    /// <summary>"1st", "2nd", "3rd", "11th", "22nd".</summary>
    public static string Ordinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>"George Whitfield and Mary Ann Cole", "A, B and C".</summary>
    public static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    /// <summary>"Stonemason" → "stonemason", but "RAF pilot" stays as it is.</summary>
    public static string LowerFirst(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && char.IsLower(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>"a stonemason", "an engineer" (left alone when it already starts with an article).</summary>
    public static string WithArticle(string s)
    {
        if (s.Length == 0) return s;
        var lower = s.ToLowerInvariant();
        if (lower.StartsWith("a ") || lower.StartsWith("an ") || lower.StartsWith("the ")) return s;
        return ("aeiou".Contains(lower[0]) ? "an " : "a ") + s;
    }

    /// <summary>"Thomas Whitfield’s", "James’".</summary>
    public static string Possessive(string name) => name.EndsWith('s') ? name + "’" : name + "’s";

    /// <summary>"1842–1911", "1842–", "–1911"; "" when nothing is known.</summary>
    public static string Years(int from, int to) =>
        from == 0 && to == 0 ? "" : from == to ? from.ToString(CultureInfo.InvariantCulture)
        : $"{(from > 0 ? from.ToString(CultureInfo.InvariantCulture) : "")}–{(to > 0 ? to.ToString(CultureInfo.InvariantCulture) : "")}";
}
