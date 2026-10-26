using Lineage.Core.Analysis;
using Lineage.Core.Model;
using static Lineage.Core.Tests.AnalysisTestData;

namespace Lineage.Core.Tests;

/// <summary>Small invented trees for the analysis tests. Every name and date here is made up.</summary>
internal static class AnalysisTestData
{
    public static Person P(FamilyTree t, string given, string surname, Sex sex = Sex.Unknown, string? birth = null, string? death = null)
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.Names.Add(new Name { Given = given, Surname = surname });
        if (birth != null) p.Events.Add(Event.Of("BIRT", GenDate.Parse(birth)));
        if (death != null) p.Events.Add(Event.Of("DEAT", GenDate.Parse(death)));
        return p;
    }

    public static Event Add(Person p, string tag, string date, string place = "")
    {
        var e = Event.Of(tag, GenDate.Parse(date), place);
        p.Events.Add(e);
        return e;
    }

    public static Family Couple(FamilyTree t, Person? husband, Person? wife, params Person[] kids)
    {
        var f = t.AddFamily(new Family());
        if (husband != null) t.SetPartner(f, husband, second: false);
        if (wife != null) t.SetPartner(f, wife, second: true);
        foreach (var k in kids) t.AddChild(f, k);
        return f;
    }

    public static Family Married(this Family f, string date)
    {
        f.Events.Add(Event.Of("MARR", GenDate.Parse(date)));
        return f;
    }
}

public class AnalysisTests
{
    private static List<Problem> Find(FamilyTree t, string code, ProblemOptions? o = null) =>
        Problems.Find(t, o).Where(p => p.Code == code).ToList();

    // ---- problem finder ----

    [Fact]
    public void TidyFamilyHasNoProblems()
    {
        var t = new FamilyTree();
        var dad = P(t, "Henry", "Ashdown", Sex.Male, "12 MAR 1820", "1890");
        var mum = P(t, "Lucy", "Pell", Sex.Female, "1825", "ABT 1880");
        var a = P(t, "Walter", "Ashdown", Sex.Male, "4 JUN 1850");
        var b = P(t, "Edith", "Ashdown", Sex.Female, "20 JUN 1851");
        var c = P(t, "Jane", "Ashdown", Sex.Female, "1855");
        Couple(t, dad, mum, a, b, c).Married("1848");
        Add(dad, "BURI", "1890");
        Add(dad, "PROB", "1891");
        Add(dad, "OCCU", "1851");
        Assert.Empty(Problems.Find(t));
    }

    [Fact]
    public void DeathBeforeBirthIsAnError()
    {
        var t = new FamilyTree();
        var ada = P(t, "Ada", "Finch", Sex.Female, "1850", "1840");
        var p = Assert.Single(Find(t, ProblemCodes.DeathBeforeBirth));
        Assert.Equal(ProblemSeverity.Error, p.Severity);
        Assert.Equal(ada.Id, p.PersonId);
        Assert.Equal("Ada Finch's death (1840) is before her birth (1850).", p.Message);
    }

    [Fact]
    public void UncertainDatesAreGivenTheBenefitOfTheDoubt()
    {
        var t = new FamilyTree();
        var ada = P(t, "Ada", "Finch", Sex.Female, "ABT 1850", "1847");            // could be born 1845
        var bea = P(t, "Bea", "Finch", Sex.Female, "ABT 1850", "1830");            // wrong even then
        var cy = P(t, "Cy", "Finch", Sex.Male, "BET 1850 AND 1860", "1855");
        var di = P(t, "Di", "Finch", Sex.Female, "1850", "MAR 1850");              // same year: fine
        var ed = P(t, "Ed", "Finch", Sex.Male, "1850", "BEF 1849");
        var found = Problems.Find(t);
        Assert.DoesNotContain(found, p => p.Severity == ProblemSeverity.Error);
        var dbb = found.Where(p => p.Code == ProblemCodes.DeathBeforeBirth).ToList();
        Assert.Equal(new[] { bea.Id, ed.Id }, dbb.Select(p => p.PersonId).Order());
        Assert.All(dbb, p => Assert.Equal(ProblemSeverity.Warning, p.Severity));
        Assert.Contains("about 1850", dbb.Single(p => p.PersonId == bea.Id).Message);
        _ = (ada, cy, di);
    }

