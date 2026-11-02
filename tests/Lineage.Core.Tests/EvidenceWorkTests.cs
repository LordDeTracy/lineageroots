using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class EvidenceWorkTests
{
    private static (FamilyTree Tree, Person Ann, Event Birth) Ann()
    {
        var t = new FamilyTree();
        var parish = t.AddSource(new Source { Title = "St Mary's baptisms", Kind = SourceKind.Original });
        var census = t.AddSource(new Source { Title = "1851 census", Kind = SourceKind.Original });
        var ann = t.AddPerson(new Person { Sex = Sex.Female });
        (ann.PrimaryName.Given, ann.PrimaryName.Surname) = ("Ann", "Holt");
        var birth = Event.Of("BIRT", GenDate.Parse("14 MAR 1842"), "Harrow, Middlesex, England");
        birth.Citations.Add(new Citation { SourceId = parish.Id, Page = "entry 118", Information = InformationKind.Primary, Evidence = EvidenceKind.Direct });
        ann.Events.Add(birth);
        var c1 = Event.Of("CENS", GenDate.Parse("30 MAR 1851"), "Harrow");
        c1.Description = "Relation to head: Daughter; Age: 9";
        c1.Citations.Add(new Citation { SourceId = census.Id, Page = "HO107/1696 f.12" });
        ann.Events.Add(c1);
        var c2 = Event.Of("CENS", GenDate.Parse("7 APR 1861"), "Leeds");
        c2.Description = "Age: 25";
        ann.Events.Add(c2);
        return (t, ann, birth);
    }

    [Fact]
    public void EveryRecordOnAFactIsLaidSideBySide()
    {
        var (t, ann, birth) = Ann();
        Assert.Equal("When and where was Ann born?", EvidenceWork.Question(t, birth, ann));
        var claims = EvidenceWork.Claims(t, birth, ann);
        Assert.Equal(3, claims.Count);
        var parish = claims.Single(c => c.Basis == "This fact");
        Assert.Equal(("St Mary's baptisms, entry 118", false), (parish.Source, parish.Disagrees));
        var agrees = claims.Single(c => c.Says.StartsWith("Age 9"));
        Assert.Equal((1841, 1842, false, EvidenceKind.Indirect), (agrees.From, agrees.To, agrees.Disagrees, agrees.Evidence));
        var differs = claims.Single(c => c.Says.StartsWith("Age 25"));   // born 1835–1836: seven years out
        Assert.True(differs.Disagrees);
        Assert.Equal(differs, claims[^1]);   // disagreements at the bottom, to weigh last
    }

    [Fact]
    public void TheProofStandardIsCheckedPartByPart()
    {
        var (t, ann, birth) = Ann();
        var gps = EvidenceWork.Gps(t, birth, ann);
        Assert.Equal(new[] { false, true, true, true, false }, gps.Select(g => g.Met));   // one source, no searches; not concluded

        ann.ResearchIds.Add(t.AddResearch(new ResearchItem { Description = "Searched the bishop's transcripts", Result = SearchResult.NotFound }).Id);
        birth.Conclusion = FactConclusion.Proven;
        birth.Reasoning = "The baptism register was made at the time by the curate; the 1851 age agrees.";
        Assert.All(EvidenceWork.Gps(t, birth, ann), g => Assert.True(g.Met));
        Assert.Equal(WorkKind.Concluded, EvidenceWork.Work(t, birth, ann).Kind);
    }

    [Fact]
    public void TheWorklistPutsConflictsAndUnsourcedFactsFirst()
    {
        var (t, ann, _) = Ann();
        ann.Events.Add(Event.Of("DEAT", GenDate.Parse("1901")));   // no source
        ann.Events.Add(Event.Of("BIRT", GenDate.Parse("1836")));    // a second birth record that disagrees
        var work = EvidenceWork.Worklist(t);
        Assert.Equal(WorkKind.Conflict, work[0].Kind);
        Assert.Contains(work, w => w.Event.Tag == "DEAT" && w.Kind == WorkKind.Unsourced);
        Assert.All(work, w => Assert.Equal("Ann Holt", w.Who));
    }
}
