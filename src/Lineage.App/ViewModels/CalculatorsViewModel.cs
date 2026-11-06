using CommunityToolkit.Mvvm.ComponentModel;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.ViewModels;

/// <summary>
/// The calculators window: Soundex codes (and the tree's surnames that share one), the time between two dates, a
/// birth date from an age, and conversions between calendars. Everything recalculates as you type.
/// </summary>
public sealed partial class CalculatorsViewModel : ObservableObject
{
    private readonly Dictionary<string, List<string>> _bySoundex;

    public CalculatorsViewModel(FamilyTree tree)
    {
        _bySoundex = tree.People.Values.Select(p => p.PrimaryName.Surname.Trim()).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(Duplicates.Soundex).ToDictionary(g => g.Key, g => g.OrderBy(s => s).ToList());
        Recalculate();
    }

    // ---- Soundex ----
    [ObservableProperty] private string _soundexName = "";
    [ObservableProperty] private string _soundexCode = "";
    [ObservableProperty] private string _soundexMatches = "";
    partial void OnSoundexNameChanged(string value) => Recalculate();

    // ---- between two dates ----
    [ObservableProperty] private string _fromDate = "";
    [ObservableProperty] private string _toDate = "";
    [ObservableProperty] private string _betweenText = "";
    partial void OnFromDateChanged(string value) => Recalculate();
    partial void OnToDateChanged(string value) => Recalculate();

    // ---- birth from age ----
    [ObservableProperty] private string _ageOnDate = "";
    [ObservableProperty] private string _ageYears = "";
    [ObservableProperty] private string _ageMonths = "";
    [ObservableProperty] private string _ageDays = "";
    [ObservableProperty] private string _bornText = "";
    partial void OnAgeOnDateChanged(string value) => Recalculate();
    partial void OnAgeYearsChanged(string value) => Recalculate();
    partial void OnAgeMonthsChanged(string value) => Recalculate();
    partial void OnAgeDaysChanged(string value) => Recalculate();

    // ---- calendars ----
    public string[] CalendarOptions { get; } =
        { "Gregorian (New Style)", "Julian (Old Style)", "Quaker (numbered months)", "Hebrew", "French Republican" };
    [ObservableProperty] private int _calendarIndex;
    [ObservableProperty] private string _calDay = "";
    [ObservableProperty] private string _calMonth = "";
    [ObservableProperty] private string _calYear = "";
    [ObservableProperty] private string _calendarHint = "";
    [ObservableProperty] private string _calendarResult = "";
    partial void OnCalendarIndexChanged(int value) => Recalculate();
    partial void OnCalDayChanged(string value) => Recalculate();
    partial void OnCalMonthChanged(string value) => Recalculate();
    partial void OnCalYearChanged(string value) => Recalculate();

    private static int Num(string s) => int.TryParse(s.Trim(), out var n) ? n : 0;

    private void Recalculate()
    {
        // Soundex
        var name = SoundexName.Trim();
        SoundexCode = name.Length > 0 ? Duplicates.Soundex(name) : "";
        SoundexMatches = SoundexCode.Length == 0 ? ""
            : _bySoundex.TryGetValue(SoundexCode, out var same) ? "Surnames in the tree with this code: " + string.Join(", ", same)
            : "No surname in the tree has this code.";

        // Between
        var from = Calculators.ToDateTime(GenDate.Parse(FromDate));
        var to = ToDate.Trim().Length == 0 ? DateTime.Today : Calculators.ToDateTime(GenDate.Parse(ToDate));
        BetweenText = from is { } a && to is { } b
            ? $"{Calculators.Between(a, b)}\n{Calculators.Format(a)} was a {Calculators.Weekday(a)}; {Calculators.Format(b)} was a {Calculators.Weekday(b)}."
            : FromDate.Trim().Length > 0 ? "Type full dates, like 14 Mar 1842." : "";

        // Born from age
        var on = Calculators.ToDateTime(GenDate.Parse(AgeOnDate));
        if (on is { } when && (Num(AgeYears) > 0 || Num(AgeMonths) > 0 || Num(AgeDays) > 0))
        {
            var (earliest, latest) = Calculators.BornFromAge(when, Num(AgeYears), Num(AgeMonths), Num(AgeDays));
            BornText = earliest == latest ? $"Born on {Calculators.Format(latest)}."
                : $"Born between {Calculators.Format(earliest)} and {Calculators.Format(latest)}.";
        }
        else BornText = AgeOnDate.Trim().Length > 0 && on == null ? "Type the full date of the record, like 6 Jun 1841." : "";

        // Calendars
        CalendarHint = CalendarIndex switch
        {
            1 => "The date as written under the Julian calendar (Britain and its colonies until 1752, Russia until 1918).",
            2 => "Quakers numbered the months. Before 1752 the 1st month was March; from 1752 it is January.",
            3 => "Months: 1 Tishrei, 2 Cheshvan, 3 Kislev, 4 Tevet, 5 Shevat, 6 Adar (Adar I), then in a leap year 7 Adar II; then Nisan, Iyar, Sivan, Tammuz, Av, Elul.",
            4 => "Year as a number (An XI = 11). Months: 1 Vendémiaire … 12 Fructidor, 13 for the extra days. Used in France 1793–1805.",
            _ => "Day, month (1–12) and year.",
        };
        CalendarResult = Convert(Num(CalDay), Num(CalMonth), Num(CalYear));
    }

    private string Convert(int day, int month, int year)
    {
        if (day == 0 || month == 0 || year == 0) return "";
        string? written = null;
        DateTime? gregorian;
        switch (CalendarIndex)
        {
            case 1: gregorian = Calculators.FromJulian(year, month, day); break;
            case 2:
                var q = Calculators.Quaker(day, month, year);
                written = q?.Written;
                gregorian = q?.Gregorian;
                break;
            case 3: gregorian = Calculators.FromHebrew(year, month, day); break;
            case 4: gregorian = Calculators.FromRepublican(year, month, day); break;
            default:
                try { gregorian = new DateTime(year, month, day); }
                catch (ArgumentOutOfRangeException) { gregorian = null; }
                break;
        }
        if (gregorian is not { } g) return "That date doesn't exist in this calendar (or is outside the years it covers).";
        var (jy, jm, jd) = Calculators.ToJulian(g);
        var lines = new List<string>();
        if (written != null) lines.Add($"Written: {written}");
        lines.Add($"Gregorian: {Calculators.Format(g)} ({Calculators.Weekday(g)})");
        lines.Add($"Julian (Old Style): {Calculators.OldStyle(jy, jm, jd)}");
        if (Calculators.ToHebrew(g) is { } h) lines.Add($"Hebrew: {h}");
        if (Calculators.ToRepublican(g) is { } r) lines.Add($"French Republican: {r}");
        return string.Join("\n", lines);
    }
}
