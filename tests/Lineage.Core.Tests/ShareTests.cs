using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Lineage.Core.Sharing;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

public class ShareTests
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

    private static FamilyTree Ours(out Person amos, out Person lettice, out Person nell, out Person uncle, out Family family)
    {
        var t = new FamilyTree { Name = "Quorrow family" };
        var register = t.AddSource(new Source { Title = "Leeds parish register", Kind = SourceKind.Original });
        var birth = E("BIRT", "1840", "Leeds, Yorkshire, England");
        birth.Citations.Add(new Citation { SourceId = register.Id, Page = "f. 3" });
        amos = P(t, "Amos", "Quorrow", Sex.Male, birth, E("DEAT", "1901"));
        lettice = P(t, "Lettice", "Brandle", Sex.Female, E("BIRT", "1842"), E("DEAT", "1903"));
        nell = P(t, "Nell", "Quorrow", Sex.Female, E("BIRT", "1863"), E("DEAT", "1930"));
        uncle = P(t, "Obadiah", "Quorrow", Sex.Male, E("BIRT", "1838"), E("DEAT", "1890"));
        family = t.AddFamily(new Family());
        t.SetPartner(family, amos, second: false);
        t.SetPartner(family, lettice, second: true);
        t.AddChild(family, nell);
        family.Events.Add(E("MARR", "1862", "Leeds, Yorkshire, England"));
        return t;
    }

    /// <summary>The relative's copy after a trip through a file, as it would arrive back.</summary>
    private static FamilyTree RoundTrip(FamilyTree copy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-share-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(copy, path);
            return LineageStore.Load(path);
        }
        finally { File.Delete(path); }
    }

    private static Person Find(FamilyTree t, Person like) => t.People.Values.Single(p => p.Uid == like.Uid);

    /// <summary>The relative's edits: a corrected date, a new fact, a new child with a new source, a renamed and a removed person.</summary>
    private static void TheirEdits(FamilyTree theirs, Person amos, Person lettice, Person nell, Person uncle, Family family)
    {
        Find(theirs, amos).Events.Single(e => e.Tag == "BIRT").Date = GenDate.Parse("12 MAR 1840");
        Find(theirs, lettice).Events.Add(E("BURI", "1903", "Leeds, Yorkshire, England"));
        Find(theirs, nell).PrimaryName.Given = "Eleanor";
        theirs.RemovePerson(Find(theirs, uncle));
        var baptism = theirs.AddSource(new Source { Title = "Baptism of Bert" });
        var bertBirth = E("BIRT", "1866", "Leeds, Yorkshire, England");
        bertBirth.Citations.Add(new Citation { SourceId = baptism.Id });
        var bert = P(theirs, "Bert", "Quorrow", Sex.Male, bertBirth);
        theirs.AddChild(theirs.Families.Values.Single(f => f.Uid == family.Uid), bert);
    }

    [Fact]
    public void TheirChangesAreFoundAndYoursAreLeftAlone()
    {
        var ours = Ours(out var amos, out var lettice, out var nell, out var uncle, out var family);
        var (copy, share) = ShareReview.CreateCopy(ours, "Cousin Ada", new PrivacyOptions { Living = LivingMode.Include });
        Assert.Same(share, Assert.Single(ours.Shares));
        Assert.Equal(share.Id, copy.SharedFrom!.Id);
        Assert.Equal("Cousin Ada", share.With);

        var theirs = RoundTrip(copy);
        Assert.Equal(share.Id, theirs.SharedFrom!.Id);   // the copy remembers where it came from
        Assert.Empty(ShareReview.Compare(ours, theirs, share));   // nothing changed yet

        TheirEdits(theirs, amos, lettice, nell, uncle, family);
        // Meanwhile you edited too: a note (they didn't touch it) and Nell's name (so did they).
        amos.Note = "Wool comber all his life";
        nell.PrimaryName.Given = "Nellie";

        var changes = ShareReview.Compare(ours, theirs, share);
        string Describe(ShareChange c) => $"{c.Who} | {c.What} | {c.Kind}{(c.Conflict ? " | conflict" : "")}";
        Assert.Equal(new[]
        {
            "Amos Quorrow | Birth | Changed",
            "Amos Quorrow and Lettice Brandle | Child: Bert Quorrow | Added",
            "Bert Quorrow | New person | Added",
            "Lettice Brandle | Burial | Added",
            "Nellie Quorrow | Names | Changed | conflict",
            "Obadiah Quorrow | Removed from their copy | Removed",
            "Sources | Source: Baptism of Bert | Added",
        }, changes.Select(Describe));
        var nellChange = changes.Single(c => c.What == "Names");
        Assert.Equal(("Nellie Quorrow", "Eleanor Quorrow"), (nellChange.Yours, nellChange.Theirs));
        Assert.Contains("12 March 1840", changes.Single(c => c.What == "Birth").Theirs);
        Assert.DoesNotContain(changes, c => c.What == "Notes");
    }

    [Fact]
    public void AcceptedChangesAreAppliedAndRefusedOnesStayRefused()
    {
        var ours = Ours(out var amos, out var lettice, out var nell, out var uncle, out var family);
        var (copy, share) = ShareReview.CreateCopy(ours, "Cousin Ada", new PrivacyOptions { Living = LivingMode.Include });
        var theirs = RoundTrip(copy);
        TheirEdits(theirs, amos, lettice, nell, uncle, family);
        nell.PrimaryName.Given = "Nellie";

        var changes = ShareReview.Compare(ours, theirs, share);
        var refused = changes.Where(c => c.What == "Names").ToList();
        var accepted = changes.Except(refused).ToList();
        var touched = ShareReview.Touched(ours, theirs, accepted);
        Assert.Contains(amos, touched);
        Assert.Contains(uncle, touched);
        Assert.Contains(family, touched);

        ShareReview.Apply(ours, theirs, accepted);
        ShareReview.Reject(share, refused);

        Assert.Equal("12 MAR 1840", amos.Events.Single(e => e.Tag == "BIRT").Date.ToGedcom());
        Assert.Contains(lettice.Events, e => e.Tag == "BURI");
        Assert.Equal("Nellie", nell.PrimaryName.Given);
        Assert.False(ours.People.ContainsKey(uncle.Id));
        var bert = ours.People.Values.Single(p => p.PrimaryName.Given == "Bert");
        Assert.Equal(new[] { family.Id }, bert.ParentFamilyIds);
        Assert.Contains(family.Children, c => c.ChildId == bert.Id);
        var source = ours.Sources[bert.Events.Single().Citations.Single().SourceId];
        Assert.Equal("Baptism of Bert", source.Title);
        Assert.Equal(2, ours.Sources.Count);
        // The original citation still points at the original source.
        Assert.Equal("Leeds parish register", ours.Sources[amos.Events.Single(e => e.Tag == "BIRT").Citations.Single().SourceId].Title);

        // Applied changes now match; the refused one isn't offered again...
        Assert.Empty(ShareReview.Compare(ours, theirs, share));
        // ...unless they change it again.
        Find(theirs, nell).PrimaryName.Given = "Ellen";
        Assert.Equal("Names", Assert.Single(ShareReview.Compare(ours, theirs, share)).What);
    }

    [Fact]
    public void ACopyReturnedAsGedcomStillMatches()
    {
        var ours = Ours(out var amos, out var lettice, out var nell, out var uncle, out var family);
        var (copy, share) = ShareReview.CreateCopy(ours, "Cousin Ada", new PrivacyOptions { Living = LivingMode.Include });
        var theirs = GedcomImporter.Import(GedcomExporter.Export(RoundTrip(copy)));
        Assert.Equal(share.Id, theirs.SharedFrom!.Id);
        var differences = ShareReview.Compare(ours, theirs, share);
        Assert.True(differences.Count == 0, string.Join(" ;; ", differences.Select(c => $"{c.Key} {c.What}: [{c.Yours}] -> [{c.Theirs}]")));   // ids of people, families, sources and facts all survive

        TheirEdits(theirs, amos, lettice, nell, uncle, family);
        Assert.Equal(7, ShareReview.Compare(ours, theirs, share).Count);
    }

    [Fact]
    public void YouDeletingSomeoneTheyEditedIsAConflict()
    {
        var ours = Ours(out var amos, out _, out _, out var uncle, out _);
        var (copy, share) = ShareReview.CreateCopy(ours, "Cousin Ada", new PrivacyOptions { Living = LivingMode.Include });
        var theirs = RoundTrip(copy);
        ours.RemovePerson(uncle);
        Assert.Empty(ShareReview.Compare(ours, theirs, share));   // they didn't touch him: nothing to say

        Find(theirs, uncle).Events.Add(E("OCCU", "1870"));
        var change = Assert.Single(ShareReview.Compare(ours, theirs, share));
        Assert.Equal((ChangeKind.Added, true), (change.Kind, change.Conflict));
        ShareReview.Apply(ours, theirs, new[] { change });
        Assert.Contains(ours.People.Values, p => p.Uid == uncle.Uid && p.Events.Count == 3);
    }

    [Fact]
    public void PrivateThingsStayHome()
    {
        var ours = Ours(out var amos, out _, out _, out _, out _);
        ours.AddResearch(new ResearchItem { Description = "Find Amos in 1851" });
        amos.ResearchIds.Add(ours.Research.Keys.Single());
        amos.DnaMatches.Add(new DnaMatch { Name = "A living cousin", SharedCm = 100 });
        var (copy, _) = ShareReview.CreateCopy(ours, "Cousin Ada", new PrivacyOptions { Living = LivingMode.Include });
        Assert.Empty(copy.Research);
        Assert.All(copy.People.Values, p => Assert.Empty(p.ResearchIds));
        Assert.All(copy.People.Values, p => Assert.Empty(p.DnaMatches));
        Assert.Empty(copy.Shares);
    }
}

