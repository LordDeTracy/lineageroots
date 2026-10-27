using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class SuggestionTests
{
    private static Person P(FamilyTree t, string given, string surname, Sex sex, params Event[] events)
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = surname;
        p.Events.AddRange(events);
        return p;
    }

    private static Event E(string tag, string date, string place = "") => Event.Of(tag, GenDate.Parse(date), place);

    private static Family Couple(FamilyTree t, Person a, Person b, params Person[] children)
    {
        var f = t.AddFamily(new Family());
        t.SetPartner(f, a, second: false);
        t.SetPartner(f, b, second: true);
        foreach (var c in children) t.AddChild(f, c);
        return f;
    }

    private static List<ResearchHint> Hints(FamilyTree t, Person p) => ResearchSuggestions.For(t, p, currentYear: 2026);

    [Fact]
    public void MissingParentsMarriageAndDeathAreSuggestedWithTheRightRecords()
    {
        var t = new FamilyTree();
        var amos = P(t, "Amos", "Quorrow", Sex.Male, E("BIRT", "1840", "Leeds, Yorkshire, England"));
        var wife = P(t, "Lettice", "Brandle", Sex.Female, E("BIRT", "1842", "Leeds, Yorkshire, England"));
        var kid = P(t, "Nell", "Quorrow", Sex.Female, E("BIRT", "1866", "Leeds, Yorkshire, England"));
        Couple(t, amos, wife, kid);

        var hints = Hints(t, amos);
        var parents = hints.Single(h => h.Key == "parents");
        Assert.Equal((1, "Find Amos Quorrow's parents"), (parents.Priority, parents.Task));
        Assert.Contains("censuses from 1851 give relationships", parents.Where);

        var marriage = hints.Single(h => h.Kind == HintKind.Marriage);
        Assert.Equal("Find the marriage of Amos Quorrow and Lettice Brandle", marriage.Task);
        Assert.Contains("first child was born in 1866", marriage.Why);
        Assert.Contains("England and Wales marriage indexes", marriage.Where);

        var death = hints.Single(h => h.Key == "death");
        Assert.Contains("Nothing records them after 1866", death.Why);
        Assert.Contains("civil registration", death.Where);

        Assert.Contains(hints, h => h.Key == "census:England:1851");
        Assert.Equal(hints.OrderBy(h => h.Priority).Select(h => h.Priority), hints.Select(h => h.Priority));
    }

    [Fact]
    public void EarlyRecordsAreNotParishRegisters()
    {
        var t = new FamilyTree();
        var knight = P(t, "Walter", "Fitzquorrow", Sex.Male, E("BIRT", "1240", "Leeds, Yorkshire, England"), E("DEAT", "1300", "Leeds, Yorkshire, England"));
        var lady = P(t, "Maud", "Brandle", Sex.Female);
        var son = P(t, "Hugh", "Fitzquorrow", Sex.Male, E("BIRT", "1270", "Leeds, Yorkshire, England"));
        Couple(t, knight, lady, son);
        var hints = Hints(t, knight);
        Assert.Contains("charters", hints.Single(h => h.Kind == HintKind.Marriage).Where);
        Assert.DoesNotContain(hints, h => h.Kind == HintKind.Probate);   // no wills that early
        Assert.DoesNotContain(hints, h => h.Kind == HintKind.Military);
    }

    [Fact]
    public void EmigrantsGetMigrationAndUsRecordHints()
    {
        var t = new FamilyTree();
        var p = P(t, "Brigid", "Fennerty", Sex.Male,
            E("BIRT", "1880", "Skibbereen, Co. Cork, Ireland"),
            E("RESI", "1903", "Skibbereen, Co. Cork, Ireland"),
            E("RESI", "1910", "Hollisford, Suffolk, Massachusetts, USA"),
            E("DEAT", "1965", "Hollisford, Suffolk, Massachusetts, USA"));
        var hints = Hints(t, p);
        var move = hints.Single(h => h.Kind == HintKind.Migration);
        Assert.Equal("Find when Brigid Fennerty left Ireland for the United States", move.Task);
        Assert.Contains("between 1903 and 1910", move.Where);
        Assert.Contains(hints, h => h.Key == "military:ww1-draft");
        Assert.Contains(hints, h => h.Key == "military:ww2-draft");
        Assert.Contains(hints, h => h.Key == "ssdi");
        Assert.Contains(hints, h => h.Key == "probate" && h.Where.Contains("County probate"));
        Assert.DoesNotContain(hints, h => h.Key == "death");   // the death is known

        // An immigration event answers the migration question; dismissed hints stay away.
        p.Events.Add(E("IMMI", "1905", "Hollisford, Suffolk, Massachusetts, USA"));
        p.DismissedHints.Add("ssdi");
        hints = Hints(t, p);
        Assert.DoesNotContain(hints, h => h.Kind == HintKind.Migration);
        Assert.DoesNotContain(hints, h => h.Key == "ssdi");
    }

    [Fact]
    public void GapsBetweenChildrenAndUnsourcedFacts()
    {
        var t = new FamilyTree();
        var dad = P(t, "Amos", "Quorrow", Sex.Male, E("BIRT", "1840"), E("DEAT", "1900"));
        var mum = P(t, "Lettice", "Brandle", Sex.Female, E("BIRT", "1842"));
        var a = P(t, "Ada", "Quorrow", Sex.Female, E("BIRT", "1865"));
        var b = P(t, "Bert", "Quorrow", Sex.Male, E("BIRT", "1867"));
        var c = P(t, "Cora", "Quorrow", Sex.Female, E("BIRT", "1874"));
        var f = Couple(t, dad, mum, a, b, c);
        f.Events.Add(E("MARR", "1864"));

        var hints = Hints(t, dad);
        var gap = Assert.Single(hints, h => h.Kind == HintKind.Children);
        Assert.Equal("Look for a child born between 1867 and 1874", gap.Task);
        Assert.Contains("7 years between Bert Quorrow and Cora Quorrow", gap.Why);
        Assert.Contains(hints, h => h.Key == "sources" && h.Why == "2 of 2 dated facts have no source.");

        dad.Events[0].Citations.Add(new Citation { SourceId = 1 });
        dad.Events[1].Citations.Add(new Citation { SourceId = 1 });
        Assert.DoesNotContain(Hints(t, dad), h => h.Key == "sources");
    }

    [Fact]
    public void LivingPeopleGetNoVitalRecordHints()
    {
        var t = new FamilyTree();
        var p = P(t, "Zed", "Quorrow", Sex.Male, E("RESI", "2001", "Leeds, Yorkshire, England"));
        Assert.DoesNotContain(Hints(t, p), h => h.Kind is HintKind.Birth or HintKind.Death);
    }
}

/// <summary>Runs the timeline, census checklist and suggestions over every person in the private samples (no-op without them).</summary>
public class SampleTreeFeatureTests
{
    public static TheoryData<string> Names() => Samples.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public void EveryoneGetsATimelineChecklistAndSuggestionsQuickly(string name)
    {
        if (Samples.Get(name) is not { } s) return;
        var t = Lineage.Core.Import.Aq.AqImporter.Import(s.Aq);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var p in t.People.Values)
        {
            LifeTimeline.Build(t, p);
            LifeTimeline.CensusChecklist(t, p);
            ResearchSuggestions.For(t, p);
        }
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }
}
