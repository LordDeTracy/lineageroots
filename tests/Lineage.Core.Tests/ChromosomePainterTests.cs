using Lineage.Core.Dna;
using Lineage.Core.Export;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

public class ChromosomePainterTests
{
    /// <summary>
    /// Ann's parents are John and Mary. John's parents are Walter and Edith, whose daughter Rose is the mother of Ann's
    /// cousin Paul; Mary's parents are Henry and Alice, whose son Frank is the father of Ann's cousin Grace. Ben is Ann's
    /// brother.
    /// </summary>
    private static (FamilyTree Tree, Person Ann) Family()
    {
        var t = new FamilyTree();
        Person P(string given, string surname, Sex sex)
        {
            var p = t.AddPerson(new Person { Sex = sex });
            (p.PrimaryName.Given, p.PrimaryName.Surname) = (given, surname);
            return p;
        }
        void Couple(Person a, Person b, params Person[] kids)
        {
            var f = t.AddFamily(new Family());
            t.SetPartner(f, a, second: false);
            if (b != null) t.SetPartner(f, b, second: true);
            foreach (var k in kids) t.AddChild(f, k, new ChildLink { ChildId = k.Id });
        }
        var walter = P("Walter", "Gray", Sex.Male); var edith = P("Edith", "Lane", Sex.Female);
        var henry = P("Henry", "Moss", Sex.Male); var alice = P("Alice", "Hart", Sex.Female);
        var john = P("John", "Gray", Sex.Male); var rose = P("Rose", "Gray", Sex.Female);
        var mary = P("Mary", "Moss", Sex.Female); var frank = P("Frank", "Moss", Sex.Male);
        var ann = P("Ann", "Gray", Sex.Female); var ben = P("Ben", "Gray", Sex.Male);
        var paul = P("Paul", "Reed", Sex.Male); var grace = P("Grace", "Moss", Sex.Female);
        Couple(walter, edith, john, rose);
        Couple(henry, alice, mary, frank);
        Couple(john, mary, ann, ben);
        Couple(P("Tom", "Reed", Sex.Male), rose, paul);
        Couple(frank, P("Jane", "Bell", Sex.Female), grace);
        return (t, ann);
    }

    private const string FamilyTreeDna =
        "Match Name,Chromosome,Start Location,End Location,Centimorgans,Matching SNPs\n" +
        "Paul Reed,1,1000000,30000000,40.5,5000\n" +
        "Paul Reed,7,50000000,90000000,35.2,4200\n" +
        "Grace Moss,1,20000000,60000000,38,4800\n" +
        "Ben Gray,2,10000000,20000000,12,1500\n" +
        "Sam Unknown,3,1000000,9000000,9.5,1100\n";

    [Fact]
    public void SegmentsAreReadFromEachCompanysFormat()
    {
        var (_, ann) = Family();
        var result = MatchImport.Import(ann, FamilyTreeDna, "FamilyTreeDNA");
        Assert.Equal("5 segments for 4 matches (4 new)", result.Summary);
        var paul = ann.DnaMatches.Single(m => m.Name == "Paul Reed");
        Assert.Equal((2, 75.7, 2, 40.5), (paul.ChromosomeSegments.Count, paul.SharedCm, paul.Segments, paul.LongestCm));
        Assert.Equal((7, 50000000L, 90000000L, 35.2, 4200), (paul.ChromosomeSegments[1].Chromosome, paul.ChromosomeSegments[1].Start,
                                                              paul.ChromosomeSegments[1].End, paul.ChromosomeSegments[1].Cm, paul.ChromosomeSegments[1].Snps));

        // Downloading again replaces a match's segments rather than doubling them.
        MatchImport.Import(ann, FamilyTreeDna, "FamilyTreeDNA");
        Assert.Equal(2, paul.ChromosomeSegments.Count);
        Assert.Equal(4, ann.DnaMatches.Count);

        // MyHeritage: the tester is "Name", the match is "Match name"; X is chromosome 23.
        MatchImport.Import(ann,
            "Name,Match name,Chromosome,Start Location,End Location,Start RSID,End RSID,Centimorgans,SNPs\n" +
            "Ann Gray,Grace Moss,X,5000000,25000000,rs1,rs2,22.5,3000\n", "FamilyTreeDNA");
        var grace = ann.DnaMatches.Single(m => m.Name == "Grace Moss");
        Assert.Equal(ChromosomePainter.X, Assert.Single(grace.ChromosomeSegments).Chromosome);

        // GEDmatch's one-to-one names nobody, so the caller says whose segments they are.
        const string gedmatch = "Chr,B37 Start Pos'n,B37 End Pos'n,Centimorgans (cM),SNPs,Segment threshold\n" +
                                "4,1000000,21000000,25.1,2900,7\n";
        Assert.True(SegmentImport.NeedsMatchName(gedmatch));
        Assert.False(SegmentImport.NeedsMatchName(FamilyTreeDna));
        Assert.NotEmpty(MatchImport.Import(ann, gedmatch).Problems);
        MatchImport.Import(ann, gedmatch, "", "Paul Reed");
        Assert.Equal(4, Assert.Single(paul.ChromosomeSegments).Chromosome);
    }

