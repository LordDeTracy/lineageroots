using System.Text.Json;
using Lineage.Core.FamilySearch;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

/// <summary>
/// The FamilySearch API itself needs an app key and a signed-in user, so these tests check the
/// parts around it: reading GEDCOM X responses (shaped like FamilySearch's documented examples),
/// comparing, and merging downloaded ancestors into a tree.
/// </summary>
public class FamilySearchTests
{
    private const string Ancestry = """
    {
      "persons": [
        { "id": "KWCB-1", "living": false, "gender": { "type": "http://gedcomx.org/Male" },
          "names": [ { "preferred": true, "nameForms": [ { "fullText": "Thomas Edward Whitfield",
             "parts": [ { "type": "http://gedcomx.org/Given", "value": "Thomas Edward" },
                        { "type": "http://gedcomx.org/Surname", "value": "Whitfield" } ] } ] } ],
          "facts": [ { "type": "http://gedcomx.org/Birth", "date": { "original": "14 March 1842" }, "place": { "original": "Harrow, Middlesex, England" } } ],
          "display": { "name": "Thomas Edward Whitfield", "lifespan": "1842-1911", "ascendancyNumber": "1" } },
        { "id": "KWCB-2", "gender": { "type": "http://gedcomx.org/Male" },
          "display": { "name": "George Whitfield", "birthDate": "about 1815", "birthPlace": "Harrow", "ascendancyNumber": "2" } },
        { "id": "KWCB-3", "gender": { "type": "http://gedcomx.org/Female" },
          "display": { "name": "Mary Ann Cole", "birthDate": "1818", "ascendancyNumber": "3" } },
        { "id": "KWCB-7", "gender": { "type": "http://gedcomx.org/Female" },
          "display": { "name": "Sarah Bell", "ascendancyNumber": "7" } },
        { "id": "KWCB-9", "display": { "name": "Orphan Line", "ascendancyNumber": "9" } }
      ]
    }
    """;

