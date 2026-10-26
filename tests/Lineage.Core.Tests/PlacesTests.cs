using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class PlacesTests
{
    private static readonly GeoPoint LeedsGps = new(53.79648, -1.54785);
    private static readonly GeoPoint OtherGps = new(53.8, -1.5);

    /// <summary>A tree with one person per place name, each with one birth there (invented data).</summary>
    private static FamilyTree WithPlaces(params string[] places)
    {
        var t = new FamilyTree();
        foreach (var place in places)
        {
            var p = t.AddPerson(new Person());
            p.PrimaryName.Given = "Test";
            p.Events.Add(Event.Of("BIRT", GenDate.Parse("1900"), place));
        }
        return t;
    }

    [Fact]
    public void BuildCountsEveryUseOfAPlace()
    {
        var t = new FamilyTree();
        var a = t.AddPerson(new Person());
        var b = t.AddPerson(new Person());
        a.Events.Add(new Event { Tag = "BIRT", Date = GenDate.Parse("1850"), Place = "Leeds, Yorkshire, England", Location = LeedsGps });
        a.Events.Add(new Event { Tag = "RESI", Date = GenDate.Parse("FROM 1870 TO 1880"), Place = "York", Place2 = " Leeds, Yorkshire, England " });
        b.Events.Add(new Event { Tag = "BIRT", Date = GenDate.Parse("1852"), Place = "Leeds, Yorkshire, England", Location = LeedsGps });
        b.Events.Add(new Event { Tag = "DEAT", Date = GenDate.Parse("1920"), Place = "Leeds, Yorkshire, England", Location = OtherGps });
        var f = t.AddFamily(new Family());
        f.Events.Add(Event.Of("MARR", GenDate.Parse("1875"), "Leeds, Yorkshire, England"));

        var places = PlaceIndex.Build(t);
        Assert.Equal(new[] { "Leeds, Yorkshire, England", "York" }, places.Select(p => p.Name));
        var leeds = places[0];
        Assert.Equal(5, leeds.EventCount);
        Assert.Equal(2, leeds.PersonCount);
        Assert.Equal(1, leeds.FamilyCount);
        Assert.Equal(LeedsGps, leeds.Location);                // the most common GPS
        Assert.Equal((1850, 1920), (leeds.FirstYear, leeds.LastYear));
        var york = places[1];
        Assert.Null(york.Location);
        Assert.Equal((1870, 1880), (york.FirstYear, york.LastYear));
    }

    [Fact]
    public void SuggestsAbbreviatedAndShorterFormsOfTheSamePlace()
    {
        var t = WithPlaces("Leeds, Yorks, Eng", "Leeds, Yorks, Eng", "Leeds, Yorkshire, England",
                           "leeds,  yorkshire , england", "Leeds", "Leek, Staffordshire, England");
        var s = Assert.Single(PlaceIndex.Suggest(t));
        Assert.Equal("Leeds, Yorkshire, England", s.Standard);
        Assert.Equal(new[] { "leeds,  yorkshire , england", "Leeds", "Leeds, Yorks, Eng" }.Order(), s.Variants.Order());
        Assert.Equal(5, s.EventCount);
        Assert.DoesNotContain("Leek, Staffordshire, England", s.Names);
    }

    [Fact]
    public void NeverGroupsPlacesWhoseFirstPartsDiffer()
    {
        Assert.Empty(PlaceIndex.Suggest(WithPlaces("Leeds", "Leek")));
        Assert.Empty(PlaceIndex.Suggest(WithPlaces("Leeds, Yorkshire", "Leek, Yorkshire")));
        Assert.Empty(PlaceIndex.Suggest(WithPlaces("Leeds", "Leek, Leeds")));
    }

    [Fact]
    public void RecognisesUsStatesAndCountryNames()
    {
        var t = WithPlaces("Boston, Suffolk, MA, USA", "Boston, Suffolk Co., Mass., U.S.A.",
                           "Boston, Suffolk County, Massachusetts, United States", "Boston, Suffolk, Massachusetts, United States of America",
                           "Boston, Lincolnshire, England");
        var s = Assert.Single(PlaceIndex.Suggest(t));
        Assert.Equal(4, s.Names.Count());
        Assert.DoesNotContain("Boston, Lincolnshire, England", s.Names);
        // Full words beat abbreviations.
        Assert.DoesNotContain("MA", s.Standard);
        Assert.DoesNotContain("Mass.", s.Standard);
        Assert.Equal(PlaceIndex.Normalize("Worcester, Worcester Co., MA, USA"), PlaceIndex.Normalize("worcester, worcester, massachusetts, united states of america"));
        Assert.Equal(PlaceIndex.Normalize("Albany, N.Y."), PlaceIndex.Normalize("Albany, New York"));
    }

    [Fact]
    public void SaintAndStAreEqualButKeptAsWritten()
    {
        var t = WithPlaces("St. Ives, Cornwall, England", "St. Ives, Cornwall, England", "Saint Ives, Cornwall, England");
        var s = Assert.Single(PlaceIndex.Suggest(t));
        Assert.Equal("St. Ives, Cornwall, England", s.Standard);   // most used; St is not expanded
        Assert.Equal(new[] { "Saint Ives, Cornwall, England" }, s.Variants);
        Assert.Equal(PlaceIndex.Normalize("Ste-Marie, Québec"), PlaceIndex.Normalize("Sainte Marie, Quebec"));
    }

    [Fact]
    public void AShorterNameMatchingTwoFullerOnesIsLeftAlone()
    {
        var t = WithPlaces("Springfield", "Springfield, Illinois, USA", "Springfield, Massachusetts, USA");
        Assert.Empty(PlaceIndex.Suggest(t));
        // A chain of ever-fuller names is one place.
        var chain = WithPlaces("Leeds", "Leeds, Yorkshire", "Leeds, Yorkshire, England");
        Assert.Equal("Leeds, Yorkshire, England", Assert.Single(PlaceIndex.Suggest(chain)).Standard);
    }

    [Fact]
    public void RenameMergesAndKeepsGps()
    {
        var t = new FamilyTree();
        var p = t.AddPerson(new Person());
        var withOwnGps = new Event { Tag = "BIRT", Place = "Leeds, Yorks", Location = OtherGps };
        var withoutGps = new Event { Tag = "BAPM", Place = " Leeds, Yorks " };
        var target = new Event { Tag = "DEAT", Place = "Leeds, Yorkshire", Location = LeedsGps };
        var period = new Event { Tag = "RESI", Place = "York", Place2 = "Leeds, Yorks" };
        p.Events.AddRange(new[] { withOwnGps, withoutGps, target, period });
        var other = t.AddPerson(new Person());
        other.Events.Add(Event.Of("BIRT", GenDate.Empty, "Bradford"));
        var f = t.AddFamily(new Family());
        f.Events.Add(Event.Of("MARR", GenDate.Empty, "Leeds, Yorks"));

        Assert.Equal(new Entity[] { p, f }, PlaceIndex.Touched(t, new[] { "Leeds, Yorks" }));
        Assert.Equal(4, PlaceIndex.Rename(t, "Leeds, Yorks", "Leeds, Yorkshire"));

        Assert.Equal(new[] { "Bradford", "Leeds, Yorkshire", "York" }, PlaceIndex.Build(t).Select(e => e.Name));
        Assert.Equal(OtherGps, withOwnGps.Location);       // kept its own
        Assert.Equal(LeedsGps, withoutGps.Location);       // took the target's
        Assert.Equal("Leeds, Yorkshire", period.Place2);
        Assert.Null(period.Location);                      // GPS belongs to Place, not Place2
        Assert.Equal(LeedsGps, f.Events[0].Location);
        Assert.Equal(0, PlaceIndex.Rename(t, "Nowhere", "Leeds"));
    }

    [Fact]
    public void ApplyingASuggestionMergesAllVariants()
    {
        var t = WithPlaces("Leeds, Yorks, Eng", "Leeds, Yorkshire, England", "Leeds");
        var s = Assert.Single(PlaceIndex.Suggest(t));
        Assert.Equal(2, PlaceIndex.ApplySuggestion(t, s));
        var only = Assert.Single(PlaceIndex.Build(t));
        Assert.Equal(("Leeds, Yorkshire, England", 3), (only.Name, only.EventCount));
    }

    [Fact]
    public void SetLocationSetsAndClearsGps()
    {
        var t = WithPlaces("Leeds", "Leeds", "York");
        t.People[3].Events.Add(new Event { Tag = "RESI", Place = "York", Place2 = "Leeds" });
        Assert.Equal(2, PlaceIndex.SetLocation(t, "Leeds", LeedsGps));
        Assert.Equal(LeedsGps, PlaceIndex.Build(t).Single(e => e.Name == "Leeds").Location);
        Assert.All(t.People[3].Events, e => Assert.Null(e.Location));   // only Place, not Place2
        Assert.Equal(0, PlaceIndex.SetLocation(t, "Leeds", LeedsGps));  // already set
        Assert.Equal(2, PlaceIndex.SetLocation(t, "Leeds", null));
        Assert.All(PlaceIndex.Build(t), e => Assert.Null(e.Location));
    }

    [Fact]
    public void PartsAreTrimmedAndEmptyOnesDropped() =>
        Assert.Equal(new[] { "Leeds", "West Yorkshire", "England" }, PlaceIndex.Parts(" Leeds ,West  Yorkshire,, England "));

    [Fact]
    public void HierarchyRunsFromCountryDown()
    {
        var t = WithPlaces("Leeds, Yorkshire, England", "Leeds, Yorkshire, England", "Leeds, Yorkshire, England",
                           "York, Yorkshire, England", "York, Yorkshire, England", "Yorkshire, England",
                           "Paris, France", "Bradford, yorkshire, England");
        var roots = PlaceIndex.Hierarchy(PlaceIndex.Build(t));
        Assert.Equal(new[] { "England", "France" }, roots.Select(r => r.Name));
        var england = roots[0];
        Assert.Equal(7, england.EventCount);
        var yorkshire = Assert.Single(england.Children);   // "yorkshire" and "Yorkshire" are one level
        Assert.Equal(("Yorkshire", "Yorkshire, England", 7), (yorkshire.Name, yorkshire.FullName, yorkshire.EventCount));
        Assert.Equal("Yorkshire, England", Assert.Single(yorkshire.Entries).Name);
        Assert.Equal(new[] { "Bradford", "Leeds", "York" }, yorkshire.Children.Select(c => c.Name));
        var leeds = yorkshire.Children[1];
        Assert.Equal(("Leeds, Yorkshire, England", 3), (leeds.FullName, leeds.EventCount));
        Assert.Empty(leeds.Children);
        Assert.Equal(1, roots[1].EventCount);
    }
}
