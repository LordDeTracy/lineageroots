using System.Globalization;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>
/// The genealogist's calculators: the time between two dates, a birth date from an age, and conversions between the
/// Gregorian, Julian (Old Style), Quaker, Hebrew and French Republican calendars. Dates are whole days; anything
/// outside what a calendar covers gives null rather than a wrong answer.
/// </summary>
public static class Calculators
{
    // ---- dates and ages -------------------------------------------------------------------------------

    /// <summary>"34 years, 2 months, 5 days" between two dates (in either order).</summary>
    public static string Between(DateTime a, DateTime b)
    {
        if (b < a) (a, b) = (b, a);
        var years = b.Year - a.Year;
        var months = b.Month - a.Month;
        var days = b.Day - a.Day;
        if (days < 0) { months--; days += DateTime.DaysInMonth(b.AddMonths(-1).Year, b.AddMonths(-1).Month); }
        if (months < 0) { years--; months += 12; }
        var parts = new List<string>();
        if (years > 0) parts.Add(years == 1 ? "1 year" : $"{years} years");
        if (months > 0) parts.Add(months == 1 ? "1 month" : $"{months} months");
        if (days > 0 || parts.Count == 0) parts.Add(days == 1 ? "1 day" : $"{days} days");
        return string.Join(", ", parts) + $" ({(b - a).TotalDays:N0} days)";
    }

    /// <summary>
    /// When someone was born, given their age on a date: aged 34 on 6 June 1841 means born between 7 June 1806 and
    /// 6 June 1807. With months and days too, the range narrows to one day.
    /// </summary>
    public static (DateTime From, DateTime To) BornFromAge(DateTime on, int years, int months = 0, int days = 0)
    {
        var latest = on.AddYears(-years).AddMonths(-months).AddDays(-days);
        if (months > 0 || days > 0)
        {
            // An age given to the day pins the birth to that day; to the month, within a month.
            var earliestByMonth = days > 0 ? latest : latest.AddMonths(-1).AddDays(1);
            return (earliestByMonth, latest);
        }
        return (latest.AddYears(-1).AddDays(1), latest);
    }

