using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Lineage.Core.Reports;
using Ext = DocumentFormat.OpenXml.ExtendedProperties;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Lineage.App.Reports;

/// <summary>
/// Writes a <see cref="ReportDocument"/> as a Word document people can keep editing. Headings use
/// Word's own Heading 1–3 styles, so the navigation pane and Insert › Table of Contents just work;
/// lists use a real bullet numbering definition; note references link to a numbered Notes section.
/// Books get a title page and a table of contents field (Word fills in page numbers when it is updated).
/// </summary>
public static class DocumentDocx
{
    private const string Ink = "22252A", Muted = "6F7378", Faint = "9A9DA2", Line = "E4DED5", Accent = "2E6B58", Tint = "F6F3EE";
    private const string NotesAnchor = "notes", IndexAnchor = "index";

    public static void Save(ReportDocument doc, string path)
    {
        using var file = File.Create(path);
        Write(doc, file);
    }

    public static void Write(ReportDocument doc, Stream stream)
    {
        using var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        Feed(main.AddNewPart<StyleDefinitionsPart>(), Styles);
        Feed(main.AddNewPart<NumberingDefinitionsPart>(), Numbering);
        Feed(main.AddNewPart<DocumentSettingsPart>(), Settings);
        var footer = main.AddNewPart<FooterPart>();
        Feed(footer, Footer);

        var letter = RegionInfo.CurrentRegion.TwoLetterISORegionName is "US" or "CA";
        var (pageWidth, pageHeight) = letter ? (12240u, 15840u) : (11906u, 16838u);
        const int margin = 1080;
        var writer = new Writer(doc, (int)pageWidth - 2 * margin);
        var body = new W.Body();
        foreach (var e in writer.Body()) body.Append(e);
        var section = new W.SectionProperties(
            new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
            new W.PageSize { Width = pageWidth, Height = pageHeight },
            new W.PageMargin { Top = margin, Right = (uint)margin, Bottom = margin, Left = (uint)margin, Header = 567, Footer = 567, Gutter = 0 });
        if (doc.IsBook) section.Append(new W.TitlePage());  // no page number on the title page
        body.Append(section);
        main.Document = new W.Document(body);

        var props = package.PackageProperties;
        props.Title = doc.Title;
        props.Subject = doc.Subtitle;
        props.Creator = doc.Author.Length > 0 ? doc.Author : "Lineage Roots";
        props.LastModifiedBy = "Lineage Roots";
        props.Created = props.Modified = DateTime.UtcNow;
        var app = package.AddExtendedFilePropertiesPart();
        app.Properties = new Ext.Properties(new Ext.Application("Lineage Roots"));
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(s);
    }

    private sealed class Writer
    {
        private readonly ReportDocument _doc;
        private readonly int _width;
        private readonly Dictionary<string, string> _bookmarks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
        private int _nextBookmark = 1;

        public Writer(ReportDocument doc, int width)
        {
            _doc = doc;
            _width = width;
            // Name every anchor up front so links can be checked against what really exists.
            foreach (var b in doc.Blocks)
                if (b switch { Heading h => h.Anchor, Paragraph p => p.Anchor, _ => null } is { Length: > 0 } a) Bookmark(a);
            foreach (var n in doc.Notes) Bookmark(n.Anchor);
            if (doc.Notes.Count > 0) Bookmark(NotesAnchor);
            if (doc.Index.Count > 0) Bookmark(IndexAnchor);
        }

