using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.History;

public enum TimelineKind { Life, Family, History }

/// <summary>
/// One line of a life timeline. <see cref="Event"/> and <see cref="Owner"/> are set for the person's
/// own events (and their marriages), <see cref="Relative"/> for family events, <see cref="History"/>
/// for historical ones.
/// </summary>
public sealed record TimelineEntry(TimelineKind Kind, int Year, int SortKey, string Title, string When, string Place,
                                   string Detail, string Age)
{
    public Event? Event { get; init; }
    public Entity? Owner { get; init; }
    public Person? Relative { get; init; }
    public HistoricalEvent? History { get; init; }
}

/// <summary>A person's timeline, plus how many of their events have no year and so aren't on it.</summary>
public sealed record Timeline(IReadOnlyList<TimelineEntry> Entries, int Undated, Lifespan Span);

/// <summary>
/// When someone lived. <see cref="Born"/> is 0 when it can't even be estimated; an estimate comes from
/// their first marriage or first child. <see cref="LastSeen"/> is the latest year anything puts them
/// alive. <see cref="End"/> is the last year worth showing: the death, or for someone whose death isn't
/// recorded, a normal lifetime (and never after this year).
/// </summary>
public sealed record Lifespan(int Born, bool BornEstimated, int Died, int LastSeen)
{
    public bool DeathKnown => Died > 0;
    public int End => Died > 0 ? Died
        : Born > 0 ? Math.Min(DateTime.Today.Year, Math.Max(LastSeen, Born + 75))
        : LastSeen;
}

public enum CensusStatus { Found, ToFind, Possible, Lost }

/// <summary>
/// One census the person should be in. <see cref="Place"/> is where they probably were (for a new
/// census event); <see cref="Record"/> is the event that records it, when found.
/// </summary>
public sealed record CensusCheck(CensusRecord Census, CensusStatus Status, string Reason, string Place)
{
    public Event? Record { get; init; }
}

/// <summary>
/// Builds a person's life timeline — their own events, what happened in their family, and history
/// where they lived — and the census checklist: every surviving census taken while they were alive,
/// in the country where they were living, and whether it has been found.
/// </summary>
public static class LifeTimeline
{
    private const int TypicalMarriageAge = 25, TypicalAgeAtFirstChild = 28;

    // ---- Lifespan ----

    public static Lifespan Span(FamilyTree tree, Person p)
    {
        var born = VitalYears.Birth(p);
        var estimated = false;
        if (born == 0)
        {
            var guesses = new List<int>();
            foreach (var (f, _, children) in tree.FamiliesOf(p))
            {
                if (Year(f.Marriage) is > 0 and var m) guesses.Add(m - TypicalMarriageAge);
                foreach (var c in children)
                    if (VitalYears.Birth(c) is > 0 and var cb) guesses.Add(cb - TypicalAgeAtFirstChild);
            }
            if (guesses.Count > 0) { born = guesses.Min(); estimated = true; }
        }

        var died = VitalYears.Death(p);
        var last = Math.Max(born, died);
        foreach (var s in Sightings(tree, p)) last = Math.Max(last, s.Year);
        return new Lifespan(born, estimated, died, last);
    }

    // ---- Timeline ----