/// <summary>Each private sample tree, shared and returned untouched, must show no changes (no-op without the samples).</summary>
public class SampleShareTests
{
    public static TheoryData<string> Names() => Samples.Names();

    [Theory]
    [MemberData(nameof(Names))]
    public void AnUntouchedCopyComesBackWithNoChanges(string name)
    {
        if (Samples.Get(name) is not { } s) return;
        var ours = Lineage.Core.Import.Aq.AqImporter.Import(s.Aq);
        var (copy, share) = ShareReview.CreateCopy(ours, "test", new PrivacyOptions { Living = LivingMode.Include });
        var path = Path.Combine(Path.GetTempPath(), $"lineage-sample-share-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(copy, path);
            var viaFile = LineageStore.Load(path);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var changes = ShareReview.Compare(ours, viaFile, share);
            Assert.True(changes.Count == 0, $"{changes.Count} false changes, e.g. {changes.FirstOrDefault()?.Key} {changes.FirstOrDefault()?.What}");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"compare took {watch.Elapsed}");

            var viaGedcom = GedcomImporter.Import(GedcomExporter.Export(viaFile));
            changes = ShareReview.Compare(ours, viaGedcom, share);
            Assert.True(changes.Count == 0, $"{changes.Count} false changes via GEDCOM, e.g. {changes.FirstOrDefault()?.Key} {changes.FirstOrDefault()?.What}: [{changes.FirstOrDefault()?.Yours}] -> [{changes.FirstOrDefault()?.Theirs}]");
        }
        finally { File.Delete(path); }
    }
}