        public IEnumerable<OpenXmlElement> Body()
        {
            if (_doc.IsBook)
            {
                yield return Para("Title", new[] { Text(_doc.Title) }, spacingBefore: 2800, center: true);
                if (_doc.Subtitle.Length > 0) yield return Para("Subtitle", new[] { Text(_doc.Subtitle) }, center: true);
                if (_doc.Author.Length > 0) yield return Para(null, new[] { Text($"Prepared by {_doc.Author}") }, spacingBefore: 600, center: true);
                yield return Para("Muted", new[] { Text($"{DateTime.Now:d MMMM yyyy} · made with Lineage Roots") }, center: true);
                yield return PageBreakParagraph();
                foreach (var e in Contents()) yield return e;
            }
            else
            {
                yield return Para("Title", new[] { Text(_doc.Title) });
                if (_doc.Subtitle.Length > 0) yield return Para("Subtitle", new[] { Text(_doc.Subtitle) });
                yield return Para("Muted", new[] { Text($"Prepared {DateTime.Now:d MMMM yyyy} with Lineage Roots") });
            }

            foreach (var block in _doc.Blocks)
                foreach (var e in Block(block)) yield return e;

            if (_doc.Notes.Count > 0)
            {
                if (_doc.IsBook) yield return PageBreakParagraph();
                yield return Para("Heading1", new[] { Text(_doc.NotesHeading) }, NotesAnchor);
                foreach (var n in _doc.Notes)
                    yield return Para("Note", new OpenXmlElement[] { Text($"{n.Number}.", color: Faint), new W.Run(new W.TabChar()), Text(n.Text) }, n.Anchor);
            }

            if (_doc.Index.Count > 0)
            {
                yield return PageBreakParagraph();
                yield return Para("Heading1", new[] { Text(_doc.IndexHeading) }, IndexAnchor);
                if (_doc.IndexNote.Length > 0) yield return Para("Muted", new[] { Text(_doc.IndexNote) });
                foreach (var entry in _doc.Index)
                {
                    var content = new List<OpenXmlElement> { Text(entry.Name), Text("  ") };
                    for (var i = 0; i < entry.Refs.Count; i++)
                    {
                        if (i > 0) content.Add(Text(", ", color: Faint));
                        content.Add(Link(entry.Refs[i].Anchor, Text(entry.Refs[i].Label, style: "Hyperlink")));
                    }
                    yield return Para("IndexEntry", content);
                }
            }
        }

        private IEnumerable<OpenXmlElement> Block(Block block)
        {
            switch (block)
            {
                case Heading h:
                    yield return Para($"Heading{Math.Clamp(h.Level, 1, 3)}", new[] { Text(h.Text) }, h.Anchor);
                    break;
                case Paragraph p:
                    yield return Para(p.Style == ParagraphStyle.Muted ? "Muted" : null, Runs(p.Runs), p.Anchor, center: p.Style == ParagraphStyle.Centered);
                    break;
                case BulletList list:
                    foreach (var item in list.Items)
                    {
                        if (item.Label is { Length: > 0 } label)
                        {
                            var labelRun = Text(label, bold: true, color: Accent);
                            var content = new List<OpenXmlElement> { item.LabelAnchor is { } a && _bookmarks.ContainsKey(a) ? Link(a, labelRun) : labelRun, new W.Run(new W.TabChar()) };
                            content.AddRange(Runs(item.Runs));
                            yield return Para("LabelledList", content);
                        }
                        else yield return Para("ListBullet", Runs(item.Runs));
                    }
                    break;
                case Table t:
                    foreach (var e in Table(t)) yield return e;
                    break;
                case PageBreak:
                    yield return PageBreakParagraph();
                    break;
            }
        }

        private IEnumerable<OpenXmlElement> Table(Table t)
        {
            var columns = Math.Max(t.Headers.Count, t.Rows.Count == 0 ? 0 : t.Rows.Max(r => r.Count));
            if (columns == 0) yield break;
            var weights = Enumerable.Range(0, columns).Select(i => t.Widths is { } w && i < w.Count && w[i] > 0 ? w[i] : 1f).ToList();
            var total = weights.Sum();
            var widths = weights.Select(w => (int)Math.Floor(_width * w / total)).ToList();

            var table = new W.Table();
            table.Append(new W.TableProperties(
                new W.TableStyle { Val = "ReportTable" },
                new W.TableWidth { Width = _width.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa },
                new W.TableLayout { Type = W.TableLayoutValues.Fixed }));
            table.Append(new W.TableGrid(widths.Select(w => new W.GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));

            var header = new W.TableRow(new W.TableRowProperties(new W.TableHeader()));
            for (var i = 0; i < columns; i++)
                header.Append(Cell(widths[i], new[] { Text(i < t.Headers.Count ? t.Headers[i] : "", bold: true, color: Muted, halfPoints: 17) }, shade: true));
            table.Append(header);
            foreach (var row in t.Rows)
            {
                var tr = new W.TableRow(new W.TableRowProperties(new W.CantSplit()));
                for (var i = 0; i < columns; i++) tr.Append(Cell(widths[i], i < row.Count ? Runs(row[i].Runs) : Array.Empty<OpenXmlElement>()));
                table.Append(tr);
            }
            yield return table;
            yield return Para("TableAfter", Array.Empty<OpenXmlElement>());  // Word needs a paragraph between neighbouring tables
        }

        private static W.TableCell Cell(int width, IEnumerable<OpenXmlElement> content, bool shade = false)
        {
            var props = new W.TableCellProperties { TableCellWidth = new W.TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa } };
            if (shade) props.Shading = new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = Tint };
            var p = new W.Paragraph(new W.ParagraphProperties { ParagraphStyleId = new W.ParagraphStyleId { Val = "TableText" } });
            foreach (var e in content) p.Append(e);
            return new W.TableCell(props, p);
        }

