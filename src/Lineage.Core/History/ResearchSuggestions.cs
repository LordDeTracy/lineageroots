using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.History;

public enum HintKind { Parents, Birth, Marriage, Death, Census, Migration, Military, Probate, Children, Sources }

/// <summary>
/// A suggested next step. <see cref="Key"/> identifies it for this person (so it can be set aside and stays
/// set aside); <see cref="Task"/> is worded as a research task; <see cref="Why"/> says what prompted it and
/// <see cref="Where"/> where such records are usually found. Priority 1 is most useful.
/// </summary>
public sealed record ResearchHint(string Key, HintKind Kind, int Priority, string Task, string Why, string Where, int Year)
{
    /// <summary>The research to-do category for a task made from this hint.</summary>
    public string Category => Kind switch
    {
        HintKind.Census => "Census",
        HintKind.Migration => "Immigration",
        HintKind.Military => "Military",
        HintKind.Probate => "Probate",
        HintKind.Sources => "Sources",
        _ => "Vital records",
    };
}

/// <summary>
/// Works out what to look for next for a person, from what is (and isn't) in the tree: missing vital records,
/// parents, a marriage, censuses they should be in, emigration, military service, probate, gaps between
/// children and facts without sources. Everything is rule-based and offline; each hint says why it was made
/// and where such records usually are for that country and period.
/// </summary>
public static class ResearchSuggestions
{
    public static List<ResearchHint> For(FamilyTree tree, Person p, int? currentYear = null)
    {
        var now = currentYear ?? DateTime.Today.Year;
        var hints = new List<ResearchHint>();
        var name = p.DisplayName;
        var span = LifeTimeline.Span(tree, p);
        var birth = Vital(p, "BIRT", "CHR", "BAPM");
        var death = Vital(p, "DEAT", "BURI", "CREM");
        var homeRegion = FirstRegion(p.Events.Where(e => e.Tag is "BIRT" or "CHR" or "BAPM"))
                         ?? FirstRegion(p.Events.OrderBy(e => e.Date.SortKey));
        var lastRegion = FirstRegion(p.Events.Where(e => e.Tag is "DEAT" or "BURI" or "CREM"))
                         ?? FirstRegion(p.Events.Where(e => e.Date.Year > 0).OrderByDescending(e => e.Date.SortKey));
        var old = span.Born > 0 && now - span.Born > 100;
        var dead = span.DeathKnown || old;

        // ---- parents ----
        if (p.ParentFamilyIds.Count == 0)
            hints.Add(new ResearchHint("parents", HintKind.Parents, 1, $"Find {name}'s parents",
                "No parents are recorded.",
                Join("A birth or baptism record usually names them, and so may a marriage record or death certificate",
                     homeRegion == Regions.UnitedStates && span.Born is > 0 and < 1950 ? "the 1880 and later US censuses list each person's relationship to the head of the household" : "",
                     homeRegion is Regions.England or Regions.Wales or Regions.Scotland && span.Born is > 0 and < 1921 ? "censuses from 1851 give relationships" : ""),
                span.Born));

        // ---- birth ----
        if (birth == null && dead)
            hints.Add(new ResearchHint("birth", HintKind.Birth, 1, $"Find {name}'s birth or baptism record",
                span.Born > 0 ? $"No birth date is recorded; they were born about {span.Born}." : "No birth date is recorded.",
                Records(homeRegion, span.Born, "birth"), span.Born));

        // ---- death ----
        if (death == null && old)
            hints.Add(new ResearchHint("death", HintKind.Death, 2, $"Find when and where {name} died",
                span.LastSeen > span.Born ? $"Nothing records them after {span.LastSeen}." : $"Born about {span.Born}, with no death recorded.",
                Records(lastRegion, Math.Max(span.LastSeen, span.Born + 40), "death"), span.LastSeen));

        // ---- marriages ----
        foreach (var (f, spouse, children) in tree.FamiliesOf(p))
        {
            if (spouse == null || f.Events.Any(e => e.Tag == "MARR" && e.Date.Year > 0)) continue;
            var first = children.Select(c => Vital(c, "BIRT", "CHR", "BAPM")?.Date.Year ?? 0).Where(y => y > 0).DefaultIfEmpty(0).Min();
            hints.Add(new ResearchHint($"marriage:{f.Id}", HintKind.Marriage, 2, $"Find the marriage of {name} and {spouse.DisplayName}",
                first > 0 ? $"No marriage date is recorded; their first child was born in {first}, so probably by then." : "No marriage date is recorded.",
                Records(homeRegion, first > 0 ? first - 1 : span.Born + 25, "marriage"), first > 0 ? first - 1 : 0));
        }

        // ---- censuses they should be in ----
        foreach (var c in LifeTimeline.CensusChecklist(tree, p).Where(c => c.Status == CensusStatus.ToFind))
            hints.Add(new ResearchHint($"census:{c.Census.Region}:{c.Census.Year}", HintKind.Census, 2,
                $"Find {name} in the {c.Census.Name}", c.Reason, c.Census.Note, c.Census.Year));

        // ---- emigration ----
        if (homeRegion != null && lastRegion != null && homeRegion != lastRegion
            && !p.Events.Any(e => e.Tag is "EMIG" or "IMMI" or "NATU"))
        {
            var (left, arrived) = MoveWindow(tree, p, homeRegion, lastRegion);
            hints.Add(new ResearchHint("migration", HintKind.Migration, 2, $"Find when {name} left {homeRegion} for {Destination(lastRegion)}",
                $"Born in {homeRegion}, later in {lastRegion}" + (arrived > 0 ? $" (by {arrived})." : "."),
                MigrationRecords(lastRegion, left, arrived), arrived));
        }

        // ---- military service ----
        if (p.Sex == Sex.Male && span.Born > 0 && !p.Events.Any(e => e.Tag == "MILI"))
        {
            bool AliveIn(int year) => span.Born <= year - 16 && (span.DeathKnown ? span.Died >= year : span.End >= year);
            bool LivedIn(string region, int year) => RegionAround(tree, p, year) == region;
            if (span.Born is >= 1730 and <= 1767 && AliveIn(1776) && LivedIn(Regions.UnitedStates, 1776))
                hints.Add(Military("revolution", 3, $"Look for {name} in Revolutionary War service and pension records",
                    "He was of fighting age during the Revolutionary War (1775–1783).",
                    "Compiled service records and pension files at the US National Archives; pension files often include family details.", 1776));
            if (span.Born is >= 1815 and <= 1847 && AliveIn(1861) && LivedIn(Regions.UnitedStates, 1861))
                hints.Add(Military("civil-war", 2, $"Look for {name}'s Civil War service",
                    "He was of fighting age during the Civil War (1861–1865).",
                    "Union and Confederate compiled service records, the 1890 veterans' schedule, and Union pension files (with widows' and children's details).", 1861));
            if (span.Born is >= 1872 and <= 1900 && AliveIn(1917) && LivedIn(Regions.UnitedStates, 1917))
                hints.Add(Military("ww1-draft", 2, $"Find {name}'s First World War draft card",
                    "Almost every man in the United States born 1872–1900 registered for the draft in 1917–18.",
                    "World War I draft registration cards give exact birth date, address, occupation and nearest relative.", 1917));
            if (span.Born is >= 1877 and <= 1897 && AliveIn(1942) && LivedIn(Regions.UnitedStates, 1942))
                hints.Add(Military("ww2-draft", 3, $"Find {name}'s 1942 draft card",
                    "Men in the United States born 1877–1897 registered in the 1942 \"old man's draft\".",
                    "World War II draft registration cards (fourth registration) give birth date and place and a contact person.", 1942));
            var british = new[] { Regions.England, Regions.Wales, Regions.Scotland, Regions.Ireland, Regions.Canada, Regions.Australia, Regions.NewZealand };
            if (span.Born is >= 1865 and <= 1901 && AliveIn(1914) && RegionAround(tree, p, 1914) is { } r1914 && british.Contains(r1914))
                hints.Add(Military("ww1", 3, $"Look for {name}'s First World War service",
                    "He was of military age during the First World War (1914–1918).",
                    "Service records, medal index cards and unit war diaries; many British service records were damaged in 1940, so check medal cards too.", 1914));
        }

        // ---- probate and Social Security ----
        if (death is { } d && d.Date.Year > 0 && !p.Events.Any(e => e.Tag is "PROB" or "WILL"))
        {
            var where = Regions.Of(d.Place) ?? lastRegion;
            var text = d.Date.Year < 1500 ? "" : where switch
            {
                Regions.England or Regions.Wales when d.Date.Year >= 1858 =>
                    $"The National Probate Calendar indexes every will and administration in England and Wales from 1858; look in {d.Date.Year} and the next few years.",
                Regions.England or Regions.Wales => "Before 1858 wills were proved in church courts (the Prerogative Court of Canterbury for wealthier people).",
                Regions.Scotland => "Scottish wills and testaments (ScotlandsPeople).",
                Regions.UnitedStates => "County probate court records, and land records that settle the estate.",
                _ => "",
            };
            if (text.Length > 0)
                hints.Add(new ResearchHint("probate", HintKind.Probate, 3, $"Look for a will or estate record for {name}",
                    $"They died in {d.Date.Year} and no will or probate is recorded.", text, d.Date.Year));
            if (where == Regions.UnitedStates && d.Date.Year >= 1962)
                hints.Add(new ResearchHint("ssdi", HintKind.Death, 3, $"Look for {name} in the Social Security Death Index",
                    $"They died in {d.Date.Year}, after the index begins.",
                    "The Social Security Death Index, and their SS-5 application, which names both parents.", d.Date.Year));
        }

        // ---- gaps between children ----
        foreach (var (f, _, children) in tree.FamiliesOf(p))
        {
            var born = children.Select(c => (c, y: Vital(c, "BIRT", "CHR", "BAPM")?.Date.Year ?? 0)).Where(x => x.y > 0).OrderBy(x => x.y).ToList();
            var mother = tree.Person(f.Partner2Id);
            var motherBorn = mother != null ? LifeTimeline.Span(tree, mother).Born : 0;
            for (var i = 1; i < born.Count; i++)
            {
                var gap = born[i].y - born[i - 1].y;
                if (gap < 5 || (motherBorn > 0 && born[i - 1].y - motherBorn > 40)) continue;
                hints.Add(new ResearchHint($"gap:{f.Id}:{born[i - 1].y}", HintKind.Children, 3,
                    $"Look for a child born between {born[i - 1].y} and {born[i].y}",
                    $"{gap} years between {born[i - 1].c.DisplayName} and {born[i].c.DisplayName} — often a child who died young.",
                    Join("Baptism and burial registers for those years", "censuses taken in between"), born[i - 1].y + 1));
            }
        }

        // ---- sources ----
        var dated = p.Events.Where(e => e.Date.Year > 0).ToList();
        var uncited = dated.Count(e => e.Citations.Count == 0);
        if (dated.Count >= 2 && uncited * 2 >= dated.Count)
            hints.Add(new ResearchHint("sources", HintKind.Sources, 3, $"Add sources for {name}'s facts",
                $"{uncited} of {dated.Count} dated facts have no source.",
                "Record where each fact came from, so it can be checked and trusted.", 0));

        var dismissed = p.DismissedHints.ToHashSet();
        // Suggestions already searched for (research log) aren't suggested again, whatever the result.
        dismissed.UnionWith(p.ResearchIds.Select(id => tree.Research.GetValueOrDefault(id)).OfType<ResearchItem>()
            .Where(r => r.Result != SearchResult.None && r.HintKey.Length > 0).Select(r => r.HintKey));
        return hints.Where(h => !dismissed.Contains(h.Key)).OrderBy(h => h.Priority).ThenBy(h => h.Year).ToList();
    }

