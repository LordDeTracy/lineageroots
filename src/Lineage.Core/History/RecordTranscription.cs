using Lineage.Core.Model;

namespace Lineage.Core.History;

/// <summary>The kinds of record the transcription form knows.</summary>
public enum RecordKind { Baptism, Marriage, Burial, Will, Deed }

/// <summary>Someone named in a record: who they are in the tree (or a new person), their role, and what it says of them.</summary>
public sealed class RecordLine
{
    public long? PersonId { get; set; }
    public bool AddAsNew { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Sex { get; set; } = "";
    public string Age { get; set; } = "";
    public string Residence { get; set; } = "";
    public string Occupation { get; set; } = "";
    /// <summary>Anything else the record says of them: a bequest, the land, "of this parish".</summary>
    public string Note { get; set; } = "";
}

/// <summary>A filled-in transcription: the record, where it is, and everyone named in it.</summary>
public sealed class Transcription
{
    public RecordKind Kind { get; set; }
    public string SourceTitle { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Date { get; set; } = "";
    /// <summary>The birth date a baptism gives, or the date a will was proved.</summary>
    public string SecondDate { get; set; } = "";
    public string Place { get; set; } = "";
    /// <summary>The record word for word; built from the lines when left empty.</summary>
    public string Text { get; set; } = "";
    public bool AddDetails { get; set; } = true;
    public List<RecordLine> Lines { get; } = new();
}

/// <summary>
/// Transcription forms for parish registers (baptisms, marriages, burials), wills and probate, and deeds: each kind
/// has its people's roles and dates, and saving turns the record into facts in one step. The principal facts (the
/// christening, the marriage, the burial, the will and probate, the deed) get a citation with the transcript; a
/// baptised child is linked to the parents named and a new couple gets a family; everyone else in the tree who is
/// named in it (a godparent, witness, beneficiary or executor) gets an "appears in this record" event — the threads
/// that cluster research follows.
/// </summary>
public static class RecordTranscription
{
    public static IReadOnlyList<string> Roles(RecordKind kind) => kind switch
    {
        RecordKind.Baptism => new[] { "Child", "Father", "Mother", "Godparent", "Minister" },
        RecordKind.Marriage => new[] { "Groom", "Bride", "Groom's father", "Bride's father", "Witness", "Minister" },
        RecordKind.Burial => new[] { "Deceased", "Relative", "Minister" },
        RecordKind.Will => new[] { "Testator", "Beneficiary", "Executor", "Witness" },
        _ => new[] { "Grantor", "Grantee", "Witness" },
    };

    public static string Label(RecordKind kind) => kind switch
    {
        RecordKind.Baptism => "Baptism",
        RecordKind.Marriage => "Marriage",
        RecordKind.Burial => "Burial",
        RecordKind.Will => "Will or probate",
        _ => "Deed",
    };

    public static string DateLabel(RecordKind kind) => kind switch
    {
        RecordKind.Baptism => "Baptism date",
        RecordKind.Marriage => "Marriage date",
        RecordKind.Burial => "Burial date",
        RecordKind.Will => "Date written",
        _ => "Date of the deed",
    };

    /// <summary>The second date the kind has, or "" when it has none.</summary>
    public static string SecondDateLabel(RecordKind kind) => kind switch
    {
        RecordKind.Baptism => "Born (if given)",
        RecordKind.Will => "Date proved",
        _ => "",
    };

    public static string SourceType(RecordKind kind) => kind switch
    {
        RecordKind.Baptism or RecordKind.Marriage or RecordKind.Burial => "Parish register",
        RecordKind.Will => "Probate record",
        _ => "Land record",
    };

    /// <summary>The role a person starts with: the principal of the record.</summary>
    public static string PrincipalRole(RecordKind kind) => Roles(kind)[0];

