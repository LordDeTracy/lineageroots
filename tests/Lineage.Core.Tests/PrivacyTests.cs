using Lineage.Core.Export;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

public class PrivacyTests
{
    private const int Now = 2026;

    private static Person P(FamilyTree t, string given, string surname, Sex sex, string? birth = null)
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = surname;
        if (birth != null) p.Events.Add(Event.Of("BIRT", GenDate.Parse(birth), "Testville"));
        return p;
    }

    private static Family Couple(FamilyTree t, Person? a, Person? b, params Person[] kids)
    {
        var f = t.AddFamily(new Family());
        if (a != null) t.SetPartner(f, a, second: false);
        if (b != null) t.SetPartner(f, b, second: true);
        foreach (var k in kids) t.AddChild(f, k);
        return f;
    }

    private static bool Living(FamilyTree t, Person p) => Privacy.IsLiving(t, p, 110, Now);

    private static string Dump(FamilyTree t) => string.Join("\n",
        t.People.Values.Select(LineageStore.ToJson)
         .Concat(t.Families.Values.Select(LineageStore.ToJson))
         .Concat(t.Sources.Values.Select(LineageStore.ToJson)));

    [Fact]
    public void DeathOrBurialMeansNotLiving()
    {
        var t = new FamilyTree();
        var young = P(t, "Ann", "Test", Sex.Female, "2000");
        Assert.True(Living(t, young));
        young.Events.Add(Event.Of("DEAT"));                 // no date needed
        Assert.False(Living(t, young));
        var buried = P(t, "Bob", "Test", Sex.Male);
        buried.Events.Add(Event.Of("BURI", GenDate.Parse("2010")));
        Assert.False(Living(t, buried));
        var cremated = P(t, "Cy", "Test", Sex.Male);
        cremated.Events.Add(Event.Of("CREM"));
        Assert.False(Living(t, cremated));
    }

    [Fact]
    public void BirthYearDecides()
    {
        var t = new FamilyTree();
        Assert.False(Living(t, P(t, "Old", "Test", Sex.Male, "1900")));
        Assert.True(Living(t, P(t, "Young", "Test", Sex.Male, "1950")));
        Assert.True(Living(t, P(t, "Edge", "Test", Sex.Male, "1916")));     // exactly 110 years
        Assert.False(Living(t, P(t, "Over", "Test", Sex.Male, "1915")));
        Assert.True(Living(t, P(t, "Unknown", "Test", Sex.Male)));         // no evidence: protect
        Assert.False(Privacy.IsLiving(t, P(t, "Short", "Test", Sex.Male, "1950"), 70, Now));
    }

    [Fact]
    public void OwnOldEventMeansNotLiving()
    {
        var t = new FamilyTree();
        var p = P(t, "Census", "Test", Sex.Male);
        p.Events.Add(Event.Of("CENS", GenDate.Parse("1881")));
        Assert.False(Living(t, p));
    }

    [Fact]
    public void WithoutABirthYearRelativesDecide()
    {
        var t = new FamilyTree();
        // A child born more than 95 (110 − 15) years ago.
        var parentOfOld = P(t, "Pat", "Test", Sex.Male);
        Couple(t, parentOfOld, null, P(t, "Kid", "Test", Sex.Male, "1920"));
        Assert.False(Living(t, parentOfOld));
        var parentOfYoung = P(t, "Pam", "Test", Sex.Female);
        Couple(t, null, parentOfYoung, P(t, "Kid", "Test", Sex.Male, "1950"));
        Assert.True(Living(t, parentOfYoung));

        // A parent born more than 125 (110 + 15) years ago.
        var childOfOld = P(t, "Carl", "Test", Sex.Male);
        Couple(t, P(t, "Dad", "Test", Sex.Male, "1890"), null, childOfOld);
        Assert.False(Living(t, childOfOld));
        var childOfYoung = P(t, "Cara", "Test", Sex.Female);
        Couple(t, P(t, "Dad", "Test", Sex.Male, "1920"), null, childOfYoung);
        Assert.True(Living(t, childOfYoung));

        // A marriage more than 95 years ago.
        var married = P(t, "Mo", "Test", Sex.Male);
        Couple(t, married, P(t, "Wife", "Test", Sex.Female)).Events.Add(Event.Of("MARR", GenDate.Parse("ABT 1925")));
        Assert.False(Living(t, married));
        var marriedRecently = P(t, "Ned", "Test", Sex.Male);
        Couple(t, marriedRecently, null).Events.Add(Event.Of("MARR", GenDate.Parse("1960")));
        Assert.True(Living(t, marriedRecently));
    }

    /// <summary>Grandad (dead) – Dad (living, lots of detail) + Mum (living) – Kid (living); Mum has a sister with no family.</summary>
    private static (FamilyTree Tree, Person Grandad, Person Dad, Person Mum, Person Kid, Family Parents, Family Couple) Sample()
    {
        var t = new FamilyTree();
        var source = t.AddSource(new Source { Title = "Invented register", Note = "source note" });
        var grandad = P(t, "Albert", "Example", Sex.Male, "1890");
        grandad.Note = "grandad note";
        grandad.Media.Add(new MediaLink { File = "grandad.jpg" });
        grandad.Events.Add(new Event { Tag = "DEAT", Date = GenDate.Parse("1960"), Note = "event note" });
        grandad.Events.Add(new Event { Tag = "MILI", Private = true, Description = "secret" });

        var dad = P(t, "Brian", "Example", Sex.Male, "1960");
        dad.Names.Add(new Name { Given = "Bri", Surname = "Example", Type = "aka" });
        dad.Note = "dad note";
        dad.Email = "brian@example.invalid";
        dad.Phone = "000";
        dad.Address = "1 Test Street";
        dad.FamilySearchId = "AAAA-111";
        dad.DnaTests.Add(new DnaTest { Kind = "Y-DNA", Haplogroup = "R1b" });
        dad.Media.Add(new MediaLink { File = "dad.jpg" });
        dad.Citations.Add(new Citation { SourceId = source.Id, Page = "p. 1" });
        dad.Events[0].Citations.Add(new Citation { SourceId = source.Id, Page = "p. 2" });
        var parents = Couple(t, grandad, null, dad);

        var mum = P(t, "Carol", "Sample", Sex.Female, "1962");
        var kid = P(t, "Dana", "Example", Sex.Female, "1990");
        var couple = Couple(t, dad, mum, kid);
        couple.Events.Add(Event.Of("MARR", GenDate.Parse("1988"), "Testville"));
        couple.Note = "couple note";

        var aunt = P(t, "Erin", "Sample", Sex.Female, "1965");
        Couple(t, null, null, aunt);
        return (t, grandad, dad, mum, kid, parents, couple);
    }

    [Fact]
    public void PrivatizeHidesDetailsButKeepsLinks()
    {
        var (t, grandad, dad, _, kid, parents, couple) = Sample();
        var before = Dump(t);

        var copy = Privacy.Filter(t, new PrivacyOptions(), Now);

        Assert.Equal(before, Dump(t));                                    // the original is untouched
        Assert.Equal(t.People.Keys, copy.People.Keys);                    // ids kept
        Assert.Equal(t.Families.Keys, copy.Families.Keys);
        var d = copy.People[dad.Id];
        Assert.NotSame(dad, d);
        Assert.Equal(("Living", "Example"), (d.PrimaryName.Given, d.PrimaryName.Surname));
        Assert.Single(d.Names);
        Assert.Empty(d.Events);
        Assert.Empty(d.Citations);
        Assert.Empty(d.Media);
        Assert.Empty(d.DnaTests);
        Assert.Equal("", d.Note + d.Email + d.Phone + d.Address + d.FamilySearchId);
        Assert.Equal(dad.Uid, d.Uid);
        Assert.Equal(new[] { parents.Id }, d.ParentFamilyIds);
        Assert.Equal(new[] { couple.Id }, d.SpouseFamilyIds);
        Assert.Equal(kid.Id, copy.Families[couple.Id].Children.Single().ChildId);
        Assert.Empty(copy.Families[couple.Id].Events);                     // a living couple's marriage
        Assert.Equal("", copy.Families[couple.Id].Note);

        var g = copy.People[grandad.Id];
        Assert.Equal("Albert", g.PrimaryName.Given);
        Assert.Equal(new[] { "BIRT", "DEAT" }, g.Events.Select(e => e.Tag));  // private MILI removed
        Assert.Equal("grandad note", g.Note);
        Assert.Single(copy.Sources);
    }

    [Fact]
    public void ExcludeRemovesLivingPeopleAndEmptyFamilies()
    {
        var (t, grandad, dad, _, _, parents, couple) = Sample();
        var before = Dump(t);

        var copy = Privacy.Filter(t, new PrivacyOptions { Living = LivingMode.Exclude }, Now);

        Assert.Equal(before, Dump(t));
        Assert.Equal(new[] { grandad.Id }, copy.People.Keys);
        Assert.Equal(new[] { parents.Id }, copy.Families.Keys);           // the couple and the aunt's family are gone
        var f = copy.Families[parents.Id];
        Assert.Equal(grandad.Id, f.Partner1Id);
        Assert.Empty(f.Children);
        Assert.Equal(new[] { parents.Id }, copy.People[grandad.Id].SpouseFamilyIds);
        Assert.Single(copy.Sources);
        Assert.True(t.Families.ContainsKey(couple.Id) && t.People.ContainsKey(dad.Id));
    }

    [Fact]
    public void IncludeKeepsLivingPeopleButOptionsStillApply()
    {
        var (t, grandad, dad, _, _, _, couple) = Sample();
        var before = Dump(t);
        var copy = Privacy.Filter(t, new PrivacyOptions
        {
            Living = LivingMode.Include, ExcludePrivateEvents = false, ExcludeNotes = true, ExcludeMedia = true,
        }, Now);

        Assert.Equal(before, Dump(t));
        Assert.Equal("Brian", copy.People[dad.Id].PrimaryName.Given);
        Assert.Equal("AAAA-111", copy.People[dad.Id].FamilySearchId);
        var g = copy.People[grandad.Id];
        Assert.Contains(g.Events, e => e.Tag == "MILI");                  // private events kept when asked
        Assert.Equal("", g.Note);
        Assert.All(g.Events, e => Assert.Equal("", e.Note));
        Assert.Empty(g.Media);
        Assert.Empty(copy.People[dad.Id].Media);
        Assert.Equal("", copy.Families[couple.Id].Note);
        Assert.Equal("", copy.Sources.Values.Single().Note);
        Assert.Equal("grandad note", t.People[grandad.Id].Note);
    }

    [Fact]
    public void PrivateEventsAreRemovedFromFamiliesToo()
    {
        var t = new FamilyTree();
        var a = P(t, "Old", "Test", Sex.Male, "1800");
        var b = P(t, "Older", "Test", Sex.Female, "1801");
        var f = Couple(t, a, b);
        f.Events.Add(Event.Of("MARR", GenDate.Parse("1825")));
        f.Events.Add(new Event { Tag = "DIV", Private = true });
        var copy = Privacy.Filter(t, new PrivacyOptions(), Now);
        Assert.Equal(new[] { "MARR" }, copy.Families[f.Id].Events.Select(e => e.Tag));
        Assert.Equal(2, f.Events.Count);
    }
}
