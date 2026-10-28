using Lineage.Core.Model;

namespace Lineage.Core.History;

/// <summary>The columns a census form can have (each census has some of them).</summary>
public enum CensusColumn { Relationship, Condition, Sex, Age, BirthDate, Occupation, Birthplace, YearsMarried, Immigrated }

/// <summary>
/// One line of a census household as transcribed: who it is in the tree (<see cref="PersonId"/>), or whether to add
/// them as a new person, and what the census says about them.
/// </summary>
public sealed class CensusLine
{
    public long? PersonId { get; set; }
    public bool AddAsNew { get; set; }
    public string Name { get; set; } = "";
    public string Relationship { get; set; } = "";
    public string Condition { get; set; } = "";
    public string Sex { get; set; } = "";
    public string Age { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Birthplace { get; set; } = "";
    public string YearsMarried { get; set; } = "";
    public string Immigrated { get; set; } = "";

    public string Get(CensusColumn c) => c switch
    {
        CensusColumn.Relationship => Relationship, CensusColumn.Condition => Condition, CensusColumn.Sex => Sex,
        CensusColumn.Age => Age, CensusColumn.BirthDate => BirthDate, CensusColumn.Occupation => Occupation,
        CensusColumn.Birthplace => Birthplace, CensusColumn.YearsMarried => YearsMarried, _ => Immigrated,
    };
}

/// <summary>
/// Entering a whole census household at once: the columns each census had, a household filled in from the tree,
/// and saving it — a cited census event for everyone on it, optionally their occupations, and new people for
/// household members not yet in the tree (linked as the head's spouse or children).
/// </summary>
public static class CensusHousehold
{
    private static readonly CensusColumn[] Early = { CensusColumn.Age, CensusColumn.Sex, CensusColumn.Occupation, CensusColumn.Birthplace };
    private static readonly CensusColumn[] Standard =
        { CensusColumn.Relationship, CensusColumn.Condition, CensusColumn.Sex, CensusColumn.Age, CensusColumn.Occupation, CensusColumn.Birthplace };

    public static string Label(CensusColumn c) => c switch
    {
        CensusColumn.Relationship => "Relation to head",
        CensusColumn.Condition => "Marital status",
        CensusColumn.BirthDate => "Born (date)",
        CensusColumn.YearsMarried => "Years married",
        CensusColumn.Immigrated => "Year immigrated",
        _ => c.ToString(),
    };

    /// <summary>The columns this census had (the name is always first and isn't listed).</summary>
    public static IReadOnlyList<CensusColumn> Columns(CensusRecord c)
    {
        var y = c.Year;
        if (c.Name.Contains("Register")) return new[] { CensusColumn.Sex, CensusColumn.BirthDate, CensusColumn.Condition, CensusColumn.Occupation };
        switch (c.Region)
        {
            case Regions.UnitedStates:
                if (y < 1850) return Array.Empty<CensusColumn>();   // heads of household only
                if (y < 1880) return Early;
                if (y == 1900) return With(Standard, CensusColumn.BirthDate, CensusColumn.YearsMarried, CensusColumn.Immigrated);
                if (y == 1910) return With(Standard, CensusColumn.YearsMarried, CensusColumn.Immigrated);
                if (y is 1920 or 1930) return With(Standard, CensusColumn.Immigrated);
                return Standard;
            case Regions.England or Regions.Wales or Regions.Scotland:
                if (y == 1841) return Early;
                return y == 1911 ? With(Standard, CensusColumn.YearsMarried) : Standard;
            case Regions.Ireland:
                return y == 1911 ? With(Standard, CensusColumn.YearsMarried) : Standard;
            case Regions.Canada:
                return y >= 1901 ? With(Standard, CensusColumn.BirthDate, CensusColumn.Immigrated) : Standard;
            default:   // Norway, Sweden: birth year rather than age
                return new[] { CensusColumn.Relationship, CensusColumn.Condition, CensusColumn.Sex, CensusColumn.BirthDate, CensusColumn.Occupation, CensusColumn.Birthplace };
        }
    }

    private static CensusColumn[] With(CensusColumn[] baseColumns, params CensusColumn[] more) => baseColumns.Concat(more).ToArray();

    /// <summary>The census day as a date.</summary>
    public static GenDate Day(CensusRecord c) =>
        c.Month > 0 ? new GenDate { Year = c.TakenYear, Month = c.Month, Day = c.Day } : new GenDate { Year = c.TakenYear };

    /// <summary>The household as the tree expects it: the head, a living spouse, and children born by census day.</summary>
    public static List<CensusLine> Prefill(FamilyTree tree, Person head, CensusRecord census)
    {
        var lines = new List<CensusLine> { LineFor(tree, head, census, "Head") };
        var day = census.DayKey;
        bool Alive(Person p)
        {
            var span = LifeTimeline.Span(tree, p);
            var born = p.Birth?.Date is { Year: > 0 } b ? b.SortKey : span.Born * 10000;
            return born > 0 && born <= day && (!span.DeathKnown || span.Died * 10000 + 1231 >= day);
        }
        foreach (var (_, spouse, children) in tree.FamiliesOf(head))
        {
            if (spouse != null && Alive(spouse))
                lines.Add(LineFor(tree, spouse, census, spouse.Sex == Sex.Female ? "Wife" : spouse.Sex == Sex.Male ? "Husband" : "Spouse"));
            foreach (var c in children.Where(Alive))
                lines.Add(LineFor(tree, c, census, c.Sex == Sex.Female ? "Daughter" : c.Sex == Sex.Male ? "Son" : "Child"));
        }
        return lines;
    }