    [Fact]
    public void AMatchListIsNotTakenForSegments()
    {
        var (_, ann) = Family();
        var result = MatchImport.Import(ann, "Name,Shared cM,Shared Segments,Longest Segment\nPaul Reed,75.7,2,40.5\n");
        Assert.Equal("1 match added, 0 updated", result.Summary);
        Assert.Empty(ann.DnaMatches.Single().ChromosomeSegments);
    }

    [Fact]
    public void SegmentsArePaintedWithTheCommonAncestors()
    {
        var (tree, ann) = Family();
        MatchImport.Import(ann, FamilyTreeDna);
        Person Named(string given) => tree.People.Values.Single(p => p.PrimaryName.Given == given);
        foreach (var name in new[] { "Paul", "Grace", "Ben" })
            ann.DnaMatches.Single(m => m.Name.StartsWith(name)).PersonId = Named(name).Id;

        var painting = ChromosomePainter.Paint(tree, ann);
        Assert.Equal((1, 0), (painting.Unlinked, painting.Unrelated));   // Sam isn't in the tree yet
        Assert.Equal(new[] { ("Walter Gray & Edith Lane", PaintSide.Father), ("Henry Moss & Alice Hart", PaintSide.Mother), ("John Gray & Mary Moss", PaintSide.Unknown) },
                     painting.Groups.Select(g => (g.Label, g.Side)));
        Assert.Equal(new[] { "Paul Reed" }, painting.Groups[0].Matches);
        Assert.Equal((2, 75.7), (painting.Groups[0].Segments, Math.Round(painting.Groups[0].Cm, 1)));
        Assert.All(painting.Segments.Where(s => s.Match.Name == "Grace Moss"), s => Assert.Equal((PaintSide.Mother, 2), (s.Side, s.Group)));

        // Paul covers 29 + 40 million base pairs of Ann's 2.88 billion on her father's side.
        Assert.Equal(69_000_000.0 / ChromosomePainter.AutosomalLength, painting.Coverage(PaintSide.Father), 6);
    }

    [Fact]
    public void SegmentsSurviveGedcom()
    {
        var (tree, ann) = Family();
        MatchImport.Import(ann, FamilyTreeDna);
        var path = Path.Combine(Path.GetTempPath(), $"lineage-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(tree, path);
            foreach (var back in new[] { GedcomImporter.Import(GedcomExporter.Export(tree)), LineageStore.Load(path) })
            {
                var paul = back.People[ann.Id].DnaMatches.Single(m => m.Name == "Paul Reed");
                Assert.Equal(new[] { "1 1000000 30000000 40.5 5000", "7 50000000 90000000 35.2 4200" },
                             paul.ChromosomeSegments.Select(s => $"{s.Chromosome} {s.Start} {s.End} {s.Cm} {s.Snps}"));
            }
        }
        finally { File.Delete(path); }
    }
}