    /// <summary>Saves the transcription: the source, the facts and the people. Returns everyone recorded.</summary>
    public static List<Person> Save(FamilyTree tree, Transcription t)
    {
        var title = t.SourceTitle.Trim().Length > 0 ? t.SourceTitle.Trim() : $"{Label(t.Kind)} record";
        var source = tree.Sources.Values.FirstOrDefault(s => s.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                     ?? tree.AddSource(new Source { Title = title, Type = SourceType(t.Kind), Kind = SourceKind.Original });
        var text = t.Text.Trim().Length > 0 ? t.Text.Trim() : Transcript(t);
        Citation Cite(EvidenceKind evidence) => new() { SourceId = source.Id, Page = t.Reference.Trim(), Text = text, Evidence = evidence };
        var date = GenDate.Parse(t.Date);
        var place = t.Place.Trim();

        var people = new List<(RecordLine Line, Person Person)>();
        foreach (var line in t.Lines)
        {
            var p = tree.Person(line.PersonId) ?? (line.AddAsNew && line.Name.Trim().Length > 0 ? NewPerson(tree, t, line) : null);
            if (p != null) people.Add((line, p));
        }
        Person? Role(string role) => people.FirstOrDefault(x => x.Line.Role == role).Person;
        IEnumerable<Person> All(string role) => people.Where(x => x.Line.Role == role).Select(x => x.Person);
        var principals = new HashSet<Person>();

        switch (t.Kind)
        {
            case RecordKind.Baptism when Role("Child") is { } child:
                child.Events.Add(Fact("CHR", date, place, Cite(EvidenceKind.Direct)));
                if (t.SecondDate.Trim().Length > 0 && child.Find("BIRT") == null)
                    child.Events.Insert(0, Fact("BIRT", GenDate.Parse(t.SecondDate), "", Cite(EvidenceKind.Direct)));
                if (Role("Father") != null || Role("Mother") != null) LinkChild(tree, child, Role("Father"), Role("Mother"));
                principals.Add(child);
                break;
            case RecordKind.Marriage when Role("Groom") is { } groom && Role("Bride") is { } bride:
                var family = tree.Families.Values.FirstOrDefault(f => f.PartnerIds.Contains(groom.Id) && f.PartnerIds.Contains(bride.Id))
                             ?? NewCouple(tree, groom, bride);
                family.Events.Add(Fact("MARR", date, place, Cite(EvidenceKind.Direct)));
                if (family.Status == CoupleStatus.Unspecified) family.Status = CoupleStatus.Married;
                principals.Add(groom);
                principals.Add(bride);
                break;
            case RecordKind.Burial when Role("Deceased") is { } dead:
                var burial = Fact("BURI", date, place, Cite(EvidenceKind.Direct));
                var age = people.First(x => x.Person == dead).Line.Age.Trim();
                if (age.Length > 0) burial.Description = $"Age: {age}";
                dead.Events.Add(burial);
                principals.Add(dead);
                break;
            case RecordKind.Will when Role("Testator") is { } testator:
                testator.Events.Add(Fact("WILL", date, place, Cite(EvidenceKind.Direct)));
                if (t.SecondDate.Trim().Length > 0) testator.Events.Add(Fact("PROB", GenDate.Parse(t.SecondDate), "", Cite(EvidenceKind.Direct)));
                principals.Add(testator);
                break;
            case RecordKind.Deed:
                var grantors = string.Join(" and ", All("Grantor").Select(p => p.DisplayName));
                var grantees = string.Join(" and ", All("Grantee").Select(p => p.DisplayName));
                foreach (var (line, p) in people.Where(x => x.Line.Role is "Grantor" or "Grantee"))
                {
                    var deed = Fact("EVEN", date, place, Cite(EvidenceKind.Direct));
                    deed.TypeName = "Deed";
                    deed.Description = string.Join("; ", new[]
                    {
                        line.Role == "Grantor" ? $"Granted to {Or(grantees, "the grantee")}" : $"Granted by {Or(grantors, "the grantor")}",
                        line.Note.Trim(),
                    }.Where(x => x.Length > 0));
                    p.Events.Add(deed);
                    principals.Add(p);
                }
                break;
        }

        // Everyone else named: an event saying where they appear, for following the family's associates.
        var principal = string.Join(" and ", principals.Select(p => p.DisplayName));
        foreach (var (line, p) in people.Where(x => !principals.Contains(x.Person)))
        {
            if (t.Kind == RecordKind.Baptism && line.Role is "Father" or "Mother") continue;   // linked as parents instead
            var named = Fact("EVEN", date, place, Cite(EvidenceKind.Indirect));
            named.TypeName = line.Role.Length > 0 ? line.Role : "Named in a record";
            named.Description = string.Join("; ", new[]
            {
                principal.Length > 0 ? $"{Label(t.Kind)} of {principal}" : Label(t.Kind), line.Note.Trim(),
            }.Where(x => x.Length > 0));
            p.Events.Add(named);
        }

        if (t.AddDetails)
            foreach (var (line, p) in people)
            {
                if (line.Occupation.Trim() is { Length: > 0 } job && !p.Events.Any(e => e.Tag == "OCCU" && e.Value == job && e.Date.Year == date.Year))
                {
                    var occupation = Fact("OCCU", date, place, Cite(EvidenceKind.Direct));
                    occupation.Value = job;
                    p.Events.Add(occupation);
                }
                if (line.Residence.Trim() is { Length: > 0 } home && !p.Events.Any(e => e.Tag == "RESI" && e.Place == home && e.Date.Year == date.Year))
                    p.Events.Add(Fact("RESI", date, home, Cite(EvidenceKind.Direct)));
            }
        return people.Select(x => x.Person).Distinct().ToList();
    }

    /// <summary>A plain transcript from the lines when the record's own words weren't typed in.</summary>
    public static string Transcript(Transcription t) => string.Join("\n", new[]
    {
        string.Join(", ", new[] { Label(t.Kind), t.Date.Trim(), t.Place.Trim() }.Where(x => x.Length > 0)),
    }.Concat(t.Lines.Where(l => l.Name.Trim().Length > 0).Select(l =>
        $"{(l.Role.Length > 0 ? l.Role + ": " : "")}{l.Name.Trim()}" +
        string.Concat(new[] { l.Age.Trim().Length > 0 ? $"age {l.Age.Trim()}" : "", l.Residence.Trim(), l.Occupation.Trim(), l.Note.Trim() }
            .Where(x => x.Length > 0).Select(x => ", " + x)))));

    private static Event Fact(string tag, GenDate date, string place, Citation citation)
    {
        var e = Event.Of(tag, date, place);
        e.Citations.Add(citation);
        return e;
    }

    private static string Or(string text, string otherwise) => text.Length > 0 ? text : otherwise;

    private static Person NewPerson(FamilyTree tree, Transcription t, RecordLine line)
    {
        var words = line.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var p = new Person
        {
            Sex = line.Sex.Trim().ToUpperInvariant() switch
            {
                "M" or "MALE" => Sex.Male,
                "F" or "FEMALE" => Sex.Female,
                _ => line.Role switch { "Groom" or "Father" or "Groom's father" or "Bride's father" => Sex.Male, "Bride" or "Mother" => Sex.Female, _ => Sex.Unknown },
            },
        };
        p.PrimaryName.Given = words.Length > 1 ? string.Join(' ', words[..^1]) : words[0];
        p.PrimaryName.Surname = words.Length > 1 ? words[^1] : "";
        // An age at a burial or marriage gives an estimated birth year.
        var year = GenDate.Parse(t.Date).NewStyleYear;
        if (year > 0 && int.TryParse(line.Age.Trim(), out var age) && age is >= 0 and < 120)
            p.Events.Add(Event.Of("BIRT", GenDate.Parse($"ABT {year - age}")));
        return tree.AddPerson(p);
    }

    /// <summary>Links a baptised child to the parents named: their existing family, or a new one.</summary>
    private static void LinkChild(FamilyTree tree, Person child, Person? father, Person? mother)
    {
        var family = tree.Families.Values.FirstOrDefault(f =>
                         (father == null || f.PartnerIds.Contains(father.Id)) && (mother == null || f.PartnerIds.Contains(mother.Id)))
                     ?? NewCouple(tree, father, mother);
        if (!family.Children.Any(c => c.ChildId == child.Id)) tree.AddChild(family, child);
    }

    private static Family NewCouple(FamilyTree tree, Person? husband, Person? wife)
    {
        var f = tree.AddFamily(new Family());
        if (husband != null) tree.SetPartner(f, husband, second: false);
        if (wife != null) tree.SetPartner(f, wife, second: true);
        return f;
    }
}