    [Fact]
    public void EventsOutsideTheLifetimeAreFound()
    {
        var t = new FamilyTree();
        var tom = P(t, "Tom", "Reed", Sex.Male, "1850", "1880");
        Add(tom, "CHR", "1849");
        Add(tom, "OCCU", "1840");
        Add(tom, "RESI", "1890");
        Add(tom, "RESI", "ABT 1886");      // could be 1881: within the grace period
        Add(tom, "CENS", "1881");
        Add(tom, "PROB", "1885");
        Add(tom, "BAPL", "1990");          // LDS ordinances are often done long after death
        Add(tom, "EVEN", "1885").TypeName = "Funeral";
        Add(tom, "BURI", "1879");
        Couple(t, tom, P(t, "Ivy", "Reed", Sex.Female, "1852"));

        var before = Find(t, ProblemCodes.EventBeforeBirth);
        Assert.Equal(2, before.Count);
        Assert.Contains(before, p => p.Message == "Tom Reed's christening (1849) is before his birth (1850).");
        Assert.Contains(before, p => p.Message == "Tom Reed's occupation (1840) is before his birth (1850).");
        var after = Assert.Single(Find(t, ProblemCodes.EventAfterDeath));
        Assert.Equal("Tom Reed's residence (1890) is more than 3 years after his death (1880).", after.Message);
        var burial = Assert.Single(Find(t, ProblemCodes.BurialBeforeDeath));
        Assert.Equal(ProblemSeverity.Error, burial.Severity);
    }

    [Fact]
    public void ImpossiblyLongLivesAreFoundAndTheLimitCanChange()
    {
        var t = new FamilyTree();
        P(t, "Agnes", "Oldcastle", Sex.Female, "1800", "1915");
        P(t, "Bert", "Oldcastle", Sex.Male, "1800", "1905");
        var p = Assert.Single(Find(t, ProblemCodes.LivedTooLong));
        Assert.Equal("Agnes Oldcastle lived to 115 (1800 to 1915).", p.Message);
        Assert.Empty(Find(t, ProblemCodes.LivedTooLong, new ProblemOptions { MaxLifespan = 120 }));
    }

    [Fact]
    public void ParentsTooOldOrTooYoung()
    {
        var t = new FamilyTree();
        var mary = P(t, "Mary", "Cole", Sex.Female, "1818");
        var ann = P(t, "Ann", "Whitfield", Sex.Female, "1879");
        Couple(t, null, mary, ann);
        var rose = P(t, "Rose", "Hart", Sex.Female, "1850");
        Couple(t, null, rose, P(t, "Lily", "Hart", Sex.Female, "1861"));
        var sam = P(t, "Sam", "Hart", Sex.Male, "1850");
        Couple(t, sam, null, P(t, "Ned", "Hart", Sex.Male, "1862"));
        var abe = P(t, "Abe", "Grey", Sex.Male, "1780");
        Couple(t, abe, null, P(t, "Ben", "Grey", Sex.Male, "1865"));

        var old = Assert.Single(Find(t, ProblemCodes.MotherTooOld));
        Assert.Equal("Mary Cole was 61 when Ann Whitfield was born (1879).", old.Message);
        Assert.Equal(mary.Id, old.PersonId);
        Assert.Equal(ProblemSeverity.Warning, old.Severity);
        Assert.Equal("Rose Hart was 11 when Lily Hart was born (1861).", Assert.Single(Find(t, ProblemCodes.MotherTooYoung)).Message);
        Assert.Equal(sam.Id, Assert.Single(Find(t, ProblemCodes.FatherTooYoung)).PersonId);
        Assert.Equal("Abe Grey was 85 when Ben Grey was born (1865).", Assert.Single(Find(t, ProblemCodes.FatherTooOld)).Message);
    }

