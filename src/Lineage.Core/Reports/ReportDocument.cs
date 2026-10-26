namespace Lineage.Core.Reports;

/// <summary>
/// A report as plain structure — headings, paragraphs, lists, tables, endnotes and an index — with no
/// layout. The narrative builders write one of these and each renderer (PDF, Word, ...) lays it out,
/// so a report is written once and every format says the same thing.
/// </summary>
public sealed class ReportDocument
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    /// <summary>Who prepared it; renderers fall back to "Lineage Roots".</summary>
    public string Author { get; set; } = "";
    /// <summary>A book gets a title page and a table of contents built from its anchored headings.</summary>
    public bool IsBook { get; set; }
    public List<Block> Blocks { get; } = new();
    /// <summary>Numbered endnotes (usually source citations); <see cref="Note.Number"/> matches the references in the text.</summary>
    public List<Note> Notes { get; } = new();
    public string NotesHeading { get; set; } = "Notes";
    /// <summary>Names and where they appear, alphabetical; empty for reports that don't need one.</summary>
    public List<IndexEntry> Index { get; } = new();
    public string IndexHeading { get; set; } = "Index of names";
    /// <summary>A line explaining the index references, e.g. what "A12" means.</summary>
    public string IndexNote { get; set; } = "";

    public ReportDocument Add(Block block) { Blocks.Add(block); return this; }

    /// <summary>Headings a table of contents should list (levels 1 and 2 that carry an anchor).</summary>
    public IEnumerable<Heading> Contents => Blocks.OfType<Heading>().Where(h => h.Anchor is { Length: > 0 } && h.Level <= 2);

    /// <summary>All the words of the document (title, blocks, notes) — handy for tests and search.</summary>
    public string PlainText()
    {
        var parts = new List<string> { Title, Subtitle };
        foreach (var b in Blocks)
            switch (b)
            {
                case Heading h: parts.Add(h.Text); break;
                case Paragraph p: parts.Add(p.Text); break;
                case BulletList l: parts.AddRange(l.Items.Select(i => (i.Label is { Length: > 0 } ? i.Label + " " : "") + Run.Join(i.Runs))); break;
                case Table t:
                    parts.Add(string.Join(" | ", t.Headers));
                    parts.AddRange(t.Rows.Select(r => string.Join(" | ", r.Select(c => c.Text))));
                    break;
            }
        parts.AddRange(Notes.Select(n => $"{n.Number}. {n.Text}"));
        parts.AddRange(Index.Select(e => $"{e.Name} {string.Join(", ", e.Refs.Select(r => r.Label))}"));
        return string.Join("\n", parts.Where(s => s.Length > 0));
    }
}

/// <summary>One piece of a report's body.</summary>
public abstract record Block;

/// <summary>A heading; level 1 starts a part or chapter, 2 a section, 3 a minor heading. The anchor lets links and contents point here.</summary>
public sealed record Heading(int Level, string Text, string? Anchor = null) : Block;

public enum ParagraphStyle { Normal, Muted, Centered }

/// <summary>Running text made of styled runs; the optional anchor marks it as a link target (e.g. a numbered person entry).</summary>
public sealed record Paragraph(IReadOnlyList<Run> Runs, string? Anchor = null, ParagraphStyle Style = ParagraphStyle.Normal) : Block
{
    public Paragraph(string text, ParagraphStyle style = ParagraphStyle.Normal) : this(new[] { new Run(text) }, null, style) { }
    public string Text => Run.Join(Runs);
}

/// <summary>
/// A list. Items without a <see cref="ListItem.Label"/> get a bullet; labelled items (such as
/// "+ 5  ii." in a register report's list of children) show the label instead.
/// </summary>
public sealed record BulletList(IReadOnlyList<ListItem> Items) : Block;

public sealed record ListItem(IReadOnlyList<Run> Runs, string? Label = null, string? LabelAnchor = null)
{
    public ListItem(string text) : this(new[] { new Run(text) }) { }
}

/// <summary>A table with a header row. <see cref="Widths"/> are optional relative column widths.</summary>
public sealed record Table(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<Cell>> Rows, IReadOnlyList<float>? Widths = null) : Block;

/// <summary>A table cell; plain strings convert implicitly.</summary>
public sealed record Cell(IReadOnlyList<Run> Runs)
{
    public static implicit operator Cell(string text) => new(new[] { new Run(text) });
    public string Text => Run.Join(Runs);
}

/// <summary>Starts a new page in paged formats.</summary>
public sealed record PageBreak : Block;

/// <summary>
/// A stretch of text. <see cref="Link"/> points at an anchor elsewhere in the document;
/// a run with <see cref="NoteNumber"/> is a superscript reference to that endnote (its text is ignored).
/// </summary>
public sealed record Run(string Text, bool Bold = false, bool Italic = false, string? Link = null, int? NoteNumber = null)
{
    public static Run B(string text) => new(text, Bold: true);
    public static Run I(string text) => new(text, Italic: true);
    public static Run To(string text, string anchor, bool bold = false) => new(text, bold, Link: anchor);
    public static Run Ref(int note) => new("", NoteNumber: note);

    /// <summary>The visible text of the run (a note reference shows its number).</summary>
    public string Visible => NoteNumber is { } n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : Text;

    public static string Join(IEnumerable<Run> runs) => string.Concat(runs.Select(r => r.NoteNumber is { } n ? $"[{n}]" : r.Text));
}

/// <summary>An endnote. Notes are numbered from 1 in the order they are first cited.</summary>
public sealed record Note(int Number, string Text)
{
    public string Anchor => NoteAnchor(Number);
    public static string NoteAnchor(int number) => $"note-{number}";
}

/// <summary>A name in the index and the entries that mention it.</summary>
public sealed record IndexEntry(string Name, IReadOnlyList<IndexRef> Refs);

/// <summary>Where a name appears: the anchor of the entry and a short label for it ("A12", "D3").</summary>
public sealed record IndexRef(string Anchor, string Label);
