using System.Globalization;
using Lineage.Core.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lineage.App.Reports;

/// <summary>
/// Lays out a <see cref="ReportDocument"/> as a PDF in the same look as <see cref="ReportBuilder"/>:
/// title header, accent headings, page numbers, numbered notes at the end and, for books, a title page,
/// a table of contents with page numbers and an index of names. Paper follows the computer's region
/// (Letter in the US and Canada, A4 elsewhere).
/// </summary>
public static class DocumentPdf
{
    private const string Ink = "#22252A", Muted = "#6F7378", Faint = "#9A9DA2", Line = "#E4DED5", Accent = "#2E6B58";

    static DocumentPdf() => QuestPDF.Settings.License = LicenseType.Community; // QuestPDF's free Community licence (questpdf.com/license)

    private static PageSize Paper => RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "CA" ? PageSizes.Letter : PageSizes.A4;

    public static void Save(ReportDocument doc, string path) => Create(doc).GeneratePdf(path);

    public static byte[] Generate(ReportDocument doc) => Create(doc).GeneratePdf();

    private const string NotesAnchor = "notes", IndexAnchor = "index";

    private static Document Create(ReportDocument doc)
    {
        // Links only to anchors that exist: a dangling link would point nowhere.
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in doc.Blocks)
            if (b switch { Heading h => h.Anchor, Paragraph p => p.Anchor, _ => null } is { Length: > 0 } a) anchors.Add(a);
        foreach (var n in doc.Notes) anchors.Add(n.Anchor);
        if (doc.Notes.Count > 0) anchors.Add(NotesAnchor);
        if (doc.Index.Count > 0) anchors.Add(IndexAnchor);
        var r = new Renderer(doc, anchors);
        var prepared = $"Prepared {DateTime.Now:d MMMM yyyy} with Lineage Roots";

        return Document.Create(c =>
        {
            if (doc.IsBook)
                c.Page(page =>
                {
                    Setup(page);
                    page.Content().PaddingTop(150).Column(col =>
                    {
                        col.Item().AlignCenter().Text(Clean(doc.Title)).FontSize(30).SemiBold();
                        if (doc.Subtitle.Length > 0) col.Item().PaddingTop(10).AlignCenter().Text(Clean(doc.Subtitle)).FontSize(14).FontColor(Muted);
                        col.Item().PaddingVertical(24).AlignCenter().Width(80).LineHorizontal(1.5f).LineColor(Accent);
                        if (doc.Author.Length > 0) col.Item().AlignCenter().Text($"Prepared by {Clean(doc.Author)}").FontSize(11);
                        col.Item().PaddingTop(4).AlignCenter()
                           .Text(doc.Author.Length > 0 ? $"{DateTime.Now:d MMMM yyyy} · made with Lineage Roots" : prepared)
                           .FontSize(9).FontColor(Faint);
                    });
                });

            c.Page(page =>
            {
                Setup(page);
                if (doc.IsBook)
                    page.Header().PaddingBottom(10).Column(h =>
                    {
                        h.Item().Text(Clean(doc.Title)).FontSize(8.5f).FontColor(Faint);
                        h.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor(Line);
                    });
                else
                    page.Header().PaddingBottom(14).Column(h =>
                    {
                        h.Item().Text(Clean(doc.Title)).FontSize(19).SemiBold();
                        if (doc.Subtitle.Length > 0) h.Item().Text(Clean(doc.Subtitle)).FontSize(10).FontColor(Muted);
                        h.Item().Text(prepared).FontSize(8.5f).FontColor(Faint);
                        h.Item().PaddingTop(8).LineHorizontal(1).LineColor(Line);
                    });
                page.Content().Column(col =>
                {
                    if (doc.IsBook) r.Contents(col);
                    foreach (var block in doc.Blocks) r.Block(col, block);
                    r.Notes(col);
                    r.Index(col);
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(8).FontColor(Faint));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }).WithMetadata(new DocumentMetadata
        {
            Title = doc.Title,
            Subject = doc.Subtitle,
            Author = doc.Author.Length > 0 ? doc.Author : "Lineage Roots",
            Creator = "Lineage Roots",
            Producer = "Lineage Roots",
        });
    }

    /// <summary>Drops control characters (left over from old imports) that would print as boxes.</summary>
    private static string Clean(string s) => s.Any(char.IsControl) ? new string(s.Select(c => char.IsControl(c) ? ' ' : c).ToArray()) : s;

    private static void Setup(PageDescriptor page)
    {
        page.Size(Paper);
        page.Margin(44);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Ink));
    }

