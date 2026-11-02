using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class CleanupTests
{
    [Theory]
    [InlineData("PENNICK", true, "Pennick")]
    [InlineData("MCDONALD", true, "McDonald")]
    [InlineData("O'BRIEN", true, "O'Brien")]
    [InlineData("PENNICK-JONES", true, "Pennick-Jones")]
    [InlineData("VAN DER BERG", true, "Van der Berg")]
    [InlineData("MARIA DE LA CRUZ", true, "Maria de la Cruz")]
    [InlineData("JOHN HENRY", false, "John Henry")]
    [InlineData("henry iii", false, "Henry III")]
    [InlineData("  Mary   Ann ", false, "Mary Ann")]
    [InlineData("Pennick", true, null)]
    [InlineData("McDonald", true, null)]
    [InlineData("J", false, null)]
    public void NamesAreTidied(string name, bool surname, string? expected) =>
        Assert.Equal(expected, Cleanup.TidyName(name, surname));

    [Theory]
    [InlineData("12/03/1842", true, "12 MAR 1842")]
    [InlineData("12/03/1842", false, "3 DEC 1842")]
    [InlineData("25/12/1842", false, "25 DEC 1842")]
    [InlineData("12/25/1842", true, "25 DEC 1842")]
    [InlineData("1842-03-12", true, "12 MAR 1842")]
    [InlineData("March 12, 1842", true, "12 MAR 1842")]
    [InlineData("12th of March 1842", true, "12 MAR 1842")]
    [InlineData("12-Mar-1842", true, "12 MAR 1842")]
    [InlineData("abt. 1850", true, "ABT 1850")]
    [InlineData("c.1850", true, "ABT 1850")]
    [InlineData("circa March 1850", true, "ABT MAR 1850")]
    [InlineData("before 3rd June, 1901", true, "BEF 3 JUN 1901")]
    [InlineData("between 1850 and 1855", true, "BET 1850 AND 1855")]
    [InlineData("31/02/1850", true, null)]
    [InlineData("sometime in spring", true, null)]
    public void DatesAreRepaired(string text, bool dayFirst, string? expected) =>
        Assert.Equal(expected, Cleanup.RepairDate(text, dayFirst, out _));

    [Fact]
    public void AmbiguousDatesSaySo()
    {
        Cleanup.RepairDate("03/04/1850", dayFirst: true, out var note);
        Assert.Contains("4 March 1850", note);
        Cleanup.RepairDate("13/04/1850", dayFirst: true, out note);
        Assert.Equal("", note);
    }

    [Theory]
    [InlineData("Leeds,Yorkshire , England", "Leeds, Yorkshire, England")]
    [InlineData("Leeds,, England,", "Leeds, England")]
    [InlineData("Leeds, England", null)]
    public void PlacesAreTidied(string place, string? expected) => Assert.Equal(expected, Cleanup.TidyPlace(place));

    [Fact]
    public void FindingChangesNothingAndApplyingFixesEverything()
    {
        var t = new FamilyTree();
        var a = t.AddPerson(new Person { Names = { new Name { Given = "JOHN", Surname = "PENNICK" } } });
        a.Events.Add(new Event { Tag = "BIRT", Date = GenDate.Parse("12/25/1842"), Place = "Leeds,Yorkshire" });
        var b = t.AddPerson(new Person { Names = { new Name { Given = "Mary", Surname = "PENNICK" } } });
        b.Events.Add(new Event { Tag = "BIRT", Date = GenDate.Parse("abt. 1845"), Place = "Leeds, Yorkshire, England" });
        b.Events.Add(new Event { Tag = "DEAT", Date = GenDate.Parse("1901"), Place = "leeds, yorks, england" });

        var fixes = Cleanup.Find(t, dayFirst: true);
        Assert.Equal("PENNICK", b.PrimaryName.Surname);   // only found, not changed

        var pennick = fixes.Single(f => f.Kind == CleanupKind.Names && f.Before == "PENNICK");
        Assert.Equal(2, pennick.Count);
        Assert.Contains(fixes, f => f.Kind == CleanupKind.Names && f.Before == "JOHN" && f.After == "John");
        Assert.Equal(2, fixes.Count(f => f.Kind == CleanupKind.Dates));
        var places = fixes.Where(f => f.Kind == CleanupKind.Places).ToList();
        Assert.All(places, f => Assert.Equal("Leeds, Yorkshire, England", f.After));
        Assert.Equal(2, places.Count);

        foreach (var f in fixes) f.Apply();
        Assert.Equal("John Pennick", a.DisplayName);
        Assert.Equal("Mary Pennick", b.DisplayName);
        Assert.Equal("25 DEC 1842", a.Events[0].Date.ToGedcom());
        Assert.Equal("ABT 1845", b.Events[0].Date.ToGedcom());
        Assert.Equal("Leeds, Yorkshire, England", a.Events[0].Place);
        Assert.Equal("Leeds, Yorkshire, England", b.Events[1].Place);
        Assert.Empty(Cleanup.Find(t, dayFirst: true));
    }
}
