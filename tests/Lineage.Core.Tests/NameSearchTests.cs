using Lineage.Core.Analysis;
using Lineage.Core.Model;
using static Lineage.Core.Tests.AnalysisTestData;

namespace Lineage.Core.Tests;

public class NameSearchTests
{
    private readonly FamilyTree _t = new();
    private readonly Person _john, _jon, _johann, _mary, _jonathan, _jose, _kameron;

    public NameSearchTests()
    {
        _john = P(_t, "John", "Pemberwick", Sex.Male, "1850");
        _jon = P(_t, "Jon", "Pemberwyck", Sex.Male, "1852");
        _johann = P(_t, "Johann", "Pembervik", Sex.Male, "1849");
        _mary = P(_t, "Mary", "Baker", Sex.Female, "1900");
        _mary.Names.Add(new Name { Given = "Mary", Surname = "Pemberwick", Type = "married" });
        _jonathan = P(_t, "Jonathan", "Pemberwickes", Sex.Male, "1851", "1910");
        _jose = P(_t, "José", "Núñez", Sex.Male);
        _kameron = P(_t, "Kameron", "Doyle", Sex.Male);
    }

    private long[] Ids(string query, bool soundsLike) =>
        NameSearch.Find(_t, query, soundsLike).Select(p => p.Id).ToArray();

    [Fact]
    public void PlainSearchMatchesWordStartsAndInsidesOnly()
    {
        // Sorted by surname of the main name: Baker (married Pemberwick), Pemberwick, Pemberwickes.
        Assert.Equal(new[] { _mary.Id, _john.Id, _jonathan.Id }, Ids("pemberwick", false));
        Assert.Equal(new[] { _jonathan.Id }, Ids("Jon Pemberwick", false));
        Assert.Equal(new[] { _jonathan.Id }, Ids("PEMBERWICK jonath", false));
        Assert.Equal(new[] { _jonathan.Id }, Ids("ickes", false));
    }

    [Fact]
    public void SoundsLikeCatchesSpellingVariantsAfterExactMatches()
    {
        // Pembervik and Pemberwyck share Pemberwick's Soundex code (P516); fuzzy matches follow the exact ones, by name.
        Assert.Equal(new[] { _mary.Id, _john.Id, _jonathan.Id, _johann.Id, _jon.Id }, Ids("pemberwick", true));
        // Jonathan Pemberwickes matches both words exactly; Pembervik (Jon ~ Johann), Pemberwick (Jon ~ John) and Pemberwyck follow.
        Assert.Equal(new[] { _jonathan.Id, _johann.Id, _john.Id, _jon.Id }, Ids("Jon Pemberwick", true));
        Assert.Equal(new[] { _kameron.Id }, Ids("Cameron", true));       // one letter off, different Soundex
        Assert.Empty(Ids("Cameron", false));
    }

    [Fact]
    public void YearsNarrowByBirthOrDeath()
    {
        Assert.Equal(new[] { _john.Id, _johann.Id, _jon.Id }, Ids("John Pemberwick 1850", true));   // 1850, 1849, 1852
        Assert.Equal(new[] { _john.Id }, Ids("John Pemberwick 1850", false));
        Assert.Equal(new[] { _mary.Id }, Ids("Pemberwick 1900", false));
        Assert.Equal(new[] { _jonathan.Id }, Ids("Pemberwick 1911", false));    // died 1910
        Assert.Empty(Ids("Pemberwick 1700", true));
    }

    [Fact]
    public void NumbersFindPeopleById()
    {
        Assert.Equal(new[] { _kameron.Id }, Ids(_kameron.Id.ToString(), false));
        Assert.Equal(new[] { _kameron.Id }, Ids("#" + _kameron.Id, true));
        Assert.Empty(Ids("Mary " + _kameron.Id, false));
    }

    [Fact]
    public void AccentsAndOtherNamesCount()
    {
        Assert.Equal(new[] { _jose.Id }, Ids("nunez", false));
        Assert.Equal(new[] { _jose.Id }, Ids("JOSÉ", false));
        Assert.Equal(new[] { _mary.Id }, Ids("Mary Baker", false));
        Assert.Equal(new[] { _mary.Id }, Ids("Mary Pemberwick", false));
    }

    [Fact]
    public void EmptyQueryListsEveryoneByName()
    {
        var all = NameSearch.Find(_t, "  ", false);
        Assert.Equal(_t.People.Count, all.Count);
        Assert.Equal(_mary.Id, all[0].Id);                                  // Baker sorts first
    }

    [Theory]
    [InlineData("pemberwick", "pemberwyck", 1)]
    [InlineData("williams", "willaims", 1)]
    [InlineData("thompson", "thomson", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    public void DamerauLevenshteinDistance(string a, string b, int expected) =>
        Assert.Equal(expected, NameSearch.Distance(a, b, 5));
}