    private static ResearchHint Military(string key, int priority, string task, string why, string where, int year) =>
        new($"military:{key}", HintKind.Military, priority, task, why, where, year);

    private static Event? Vital(Person p, params string[] tags) =>
        tags.Select(t => p.Events.FirstOrDefault(e => e.Tag == t && e.Date.Year > 0 && e.Date.Kind != DateKind.Text)).OfType<Event>().FirstOrDefault();

    private static string? FirstRegion(IEnumerable<Event> events) =>
        events.Select(e => Regions.Of(e.Place)).OfType<string>().FirstOrDefault();

    /// <summary>The country they were living in about this year: the nearest dated event with a recognised place.</summary>
    private static string? RegionAround(FamilyTree tree, Person p, int year) =>
        p.Events.Concat(tree.FamiliesOf(p).SelectMany(x => x.Family.Events))
            .Where(e => e.Date.Year > 0 && Regions.Of(e.Place) != null)
            .OrderBy(e => Math.Abs(e.Date.Year - year)).ThenByDescending(e => e.Date.Year <= year)
            .Select(e => Regions.Of(e.Place)).FirstOrDefault();

    /// <summary>The last year seen in the first country and the first year seen in the second.</summary>
    private static (int Left, int Arrived) MoveWindow(FamilyTree tree, Person p, string from, string to)
    {
        var events = p.Events.Concat(tree.FamiliesOf(p).SelectMany(x => x.Family.Events))
            .Concat(tree.FamiliesOf(p).SelectMany(x => x.Children).Select(c => c.Birth).OfType<Event>())
            .Where(e => e.Date.Year > 0).Select(e => (e.Date.Year, Region: Regions.Of(e.Place))).ToList();
        var left = events.Where(e => e.Region == from).Select(e => e.Year).DefaultIfEmpty(0).Max();
        var arrived = events.Where(e => e.Region == to && e.Year >= left).Select(e => e.Year).DefaultIfEmpty(0).Min();
        return (left, arrived);
    }

