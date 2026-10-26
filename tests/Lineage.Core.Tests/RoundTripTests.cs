using System.Text.Json;
using Lineage.Core.Export;
using Lineage.Core.Import.Aq;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Tests;

public class RoundTripTests
{
    /// <summary>A small hand-built tree that exercises every model feature.</summary>
    public static FamilyTree SampleTree()
    {
        var t = new FamilyTree { Name = "Test" };
        var repo = t.AddRepository(new Repository { Name = "County Archive", Address = "1 Main St\nTown" });
        var src = t.AddSource(new Source { Title = "Parish Register", Author = "Clerk", RepositoryId = repo.Id, Quality = 3 });
        var dad = t.AddPerson(new Person { Sex = Sex.Male, Note = "A long note " + new string('x', 600) + " end." });
        dad.PrimaryName.Given = "John Henry"; dad.PrimaryName.Surname = "Doe";
        dad.Events.Add(new Event { Tag = "BIRT", Date = GenDate.Parse("ABT 1900"), Place = "Townsville, County, State",
                                   Citations = { } });
        dad.Events[0].Citations.Add(new Citation { SourceId = src.Id, Page = "p. 12", Quality = 2 });
        dad.Events[0].Location = new GeoPoint(42.2626, -71.8023);
        dad.Media.Add(new MediaLink { File = @"C:\Photos\john.jpg", Title = "John at the farm", MediaType = "PHOTO",
                                      Primary = true, Location = new GeoPoint(-33.8688, 151.2093), Taken = "1925:06:01 12:00:00" });
        dad.Events.Add(new Event { Tag = "OCCU", Value = "Farmer", Date = GenDate.Parse("FROM 1920 TO 1960") });
        dad.Tags.Add("Direct line");
        dad.Colour = "Green";
        dad.DnaTests.Add(new DnaTest { Kind = "Y-DNA", Haplogroup = "R1b", Markers = { new("DYS393", 13) } });
        var mum = t.AddPerson(new Person { Sex = Sex.Female });
        mum.PrimaryName.Given = "Mary"; mum.PrimaryName.Surname = "Roe";
        var kid = t.AddPerson(new Person { Sex = Sex.Female });
        kid.PrimaryName.Given = "Ann"; kid.PrimaryName.Surname = "Doe";
        var fam = t.AddFamily(new Family { Status = CoupleStatus.Married });
        t.SetPartner(fam, dad, second: false);
        t.SetPartner(fam, mum, second: true);
        fam.Events.Add(Event.Of("MARR", GenDate.Parse("7 JUN 1928"), "Chapel"));
        t.AddChild(fam, kid, new ChildLink { ChildId = kid.Id, ToFather = ParentRelation.Adopted });
        var todo = t.AddResearch(new ResearchItem { Description = "Find census", Priority = 2 });
        dad.ResearchIds.Add(todo.Id);
        return t;
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static string Fingerprint(FamilyTree t) => JsonSerializer.Serialize(new
    {
        People = t.People.Values.OrderBy(p => p.Id).Select(p => new
        {
            p.Id, p.DisplayName, p.Sex, p.Note, p.Colour, Tags = string.Join("|", p.Tags),
            Events = p.Events.Select(e => $"{e.Tag}|{e.Value}|{e.Date.ToGedcom()}|{e.Place}|{e.Location}|{e.Citations.Count}").ToArray(),
            Media = p.Media.Select(m => $"{m.File}|{m.Title}|{m.Primary}|{m.Location}|{m.Taken}").ToArray(),
            p.ParentFamilyIds, p.SpouseFamilyIds, Dna = p.DnaTests.Count, p.ResearchIds,
        }),
        Families = t.Families.Values.OrderBy(f => f.Id).Select(f => new
        {
            f.Id, f.Partner1Id, f.Partner2Id, f.Status,
            Children = f.Children.Select(c => $"{c.ChildId}:{c.ToFather}:{c.ToMother}").ToArray(),
            Events = f.Events.Select(e => $"{e.Tag}|{e.Date.ToGedcom()}|{e.Place}").ToArray(),
        }),
        Sources = t.Sources.Values.Select(s => $"{s.Id}|{s.Title}|{s.RepositoryId}|{s.Quality}"),
        Repos = t.Repositories.Values.Select(r => $"{r.Id}|{r.Name}"),
        Research = t.Research.Values.Select(r => $"{r.Id}|{r.Description}|{r.Priority}"),
    }, Json);

    [Fact]
    public void StoreSaveLoadPreservesEverything()
    {
        var t = SampleTree();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(t, path);
            var back = LineageStore.Load(path);
            Assert.Equal(Fingerprint(t), Fingerprint(back));
            Assert.Equal("Test", back.Name);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void GedcomExportImportPreservesEverything()
    {
        var t = SampleTree();
        var text = GedcomExporter.Export(t);
        Assert.All(text.Split('\n'), l => Assert.True(l.Length <= 255, "line too long: " + l));
        var back = GedcomImporter.Import(text);
        back.Name = t.Name;
        Assert.Equal(Fingerprint(t), Fingerprint(back));
    }

    public static TheoryData<string> SampleNames => Samples.Names();

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void RealTreeSurvivesStoreAndGedcom(string name)
    {
        if (Samples.Get(name) is not var (aqPath, _)) return;
        var t = AqImporter.Import(aqPath);
        var path = Path.Combine(Path.GetTempPath(), $"lineage-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(t, path);
            var stored = LineageStore.Load(path);
            Assert.Equal(Fingerprint(t), Fingerprint(stored));
            var viaGedcom = GedcomImporter.Import(GedcomExporter.Export(stored));
            viaGedcom.Name = t.Name;
            Assert.Equal(Fingerprint(t), Fingerprint(viaGedcom));
        }
        finally { File.Delete(path); }
    }
}