    [Fact]
    public void ApproximateOrAdoptiveParentsAreNotFlagged()
    {
        var t = new FamilyTree();
        var mary = P(t, "Mary", "Cole", Sex.Female, "ABT 1820");          // 59 nominally, but could be 54
        Couple(t, null, mary, P(t, "Ann", "Cole", Sex.Female, "1879"));
        var old = P(t, "Olive", "Cole", Sex.Female, "1800");
        var f = Couple(t, null, old);
        var adopted = P(t, "Pat", "Cole", Sex.Male, "1870");
        t.AddChild(f, adopted, new ChildLink { ChildId = adopted.Id, ToMother = ParentRelation.Adopted, ToFather = ParentRelation.Adopted });
        Assert.Empty(Find(t, ProblemCodes.MotherTooOld));

        var older = P(t, "Opal", "Cole", Sex.Female, "ABT 1810");          // wrong even if born 1815
        Couple(t, null, older, P(t, "Ida", "Cole", Sex.Female, "1879"));
        var p = Assert.Single(Find(t, ProblemCodes.MotherTooOld));
        Assert.Equal("Opal Cole was about 69 when Ida Cole was born (1879).", p.Message);
    }

    [Fact]
    public void ChildrenBornAfterAParentDied()
    {
        var t = new FamilyTree();
        var jane = P(t, "Jane", "Moss", Sex.Female, "1820", "1850");
        var kit = P(t, "Kit", "Moss", Sex.Male, "1852");
        Couple(t, null, jane, kit);
        var p = Assert.Single(Find(t, ProblemCodes.BornAfterMotherDied));
        Assert.Equal(kit.Id, p.PersonId);
        Assert.Equal(ProblemSeverity.Error, p.Severity);

        // Christened a month after the mother died: fine, the birth was earlier.
        var eve = P(t, "Eve", "Lake", Sex.Female, "1820", "12 MAR 1850");
        var joy = P(t, "Joy", "Lake", Sex.Female);
        Add(joy, "CHR", "20 APR 1850");
        Couple(t, null, eve, joy);
        Assert.Single(Find(t, ProblemCodes.BornAfterMotherDied));

        var ike = P(t, "Ike", "Dunn", Sex.Male, "1820", "JUN 1850");
        var amy = P(t, "Amy", "Dunn", Sex.Female, "JUN 1851");             // a year later
        var bo = P(t, "Bo", "Dunn", Sex.Male, "MAR 1851");                 // nine months: fine
        Couple(t, ike, null, amy);
        Couple(t, ike, null, bo);
        var father = Assert.Single(Find(t, ProblemCodes.BornAfterFatherDied));
        Assert.Equal(amy.Id, father.PersonId);
        Assert.Equal("Amy Dunn was born (Jun 1851) more than 10 months after her father Ike Dunn's death (Jun 1850).", father.Message);
    }

    [Fact]
    public void MarriagesTooYoungOrAfterDeath()
    {
        var t = new FamilyTree();
        var pip = P(t, "Pip", "Lane", Sex.Male, "1850");
        var meg = P(t, "Meg", "Lane", Sex.Female, "1840");
        Couple(t, pip, meg).Married("1861");
        var tess = P(t, "Tess", "Ford", Sex.Female, "1820", "1870");
        var will = P(t, "Will", "Ford", Sex.Male, "1820");
        Couple(t, will, tess).Married("1875");

        var young = Assert.Single(Find(t, ProblemCodes.MarriedTooYoung));
        Assert.Equal("Pip Lane was 11 when married to Meg Lane (1861).", young.Message);
        Assert.Equal(ProblemSeverity.Warning, young.Severity);
        var late = Assert.Single(Find(t, ProblemCodes.MarriedAfterDeath));
        Assert.Equal(tess.Id, late.PersonId);
        Assert.Equal(ProblemSeverity.Error, late.Severity);
    }

    [Fact]
    public void SiblingsTooCloseButNotTwins()
    {
        var t = new FamilyTree();
        var dad = P(t, "Hugh", "Bell", Sex.Male, "1820");
        var mum = P(t, "Ruth", "Bell", Sex.Female, "1825");
        var a = P(t, "Ann", "Bell", Sex.Female, "1 MAR 1850");
        var b = P(t, "Bea", "Bell", Sex.Female, "1 OCT 1850");
        var twin1 = P(t, "Carl", "Bell", Sex.Male, "1 MAR 1860");
        var twin2 = P(t, "Dirk", "Bell", Sex.Male, "2 MAR 1860");
        var e = P(t, "Elsa", "Bell", Sex.Female, "1868");
        var f = P(t, "Fern", "Bell", Sex.Female, "1868");                  // same year, no months: can't tell
        Couple(t, dad, mum, a, b, twin1, twin2, e, f);

        var p = Assert.Single(Find(t, ProblemCodes.SiblingsTooClose));
        Assert.Equal(b.Id, p.PersonId);
        Assert.Equal("Ann Bell (1 Mar 1850) and Bea Bell (1 Oct 1850) were born only 7 months apart.", p.Message);
    }