    private static CensusLine LineFor(FamilyTree tree, Person p, CensusRecord census, string relationship)
    {
        var span = LifeTimeline.Span(tree, p);
        var birth = p.Birth;
        var age = span.Born > 0 ? census.TakenYear - span.Born
                  - (birth?.Date is { Month: > 0 } b && (b.Month * 100 + b.Day) > (census.Month * 100 + census.Day) ? 1 : 0) : -1;
        return new CensusLine
        {
            PersonId = p.Id, Name = p.DisplayName, Relationship = relationship,
            Sex = p.Sex switch { Sex.Male => "M", Sex.Female => "F", _ => "" },
            Age = age >= 0 ? age.ToString() : "",
            BirthDate = birth != null && !birth.Date.IsEmpty ? Reports.ReportText.Date(birth.Date) : "",
            Birthplace = birth?.Place ?? "",
        };
    }

    /// <summary>
    /// Saves the household: a census source (created if needed), a census event citing it for each person, their
    /// occupations if asked, and new people for lines marked to add. Returns the people given a census event.
    /// </summary>
    public static List<Person> Save(FamilyTree tree, CensusRecord census, Person head, string place, string reference,
                                    IReadOnlyList<CensusLine> lines, bool addOccupations)
    {
        var source = tree.Sources.Values.FirstOrDefault(s => s.Title.Equals(census.Name, StringComparison.OrdinalIgnoreCase))
                     ?? tree.AddSource(new Source { Title = census.Name, Type = "Census", Kind = SourceKind.Original });
        var columns = Columns(census);
        var transcript = string.Join("\n", lines.Select(l =>
            string.Join(" | ", new[] { l.Name }.Concat(columns.Select(c => $"{Label(c)}: {l.Get(c)}")).Where(x => !x.EndsWith(": ")))));
        var day = Day(census);
        var recorded = new List<Person>();

        foreach (var line in lines)
        {
            var person = tree.Person(line.PersonId);
            if (person == null && line.AddAsNew && line.Name.Trim().Length > 0) person = AddPerson(tree, census, head, line);
            if (person == null) continue;

            var details = columns.Select(c => (c, v: line.Get(c).Trim())).Where(x => x.v.Length > 0).Select(x => $"{Label(x.c)}: {x.v}");
            var e = Event.Of("CENS", day, place.Trim());
            e.Description = string.Join("; ", details);
            e.Citations.Add(new Citation { SourceId = source.Id, Page = reference.Trim(), Text = transcript, Evidence = EvidenceKind.Direct });
            person.Events.Add(e);
            recorded.Add(person);

            var job = line.Occupation.Trim();
            if (addOccupations && job.Length > 0 && !person.Events.Any(x => x.Tag == "OCCU" && x.Date.Year == census.TakenYear))
            {
                var occu = Event.Of("OCCU", day, place.Trim());
                occu.Value = job;
                occu.Citations.Add(new Citation { SourceId = source.Id, Page = reference.Trim(), Evidence = EvidenceKind.Direct });
                person.Events.Add(occu);
            }
        }
        return recorded;
    }

    /// <summary>A household member not yet in the tree; a wife, husband, son or daughter is linked to the head.</summary>
    private static Person AddPerson(FamilyTree tree, CensusRecord census, Person head, CensusLine line)
    {
        var words = line.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var p = new Person
        {
            Sex = line.Sex.Trim().ToUpperInvariant() switch { "M" or "MALE" => Sex.Male, "F" or "FEMALE" => Sex.Female, _ => Sex.Unknown },
        };
        p.PrimaryName.Given = words.Length > 1 ? string.Join(' ', words[..^1]) : words[0];
        p.PrimaryName.Surname = words.Length > 1 ? words[^1] : "";
        if (int.TryParse(line.Age.Trim(), out var age) && age >= 0)
            p.Events.Add(Event.Of("BIRT", GenDate.Parse($"ABT {census.TakenYear - age}"), line.Birthplace.Trim()));
        else if (line.BirthDate.Trim().Length > 0)
            p.Events.Add(Event.Of("BIRT", GenDate.Parse(line.BirthDate), line.Birthplace.Trim()));
        tree.AddPerson(p);

        var rel = line.Relationship.Trim().ToLowerInvariant();
        if (rel is "wife" or "husband" or "spouse")
        {
            var f = tree.FamiliesOf(head).Select(x => x.Family).FirstOrDefault(x => x.PartnerIds.Count() < 2) ?? NewFamily(tree, head);
            tree.SetPartner(f, p, second: f.Partner1Id != null);
        }
        else if (rel is "son" or "daughter" or "child" or "dau" or "dau.")
        {
            var f = tree.FamiliesOf(head).Select(x => x.Family).FirstOrDefault() ?? NewFamily(tree, head);
            tree.AddChild(f, p);
        }
        return p;
    }

    private static Family NewFamily(FamilyTree tree, Person head)
    {
        var f = tree.AddFamily(new Family());
        tree.SetPartner(f, head, second: head.Sex == Sex.Female);
        return f;
    }
}
