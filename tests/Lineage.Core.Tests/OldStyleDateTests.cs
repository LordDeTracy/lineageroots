using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

/// <summary>Before 1752 the year began on 25 March: "10 Feb 1600" then is February 1601 as we count now.</summary>
public class OldStyleDateTests
{
    private static List<Problem> ProblemsFor(string born, string died)
    {
        var t = new FamilyTree();
        var p = t.AddPerson(new Person { Sex = Sex.Female });
        (p.PrimaryName.Given, p.PrimaryName.Surname) = ("Mercy", "Holt");
        p.Events.Add(Event.Of("BIRT", GenDate.Parse(born)));
        p.Events.Add(Event.Of("DEAT", GenDate.Parse(died)));
        return Problems.Find(t);
    }

    [Fact]
    public void OldStyleYearsAreNotMistakenForErrors()
    {
        // Born December 1600, died the following February — written "1600" (old style) or "1600/01" (both years).
        Assert.DoesNotContain(ProblemsFor("20 DEC 1600", "10 FEB 1600"), p => p.Code == ProblemCodes.DeathBeforeBirth);
        Assert.DoesNotContain(ProblemsFor("20 DEC 1600", "10 FEB 1600/01"), p => p.Code == ProblemCodes.DeathBeforeBirth);
        // A real mistake is still caught: June comes after December only in a later year.
        Assert.Contains(ProblemsFor("20 DEC 1600", "10 JUN 1600"), p => p.Code == ProblemCodes.DeathBeforeBirth);
        // After 1752 the year began on 1 January, so there's no doubt.
        Assert.Contains(ProblemsFor("20 DEC 1800", "10 FEB 1800"), p => p.Code == ProblemCodes.DeathBeforeBirth);
    }

    [Fact]
    public void DualDatesSortByTheYearAsCountedNow()
    {
        Assert.True(GenDate.Parse("10 FEB 1600/01").SortKey > GenDate.Parse("20 DEC 1600").SortKey);
        Assert.Equal(1601, GenDate.Parse("10 FEB 1600/1601").NewStyleYear);
        Assert.Equal("10 FEB 1600/1601", GenDate.Parse("10 Feb 1600/1601").ToGedcom());   // kept as written
    }
}