    private sealed class Renderer
    {
        private readonly ReportDocument _doc;
        private readonly HashSet<string> _anchors;

        public Renderer(ReportDocument doc, HashSet<string> anchors) { _doc = doc; _anchors = anchors; }

        public void Block(ColumnDescriptor col, Block block)
        {
            switch (block)
            {
                case Heading h: Heading(col, h.Level, h.Text, h.Anchor); break;
                case Paragraph p: Paragraph(col, p); break;
                case BulletList l: List(col, l); break;
                case Table t: Table(col, t); break;
                case PageBreak: col.Item().PageBreak(); break;
            }
        }

        private void Heading(ColumnDescriptor col, int level, string text, string? anchor)
        {
            text = Clean(text);
            var item = col.Item().EnsureSpace(70);  // keep a heading with the text that follows it
            if (anchor is { Length: > 0 }) item = item.Section(anchor);
            switch (level)
            {
                case <= 1 when _doc.IsBook:
                    item.PaddingTop(6).PaddingBottom(10).Column(c =>
                    {
                        c.Item().Text(text).FontSize(21).SemiBold().FontColor(Accent);
                        c.Item().PaddingTop(6).Width(56).LineHorizontal(1.5f).LineColor(Accent);
                    });
                    break;
                case <= 1:
                    item.PaddingTop(18).PaddingBottom(6).Text(text).FontSize(13.5f).SemiBold().FontColor(Accent);
                    break;
                case 2:
                    item.PaddingTop(14).PaddingBottom(4).Text(text).FontSize(11.5f).SemiBold();
                    break;
                default:
                    item.PaddingTop(12).PaddingBottom(3).Text(text.ToUpperInvariant()).FontSize(8.5f).SemiBold().FontColor(Accent).LetterSpacing(0.08f);
                    break;
            }
        }

        private void Paragraph(ColumnDescriptor col, Paragraph p)
        {
            var item = col.Item();
            if (p.Anchor is { Length: > 0 } a) item = item.Section(a);
            item = item.PaddingTop(p.Style == ParagraphStyle.Muted ? 2 : 6).PaddingBottom(2);
            if (p.Style == ParagraphStyle.Centered) item = item.AlignCenter();
            item.Text(t =>
            {
                t.DefaultTextStyle(x => p.Style == ParagraphStyle.Muted ? x.FontSize(9).FontColor(Muted).LineHeight(1.35f) : x.LineHeight(1.4f));
                Runs(t, p.Runs);
            });
        }