    public static Timeline Build(FamilyTree tree, Person p, bool family = true, bool history = true)
    {
        var span = Span(tree, p);
        var birth = p.Birth;
        var entries = new List<TimelineEntry>();
        var undated = 0;

        void Own(Event e, Entity owner, string title)
        {
            if (!Dated(e.Date)) { undated++; return; }
            var place = e.Place2.Length > 0 ? $"{e.Place} → {e.Place2}" : e.Place;
            var age = ReferenceEquals(e, birth) ? "" : AgeAt(birth, span, e.Date);
            entries.Add(new TimelineEntry(TimelineKind.Life, e.Date.Year, e.Date.SortKey, title, Reports.ReportText.Date(e.Date),
                                          place, Detail(e), age) { Event = e, Owner = owner });
        }

        foreach (var e in p.Events) Own(e, p, Title(e));
        foreach (var (f, spouse, children) in tree.FamiliesOf(p))
        {
            foreach (var e in f.Events)
                Own(e, f, spouse == null ? EventTypes.Label(e) : e.Tag switch
                {
                    "MARR" => $"Marriage to {spouse.DisplayName}",
                    "DIV" => $"Divorce from {spouse.DisplayName}",
                    "ENGA" => $"Engagement to {spouse.DisplayName}",
                    _ => $"{EventTypes.Label(e)} (with {spouse.DisplayName})",
                });
            if (!family) continue;
            if (spouse != null)
                Relative(entries, p, birth, span, spouse.Death, spouse, $"Death of {Role(spouse, "husband", "wife", "spouse")}, {spouse.DisplayName}");
            foreach (var c in children)
            {
                var role = Role(c, "son", "daughter", "child");
                Relative(entries, p, birth, span, c.Birth, c, $"Birth of {role}, {c.DisplayName}");
                foreach (var (cf, childSpouse, _) in tree.FamiliesOf(c))
                    if (cf.Marriage is { } m)
                        Relative(entries, p, birth, span, m, c, childSpouse == null
                            ? $"Marriage of {role}, {c.DisplayName}"
                            : $"Marriage of {role}, {c.DisplayName}, to {childSpouse.DisplayName}");
                Relative(entries, p, birth, span, c.Death, c, $"Death of {role}, {c.DisplayName}");
            }
        }
        if (family)
            foreach (var parent in tree.ParentsOf(p))
                Relative(entries, p, birth, span, parent.Death, parent, $"Death of {Role(parent, "father", "mother", "parent")}, {parent.DisplayName}");

        if (history && span.Born > 0)
        {
            var sightings = Sightings(tree, p);
            // Something already going on when they were born goes just after the birth.
            var bornKey = birth != null && Dated(birth.Date) && birth.Date.Year == span.Born ? birth.Date.SortKey : span.Born * 10000;
            foreach (var h in HistoricalEvents.All)
            {
                if (h.To < span.Born || h.From > span.End) continue;
                if (!h.Everywhere && !RegionsBetween(sightings, p, h.From, h.To).Any(h.AppliesTo)) continue;
                var (year, key, age) = h.From > span.Born
                    ? (h.From, h.From * 10000, AgeText(h.From - span.Born, approximate: true))
                    : (span.Born, bornKey, "");
                entries.Add(new TimelineEntry(TimelineKind.History, year, key, h.Title, h.Years, "", h.Detail, age) { History = h });
            }
        }

        entries.Sort((a, b) => a.SortKey != b.SortKey ? a.SortKey.CompareTo(b.SortKey) : a.Kind.CompareTo(b.Kind));
        return new Timeline(entries, undated, span);
    }

    /// <summary>Adds a relative's event if it happened during the person's life.</summary>
    private static void Relative(List<TimelineEntry> entries, Person p, Event? birth, Lifespan span, Event? e, Person relative, string title)
    {
        if (e == null || !Dated(e.Date)) return;
        var y = e.Date.Year;
        if (span.Born > 0 && y < span.Born) return;
        if (span.DeathKnown && y > span.Died) return;
        if (span.Born == 0 && !span.DeathKnown) return;   // no way to tell whether they were alive
        entries.Add(new TimelineEntry(TimelineKind.Family, y, e.Date.SortKey, title, Reports.ReportText.Date(e.Date),
                                      e.Place, "", AgeAt(birth, span, e.Date)) { Event = e, Relative = relative });
    }

    private static string Title(Event e) =>
        e.Tag is "OCCU" or "EDUC" or "RELI" or "NATI" or "PROP" or "TITL" && e.Value.Length > 0 && e.Value != "Y"
            ? $"{EventTypes.Label(e)}: {e.Value}"
            : EventTypes.Label(e);

