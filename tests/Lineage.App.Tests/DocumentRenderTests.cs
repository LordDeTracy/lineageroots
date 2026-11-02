using QuestPDF.Fluent;
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
        yield return ("book with photos", BookWithPhoto(t));
    }

    /// <summary>A small portrait on disk, made the way a camera's JPEG would be read.</summary>
    private static readonly Lazy<string> Portrait = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"lineage-portrait-{Guid.NewGuid():N}.png");
        using var bmp = new SkiaSharp.SKBitmap(60, 80);
        bmp.Erase(new SkiaSharp.SKColor(120, 90, 60));
        using var data = SkiaSharp.SKImage.FromBitmap(bmp).Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    });

    private static ReportDocument BookWithPhoto(FamilyTree t)
    {
        t.People[1].Media.Add(new MediaLink { File = Portrait.Value, Title = "Thomas in 1900", MediaType = "PHOTO", Primary = true });
        var options = new FamilyBookOptions { Author = "A. Cousin", FindMedia = f => f, AncestorGenerations = 0 };
        options.Chapters.Add(new BookChapter("Stories from the farm", "Grandmother kept bees.\n\nThe barn burned in 1902."));
        return Narrative.FamilyBook(t, t.People[1], options);
    }

    [AvaloniaFact]
    public void ABookCarriesPhotosAndChaptersYouWrite()
    {
        var doc = BookWithPhoto(Tree());
        Assert.Contains(doc.Blocks.OfType<Heading>(), h => h.Text == "Stories from the farm");
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text == "Grandmother kept bees.");
        Assert.DoesNotContain(doc.Blocks.OfType<Heading>(), h => h.Text.StartsWith("Ancestors of"));   // left out
        Assert.Contains(doc.Blocks.OfType<Picture>(), p => p.Caption == "Thomas in 1900");

        var path = Path.Combine(Path.GetTempPath(), $"lineage-photo-{Guid.NewGuid():N}.docx");
        try
        {
            DocumentDocx.Save(doc, path);
            using var word = WordprocessingDocument.Open(path, false);
            Assert.Single(word.MainDocumentPart!.ImageParts);
            Assert.True(DocumentPdf.Generate(doc).Length > DocumentPdf.Generate(Narrative.FamilyBook(Tree(), Tree().People[1],
                new FamilyBookOptions { AncestorGenerations = 0, IncludePhotos = false })).Length);
        }
        finally { File.Delete(path); }
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

public class BuiltInFontTests
{
    [Fact]
    public void LatoIsBuiltInAndThereIsNoFontFolder()
    {
        Assert.Equal(18, Lineage.App.Reports.PdfFonts.Count);
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "LatoFont")));

        // A PDF made now embeds Lato (QuestPDF's default), so the built-in copy really is being used.
        Lineage.App.Reports.PdfFonts.Setup();
        var pdf = QuestPDF.Fluent.Document.Create(d => d.Page(page => page.Content().Text("Lineage Roots"))).GeneratePdf();
        Assert.Contains("Lato", System.Text.Encoding.Latin1.GetString(pdf));
    }
}