        private void List(ColumnDescriptor col, BulletList list)
        {
            var longest = list.Items.Max(i => i.Label?.Length ?? 0);
            var labelWidth = longest == 0 ? 12f : Math.Clamp(longest * 5.2f + 8, 18f, 90f);
            foreach (var i in list.Items)
                col.Item().PaddingTop(3).PaddingLeft(12).Row(row =>
                {
                    var label = row.ConstantItem(labelWidth);
                    if (i.Label is { Length: > 0 } text)
                        label.Text(t =>
                        {
                            var span = i.LabelAnchor is { } anchor && _anchors.Contains(anchor) ? t.SectionLink(Clean(text), anchor) : t.Span(Clean(text));
                            span.FontSize(9.5f).FontColor(Accent).SemiBold();
                        });
                    else label.Text("•").FontColor(Accent);
                    row.RelativeItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(9.5f).LineHeight(1.35f));
                        Runs(t, i.Runs);
                    });
                });
        }

        private void Table(ColumnDescriptor col, Table table)
        {
            var columns = Math.Max(table.Headers.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count));
            if (columns == 0) return;
            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    for (var i = 0; i < columns; i++)
                        c.RelativeColumn(table.Widths is { } w && i < w.Count && w[i] > 0 ? w[i] : 1);
                });
                t.Header(h =>
                {
                    for (var i = 0; i < columns; i++)
                        h.Cell().BorderBottom(1).BorderColor(Line).PaddingVertical(4).PaddingRight(6)
                         .Text(i < table.Headers.Count ? Clean(table.Headers[i]) : "").FontSize(8.5f).FontColor(Faint).SemiBold();
                });
                foreach (var row in table.Rows)
                    for (var i = 0; i < columns; i++)
                        t.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingRight(6).Text(x =>
                        {
                            x.DefaultTextStyle(s => s.FontSize(9.5f));
                            if (i < row.Count) Runs(x, row[i].Runs);
                        });
            });
        }

        /// <summary>The table of contents: every anchored level 1 and 2 heading, then the notes and index.</summary>
        public void Contents(ColumnDescriptor col)
        {
            var entries = _doc.Contents.Select(h => (h.Level, h.Text, Anchor: h.Anchor!)).ToList();
            if (entries.All(e => e.Level != 1)) return;
            if (_doc.Notes.Count > 0) entries.Add((1, _doc.NotesHeading, NotesAnchor));
            if (_doc.Index.Count > 0) entries.Add((1, _doc.IndexHeading, IndexAnchor));
            col.Item().PaddingBottom(14).Text("Contents").FontSize(21).SemiBold().FontColor(Accent);
            foreach (var (level, text, anchor) in entries)
                col.Item().PaddingLeft(level == 1 ? 0 : 16).PaddingTop(level == 1 ? 7 : 2).SectionLink(anchor).Row(row =>
                {
                    var name = row.RelativeItem().Text(Clean(text));
                    if (level == 1) name.SemiBold(); else name.FontSize(9.5f).FontColor(Muted);
                    row.ConstantItem(40).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(level == 1 ? 10 : 9.5f).FontColor(level == 1 ? Ink : Muted));
                        t.BeginPageNumberOfSection(anchor);
                    });
                });
            col.Item().PageBreak();
        }

        public void Notes(ColumnDescriptor col)
        {
            if (_doc.Notes.Count == 0) return;
            if (_doc.IsBook) col.Item().PageBreak();
            Heading(col, 1, _doc.NotesHeading, NotesAnchor);
            foreach (var n in _doc.Notes)
                col.Item().Section(n.Anchor).PaddingTop(3).Row(row =>
                {
                    row.ConstantItem(26).Text($"{n.Number}.").FontSize(9).FontColor(Faint);
                    row.RelativeItem().Text(Clean(n.Text)).FontSize(9).LineHeight(1.3f);
                });
        }

        public void Index(ColumnDescriptor col)
        {
            if (_doc.Index.Count == 0) return;
            col.Item().PageBreak();
            Heading(col, 1, _doc.IndexHeading, IndexAnchor);
            if (_doc.IndexNote.Length > 0) col.Item().PaddingBottom(6).Text(Clean(_doc.IndexNote)).FontSize(9).FontColor(Muted);
            col.Item().MultiColumn(mc =>
            {
                mc.Columns(2);
                mc.Spacing(18);
                mc.Content().Column(c =>
                {
                    foreach (var e in _doc.Index)
                        c.Item().PaddingTop(2).Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(9));
                            t.Span(Clean(e.Name));
                            t.Span("  ");
                            for (var i = 0; i < e.Refs.Count; i++)
                            {
                                if (i > 0) t.Span(", ").FontColor(Faint);
                                var r = e.Refs[i];
                                (_anchors.Contains(r.Anchor) ? t.SectionLink(r.Label, r.Anchor) : t.Span(r.Label)).FontColor(Accent);
                            }
                        });
                });
            });
        }

        private void Runs(TextDescriptor t, IEnumerable<Run> runs)
        {
            foreach (var r in runs)
            {
                if (r.NoteNumber is { } n)
                {
                    var number = n.ToString(CultureInfo.InvariantCulture);
                    var anchor = Note.NoteAnchor(n);
                    (_anchors.Contains(anchor) ? t.SectionLink(number, anchor) : t.Span(number)).Superscript().FontColor(Accent);
                    continue;
                }
                if (r.Text.Length == 0) continue;
                var linked = r.Link is { } link && _anchors.Contains(link);
                var text = Clean(r.Text);
                var span = linked ? t.SectionLink(text, r.Link!) : t.Span(text);
                if (r.Bold) span.SemiBold();
                if (r.Italic) span.Italic();
                if (linked) span.FontColor(Accent);
            }
        }
    }
}