    private static string Detail(Event e)
    {
        var parts = new List<string>();
        if (e.Value.Length > 0 && e.Value != "Y" && e.Tag is not ("OCCU" or "EDUC" or "RELI" or "NATI" or "PROP" or "TITL")) parts.Add(e.Value);
        if (e.Description.Length > 0) parts.Add(e.Description);
        if (e.Cause.Length > 0) parts.Add("Cause: " + e.Cause);
        return string.Join("  ·  ", parts);
    }

    private static string Role(Person p, string male, string female, string other) =>
        p.Sex switch { Sex.Male => male, Sex.Female => female, _ => other };

    private static bool Dated(GenDate d) => d.Kind != DateKind.Text && d.Year > 0;

    private static int Year(Event? e) => e != null && Dated(e.Date) ? e.Date.Year : 0;

    /// <summary>"age 23", "age about 23", "under 1", or "" when it can't be worked out.</summary>
    private static string AgeAt(Event? birth, Lifespan span, GenDate when)
    {
        if (!Dated(when) || span.Born == 0) return "";
        var b = birth?.Date;
        if (b == null || !Dated(b) || span.BornEstimated)
            return AgeText(when.Year - span.Born, approximate: true);
        var years = when.Year - b.Year;
        var exact = b.Month > 0 && when.Month > 0 && b.Qualifier == DateQualifier.None && when.Qualifier == DateQualifier.None
                    && b.Kind == DateKind.Simple && when.Kind == DateKind.Simple && birth!.Tag == "BIRT";
        if (b.Month > 0 && when.Month > 0 && (when.Month < b.Month || (when.Month == b.Month && b.Day > 0 && when.Day > 0 && when.Day < b.Day)))
            years--;
        if (b.Month > 0 && when.Month == b.Month && (b.Day == 0 || when.Day == 0)) exact = false;
        return AgeText(years, approximate: !exact);
    }

    private static string AgeText(int years, bool approximate) => years switch
    {
        < 0 or > 125 => "",
        0 => approximate ? "" : "under 1",
        _ => approximate ? $"age about {years}" : $"age {years}",
    };

    // ---- Where they lived ----

    /// <summary>A dated place where the person was: their own events, their marriages, and their children's births.</summary>
    private readonly record struct Sighting(int Year, int Key, string Place);

