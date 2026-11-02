using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class RecordTranscriptionTests
{
    private static (FamilyTree Tree, Person John, Person Mary) Couple()
    {
        var t = new FamilyTree();
        Person P(string given, string surname, Sex sex)
        {
            var p = t.AddPerson(new Person { Sex = sex });
            (p.PrimaryName.Given, p.PrimaryName.Surname) = (given, surname);
            return p;
        }
        return (t, P("John", "Gray", Sex.Male), P("Mary", "Moss", Sex.Female));
    }

    private static RecordLine Line(Person? p, string role, string name = "") =>
        new() { PersonId = p?.Id, AddAsNew = p == null, Name = p?.DisplayName ?? name, Role = role };

    [Fact]
    public void AMarriageEntryMakesTheCoupleAndNamesTheWitnesses()
    {
        var (t, john, mary) = Couple();
        var witness = t.AddPerson(new Person { Sex = Sex.Male });
        (witness.PrimaryName.Given, witness.PrimaryName.Surname) = ("Henry", "Moss");
        var tr = new Transcription
        {
            Kind = RecordKind.Marriage, SourceTitle = "St Peter's, Leeds: marriages", Reference = "1842 entry 57",
            Date = "12 Jun 1842", Place = "St Peter, Leeds, Yorkshire, England",
            Lines = { Line(john, "Groom"), Line(mary, "Bride"), Line(witness, "Witness") },
        };
        tr.Lines[0].Occupation = "Weaver";
        tr.Lines[1].Residence = "Kirkgate, Leeds";
        RecordTranscription.Save(t, tr);

        var family = Assert.Single(t.Families.Values);
        Assert.Equal(new[] { john.Id, mary.Id }, family.PartnerIds);
        var marr = Assert.Single(family.Events);
        Assert.Equal(("MARR", "12 JUN 1842"), (marr.Tag, marr.Date.ToGedcom()));
        var cite = Assert.Single(marr.Citations);
        Assert.Equal(("1842 entry 57", EvidenceKind.Direct), (cite.Page, cite.Evidence));
        Assert.Contains("Witness: Henry Moss", cite.Text);   // a transcript made from the lines
        Assert.Equal("Parish register", t.Sources[cite.SourceId].Type);

        var named = Assert.Single(witness.Events);
        Assert.Equal(("Witness", "Marriage of John Gray and Mary Moss"), (named.TypeName, named.Description));
        Assert.Equal("Weaver", john.Events.Single(e => e.Tag == "OCCU").Value);
        Assert.Equal("Kirkgate, Leeds", mary.Events.Single(e => e.Tag == "RESI").Place);
    }

    [Fact]
    public void ABaptismLinksTheNewChildToTheParents()
    {
        var (t, john, mary) = Couple();
        RecordTranscription.Save(t, new Transcription
        {
            Kind = RecordKind.Baptism, SourceTitle = "St Peter's, Leeds: baptisms", Date = "3 Mar 1844", SecondDate = "10 Feb 1844",
            Place = "St Peter, Leeds", Lines = { Line(null, "Child", "Ann Gray"), Line(john, "Father"), Line(mary, "Mother") },
        });
        var ann = t.People.Values.Single(p => p.PrimaryName.Given == "Ann");
        Assert.Equal("3 MAR 1844", ann.Find("CHR")!.Date.ToGedcom());
        Assert.Equal("10 FEB 1844", ann.Find("BIRT")!.Date.ToGedcom());
        var family = Assert.Single(t.Families.Values);
        Assert.Equal(new[] { john.Id, mary.Id }, family.PartnerIds);
        Assert.Contains(family.Children, c => c.ChildId == ann.Id);
        Assert.Empty(john.Events);   // parents are linked, not given "named in" events
    }

    [Fact]
    public void AWillRecordsProbateAndTheBeneficiaries()
    {
        var (t, john, mary) = Couple();
        var tr = new Transcription
        {
            Kind = RecordKind.Will, SourceTitle = "Prerogative Court of York", Date = "2 Jan 1870", SecondDate = "5 May 1871",
            Lines = { Line(john, "Testator"), Line(mary, "Beneficiary"), Line(null, "Executor", "Thomas Reed") },
        };
        tr.Lines[1].Note = "my dwelling house in Kirkgate";
        RecordTranscription.Save(t, tr);
        Assert.Equal("2 JAN 1870", john.Find("WILL")!.Date.ToGedcom());
        Assert.Equal("5 MAY 1871", john.Find("PROB")!.Date.ToGedcom());
        var bequest = Assert.Single(mary.Events);
        Assert.Equal(("Beneficiary", "Will or probate of John Gray; my dwelling house in Kirkgate"), (bequest.TypeName, bequest.Description));
        var executor = t.People.Values.Single(p => p.PrimaryName.Surname == "Reed");
        Assert.Equal("Executor", Assert.Single(executor.Events).TypeName);
    }

    [Fact]
    public void ADeedAndABurialBecomeFacts()
    {
        var (t, john, mary) = Couple();
        RecordTranscription.Save(t, new Transcription
        {
            Kind = RecordKind.Deed, Date = "1 Apr 1850", Place = "Leeds",
            Lines = { Line(john, "Grantor"), Line(mary, "Grantee") },
        });
        var deed = john.Events.Single(e => e.TypeName == "Deed");
        Assert.Equal("Granted to Mary Moss", deed.Description);
        Assert.Equal("Granted by John Gray", mary.Events.Single(e => e.TypeName == "Deed").Description);

        var burial = new Transcription { Kind = RecordKind.Burial, Date = "9 Nov 1871", Place = "Leeds", Lines = { Line(null, "Deceased", "Sarah Gray") } };
        burial.Lines[0].Age = "71";
        RecordTranscription.Save(t, burial);
        var sarah = t.People.Values.Single(p => p.PrimaryName.Given == "Sarah");
        Assert.Equal(("9 NOV 1871", "Age: 71"), (sarah.Find("BURI")!.Date.ToGedcom(), sarah.Find("BURI")!.Description));
        Assert.Equal("ABT 1800", sarah.Find("BIRT")!.Date.ToGedcom());   // estimated from the age
    }
}