    [Fact]
    public void ParentYoungerThanChildIsReportedOnce()
    {
        var t = new FamilyTree();
        var zoe = P(t, "Zoe", "Park", Sex.Female, "1880");
        Couple(t, null, zoe, P(t, "Al", "Park", Sex.Male, "1870"));
        var p = Assert.Single(Problems.Find(t));
        Assert.Equal(ProblemCodes.ParentYoungerThanChild, p.Code);
        Assert.Equal(zoe.Id, p.PersonId);
        Assert.Equal(ProblemSeverity.Error, p.Severity);
    }

    [Fact]
    public void OwnAncestorLoopsAreFoundWithoutHanging()
    {
        var t = new FamilyTree();
        var a = P(t, "Loop", "One", Sex.Male, "1800");
        var b = P(t, "Loop", "Two", Sex.Male, "1830");
        var c = P(t, "Outside", "Loop", Sex.Male, "1770");
        Couple(t, a, null, b);
        Couple(t, b, null, a);
        Couple(t, c, null, a);
        var self = P(t, "Self", "Made", Sex.Female);
        Couple(t, null, self, self);

        var loops = Find(t, ProblemCodes.OwnAncestor);
        Assert.Equal(new[] { a.Id, b.Id, self.Id }, loops.Select(p => p.PersonId).Order());
        Assert.Contains("own ancestor (through Loop Two)", loops.Single(p => p.PersonId == a.Id).Message);
        Assert.Equal("Self Made is recorded as her own parent.", loops.Single(p => p.PersonId == self.Id).Message);

        // The relationship calculator must also finish on looped data.
        Assert.True(Relationship.Describe(t, a, b).Related);
        Assert.Equal("father", Relationship.Describe(t, a, c).Text);
    }

    [Fact]
    public void UnreadableDatesAndUnlinkedPeopleAreInfo()
    {
        var t = new FamilyTree();
        var lone = P(t, "Lone", "Wolfe", Sex.Male, "about Easter 1850");
        var found = Problems.Find(t);
        var date = Assert.Single(found, p => p.Code == ProblemCodes.UnreadableDate);
        Assert.Equal(ProblemSeverity.Info, date.Severity);
        Assert.Contains("\"about Easter 1850\"", date.Message);
        var unlinked = Assert.Single(found, p => p.Code == ProblemCodes.NoFamily);
        Assert.Equal(lone.Id, unlinked.PersonId);
        Assert.Equal(ProblemSeverity.Info, unlinked.Severity);

        Assert.Empty(Problems.Find(t, new ProblemOptions { IncludeInfo = false }));
        Couple(t, lone, P(t, "Wren", "Wolfe", Sex.Female));
        Assert.DoesNotContain(Problems.Find(t), p => p.Code == ProblemCodes.NoFamily);
    }

    // ---- dashboard ----

