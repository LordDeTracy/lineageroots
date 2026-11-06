using Lineage.Core.Model;
using Lineage.Core.Reports;
using Lineage.Core.Sources;
using Lineage.Core.Storage;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class SourceTemplateTests
{
    private static Source Census() => new()
    {
        Template = "census-uk",
        Fields = { ["Year"] = "1881", ["Parish"] = "Leeds", ["County"] = "Yorkshire", ["Archive"] = "The National Archives", ["Piece"] = "RG 11/4532" },
    };

    [Fact]
    public void NotesAndBibliographyFollowTheTemplate()
    {
        var s = Census();
        Assert.Equal("1881 census of England and Wales, Leeds, Yorkshire, folio 12, p. 3; The National Archives, RG 11/4532",
            SourceTemplates.FullNote(s, "folio 12, p. 3"));
        Assert.Equal("1881 census, Leeds, Yorkshire, folio 14", SourceTemplates.ShortNote(s, "folio 14"));
        Assert.Equal("England. Yorkshire. Leeds. 1881 census. The National Archives, RG 11/4532.", SourceTemplates.Bibliography(s));
        Assert.Equal("1881 census, Leeds, Yorkshire", SourceTemplates.Title(s));
    }

    [Fact]
    public void EmptyFieldsTakeTheirOptionalPartsWithThem()
    {
        var s = Census();
        s.Fields.Remove("Piece");
        Assert.Equal("1881 census of England and Wales, Leeds, Yorkshire; The National Archives", SourceTemplates.FullNote(s, ""));
        Assert.Equal("England. Yorkshire. Leeds. 1881 census. The National Archives.", SourceTemplates.Bibliography(s));
        s.Fields.Remove("Archive");
        Assert.Equal("England. Yorkshire. Leeds. 1881 census.", SourceTemplates.Bibliography(s));   // "[ {Archive}[, {Piece}].]" goes whole

        var book = new Source { Template = "book", Fields = { ["Author"] = "Margaret Hartley", ["Title"] = "The Hartleys of Leeds", ["Publisher"] = "Thoresby Society", ["Year"] = "1931" } };
        Assert.Equal("Margaret Hartley, The Hartleys of Leeds (Thoresby Society, 1931), 45", SourceTemplates.FullNote(book, "45"));
        Assert.Equal("Margaret Hartley. The Hartleys of Leeds. Thoresby Society, 1931.", SourceTemplates.Bibliography(book));
    }

    [Fact]
    public void EveryTemplateRendersFromItsOwnExamples()
    {
        foreach (var t in SourceTemplates.All)
        {
            var s = new Source { Template = t.Id };
            foreach (var f in t.Fields) s.Fields[f.Key] = f.Example;
            foreach (var text in new[] { SourceTemplates.FullNote(s, "p. 12"), SourceTemplates.ShortNote(s, "p. 12"), SourceTemplates.Bibliography(s), SourceTemplates.Title(s) })
            {
                Assert.False(string.IsNullOrWhiteSpace(text), t.Id);
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain("[", text);
                Assert.DoesNotContain("  ", text);
                Assert.DoesNotContain(",,", text);
            }
            Assert.Contains("p. 12", SourceTemplates.FullNote(s, "p. 12"));
        }
        Assert.Equal(SourceTemplates.All.Count, SourceTemplates.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void AReportCitesInFullFirstThenShort()
    {
        var t = Whitfields();
        var census = t.AddSource(Census());
        var thomas = t.People[1];
        var occupation = thomas.Events.First(e => e.Tag == "OCCU");
        occupation.Citations.Add(new Citation { SourceId = census.Id, Page = "folio 12" });
        thomas.Events.First(e => e.Tag == "DEAT").Citations.Add(new Citation { SourceId = census.Id, Page = "folio 14" });
        var doc = Narrative.Register(t, thomas, 2);
        var notes = doc.Notes.Select(n => n.Text).ToList();
        Assert.Contains("1881 census of England and Wales, Leeds, Yorkshire, folio 12; The National Archives, RG 11/4532.", notes);
        Assert.Contains("1881 census, Leeds, Yorkshire, folio 14.", notes);
    }

    [Fact]
    public void TemplatesAreSavedWithTheTree()
    {
        var t = Whitfields();
        var census = t.AddSource(Census());
        var path = Path.Combine(Path.GetTempPath(), $"lineage-template-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(t, path);
            var back = LineageStore.Load(path).Sources[census.Id];
            Assert.Equal("census-uk", back.Template);
            Assert.Equal("RG 11/4532", back.Fields["Piece"]);
        }
        finally { File.Delete(path); }
    }
}
