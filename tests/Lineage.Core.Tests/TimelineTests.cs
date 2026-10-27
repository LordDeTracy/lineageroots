using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class TimelineTests
{
    private static Person Add(FamilyTree t, string given, string surname, Sex sex, params Event[] events)
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = surname;
        p.Events.AddRange(events);
        return p;
    }

    private static Event E(string tag, string date, string place = "") => Event.Of(tag, GenDate.Parse(date), place);

    private static Family Marry(FamilyTree t, Person a, Person b, string date, string place, params Person[] children)
    {
        var f = t.AddFamily(new Family());
        t.SetPartner(f, a, second: false);
        t.SetPartner(f, b, second: true);
        if (date.Length > 0) f.Events.Add(E("MARR", date, place));
        foreach (var c in children) t.AddChild(f, c);
        return f;
    }

    // ---- Regions ----

    [Theory]
    [InlineData("Leeds, Yorkshire, England", Regions.England)]
    [InlineData("Leeds, West Riding of Yorkshire", Regions.England)]
    [InlineData("Swansea, Glamorgan", Regions.Wales)]
    [InlineData("Paisley, Renfrewshire, Scotland", Regions.Scotland)]
    [InlineData("Skibbereen, Co. Cork", Regions.Ireland)]
    [InlineData("Worcester, Worcester, Massachusetts, USA", Regions.UnitedStates)]
    [InlineData("Norfolk, VA", Regions.UnitedStates)]
    [InlineData("Ohio", Regions.UnitedStates)]
    [InlineData("Victoria, British Columbia, Canada", Regions.Canada)]
    [InlineData("London, Ontario", Regions.Canada)]
    [InlineData("Ballarat, Victoria, Australia", Regions.Australia)]
    [InlineData("Stuttgart, Württemberg", Regions.Germany)]
    [InlineData("Bergen, Norge", Regions.Norway)]
    [InlineData("Lillby, Nordmark, Swed", Regions.Sweden)]
    [InlineData("Vlaanderbeek, Sp Neth (Belgium)", Regions.Belgium)]
    [InlineData("Graafwijk, Neth", Regions.Netherlands)]
    [InlineData("Hollisford, New England", Regions.UnitedStates)]
    [InlineData("Kirkbrae, Fife, of Scotland", Regions.Scotland)]
    public void PlacesAreInTheRightCountry(string place, string region) => Assert.Equal(region, Regions.Of(place));

    [Theory]
    [InlineData("")]
    [InlineData("Springfield")]
    [InlineData("Victoria, Smallville")]   // a first part is a town, even when it shares a state's name
    public void UnknownPlacesHaveNoRegion(string place) => Assert.Null(Regions.Of(place));

    [Fact]
    public void CensusesKnowWhichPartOfACountryTheyCover()
    {
        var prairie = Censuses.All.Single(c => c.Region == Regions.Canada && c.Year == 1906);
        Assert.True(prairie.Covers("Regina, Saskatchewan, Canada"));
        Assert.False(prairie.Covers("Toronto, Ontario, Canada"));
        var free = Censuses.All.Single(c => c.Region == Regions.Ireland && c.Year == 1926);
        Assert.True(free.Covers("Tralee, Kerry, Ireland"));
        Assert.False(free.Covers("Lisburn, Co. Antrim, Ireland"));
    }

    // ---- Lifespan ----

    [Fact]
    public void BirthIsEstimatedFromMarriageOrFirstChild()
    {
        var t = new FamilyTree();
        var child = Add(t, "Nell", "Quorrow", Sex.Female, E("BIRT", "1862", "Leeds, Yorkshire, England"));
        var father = Add(t, "Amos", "Quorrow", Sex.Male);
        var mother = Add(t, "Lettice", "Brandle", Sex.Female);
        Marry(t, father, mother, "1860", "Leeds, Yorkshire, England", child);

        var span = LifeTimeline.Span(t, father);
        Assert.Equal(1834, span.Born);   // first child in 1862 at about 28 (earlier than marrying in 1860 at 25)
        Assert.True(span.BornEstimated);
        Assert.False(span.DeathKnown);
        Assert.Equal(1862, span.LastSeen);
    }

    // ---- Timeline ----

    [Fact]
    public void TimelineMixesOwnFamilyAndHistoryInOrderWithAges()
    {
        var t = new FamilyTree();
        var son = Add(t, "Walter", "Quorrow", Sex.Male, E("BIRT", "3 MAR 1863", "Leeds, Yorkshire, England"), E("DEAT", "1864", "Leeds, Yorkshire, England"));
        var father = Add(t, "Amos", "Quorrow", Sex.Male,
            E("BIRT", "10 JUN 1840", "Leeds, Yorkshire, England"),
            E("OCCU", "1871", "Leeds, Yorkshire, England"),
            E("RESI", ""),   // no date: not on the timeline
            E("DEAT", "5 FEB 1920", "Leeds, Yorkshire, England"));
        father.Events[1].Value = "Wool comber";
        var mother = Add(t, "Lettice", "Brandle", Sex.Female, E("DEAT", "1900", "Leeds, Yorkshire, England"));
        Marry(t, father, mother, "1862", "Leeds, Yorkshire, England", son);

        var timeline = LifeTimeline.Build(t, father);
        Assert.Equal(1, timeline.Undated);
        var titles = timeline.Entries.Select(e => e.Title).ToList();

        Assert.Equal("Birth", titles[0]);
        Assert.Contains("Marriage to Lettice Brandle", titles);
        Assert.Contains("Birth of son, Walter Quorrow", titles);
        Assert.Contains("Death of son, Walter Quorrow", titles);
        Assert.Contains("Occupation: Wool comber", titles);
        Assert.Contains("Death of wife, Lettice Brandle", titles);
        Assert.Equal("Death", titles[^1]);

        // History where he lived (England), not elsewhere; nothing before his birth or after his death.
        Assert.Contains("Cholera epidemic", titles);           // 1866, England
        Assert.Contains("First World War", titles);
        Assert.Contains("Influenza pandemic", titles);
        Assert.DoesNotContain("American Civil War", titles);
        Assert.DoesNotContain("Calendar change", titles);
        Assert.DoesNotContain("The Great Depression", titles);

        var years = timeline.Entries.Select(e => e.SortKey).ToList();
        Assert.Equal(years.OrderBy(y => y), years);
        Assert.Equal("age 22", timeline.Entries.Single(e => e.Title.StartsWith("Birth of son")).Age);
        Assert.Equal("age 79", timeline.Entries[^1].Age);
        Assert.Equal("age about 74", timeline.Entries.Single(e => e.Title == "First World War").Age);
    }

    [Fact]
    public void FamilyAndHistoryCanBeLeftOut()
    {
        var t = new FamilyTree();
        var p = Add(t, "Amos", "Quorrow", Sex.Male, E("BIRT", "1840", "Leeds, Yorkshire, England"), E("DEAT", "1920", "Leeds, Yorkshire, England"));
        var father = Add(t, "Old", "Quorrow", Sex.Male, E("DEAT", "1870"));
        var mother = Add(t, "Old", "Brandle", Sex.Female);
        Marry(t, father, mother, "", "", p);

        Assert.Contains(LifeTimeline.Build(t, p).Entries, e => e.Kind == TimelineKind.Family && e.Title == "Death of father, Old Quorrow");
        var bare = LifeTimeline.Build(t, p, family: false, history: false).Entries;
        Assert.All(bare, e => Assert.Equal(TimelineKind.Life, e.Kind));
        Assert.Equal(2, bare.Count);
    }

    [Fact]
    public void HistoryFollowsThePersonWhenTheyEmigrate()
    {
        var t = new FamilyTree();
        var p = Add(t, "Brigid", "Fennerty", Sex.Female,
            E("BIRT", "1840", "Skibbereen, Co. Cork, Ireland"),
            E("IMMI", "1850", "Boston, Suffolk, Massachusetts, USA"),
            E("DEAT", "1890", "Boston, Suffolk, Massachusetts, USA"));
        var titles = LifeTimeline.Build(t, p).Entries.Select(e => e.Title).ToList();
        Assert.Contains("The Great Famine", titles);       // 1845–52, while still in Ireland
        Assert.Contains("American Civil War", titles);     // 1861–65, in the United States
        Assert.DoesNotContain("Civil registration begins", titles);   // Ireland 1864: she had left
    }

    // ---- Census checklist ----

    [Fact]
    public void ChecklistListsEachCensusWhileAliveWhereTheyLived()
    {
        var t = new FamilyTree();
        var p = Add(t, "Amos", "Quorrow", Sex.Male,
            E("BIRT", "10 JUN 1840", "Leeds, Yorkshire, England"),
            E("CENS", "1851", "Leeds, Yorkshire, England"),
            E("DEAT", "1 MAR 1891", "Leeds, Yorkshire, England"));
        var source = t.AddSource(new Source { Title = "1881 England Census" });
        var resi = E("RESI", "1881", "Leeds, Yorkshire, England");
        resi.Citations.Add(new Citation { SourceId = source.Id });
        p.Events.Add(resi);

        var list = LifeTimeline.CensusChecklist(t, p);
        // Born June 1840: misses 1841 (6 June)? No — census day 6 June 1841 is after his birth.
        Assert.Equal(new[] { 1841, 1851, 1861, 1871, 1881 }, list.Select(c => c.Census.Year));
        Assert.All(list, c => Assert.Equal(Regions.England, c.Census.Region));
        Assert.Equal(CensusStatus.Found, list.Single(c => c.Census.Year == 1851).Status);
        Assert.Equal(CensusStatus.Found, list.Single(c => c.Census.Year == 1881).Status);
        Assert.Same(resi, list.Single(c => c.Census.Year == 1881).Record);
        Assert.Equal(CensusStatus.ToFind, list.Single(c => c.Census.Year == 1861).Status);
        Assert.Equal("Leeds, Yorkshire, England", list[0].Place);
    }

    [Fact]
    public void ChecklistLeavesOutCensusesBeforeBirthOrAfterDeath()
    {
        var t = new FamilyTree();
        var p = Add(t, "Amos", "Quorrow", Sex.Male,
            E("BIRT", "1 MAY 1841", "Leeds, Yorkshire, England"),    // before census day, 6 June 1841
            E("DEAT", "1 APR 1851", "Leeds, Yorkshire, England"));   // after census day, 30 March 1851
        Assert.Equal(new[] { 1841, 1851 }, LifeTimeline.CensusChecklist(t, p).Select(c => c.Census.Year));

        p.Events[0].Date = GenDate.Parse("1 JUL 1841");
        p.Events[1].Date = GenDate.Parse("1 MAR 1851");
        Assert.Empty(LifeTimeline.CensusChecklist(t, p));
    }

    [Fact]
    public void LostCensusesAreMarkedAndMovesAreFlagged()
    {
        var t = new FamilyTree();
        var p = Add(t, "Brigid", "Fennerty", Sex.Female,
            E("BIRT", "1840", "Skibbereen, Co. Cork, Ireland"),
            E("IMMI", "1856", "Boston, Suffolk, Massachusetts, USA"),
            E("DEAT", "1895", "Boston, Suffolk, Massachusetts, USA"));
        var list = LifeTimeline.CensusChecklist(t, p);

        Assert.Equal(CensusStatus.Lost, list.Single(c => c.Census.Name == "1841 Ireland census").Status);
        // Between 1840 and 1856 she was in Ireland or the United States: both 1850 and 1851 are possible.
        Assert.Equal(CensusStatus.Possible, list.Single(c => c.Census.Name == "1850 US census").Status);
        Assert.Equal(CensusStatus.ToFind, list.Single(c => c.Census.Name == "1860 US census").Status);
        Assert.Equal(CensusStatus.Lost, list.Single(c => c.Census.Name == "1890 US census").Status);
        Assert.DoesNotContain(list, c => c.Census.Name == "1901 Ireland census");
    }

    [Fact]
    public void WithoutADeathLaterCensusesAreOnlyPossible()
    {
        var t = new FamilyTree();
        var p = Add(t, "Amos", "Quorrow", Sex.Male,
            E("BIRT", "1850", "Columbus, Franklin, Ohio, USA"),
            E("RESI", "1885", "Columbus, Franklin, Ohio, USA"));
        var list = LifeTimeline.CensusChecklist(t, p);
        Assert.Equal(CensusStatus.ToFind, list.Single(c => c.Census.Year == 1880).Status);
        Assert.Equal(CensusStatus.Possible, list.Single(c => c.Census.Year == 1900).Status);
        Assert.DoesNotContain(list, c => c.Census.Year > 1925);   // about 75 years is assumed when the death isn't recorded
    }

    [Fact]
    public void NoPlacesMeansNoChecklist()
    {
        var t = new FamilyTree();
        var p = Add(t, "Amos", "Quorrow", Sex.Male, E("BIRT", "1850"));
        Assert.Empty(LifeTimeline.CensusChecklist(t, p));
    }
}
