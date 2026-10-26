using Lineage.Core.Model;

namespace Lineage.Core.Tests;

/// <summary>An invented family for the report and website tests.</summary>
internal static class ReportTrees
{
    public static Person P(FamilyTree t, string given, string surname, Sex sex, params Event[] events)
    {
        var p = t.AddPerson(new Person { Sex = sex });
        p.PrimaryName.Given = given;
        p.PrimaryName.Surname = surname;
        p.Events.AddRange(events);
        return p;
    }

    public static Event E(string tag, string date = "", string place = "", string value = "", bool isPrivate = false) =>
        new() { Tag = tag, Date = GenDate.Parse(date), Place = place, Value = value, Private = isPrivate };

    public static Family Couple(FamilyTree t, Person a, Person? b, params Person[] kids)
    {
        var f = t.AddFamily(new Family());
        t.SetPartner(f, a, second: false);
        if (b != null) t.SetPartner(f, b, second: true);
        foreach (var k in kids) t.AddChild(f, k);
        return f;
    }

    /// <summary>
    /// 1 Thomas Edward Whitfield (1842–1911) — parents 2 George and 3 Mary Ann — married 4 Eliza Hartley;
    /// their children 5 Clara (married 9 John Pryor, daughter 10 Alice), 6 Sam (sex unknown, died young)
    /// and 7 Henry (married 8 Ada Moss, no children).
    /// </summary>
    public static FamilyTree Whitfields()
    {
        var t = new FamilyTree { Name = "Whitfield family" };
        var parish = t.AddSource(new Source { Title = "Parish registers of St Mary, Harrow", Author = "Church of England" });
        var leeds = t.AddSource(new Source { Title = "Leeds marriages", Publication = "Leeds, 1868" });

        var birth = E("BIRT", "14 MAR 1842", "Harrow, Middlesex, England");
        birth.Citations.Add(new Citation { SourceId = parish.Id, Page = "Baptisms 1842, entry 118" });
        var death = E("DEAT", "9 NOV 1911", "Worcester, Massachusetts, USA");
        death.Citations.Add(new Citation { SourceId = parish.Id, Page = "Baptisms 1842, entry 118" }); // same note again
        var thomas = P(t, "Thomas Edward", "Whitfield", Sex.Male, birth,
            E("OCCU", "1871", value: "Stonemason"),
            E("RESI", "1890", "12 Secret Street, Worcester", isPrivate: true),
            death,
            E("BURI", "13 NOV 1911", "Hope Cemetery, Worcester, Massachusetts, USA"));
        thomas.Names.Add(new Name { Given = "Tom", Surname = "Whitfield", Type = "aka" });
        thomas.Note = "Thomas carved the font at St Mary’s.\n\nHe sailed for Boston in 1880.";

        var george = P(t, "George", "Whitfield", Sex.Male, E("BIRT", "ABT 1815"));
        var mary = P(t, "Mary Ann", "Cole", Sex.Female);
        var eliza = P(t, "Eliza", "Hartley", Sex.Female, E("BIRT", "1845", "Leeds, Yorkshire, England"), E("DEAT", "3 APR 1920", "Worcester, Massachusetts, USA"));
        var clara = P(t, "Clara", "Whitfield", Sex.Female, E("BIRT", "1872", "Leeds, Yorkshire, England"));
        var sam = P(t, "Sam", "Whitfield", Sex.Unknown, E("BIRT", "3 MAY 1874", "Leeds, Yorkshire, England"), E("DEAT", "1875"));
        var henry = P(t, "Henry", "Whitfield", Sex.Male, E("BIRT", "1876"));
        var ada = P(t, "Ada", "Moss", Sex.Female);
        var john = P(t, "John", "Pryor", Sex.Male);
        var alice = P(t, "Alice", "Pryor", Sex.Female, E("BIRT", "2 FEB 1898", "Boston, Massachusetts, USA"));

        Couple(t, george, mary, thomas);
        var f = Couple(t, thomas, eliza, clara, sam, henry);
        var marriage = E("MARR", "18 JUN 1868", "Leeds, Yorkshire, England");
        marriage.Citations.Add(new Citation { SourceId = leeds.Id, Page = "Folio 22" });
        f.Events.Add(marriage);
        Couple(t, john, clara, alice);
        Couple(t, henry, ada);
        return t;
    }
}
