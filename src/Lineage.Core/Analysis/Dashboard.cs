using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

public enum AnniversaryKind { Birth, Marriage, Death }

/// <summary>
/// A birth, marriage or death whose day comes round soon. <see cref="On"/> is the coming date, <see cref="Year"/>
/// the year it happened and <see cref="YearsAgo"/> how many years it will be on that day. For a marriage
/// <see cref="PersonId"/> is the first partner and <see cref="SpouseId"/> the other.
/// </summary>
public sealed record Anniversary(AnniversaryKind Kind, long PersonId, long? FamilyId, long? SpouseId, DateOnly On, int Year, int YearsAgo);

/// <summary>People with gaps worth filling in, as id lists (in id order) with their counts.</summary>
public sealed record MissingDataReport(
    IReadOnlyList<long> NoBirthDate,
    IReadOnlyList<long> NoCitations,
    IReadOnlyList<long> NoParents,
    IReadOnlyList<long> EventsWithoutPlace)
{
    public int NoBirthDateCount => NoBirthDate.Count;
    public int NoCitationsCount => NoCitations.Count;
    public int NoParentsCount => NoParents.Count;
    public int EventsWithoutPlaceCount => EventsWithoutPlace.Count;
}

public sealed record SurnameCount(string Surname, int Count);

/// <summary>
/// Headline numbers for a tree. <see cref="Surnames"/> counts distinct surnames of people's main names (ignoring
/// case); <see cref="AverageLifespan"/> is in years, over people with both a birth and a death year.
/// </summary>
public sealed record TreeStats(
    int People,
    int Families,
    int Surnames,
    int? EarliestBirthYear,
    double? AverageLifespan,
    IReadOnlyList<SurnameCount> TopSurnames);

/// <summary>
/// Numbers for the home screen: anniversaries coming up, what's missing, and a summary of the tree.
/// Each is one pass over the tree, so they can be recomputed whenever the home screen is shown.
/// </summary>
public static class Dashboard
{
    /// <summary>Facts (not events) that have no place of their own, so a blank place isn't a gap.</summary>
    private static readonly HashSet<string> PlacelessFacts = new()
    {
        "OCCU", "RELI", "NATI", "CAST", "NCHI", "NMR", "EDUC", "PROP", "TITL", "DSCR", "IDNO", "SSN",
    };

    /// <summary>
    /// Births, marriages and deaths with a known day and month that fall from <paramref name="from"/> for
    /// <paramref name="days"/> days (running on into the next year if needed), soonest first. Approximate dates
    /// are left out, and someone born on 29 February is remembered on the 28th in other years.
    /// </summary>
    public static List<Anniversary> Anniversaries(FamilyTree tree, DateOnly from, int days)
    {
        var list = new List<Anniversary>();
        days = Math.Min(days, DateOnly.MaxValue.DayNumber - from.DayNumber);
        if (days <= 0) return list;
        var end = from.AddDays(days);   // exclusive

        void Try(AnniversaryKind kind, Event? e, long personId, long? familyId, long? spouseId)
        {
            if (e?.Date is not { Kind: DateKind.Simple, Qualifier: DateQualifier.None, Year: > 0, Month: >= 1 and <= 12, Day: >= 1 } d) return;
            if (d.Day > DateTime.DaysInMonth(2000, d.Month)) return;   // no such day (31 April)
            for (var y = Math.Max(from.Year, d.Year + 1); y <= end.Year; y++)
            {
                var on = new DateOnly(y, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(y, d.Month)));
                if (on >= from && on < end) list.Add(new Anniversary(kind, personId, familyId, spouseId, on, d.Year, y - d.Year));
            }
        }

        foreach (var p in tree.People.Values)
        {
            Try(AnniversaryKind.Birth, p.Find("BIRT"), p.Id, null, null);
            Try(AnniversaryKind.Death, p.Find("DEAT"), p.Id, null, null);
        }
        foreach (var f in tree.Families.Values)
        {
            var first = f.Partner1Id ?? f.Partner2Id;
            if (first is { } id) Try(AnniversaryKind.Marriage, f.Marriage, id, f.Id, f.Partner1Id.HasValue ? f.Partner2Id : null);
        }
        return list.OrderBy(a => a.On).ThenBy(a => a.Kind).ThenBy(a => a.Year).ThenBy(a => a.PersonId).ToList();
    }

    /// <summary>
    /// Who has no birth date (birth, christening or baptism), no source cited (on them or any of their events),
    /// no parents recorded, or an event with no place.
    /// </summary>
    public static MissingDataReport MissingData(FamilyTree tree)
    {
        var noBirth = new List<long>();
        var noCitations = new List<long>();
        var noParents = new List<long>();
        var noPlace = new List<long>();
        foreach (var p in tree.People.Values.OrderBy(p => p.Id))
        {
            if (!p.Events.Any(e => e.Tag is "BIRT" or "CHR" or "BAPM" && !e.Date.IsEmpty)) noBirth.Add(p.Id);
            if (p.Citations.Count == 0 && p.Events.All(e => e.Citations.Count == 0)) noCitations.Add(p.Id);
            if (!p.ParentFamilyIds.Any(id => tree.Family(id) is { } f && f.PartnerIds.Any(tree.People.ContainsKey))) noParents.Add(p.Id);
            if (p.Events.Any(e => NeedsPlace(e) && string.IsNullOrWhiteSpace(e.Place))) noPlace.Add(p.Id);
        }
        return new MissingDataReport(noBirth, noCitations, noParents, noPlace);
    }

    private static bool NeedsPlace(Event e) => !PlacelessFacts.Contains(e.Tag) && !EventTypes.IsLds(e.Tag);

    /// <summary>
    /// Counts, the earliest birth year, the average lifespan (ignoring impossible spans over 150 years, which the
    /// problem finder reports) and the ten commonest surnames.
    /// </summary>
    public static TreeStats Stats(FamilyTree tree)
    {
        var surnames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int? earliest = null;
        long years = 0;
        var lives = 0;
        foreach (var p in tree.People.Values)
        {
            if (p.Names.Count > 0 && p.Names[0].Surname.Trim() is { Length: > 0 } surname)
                surnames[surname] = surnames.GetValueOrDefault(surname) + 1;
            var birth = VitalYears.Birth(p);
            var death = VitalYears.Death(p);
            if (birth > 0 && (earliest is null || birth < earliest)) earliest = birth;
            if (birth > 0 && death >= birth && death - birth <= 150)
            {
                years += death - birth;
                lives++;
            }
        }
        var top = surnames.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(10).Select(kv => new SurnameCount(kv.Key, kv.Value)).ToList();
        return new TreeStats(tree.People.Count, tree.Families.Count, surnames.Count, earliest,
            lives > 0 ? Math.Round((double)years / lives, 1) : null, top);
    }
}
