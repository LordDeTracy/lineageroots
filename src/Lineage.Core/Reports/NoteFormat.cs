using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Lineage.Core.Reports;

/// <summary>
/// Light formatting in notes, typed as plain text so files and GEDCOM stay readable anywhere:
/// **bold**, *italic*, a line starting "- " (or "• ") for a bullet point, and "## " for a small heading.
/// A blank line starts a new paragraph; single line breaks inside a paragraph are kept.
/// </summary>
public static class NoteFormat
{
    public enum Kind { Paragraph, Bullet, Heading }

    /// <summary>A paragraph, bullet point or heading, with its text split into plain, bold and italic pieces.</summary>
    public sealed record Piece(Kind Kind, IReadOnlyList<Run> Runs);

    private static readonly Regex Marks = new(@"\*\*(.+?)\*\*|(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])", RegexOptions.Compiled);

    /// <summary>The note in pieces, in order.</summary>
    public static IReadOnlyList<Piece> Parse(string? note)
    {
        var pieces = new List<Piece>();
        var para = new List<string>();
        void Flush()
        {
            if (para.Count > 0) pieces.Add(new Piece(Kind.Paragraph, Inline(string.Join("\n", para))));
            para.Clear();
        }
        foreach (var raw in (note ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0) { Flush(); continue; }
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("• ") || trimmed.StartsWith("* ") && !trimmed.StartsWith("**"))
            {
                Flush();
                pieces.Add(new Piece(Kind.Bullet, Inline(trimmed[2..].Trim())));
            }
            else if (trimmed.StartsWith("## ") || trimmed.StartsWith("# "))
            {
                Flush();
                pieces.Add(new Piece(Kind.Heading, Inline(trimmed.TrimStart('#').Trim())));
            }
            else para.Add(trimmed);
        }
        Flush();
        return pieces;
    }

    /// <summary>Plain, bold and italic pieces of one line or paragraph.</summary>
    public static IReadOnlyList<Run> Inline(string text)
    {
        var runs = new List<Run>();
        var at = 0;
        foreach (Match m in Marks.Matches(text))
        {
            if (m.Index > at) runs.Add(new Run(text[at..m.Index]));
            runs.Add(m.Groups[1].Success ? new Run(m.Groups[1].Value, Bold: true) : new Run(m.Groups[2].Value, Italic: true));
            at = m.Index + m.Length;
        }
        if (at < text.Length) runs.Add(new Run(text[at..]));
        return runs;
    }

    /// <summary>The note's words without the marks, e.g. for search results and tooltips.</summary>
    public static string Plain(string? note) =>
        string.Join("\n\n", Parse(note).Select(p => (p.Kind == Kind.Bullet ? "• " : "") + string.Concat(p.Runs.Select(r => r.Text))));

    /// <summary>The note as report blocks: paragraphs, a bullet list for each run of bullet points, small headings.</summary>
    public static IEnumerable<Block> Blocks(string? note)
    {
        var bullets = new List<ListItem>();
        foreach (var piece in Parse(note))
        {
            if (piece.Kind == Kind.Bullet) { bullets.Add(new ListItem(piece.Runs)); continue; }
            if (bullets.Count > 0) { yield return new BulletList(bullets.ToList()); bullets.Clear(); }
            yield return piece.Kind == Kind.Heading
                ? new Heading(3, string.Concat(piece.Runs.Select(r => r.Text)))
                : new Paragraph(piece.Runs.Select(r => r with { Text = r.Text.Replace('\n', ' ') }).ToList());
        }
        if (bullets.Count > 0) yield return new BulletList(bullets);
    }

    /// <summary>The note as safe HTML (everything typed is escaped), for the family website and the phone viewer.</summary>
    public static string Html(string? note)
    {
        var sb = new StringBuilder();
        var inList = false;
        foreach (var piece in Parse(note))
        {
            if (piece.Kind == Kind.Bullet && !inList) { sb.Append("<ul>"); inList = true; }
            if (piece.Kind != Kind.Bullet && inList) { sb.Append("</ul>"); inList = false; }
            var inner = string.Concat(piece.Runs.Select(r =>
            {
                var t = WebUtility.HtmlEncode(r.Text).Replace("\n", "<br>");
                return r.Bold ? $"<strong>{t}</strong>" : r.Italic ? $"<em>{t}</em>" : t;
            }));
            sb.Append(piece.Kind switch { Kind.Bullet => $"<li>{inner}</li>", Kind.Heading => $"<h3>{inner}</h3>", _ => $"<p>{inner}</p>" });
        }
        if (inList) sb.Append("</ul>");
        return sb.ToString();
    }
}
