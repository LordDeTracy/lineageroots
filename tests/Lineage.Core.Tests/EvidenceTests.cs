using Lineage.Core.Analysis;
using Lineage.Core.Editing;
using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class EvidenceTests
{
    private static Person P(FamilyTree t, string given)
    {
        var p = t.AddPerson(new Person { Sex = Sex.Female });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = "Quorrow";
        return p;
    }

    private static Source S(FamilyTree t, string title, SourceKind kind = SourceKind.Unknown) =>
        t.AddSource(new Source { Title = title, Kind = kind });

    private static Citation Cite(Source s, InformationKind info = InformationKind.Unknown, EvidenceKind ev = EvidenceKind.Unknown, int? quay = null) =>
        new() { SourceId = s.Id, Information = info, Evidence = ev, Quality = quay };

    private static Event Birth(string date, string place, params Citation[] citations)
    {
        var e = Event.Of("BIRT", GenDate.Parse(date), place);
        e.Citations.AddRange(citations);
        return e;
    }

    [Fact]
    public void CitationsAreWeighedByRecordInformantAndEvidence()
    {
        var t = new FamilyTree();
        var original = S(t, "Birth certificate", SourceKind.Original);
        var index = S(t, "Birth index", SourceKind.Derivative);
        var compiled = S(t, "A family history", SourceKind.Authored);
        var unrated = S(t, "Some website");
        var best = Evidence.Weight(t, Cite(original, InformationKind.Primary, EvidenceKind.Direct));
        Assert.Equal(0.8, best, 3);
        Assert.True(best > Evidence.Weight(t, Cite(index, InformationKind.Primary, EvidenceKind.Direct)));
        Assert.True(Evidence.Weight(t, Cite(index, InformationKind.Primary, EvidenceKind.Direct)) > Evidence.Weight(t, Cite(compiled, InformationKind.Primary, EvidenceKind.Direct)));
        Assert.True(best > Evidence.Weight(t, Cite(original, InformationKind.Secondary, EvidenceKind.Direct)));
        Assert.True(best > Evidence.Weight(t, Cite(original, InformationKind.Primary, EvidenceKind.Indirect)));
        // The GEDCOM quality rating stands in when nothing else is rated, and is written back from the analysis.
        Assert.Equal(0.75, Evidence.Weight(t, Cite(unrated, quay: 3)), 3);
        Assert.Equal(0.05, Evidence.Weight(t, Cite(unrated, quay: 0)), 3);
        Assert.Equal(3, Evidence.QualityFor(t, Cite(original, InformationKind.Primary, EvidenceKind.Direct)));
        Assert.Equal(1, Evidence.QualityFor(t, Cite(compiled, InformationKind.Secondary, EvidenceKind.Direct)));
        Assert.Null(Evidence.QualityFor(t, Cite(unrated)));
    }

    [Fact]
    public void LevelsRiseWithIndependentGoodSources()
    {
        var t = new FamilyTree();
        var cert = S(t, "Birth certificate", SourceKind.Original);
        var baptism = S(t, "Parish register", SourceKind.Original);
        var site = S(t, "Some website");
        var other = S(t, "Another website");

        Assert.Equal(ProofLevel.Unsourced, Evidence.Assess(t, Birth("1840", "")).Level);
        Assert.Equal(ProofLevel.Weak, Evidence.Assess(t, Birth("1840", "", Cite(site))).Level);
        Assert.Equal(ProofLevel.Weak, Evidence.Assess(t, Birth("1840", "", Cite(site), Cite(site))).Level);   // the same source twice is one source
        Assert.Equal(ProofLevel.Good, Evidence.Assess(t, Birth("1840", "", Cite(site), Cite(other))).Level);
        var one = Evidence.Assess(t, Birth("1840", "", Cite(cert, InformationKind.Primary, EvidenceKind.Direct)));
        Assert.Equal(ProofLevel.Good, one.Level);                                                             // one record is not yet corroborated
        Assert.Contains(one.Tips, x => x.Contains("second, independent record"));
        var two = Evidence.Assess(t, Birth("1840", "", Cite(cert, InformationKind.Primary, EvidenceKind.Direct),
                                                       Cite(baptism, InformationKind.Primary, EvidenceKind.Direct)));
        Assert.Equal(ProofLevel.Strong, two.Level);
        Assert.Equal("Strong evidence: 2 sources", two.Summary);
        Assert.Contains("Best: an original record with first-hand information stating it directly.", two.Reasons);
    }

    [Fact]
    public void SourcesCitedForTheWholePersonSupportTheirVitalFactsAtHalfWeight()
    {
        var t = new FamilyTree();
        var p = P(t, "Nell");
        var cert = S(t, "Death certificate", SourceKind.Original);
        p.Citations.Add(Cite(cert, InformationKind.Primary, EvidenceKind.Direct));
        var birth = Birth("1840", "");
        var job = Event.Of("OCCU", GenDate.Parse("1870"));
        p.Events.AddRange(new[] { birth, job });

        var a = Evidence.Assess(t, birth, p);
        Assert.Equal(ProofLevel.Weak, a.Level);                // 0.8 at half weight
        Assert.Equal(0.4, a.Score, 3);
        Assert.Contains(a.Reasons, r => r.Contains("whole record"));
        Assert.Contains("Attach the sources to the facts they prove.", a.Tips);
        Assert.Equal(ProofLevel.Unsourced, Evidence.Assess(t, job, p).Level);   // only vital facts
        Assert.Equal(ProofLevel.Unsourced, Evidence.Assess(t, birth).Level);    // and only when the owner is known
    }

    [Fact]
    public void ConflictsAndConclusions()
    {
        var t = new FamilyTree();
        var cert = S(t, "Birth certificate", SourceKind.Original);
        var e = Birth("1840", "", Cite(cert, InformationKind.Primary, EvidenceKind.Direct));
        e.Conflicts.Add(new EvidenceConflict { Claim = "1851 census: aged 13, so born about 1838" });
        var a = Evidence.Assess(t, e);
        Assert.Equal((ProofLevel.Conflicting, 1, "1 unresolved conflict"), (a.Level, a.OpenConflicts, a.Summary));
        Assert.Contains("Unresolved conflict: 1851 census: aged 13, so born about 1838", a.Reasons);

        e.Conflicts[0].Resolved = true;
        Assert.Equal(ProofLevel.Good, Evidence.Assess(t, e).Level);

        e.Conclusion = FactConclusion.Proven;
        var proven = Evidence.Assess(t, e);
        Assert.Equal(ProofLevel.Proven, proven.Level);
        Assert.Equal(new[] { "Write down the reasoning that proves it." }, proven.Tips);
        e.Reasoning = "The certificate was registered within days.";
        Assert.Empty(Evidence.Assess(t, e).Tips);
        e.Conclusion = FactConclusion.Disproved;
        Assert.Equal(ProofLevel.Disproved, Evidence.Assess(t, e).Level);
    }

    [Fact]
    public void TwoRecordsOfTheSameFactThatDisagreeAreAConflict()
    {
        var t = new FamilyTree();
        var p = P(t, "Nell");
        var a = Birth("1842", "Leeds");
        var b = Birth("1838", "Leeds, Yorkshire, England");
        p.Events.AddRange(new[] { a, b });
        var assess = Evidence.Assess(t, a, p);
        Assert.Equal(ProofLevel.Conflicting, assess.Level);
        Assert.Contains("Another birth record says 1838, Leeds, Yorkshire, England.", assess.Reasons);

        b.Date = GenDate.Parse("1842");   // same year; "Leeds" is the short form of the fuller place
        Assert.Equal(ProofLevel.Unsourced, Evidence.Assess(t, a, p).Level);
        b.Place = "York";
        Assert.Equal(ProofLevel.Conflicting, Evidence.Assess(t, a, p).Level);

        b.Conclusion = FactConclusion.Disproved;   // settled: that record is wrong
        Assert.Equal(ProofLevel.Unsourced, Evidence.Assess(t, a, p).Level);
        Assert.Equal(ProofLevel.Disproved, Evidence.Assess(t, b, p).Level);
    }

    [Fact]
    public void PersonLevelIsTheWeakerOfBirthAndDeath()
    {
        var t = new FamilyTree();
        var cert = S(t, "Certificate", SourceKind.Original);
        var p = P(t, "Nell");
        p.Events.Add(Birth("1840", "", Cite(cert, InformationKind.Primary, EvidenceKind.Direct), Cite(S(t, "Register", SourceKind.Original), InformationKind.Primary, EvidenceKind.Direct)));
        Assert.Equal(ProofLevel.Strong, Evidence.PersonLevel(t, p));
        p.Events.Add(Event.Of("DEAT", GenDate.Parse("1900")));
        Assert.Equal(ProofLevel.Unsourced, Evidence.PersonLevel(t, p));
        Assert.Null(Evidence.PersonLevel(t, P(t, "Nobody")));

        var tally = Evidence.Tally(t, p);
        Assert.Equal((1, 1), (tally[ProofLevel.Strong], tally[ProofLevel.Unsourced]));
    }

    [Fact]
    public void ProblemFinderReportsConflictingRecords()
    {
        var t = new FamilyTree();
        var p = P(t, "Nell");
        p.Events.Add(Birth("1842", "Leeds"));
        p.Events.Add(Birth("1838", "Leeds"));
        var problems = Problems.Find(t).Where(x => x.Code == ProblemCodes.ConflictingEvidence).ToList();
        Assert.Equal(2, problems.Count);   // one for each record
        Assert.Contains("records disagree about the birth", problems[0].Message);
    }

    [Fact]
    public void EvidenceSurvivesGedcomAndMergesAndPrivacy()
    {
        var t = new FamilyTree();
        var cert = S(t, "Birth certificate", SourceKind.Original);
        var census = S(t, "1851 census", SourceKind.Original);
        var p = P(t, "Nell");
        var e = Birth("12 MAR 1840", "Leeds", Cite(cert, InformationKind.Primary, EvidenceKind.Direct));
        e.Conclusion = FactConclusion.Proven;
        e.Reasoning = "The certificate was registered within six weeks.\nThe census age is rounded.";
        e.Conflicts.Add(new EvidenceConflict { Claim = "Aged 13 in 1851", SourceId = census.Id, Page = "f. 12", Resolution = "Ages were often rounded", Resolved = true });
        p.Events.Add(e);

        var text = GedcomExporter.Export(t);
        Assert.Contains("3 QUAY 3", text);   // the standard rating, for other programs
        var back = GedcomImporter.Import(text);
        var e2 = back.People.Values.Single().Events.Single();
        Assert.Equal((FactConclusion.Proven, e.Reasoning), (e2.Conclusion, e2.Reasoning));
        var c = Assert.Single(e2.Citations);
        Assert.Equal((InformationKind.Primary, EvidenceKind.Direct), (c.Information, c.Evidence));
        Assert.Equal(SourceKind.Original, back.Sources[c.SourceId].Kind);
        var conflict = Assert.Single(e2.Conflicts);
        Assert.Equal(("Aged 13 in 1851", "1851 census", "f. 12", "Ages were often rounded", true),
                     (conflict.Claim, back.Sources[conflict.SourceId!.Value].Title, conflict.Page, conflict.Resolution, conflict.Resolved));

        // Notes left out of an export take the reasoning and conflicts with them.
        var filtered = Privacy.Filter(t, new PrivacyOptions { Living = LivingMode.Include, ExcludeNotes = true });
        var e3 = filtered.People[p.Id].Events.Single();
        Assert.Equal("", e3.Reasoning);
        Assert.Empty(e3.Conflicts);
        Assert.Equal(FactConclusion.Proven, e3.Conclusion);

        // Merging two records of a person whose births disagree leaves an open conflict to settle.
        var dup = P(t, "Nell");
        dup.Events.Add(Birth("1838", "Leeds"));
        Merger.Merge(t, p, dup);
        var merged = p.Events.Single(x => x.Tag == "BIRT");
        Assert.Contains(merged.Conflicts, x => !x.Resolved && x.Claim == "The merged record gives 1838");
    }
}
