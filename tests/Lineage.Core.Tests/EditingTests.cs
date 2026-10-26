using Lineage.Core.Analysis;
using Lineage.Core.Editing;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

public class EditingTests
{
    private static Person P(FamilyTree t, string given, string surname, Sex sex, string? birth = null, string place = "")
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = surname;
        if (birth != null) p.Events.Add(Event.Of("BIRT", GenDate.Parse(birth), place));
        return p;
    }

    private static Family Couple(FamilyTree t, Person a, Person b, params Person[] kids)
    {
        var f = t.AddFamily(new Family());
        t.SetPartner(f, a, second: false);
        t.SetPartner(f, b, second: true);
        foreach (var k in kids) t.AddChild(f, k);
        return f;
    }

    private static string Dump(FamilyTree t) => string.Join("\n",
        t.People.Values.OrderBy(p => p.Id).Select(LineageStore.ToJson)
         .Concat(t.Families.Values.OrderBy(f => f.Id).Select(LineageStore.ToJson))
         .Concat(t.Sources.Values.OrderBy(s => s.Id).Select(LineageStore.ToJson)));

    [Fact]
    public void UndoAndRedoRestoreEveryRecord()
    {
        var t = RoundTripTests.SampleTree();
        var history = new UndoHistory(t);
        var start = Dump(t);
        var dad = t.People[1];

        var edit = history.Begin("Add child", t.Around(dad));
        var kid = P(t, "New", "Doe", Sex.Male, "1931");
        t.AddChild(t.Family(dad.SpouseFamilyIds[0])!, kid);
        dad.Note = "changed";
        edit.Commit();
        var after = Dump(t);
        Assert.NotEqual(start, after);

        Assert.Equal("Add child", history.Undo());
        Assert.Equal(start, Dump(t));
        Assert.Equal("Add child", history.Redo());
        Assert.Equal(after, Dump(t));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void UndoOfDeleteBringsLinksBack()
    {
        var t = RoundTripTests.SampleTree();
        var history = new UndoHistory(t);
        var start = Dump(t);
        var kid = t.People[3];
        var edit = history.Begin("Delete", t.Around(kid));
        t.RemovePerson(kid);
        edit.Commit();
        Assert.Null(t.Person(3));
        history.Undo();
        Assert.Equal(start, Dump(t));
    }

    [Fact]
    public void UnchangedOrCancelledEditsAreNotRecorded()
    {
        var t = RoundTripTests.SampleTree();
        var history = new UndoHistory(t);
        history.Begin("Nothing", t.Around(t.People[1])).Commit();
        var e = history.Begin("Cancelled", t.Around(t.People[1]));
        e.Cancel();
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void MergeKeepsEverythingAndMovesLinks()
    {
        var t = new FamilyTree();
        var dad = P(t, "John", "Pemberwick", Sex.Male, "1850");
        var mum = P(t, "Mary", "Jones", Sex.Female, "1852");
        var kid1 = P(t, "Will", "Pemberwick", Sex.Male, "1875", "Leeds");
        Couple(t, dad, mum, kid1);
        // A second copy of the father with a second copy of the family (e.g. from another GEDCOM).
        var dad2 = P(t, "John Henry", "Pemberwick", Sex.Male, "ABT 1850");
        dad2.Events.Add(Event.Of("DEAT", GenDate.Parse("1920"), "York"));
        dad2.Media.Add(new MediaLink { File = "a.jpg" });
        var kid2 = P(t, "Ann", "Pemberwick", Sex.Female, "1877");
        Couple(t, dad2, mum, kid2);
        var history = new UndoHistory(t);
        var before = Dump(t);

        var edit = history.Begin("Merge", Merger.Touched(t, dad, dad2));
        Merger.Merge(t, dad, dad2);
        edit.Commit();

        Assert.Null(t.Person(dad2.Id));
        Assert.Contains(dad.Names, n => n.Full == "John Henry Pemberwick" && n.Type == "aka");
        Assert.Equal("1920", dad.Find("DEAT")!.Date.ToGedcom());
        Assert.Single(dad.Media);
        // Both families had the same couple, so they become one with both children.
        var fam = Assert.Single(dad.SpouseFamilyIds);
        Assert.Single(mum.SpouseFamilyIds);
        Assert.Equal(new[] { kid1.Id, kid2.Id }, t.Family(fam)!.Children.Select(c => c.ChildId));
        Assert.Equal(fam, kid2.ParentFamilyIds.Single());
        Assert.Single(t.Families);

        history.Undo();
        Assert.Equal(before, Dump(t));
    }

    [Fact]
    public void FindsLikelyDuplicatesButNotNamesakeSiblings()
    {
        var t = new FamilyTree();
        var dad = P(t, "John", "Pemberwick", Sex.Male, "1850");
        var mum = P(t, "Mary", "Jones", Sex.Female, "1852");
        var a = P(t, "Thomas", "Pemberwick", Sex.Male, "3 MAR 1880", "Leeds, Yorkshire");
        Couple(t, dad, mum, a);
        var b = P(t, "Thomas", "Pemberwyck", Sex.Male, "1880", "Leeds");    // a likely duplicate
        var c = P(t, "Thomas", "Pemberwick", Sex.Male, "1930");             // same name, different generation
        var d = P(t, "Thomas", "Pemberwick", Sex.Female, "1880");           // different sex
        var e1 = P(t, "Henry", "Pemberwick", Sex.Male, "1882");             // namesake siblings
        var e2 = P(t, "Henry", "Pemberwick", Sex.Male, "1886");
        t.AddChild(t.Family(dad.SpouseFamilyIds[0])!, e1);
        t.AddChild(t.Family(dad.SpouseFamilyIds[0])!, e2);

        var found = Duplicates.Find(t);
        var pair = Assert.Single(found);
        Assert.Equal((a.Id, b.Id), (pair.A.Id, pair.B.Id));
        Assert.Contains("Surnames sound alike", pair.Reasons);

        t.NotDuplicates.Add(FamilyTree.PairKey(b.Id, a.Id));
        Assert.Empty(Duplicates.Find(t));
        _ = (c, d);
    }

    [Theory]
    [InlineData("Robert", "R163")]
    [InlineData("Rupert", "R163")]
    [InlineData("Ashcraft", "A261")]
    [InlineData("Tymczak", "T522")]
    [InlineData("Pfister", "P236")]
    [InlineData("Lee", "L000")]
    public void SoundexMatchesTheStandardExamples(string name, string code) =>
        Assert.Equal(code, Duplicates.Soundex(name));

    [Fact]
    public void AncestorsAreNumberedAhnentafelStyle()
    {
        var t = new FamilyTree();
        var me = P(t, "Me", "A", Sex.Male);
        var dad = P(t, "Dad", "A", Sex.Male);
        var mum = P(t, "Mum", "B", Sex.Female);
        var gm = P(t, "Gran", "C", Sex.Female);
        Couple(t, dad, mum, me);
        var f = t.AddFamily(new Family());
        t.SetPartner(f, gm, second: true);
        t.AddChild(f, mum);
        var a = t.Ancestors(me, 4);
        Assert.Equal(new[] { 1, 2, 3, 7 }, a.Keys.Order());
        Assert.Same(gm, a[7]);
    }
}