    /// <summary>A whole-day date from a <see cref="GenDate"/> (Gregorian), or null when it isn't a full date.</summary>
    public static DateTime? ToDateTime(GenDate d)
    {
        if (d.Kind == DateKind.Text || d.Year < 1 || d.Month is < 1 or > 12 || d.Day < 1) return null;
        try { return new DateTime(d.NewStyleYear, d.Month, d.Day); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static string Format(DateTime d) => d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
    public static string Weekday(DateTime d) => d.DayOfWeek.ToString();

    // ---- Julian and Gregorian ----------------------------------------------------------------------------

    private static readonly JulianCalendar Julian = new();

    /// <summary>A date written in the Julian (Old Style) calendar, as the Gregorian (New Style) date.</summary>
    public static DateTime? FromJulian(int year, int month, int day)
    {
        try { return Julian.ToDateTime(year, month, day, 0, 0, 0, 0); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>A Gregorian date as it was written in the Julian calendar: (year, month, day).</summary>
    public static (int Year, int Month, int Day) ToJulian(DateTime d) => (Julian.GetYear(d), Julian.GetMonth(d), Julian.GetDayOfMonth(d));

    /// <summary>
    /// "14 March 1741/42": a Julian date under the English Old Style, when the year number changed on 25 March, so
    /// January to 24 March carry both years. Britain and its colonies changed calendars in 1752; most of Catholic
    /// Europe in 1582.
    /// </summary>
    public static string OldStyle(int julianYear, int month, int day)
    {
        var name = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
        var dual = month < 3 || month == 3 && day < 25;
        return dual ? $"{day} {name} {julianYear - 1}/{(julianYear % 100):00}" : $"{day} {name} {julianYear}";
    }

    // ---- Quaker dates ------------------------------------------------------------------------------------

    /// <summary>
    /// A Quaker date ("5th day of 3rd month 1745") as a calendar date. Before 1752 the year began in March, so the
    /// 1st month was March and the 11th and 12th were January and February; from 1752 the 1st month is January.
    /// Before 1752 the result is Julian (Old Style); the Gregorian date is given too.
    /// </summary>
    public static (string Written, DateTime? Gregorian)? Quaker(int day, int month, int year)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;
        if (year >= 1752 && !(year == 1752 && month < 3))
        {
            try
            {
                var d = new DateTime(year, month, day);
                return (Format(d), d);
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        // Old Style: month 1 = March … 10 = December, 11 = January, 12 = February of the next calendar year.
        var realMonth = (month + 1) % 12 + 1;
        var calendarYear = month >= 11 ? year + 1 : year;
        var gregorian = FromJulian(calendarYear, realMonth, day);
        if (gregorian == null) return null;
        var name = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(realMonth);
        var written = month >= 11 ? $"{day} {name} {year}/{(calendarYear % 100):00}" : $"{day} {name} {year}";
        return (written, gregorian);
    }

    // ---- Hebrew ------------------------------------------------------------------------------------------

    private static readonly HebrewCalendar Hebrew = new();
    private static readonly string[] HebrewMonths = { "Tishrei", "Cheshvan", "Kislev", "Tevet", "Shevat", "Adar", "Nisan", "Iyar", "Sivan", "Tammuz", "Av", "Elul" };
    private static readonly string[] HebrewLeapMonths = { "Tishrei", "Cheshvan", "Kislev", "Tevet", "Shevat", "Adar I", "Adar II", "Nisan", "Iyar", "Sivan", "Tammuz", "Av", "Elul" };

    /// <summary>"14 Adar 5602" for a Gregorian date between 1583 and 2239.</summary>
    public static string? ToHebrew(DateTime d)
    {
        if (d < Hebrew.MinSupportedDateTime || d > Hebrew.MaxSupportedDateTime) return null;
        var year = Hebrew.GetYear(d);
        var names = Hebrew.IsLeapYear(year) ? HebrewLeapMonths : HebrewMonths;
        return $"{Hebrew.GetDayOfMonth(d)} {names[Hebrew.GetMonth(d) - 1]} {year}";
    }

    /// <summary>The month names of a Hebrew year, in order (13 in a leap year).</summary>
    public static IReadOnlyList<string> HebrewMonthNames(int year) => Hebrew.IsLeapYear(year) ? HebrewLeapMonths : HebrewMonths;

    /// <summary>A Hebrew date (month by its position in <see cref="HebrewMonthNames"/>) as a Gregorian date.</summary>
    public static DateTime? FromHebrew(int year, int month, int day)
    {
        try { return Hebrew.ToDateTime(year, month, day, 0, 0, 0, 0); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // ---- French Republican ---------------------------------------------------------------------------------

    public static readonly string[] RepublicanMonths =
        { "Vendémiaire", "Brumaire", "Frimaire", "Nivôse", "Pluviôse", "Ventôse", "Germinal", "Floréal", "Prairial", "Messidor", "Thermidor", "Fructidor", "jours complémentaires" };

    /// <summary>The first day of each Republican year (An I to An XIV), as the calendar was actually kept.</summary>
    private static readonly DateTime[] RepublicanYearStarts =
    {
        new(1792, 9, 22), new(1793, 9, 22), new(1794, 9, 22), new(1795, 9, 23), new(1796, 9, 22), new(1797, 9, 22), new(1798, 9, 22),
        new(1799, 9, 23), new(1800, 9, 23), new(1801, 9, 23), new(1802, 9, 23), new(1803, 9, 24), new(1804, 9, 23), new(1805, 9, 23),
    };
    private static readonly DateTime RepublicanEnd = new(1805, 12, 31);   // the calendar was dropped on 1 January 1806

    /// <summary>A French Republican date ("14 Germinal An XI": year 11, month 7) as the Gregorian date; month 13 is the extra days.</summary>
    public static DateTime? FromRepublican(int year, int month, int day)
    {
        if (year < 1 || year > RepublicanYearStarts.Length || month is < 1 or > 13 || day < 1 || day > (month == 13 ? 6 : 30)) return null;
        var d = RepublicanYearStarts[year - 1].AddDays((month - 1) * 30 + day - 1);
        var nextStart = year < RepublicanYearStarts.Length ? RepublicanYearStarts[year] : RepublicanEnd.AddDays(1);
        return d < nextStart && d <= RepublicanEnd ? d : null;
    }

    /// <summary>"14 Germinal An XI" for a date between 22 September 1792 and 31 December 1805.</summary>
    public static string? ToRepublican(DateTime d)
    {
        if (d < RepublicanYearStarts[0] || d > RepublicanEnd) return null;
        var year = Array.FindLastIndex(RepublicanYearStarts, s => s <= d) + 1;
        var offset = (d - RepublicanYearStarts[year - 1]).Days;
        var month = offset / 30 + 1;
        var day = offset % 30 + 1;
        return $"{day} {RepublicanMonths[month - 1]} An {Roman(year)}";
    }

    private static string Roman(int n) => Lineage.Core.Reports.ReportText.Roman(n).ToUpperInvariant();
}