    private static List<FsPerson> Parse(string json) => GedcomX.Persons(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void ReadsNamesFactsAndNumbers()
    {
        var people = Parse(Ancestry);
        var me = people[0];
        Assert.Equal(("Thomas Edward", "Whitfield", Sex.Male, 1), (me.Given, me.Surname, me.Sex, me.AscendancyNumber));
        Assert.Equal(new FsFact("BIRT", "14 March 1842", "Harrow, Middlesex, England"), me.Fact("BIRT"));
        // A summary-only person: the name is split and display dates become facts.
        var george = people[1];
        Assert.Equal(("George", "Whitfield"), (george.Given, george.Surname));
        Assert.Equal("about 1815", george.Fact("BIRT")!.Date);
        Assert.Equal(Sex.Female, people[2].Sex);
    }

    [Fact]
    public void ReadsSearchResults()
    {
        var json = """
        { "entries": [ { "id": "ABCD-123", "score": 4.5, "content": { "gedcomx": { "persons": [
            { "id": "ABCD-123", "display": { "name": "Eliza Hartley", "gender": "Female", "birthDate": "1845", "lifespan": "1845-1920" } } ] } } } ] }
        """;
        var r = Assert.Single(GedcomX.SearchResults(JsonDocument.Parse(json).RootElement));
        Assert.Equal(("ABCD-123", "Eliza", "Hartley", Sex.Female, 4.5), (r.Id, r.Given, r.Surname, r.Sex, r.Score));
    }

    [Fact]
    public void AddsMissingAncestorsAndLinksExistingOnes()
    {
        var t = new FamilyTree();
        var me = t.AddPerson(new Person { Sex = Sex.Male });
        me.PrimaryName.Given = "Thomas Edward"; me.PrimaryName.Surname = "Whitfield";
        // The father is already in the tree; the mother isn't.
        var dad = t.AddPerson(new Person { Sex = Sex.Male });
        dad.PrimaryName.Given = "George"; dad.PrimaryName.Surname = "Whitfield";
        var fam = t.AddFamily(new Family());
        t.SetPartner(fam, dad, second: false);
        t.AddChild(fam, me);

        var added = FsSync.AddAncestors(t, me, Parse(Ancestry));

        Assert.Equal(2, added);                              // mother (3) and maternal grandmother (7)
        Assert.Equal("KWCB-1", me.FamilySearchId);
        Assert.Equal("KWCB-2", dad.FamilySearchId);          // linked, not duplicated
        var (f, m) = t.PrimaryParents(me);
        Assert.Same(dad, f);
        Assert.Equal("Mary Ann Cole", m!.DisplayName);
        Assert.Equal("Sarah Bell", t.PrimaryParents(m).Mother!.DisplayName);
        Assert.DoesNotContain(t.People.Values, p => p.DisplayName == "Orphan Line"); // its child (4) wasn't in the data
        Assert.Equal(4, t.People.Count);

        // Running it again adds nobody.
        Assert.Equal(0, FsSync.AddAncestors(t, me, Parse(Ancestry)));
    }

    [Fact]
    public void DifferentYearsDifferButSameDateWrittenDifferentlyDoesNot()
    {
        Assert.True(new FieldDiff("BIRT.date", "Birth date", "1850", "1851").Differs);
        Assert.False(new FieldDiff("BIRT.date", "Birth date", "14 MAR 1842", "14 March 1842").Differs);
        Assert.True(new FieldDiff("BIRT.place", "Birth place", "Ward 1, Leeds", "Ward 2, Leeds").Differs);
        Assert.False(new FieldDiff("given", "Given names", "José", "jose").Differs);
    }

    [Fact]
    public void ComparesAndCopiesChosenFields()
    {
        var me = new Person { Sex = Sex.Male };
        me.PrimaryName.Given = "Thomas"; me.PrimaryName.Surname = "Whitfield";
        me.Events.Add(Event.Of("BIRT", GenDate.Parse("1842"), "Harrow"));
        var fs = Parse(Ancestry)[0];
        var diffs = FsSync.Compare(me, fs);
        Assert.Contains(diffs, d => d.Key == "given" && d.Differs);
        Assert.Contains(diffs, d => d.Key == "surname" && !d.Differs);
        FsSync.Apply(me, fs, new[] { "given", "BIRT.date" });
        Assert.Equal("Thomas Edward", me.PrimaryName.Given);
        Assert.Equal("14 MAR 1842", me.Find("BIRT")!.Date.ToGedcom());
        Assert.Equal("Harrow", me.Find("BIRT")!.Place);       // not chosen, left alone
        Assert.Equal("KWCB-1", me.FamilySearchId);
    }
}

public class FamilySearchDescendancyTests
{
    private const string Descendancy = """
    {
      "persons": [
        { "id": "KWD-1", "gender": { "type": "http://gedcomx.org/Male" }, "display": { "name": "George Whitfield", "descendancyNumber": "1" } },
        { "id": "KWD-2", "gender": { "type": "http://gedcomx.org/Female" }, "display": { "name": "Mary Ann Cole", "descendancyNumber": "1-S" } },
        { "id": "KWD-3", "gender": { "type": "http://gedcomx.org/Male" }, "display": { "name": "Thomas Whitfield", "descendancyNumber": "1.1" } },
        { "id": "KWD-4", "gender": { "type": "http://gedcomx.org/Female" }, "display": { "name": "Ellen Whitfield", "descendancyNumber": "1.2" } },
        { "id": "KWD-5", "gender": { "type": "http://gedcomx.org/Female" }, "display": { "name": "Sarah Bell", "descendancyNumber": "1.1-S" } },
        { "id": "KWD-6", "display": { "name": "Arthur Whitfield", "descendancyNumber": "1.1.1" } }
      ]
    }
    """;

    [Fact]
    public void DescendantsAreAddedOnceAndLinked()
    {
        var people = GedcomX.Persons(System.Text.Json.JsonDocument.Parse(Descendancy).RootElement);
        Assert.Equal("1.1-S", people[4].DescendancyNumber);

        var t = new FamilyTree();
        var george = t.AddPerson(new Person { Sex = Sex.Male, Names = { new Name { Given = "George", Surname = "Whitfield" } } });
        var fam = t.AddFamily(new Family());
        t.SetPartner(fam, george, second: false);
        var thomas = t.AddPerson(new Person { Sex = Sex.Male, Names = { new Name { Given = "Thomas", Surname = "Whitfield" } } });
        t.AddChild(fam, thomas);

        var added = FsSync.AddDescendants(t, george, people);

        Assert.Equal(4, added);                              // Mary Ann, Ellen, Sarah, Arthur; Thomas was already there
        Assert.Equal("KWD-1", george.FamilySearchId);
        Assert.Equal("KWD-3", thomas.FamilySearchId);
        Assert.Equal("Mary Ann Cole", t.Person(fam.Partner2Id)!.DisplayName);
        Assert.Equal(2, fam.Children.Count);
        var arthur = t.People.Values.Single(p => p.DisplayName == "Arthur Whitfield");
        var (f, m) = t.PrimaryParents(arthur);
        Assert.Same(thomas, f);
        Assert.Equal("Sarah Bell", m!.DisplayName);

        Assert.Equal(0, FsSync.AddDescendants(t, george, people));
        Assert.Equal(6, t.People.Count);
    }
}