        /// <summary>A Word TOC field over the book's anchored headings; the entries are links until Word updates it with page numbers.</summary>
        private IEnumerable<OpenXmlElement> Contents()
        {
            var entries = _doc.Contents.Select(h => (h.Level, h.Text, Anchor: h.Anchor!)).ToList();
            if (entries.All(e => e.Level != 1)) yield break;
            if (_doc.Notes.Count > 0) entries.Add((1, _doc.NotesHeading, NotesAnchor));
            if (_doc.Index.Count > 0) entries.Add((1, _doc.IndexHeading, IndexAnchor));

            yield return Para("ContentsHeading", new[] { Text("Contents") });
            for (var i = 0; i < entries.Count; i++)
            {
                var (level, text, anchor) = entries[i];
                var content = new List<OpenXmlElement>();
                if (i == 0)
                {
                    content.Add(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }));
                    content.Add(new W.Run(new W.FieldCode(" TOC \\o \"1-2\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }));
                    content.Add(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }));
                }
                content.Add(Link(anchor, Text(text)));
                if (i == entries.Count - 1) content.Add(new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End }));
                yield return Para(level == 1 ? "TOC1" : "TOC2", content);
            }
            yield return Para("Muted", new[] { Text("To add page numbers, right-click the table of contents and choose Update Field.") });
            yield return PageBreakParagraph();
        }

        // ---- building blocks ---------------------------------------------------------------------

        private W.Paragraph Para(string? style, IEnumerable<OpenXmlElement> content, string? anchor = null, int spacingBefore = 0, bool center = false)
        {
            var p = new W.Paragraph();
            var props = new W.ParagraphProperties();
            if (style != null) props.ParagraphStyleId = new W.ParagraphStyleId { Val = style };
            if (spacingBefore > 0) props.SpacingBetweenLines = new W.SpacingBetweenLines { Before = spacingBefore.ToString(CultureInfo.InvariantCulture) };
            if (center) props.Justification = new W.Justification { Val = W.JustificationValues.Center };
            if (props.HasChildren) p.Append(props);
            string? id = null;
            if (anchor is { Length: > 0 } && _bookmarks.TryGetValue(anchor, out var name))
            {
                id = (_nextBookmark++).ToString(CultureInfo.InvariantCulture);
                p.Append(new W.BookmarkStart { Name = name, Id = id });
            }
            foreach (var e in content) p.Append(e);
            if (id != null) p.Append(new W.BookmarkEnd { Id = id });
            return p;
        }

        private static W.Paragraph PageBreakParagraph() => new(new W.Run(new W.Break { Type = W.BreakValues.Page }));

        private IEnumerable<OpenXmlElement> Runs(IEnumerable<Run> runs)
        {
            foreach (var r in runs)
            {
                if (r.NoteNumber is { } n)
                {
                    var number = Text(n.ToString(CultureInfo.InvariantCulture), style: "NoteReference");
                    yield return _bookmarks.ContainsKey(Note.NoteAnchor(n)) ? Link(Note.NoteAnchor(n), number) : number;
                    continue;
                }
                if (r.Text.Length == 0) continue;
                if (r.Link is { } link && _bookmarks.ContainsKey(link))
                    yield return Link(link, Text(r.Text, r.Bold, r.Italic, style: "Hyperlink"));
                else yield return Text(r.Text, r.Bold, r.Italic);
            }
        }

        private W.Hyperlink Link(string anchor, W.Run run) => new(run) { Anchor = _bookmarks[anchor], History = true };

        private static W.Run Text(string text, bool bold = false, bool italic = false, string? style = null, string? color = null, int? halfPoints = null)
        {
            var props = new W.RunProperties();
            if (style != null) props.RunStyle = new W.RunStyle { Val = style };
            if (bold) props.Bold = new W.Bold();
            if (italic) props.Italic = new W.Italic();
            if (color != null) props.Color = new W.Color { Val = color };
            if (halfPoints is { } hp)
            {
                props.FontSize = new W.FontSize { Val = hp.ToString(CultureInfo.InvariantCulture) };
                props.FontSizeComplexScript = new W.FontSizeComplexScript { Val = hp.ToString(CultureInfo.InvariantCulture) };
            }
            var run = new W.Run();
            if (props.HasChildren) run.Append(props);
            run.Append(new W.Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve });
            return run;
        }

        /// <summary>A Word bookmark name for an anchor: letters, digits and underscores, at most 40 characters, unique.</summary>
        private string Bookmark(string anchor)
        {
            if (_bookmarks.TryGetValue(anchor, out var existing)) return existing;
            var sb = new StringBuilder("r_");
            foreach (var c in anchor) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
            var name = sb.Length > 36 ? sb.ToString(0, 36) : sb.ToString();
            for (var n = 2; _usedNames.Contains(name); n++) name = $"{(sb.Length > 32 ? sb.ToString(0, 32) : sb.ToString())}_{n}";
            _usedNames.Add(name);
            _bookmarks[anchor] = name;
            return name;
        }

        /// <summary>Drops characters XML can't carry (stray control codes from old imports).</summary>
        private static string Clean(string s)
        {
            if (s.All(c => c >= ' ' && !char.IsSurrogate(c))) return s;
            var sb = new StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
                if (char.IsSurrogate(c)) continue;
                if (c == '\t' || c == '\n' || c == '\r') sb.Append(' ');
                else if (c >= ' ') sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // ---- package parts ---------------------------------------------------------------------------

    private const string Ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly string Styles = $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="{{Ns}}">
          <w:docDefaults>
            <w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:eastAsia="Calibri" w:cs="Calibri"/><w:color w:val="{{Ink}}"/><w:sz w:val="21"/><w:szCs w:val="21"/></w:rPr></w:rPrDefault>
            <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault>
          </w:docDefaults>
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
          <w:style w:type="character" w:default="1" w:styleId="DefaultParagraphFont"><w:name w:val="Default Paragraph Font"/><w:uiPriority w:val="1"/><w:semiHidden/><w:unhideWhenUsed/></w:style>
          <w:style w:type="table" w:default="1" w:styleId="TableNormal"><w:name w:val="Normal Table"/><w:uiPriority w:val="99"/><w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w="0" w:type="dxa"/><w:tblCellMar><w:top w:w="0" w:type="dxa"/><w:left w:w="108" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/><w:right w:w="108" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style>
          <w:style w:type="numbering" w:default="1" w:styleId="NoList"><w:name w:val="No List"/><w:uiPriority w:val="99"/><w:semiHidden/><w:unhideWhenUsed/></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="10"/><w:qFormat/><w:pPr><w:spacing w:after="60" w:line="240" w:lineRule="auto"/><w:contextualSpacing/></w:pPr><w:rPr><w:b/><w:sz w:val="48"/><w:szCs w:val="48"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="11"/><w:qFormat/><w:pPr><w:spacing w:after="60"/></w:pPr><w:rPr><w:color w:val="{{Muted}}"/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="9"/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before="360" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="{{Accent}}"/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="9"/><w:unhideWhenUsed/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before="280" w:after="80"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:sz w:val="25"/><w:szCs w:val="25"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="9"/><w:unhideWhenUsed/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before="220" w:after="60"/><w:outlineLvl w:val="2"/></w:pPr><w:rPr><w:b/><w:caps/><w:color w:val="{{Accent}}"/><w:spacing w:val="10"/><w:sz w:val="17"/><w:szCs w:val="17"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="Muted"><w:name w:val="Lineage Muted"/><w:basedOn w:val="Normal"/><w:qFormat/><w:rPr><w:color w:val="{{Muted}}"/><w:sz w:val="18"/><w:szCs w:val="18"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="ListBullet"><w:name w:val="List Bullet"/><w:basedOn w:val="Normal"/><w:uiPriority w:val="99"/><w:unhideWhenUsed/><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr><w:spacing w:after="40"/><w:ind w:left="720" w:hanging="360"/><w:contextualSpacing/></w:pPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="LabelledList"><w:name w:val="Lineage Labelled List"/><w:basedOn w:val="Normal"/><w:pPr><w:tabs><w:tab w:val="left" w:pos="1440"/></w:tabs><w:spacing w:after="40"/><w:ind w:left="1440" w:hanging="1080"/></w:pPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="Note"><w:name w:val="Lineage Note"/><w:basedOn w:val="Normal"/><w:pPr><w:tabs><w:tab w:val="left" w:pos="500"/></w:tabs><w:spacing w:after="60" w:line="240" w:lineRule="auto"/><w:ind w:left="500" w:hanging="500"/></w:pPr><w:rPr><w:sz w:val="18"/><w:szCs w:val="18"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="IndexEntry"><w:name w:val="index 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="99"/><w:unhideWhenUsed/><w:pPr><w:spacing w:after="20" w:line="240" w:lineRule="auto"/><w:ind w:left="220" w:hanging="220"/></w:pPr><w:rPr><w:sz w:val="18"/><w:szCs w:val="18"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="TableText"><w:name w:val="Lineage Table Text"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:before="40" w:after="40" w:line="240" w:lineRule="auto"/></w:pPr><w:rPr><w:sz w:val="19"/><w:szCs w:val="19"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="TableAfter"><w:name w:val="Lineage After Table"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="0" w:line="120" w:lineRule="exact"/></w:pPr><w:rPr><w:sz w:val="8"/><w:szCs w:val="8"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:customStyle="1" w:styleId="ContentsHeading"><w:name w:val="Lineage Contents Heading"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="0" w:after="240"/></w:pPr><w:rPr><w:b/><w:color w:val="{{Accent}}"/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="TOC1"><w:name w:val="toc 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="39"/><w:unhideWhenUsed/><w:pPr><w:spacing w:before="120" w:after="40"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="TOC2"><w:name w:val="toc 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="39"/><w:unhideWhenUsed/><w:pPr><w:spacing w:after="20"/><w:ind w:left="240"/></w:pPr><w:rPr><w:color w:val="{{Muted}}"/></w:rPr></w:style>
          <w:style w:type="character" w:styleId="Hyperlink"><w:name w:val="Hyperlink"/><w:basedOn w:val="DefaultParagraphFont"/><w:uiPriority w:val="99"/><w:unhideWhenUsed/><w:rPr><w:color w:val="{{Accent}}"/></w:rPr></w:style>
          <w:style w:type="character" w:customStyle="1" w:styleId="NoteReference"><w:name w:val="Lineage Note Reference"/><w:basedOn w:val="DefaultParagraphFont"/><w:rPr><w:color w:val="{{Accent}}"/><w:vertAlign w:val="superscript"/></w:rPr></w:style>
          <w:style w:type="table" w:customStyle="1" w:styleId="ReportTable"><w:name w:val="Lineage Table"/><w:basedOn w:val="TableNormal"/><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:space="0" w:color="{{Line}}"/><w:bottom w:val="single" w:sz="4" w:space="0" w:color="{{Line}}"/><w:insideH w:val="single" w:sz="4" w:space="0" w:color="{{Line}}"/></w:tblBorders><w:tblCellMar><w:top w:w="0" w:type="dxa"/><w:left w:w="80" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/><w:right w:w="80" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style>
        </w:styles>
        """;

    private static readonly string Numbering = $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:numbering xmlns:w="{{Ns}}">
          <w:abstractNum w:abstractNumId="0">
            <w:multiLevelType w:val="singleLevel"/>
            <w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="•"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr><w:rPr><w:color w:val="{{Accent}}"/></w:rPr></w:lvl>
          </w:abstractNum>
          <w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
        </w:numbering>
        """;

    private const string Settings = $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:settings xmlns:w="{{Ns}}">
          <w:defaultTabStop w:val="720"/>
          <w:characterSpacingControl w:val="doNotCompress"/>
          <w:compat><w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/></w:compat>
        </w:settings>
        """;

    private const string Footer = $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:ftr xmlns:w="{{Ns}}">
          <w:p>
            <w:pPr><w:jc w:val="center"/></w:pPr>
            <w:r><w:rPr><w:color w:val="{{Faint}}"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr><w:t xml:space="preserve">Page </w:t></w:r>
            <w:fldSimple w:instr=" PAGE "><w:r><w:rPr><w:color w:val="{{Faint}}"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>
            <w:r><w:rPr><w:color w:val="{{Faint}}"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr><w:t xml:space="preserve"> of </w:t></w:r>
            <w:fldSimple w:instr=" NUMPAGES "><w:r><w:rPr><w:color w:val="{{Faint}}"/><w:sz w:val="16"/><w:szCs w:val="16"/></w:rPr><w:t>1</w:t></w:r></w:fldSimple>
          </w:p>
        </w:ftr>
        """;
}
