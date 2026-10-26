using System.IO.Compression;
using Avalonia.Headless.XUnit;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Lineage.App.Reports;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.App.Tests;

/// <summary>Every narrative report renders to a real PDF and to a Word file that validates cleanly.</summary>
public class DocumentRenderTests
{
    private static FamilyTree Tree()
    {
        var t = GedcomImporter.Import(Demo.Gedcom);
        var thomas = t.People[1];
        thomas.Events.Add(new Event { Tag = "OCCU", Value = "Stonemason", Date = GenDate.Parse("1871") });
        thomas.Events.Add(new Event { Tag = "RESI", Place = "Hidden Lane", Private = true });
        thomas.Note = "He carved the font at St Mary’s.\n\nControl \u0001characters & <angle brackets> are fine.";
        return t;
    }

    private static IEnumerable<(string Name, ReportDocument Doc)> Documents(FamilyTree t)
    {
        var thomas = t.People[1];
        yield return ("register", Narrative.Register(t, thomas, 4));
        yield return ("summary", Narrative.IndividualSummary(t, thomas));
        yield return ("places", Narrative.PlaceList(t));
        yield return ("surnames", Narrative.SurnameList(t));
        yield return ("calendar", Narrative.BirthdayCalendar(t));
        yield return ("book", Narrative.FamilyBook(t, t.People[5], new FamilyBookOptions { Author = "A. Cousin" }));
    }

    [AvaloniaFact]
    public void EveryReportRendersAsPdf()
    {
        foreach (var (name, doc) in Documents(Tree()))
        {
            var path = Path.Combine(Path.GetTempPath(), $"lineage-{name}-{Guid.NewGuid():N}.pdf");
            try
            {
                DocumentPdf.Save(doc, path);
                var head = new byte[5];
                using (var f = File.OpenRead(path)) Assert.Equal(5, f.Read(head));
                Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
                Assert.True(new FileInfo(path).Length > 2000, name);
            }
            finally { File.Delete(path); }
        }
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(DocumentPdf.Generate(Narrative.SurnameList(Tree())), 0, 5));
    }

    [AvaloniaFact]
    public void EveryReportRendersAsAValidWordDocument()
    {
        foreach (var (name, doc) in Documents(Tree()))
        {
            var path = Path.Combine(Path.GetTempPath(), $"lineage-{name}-{Guid.NewGuid():N}.docx");
            try
            {
                DocumentDocx.Save(doc, path);
                using (var zip = ZipFile.OpenRead(path))
                {
                    Assert.NotNull(zip.GetEntry("word/document.xml"));
                    Assert.NotNull(zip.GetEntry("word/styles.xml"));
                    Assert.NotNull(zip.GetEntry("word/numbering.xml"));
                }

                using var word = WordprocessingDocument.Open(path, false);
                var body = word.MainDocumentPart!.Document.Body!;
                var text = body.InnerText;
                Assert.Contains(name is "places" ? "Harrow, Middlesex, England" : "Whitfield", text);
                Assert.DoesNotContain("Hidden Lane", text);
                Assert.Equal("Lineage Roots", word.PackageProperties.LastModifiedBy);
                Assert.Equal(doc.Title, word.PackageProperties.Title);

                var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(word)
                    .Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
                Assert.True(errors.Count == 0, $"{name}: {string.Join("\n", errors)}");
            }
            finally { File.Delete(path); }
        }
    }

    [AvaloniaFact]
    public void WordHeadingsUseWordsOwnStylesAndNotesLinkToTheirText()
    {
        var t = Tree();
        var doc = Narrative.FamilyBook(t, t.People[5]);
        using var stream = new MemoryStream();
        DocumentDocx.Write(doc, stream);
        stream.Position = 0;
        using var word = WordprocessingDocument.Open(stream, false);
        var main = word.MainDocumentPart!;
        var paragraphs = main.Document.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();
        string? Style(DocumentFormat.OpenXml.Wordprocessing.Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

        Assert.Contains(paragraphs, p => Style(p) == "Heading1" && p.InnerText == "Ancestors of Clara Whitfield");
        Assert.Contains(paragraphs, p => Style(p) == "Heading2" && p.InnerText == "Generation 2 — parents");
        Assert.Contains(paragraphs, p => Style(p) == "ListBullet");
        Assert.Contains(paragraphs, p => Style(p) == "TOC1");
        var styles = main.StyleDefinitionsPart!.Styles!.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().ToList();
        Assert.Contains(styles, s => s.StyleId == "Heading1" && s.StyleName!.Val == "heading 1");

        // every internal link has a bookmark to land on
        var bookmarks = main.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.BookmarkStart>().Select(b => b.Name!.Value).ToHashSet();
        var links = main.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.Hyperlink>().Select(h => h.Anchor!.Value).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, a => Assert.Contains(a, bookmarks));
        Assert.Contains(paragraphs, p => Style(p) == "Note" && p.InnerText.Contains("Parish registers of St Mary, Harrow"));
    }
}