    private static List<Sighting> Sightings(FamilyTree tree, Person p)
    {
        var list = new List<Sighting>();
        void Add(Event? e)
        {
            if (e == null || !Dated(e.Date)) return;
            if (e.Place.Trim().Length > 0) list.Add(new Sighting(e.Date.Year, e.Date.SortKey, e.Place));
            if (e.Place2.Trim().Length > 0 && e.Date.End is { } end && Dated(end))
                list.Add(new Sighting(end.Year, end.SortKey, e.Place2));
        }
        foreach (var e in p.Events) Add(e);
        foreach (var (f, _, children) in tree.FamiliesOf(p))
        {
            foreach (var e in f.Events) Add(e);
            foreach (var c in children) Add(c.Birth);
        }
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    /// <summary>The places just before and just after a date (one of each), or every undated place when nothing is dated.</summary>
    private static List<string> PlacesAround(List<Sighting> sightings, Person p, int key)
    {
        if (sightings.Count == 0)
            return p.Events.Select(e => e.Place).Where(pl => pl.Trim().Length > 0).Distinct().ToList();
        Sighting? before = null, after = null;
        foreach (var s in sightings)
        {
            if (s.Key <= key) before = s;
            else { after = s; break; }
        }
        var result = new List<string>();
        if (before is { } b) result.Add(b.Place);
        if (after is { } a && (before == null || a.Place != before.Value.Place)) result.Add(a.Place);
        return result;
    }

    private static IEnumerable<string> RegionsBetween(List<Sighting> sightings, Person p, int from, int to)
    {
        var places = PlacesAround(sightings, p, from * 10000 + 101)
            .Concat(PlacesAround(sightings, p, to * 10000 + 1231))
            .Concat(sightings.Where(s => s.Year >= from && s.Year <= to).Select(s => s.Place));
        return places.Select(Regions.Of).OfType<string>().Distinct();
    }

    // ---- Census checklist ----

    public static List<CensusCheck> CensusChecklist(FamilyTree tree, Person p)
    {
        var result = new List<CensusCheck>();
        var span = Span(tree, p);
        if (span.Born == 0) return result;

        var birth = p.Birth;
        var bornKey = birth != null && Dated(birth.Date) && !span.BornEstimated && birth.Date.Kind == DateKind.Simple
                      && birth.Date.Qualifier == DateQualifier.None && birth.Date.Month > 0
            ? birth.Date.SortKey
            : span.Born * 10000 + 101;
        var death = p.Death;
        var diedKey = !span.DeathKnown ? int.MaxValue
            : death != null && Dated(death.Date) && death.Date.Year == span.Died && death.Date.Kind == DateKind.Simple
              && death.Date.Qualifier == DateQualifier.None && death.Date.Month > 0
                ? death.Date.SortKey
                : span.Died * 10000 + 1231;
        var sightings = Sightings(tree, p);

        foreach (var census in Censuses.All.OrderBy(c => c.DayKey))
        {
            var key = census.DayKey;
            if (key < bornKey || key > diedKey) continue;
            if (!span.DeathKnown && census.TakenYear > span.End) continue;

            var places = PlacesAround(sightings, p, key);
            var place = places.FirstOrDefault(census.Covers);
            if (place == null) continue;

            var record = FindRecord(tree, p, census);
            var moved = places.Select(Regions.Of).OfType<string>().Distinct().Count() > 1;
            var maybeDead = !span.DeathKnown && census.TakenYear > span.LastSeen;
            var estimatedBirth = span.BornEstimated && census.TakenYear < span.Born + 5;

            var (status, reason) =
                record != null ? (CensusStatus.Found, $"Recorded: {Reports.ReportText.Date(record.Date)}{(record.Place.Length > 0 ? ", " + record.Place : "")}")
                : !census.Survives ? (CensusStatus.Lost, census.Note)
                : maybeDead ? (CensusStatus.Possible, $"No record of them after {span.LastSeen} — look if they were still living.")
                : moved ? (CensusStatus.Possible, $"They moved around this time, so they may be in another country's census instead.")
                : estimatedBirth ? (CensusStatus.Possible, $"Their birth year is only estimated (about {span.Born}).")
                : (CensusStatus.ToFind, $"Living in {place}" + (NearestYear(sightings, key) is > 0 and var y ? $" ({y})" : "") + ".");
            result.Add(new CensusCheck(census, status, reason, place) { Record = record });
        }
        return result;
    }

    private static int NearestYear(List<Sighting> sightings, int key) =>
        sightings.Count == 0 ? 0 : sightings.MinBy(s => Math.Abs(s.Key - key)).Year;

    /// <summary>
    /// The event that records the person in this census: a census event in the census year (or the year it
    /// was taken), or any event that year with a citation to a source whose title mentions a census or register.
    /// </summary>
    public static Event? FindRecord(FamilyTree tree, Person p, CensusRecord census)
    {
        var events = p.Events.Concat(tree.FamiliesOf(p).SelectMany(x => x.Family.Events));
        Event? cited = null;
        foreach (var e in events)
        {
            if (!Dated(e.Date) || (e.Date.Year != census.Year && e.Date.Year != census.TakenYear)) continue;
            if (e.Tag == "CENS") return e;
            if (cited == null && e.Citations.Any(c => tree.Sources.TryGetValue(c.SourceId, out var s) && IsCensusSource(s)))
                cited = e;
        }
        return cited;
    }

    private static bool IsCensusSource(Source s) =>
        s.Title.Contains("census", StringComparison.OrdinalIgnoreCase)
        || s.Title.Contains("1939 register", StringComparison.OrdinalIgnoreCase);
}