    [Fact]
    public void AnniversariesWrapAroundTheYearEnd()
    {
        var t = new FamilyTree();
        var noel = P(t, "Noel", "Frost", Sex.Male, "30 DEC 1900", "3 JAN 1950");
        var holly = P(t, "Holly", "Frost", Sex.Female, "8 JAN 1902");      // just after the window
        var ivy = P(t, "Ivy", "Frost", Sex.Female, "1 JAN 1903");
        P(t, "Rough", "Frost", Sex.Male, "ABT 29 DEC 1900");                // approximate: left out
        P(t, "Year", "Frost", Sex.Male, "1900");                            // no day: left out
        P(t, "Jan", "Frost", Sex.Male, "1880", "7 JAN 1950");               // the window's end is exclusive
        var f = Couple(t, noel, holly).Married("28 DEC 1925");

        var list = Dashboard.Anniversaries(t, new DateOnly(2026, 12, 28), 10);
        Assert.Equal(new[]
        {
            (AnniversaryKind.Marriage, noel.Id, new DateOnly(2026, 12, 28), 1925, 101),
            (AnniversaryKind.Birth, noel.Id, new DateOnly(2026, 12, 30), 1900, 126),
            (AnniversaryKind.Birth, ivy.Id, new DateOnly(2027, 1, 1), 1903, 124),
            (AnniversaryKind.Death, noel.Id, new DateOnly(2027, 1, 3), 1950, 77),
        }, list.Select(a => (a.Kind, a.PersonId, a.On, a.Year, a.YearsAgo)));
        Assert.Equal(f.Id, list[0].FamilyId);
        Assert.Equal(holly.Id, list[0].SpouseId);
    }

    [Fact]
    public void LeapDayBirthdaysFallOnTheTwentyEighth()
    {
        var t = new FamilyTree();
        P(t, "Leap", "Day", Sex.Female, "29 FEB 1904");
        Assert.Equal(new DateOnly(2027, 2, 28), Assert.Single(Dashboard.Anniversaries(t, new DateOnly(2027, 2, 20), 14)).On);
        Assert.Equal(new DateOnly(2028, 2, 29), Assert.Single(Dashboard.Anniversaries(t, new DateOnly(2028, 2, 20), 14)).On);
        Assert.Empty(Dashboard.Anniversaries(t, new DateOnly(2027, 2, 20), 0));
    }

    [Fact]
    public void MissingDataListsTheGaps()
    {
        var t = new FamilyTree();
        var dad = P(t, "Owen", "Vale", Sex.Male, "1820");                  // birth with no place
        var mum = P(t, "Nell", "Vale", Sex.Female);
        var kate = P(t, "Kate", "Vale", Sex.Female);
        var birth = Add(kate, "BIRT", "1850", "Leeds, Yorkshire");
        birth.Citations.Add(new Citation { SourceId = 1 });
        Add(kate, "OCCU", "1871");                                          // a fact: no place needed
        Couple(t, dad, mum, kate);
        var bare = P(t, "Bare", "Bones");

        var m = Dashboard.MissingData(t);
        Assert.Equal(new[] { mum.Id, bare.Id }, m.NoBirthDate);
        Assert.Equal(new[] { dad.Id, mum.Id, bare.Id }, m.NoCitations);
        Assert.Equal(new[] { dad.Id, mum.Id, bare.Id }, m.NoParents);
        Assert.Equal(new[] { dad.Id }, m.EventsWithoutPlace);
        Assert.Equal((2, 3, 3, 1), (m.NoBirthDateCount, m.NoCitationsCount, m.NoParentsCount, m.EventsWithoutPlaceCount));
    }

    [Fact]
    public void StatsSummariseTheTree()
    {
        var t = new FamilyTree();
        var john = P(t, "John", "Smith", Sex.Male, "1850", "1920");
        var mary = P(t, "Mary", "SMITH", Sex.Female, "1855", "1935");
        var kid = P(t, "Tom", "Smith", Sex.Male);
        P(t, "Ann", "Jones", Sex.Female, "1790");
        P(t, "Bob", "Jones", Sex.Male);
        var cy = P(t, "Cy", "Lee", Sex.Male);
        Add(cy, "CHR", "1800");
        Add(cy, "BURI", "1860");
        t.AddPerson(new Person());                                           // no name at all
        Couple(t, john, mary, kid);
        Couple(t, cy, null);

        var s = Dashboard.Stats(t);
        Assert.Equal(7, s.People);
        Assert.Equal(2, s.Families);
        Assert.Equal(3, s.Surnames);
        Assert.Equal(1790, s.EarliestBirthYear);
        Assert.Equal(70.0, s.AverageLifespan);                               // (70 + 80 + 60) / 3
        Assert.Equal(new[] { ("Smith", 3), ("Jones", 2), ("Lee", 1) }, s.TopSurnames.Select(x => (x.Surname, x.Count)));
        Assert.Empty(t.People.Values.Last().Names);                          // stats didn't invent a name
    }
}