    private static string Destination(string region) => region == Regions.UnitedStates ? "the United States" : region;

    private static string MigrationRecords(string to, int left, int arrived)
    {
        var when = left > 0 && arrived > 0 ? $" Look between {left} and {arrived}." : "";
        return to switch
        {
            Regions.UnitedStates => "Passenger arrival lists (from 1820; Ellis Island from 1892) and naturalization records — after 1906 these give the ship and date of arrival." + when,
            Regions.Canada => "Canadian passenger lists (from 1865) and border-crossing records." + when,
            Regions.Australia or Regions.NewZealand => "Assisted and unassisted passenger lists, and convict records for transported people." + when,
            Regions.England or Regions.Wales or Regions.Scotland => "UK incoming passenger lists (from 1878) and naturalisation papers." + when,
            _ => "Passenger lists and the emigration records kept by the home country." + when,
        };
    }

    /// <summary>Where birth, marriage or death records usually are for this country and year.</summary>
    private static string Records(string? region, int year, string kind) => year > 0 && year < RegistersBegin(region) ? Medieval : region switch
    {
        Regions.England or Regions.Wales => year >= 1837
            ? $"The England and Wales {kind} indexes (civil registration began 1 July 1837), then the certificate from the General Register Office."
            : $"Parish registers ({(kind == "birth" ? "baptisms" : kind == "death" ? "burials" : "marriages")}) and bishops' transcripts.",
        Regions.Scotland => year >= 1855
            ? $"Scottish statutory {kind} registers (from 1855) on ScotlandsPeople."
            : "Old parish registers (OPRs) on ScotlandsPeople.",
        Regions.Ireland => year >= 1864 || (kind == "marriage" && year >= 1845)
            ? $"Irish civil registration (from 1864; non-Catholic marriages from 1845), free at irishgenealogy.ie."
            : "Catholic or Church of Ireland parish registers; many Church of Ireland registers were lost in 1922.",
        Regions.UnitedStates => kind switch
        {
            "birth" => year >= 1910 ? "State birth certificates (most states registered births by 1920)." : "Church baptism records, family Bibles, and delayed birth certificates; most states didn't register births until 1900–1920.",
            "death" => year >= 1900 ? "State death certificates, Find a Grave, and newspaper obituaries." : "Cemetery and church burial records, newspaper notices, and probate records.",
            _ => "County marriage records (licences and returns), and church registers.",
        },
        Regions.Canada => year >= 1900 ? "Provincial vital statistics registrations." : "Church registers (in Quebec, the Catholic registers are nearly complete).",
        Regions.Germany => year >= 1876 ? "Civil registry (Standesamt) records, kept from 1876 (1874 in Prussia)." : "Church books (Kirchenbücher) of the parish.",
        Regions.Netherlands => year >= 1811 ? "Dutch civil registration (from 1811), searchable on OpenArchives and WieWasWie." : "Church registers (doop-, trouw- en begraafboeken).",
        Regions.France => year >= 1792 ? "État civil registers, online at the departmental archives." : "Parish registers, online at the departmental archives.",
        Regions.Sweden or Regions.Norway or Regions.Denmark =>
            "The parish church books, which record births, marriages, deaths and moves; Swedish household examination rolls list the family almost every year.",
        _ => "Church and civil records where they lived.",
    };

    /// <summary>Records before parish registers mostly concern landholders.</summary>
    private const string Medieval =
        "Records this early mostly concern landholders: charters, manor court rolls, inquisitions post mortem and tax lists, and published pedigrees (check those against the original records).";

    /// <summary>The year parish registers (or their equivalent) began, roughly.</summary>
    private static int RegistersBegin(string? region) => region switch
    {
        Regions.England or Regions.Wales => 1538,
        Regions.Scotland => 1553,
        Regions.Ireland => 1600,
        Regions.UnitedStates or Regions.Canada => 1600,
        _ => 1500,
    };

    private static string Join(params string[] parts)
    {
        var list = parts.Where(x => x.Length > 0).ToList();
        if (list.Count == 0) return "";
        var text = string.Join("; ", list);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }
}
