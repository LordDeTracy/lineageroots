using Lineage.Core.Reports;

namespace Lineage.Core.Tests;

public class NoteFormatTests
{
    private const string Note = "## His trade\nHe was a **stonemason** and *very* proud of it.\nHe signed with a mark.\n\n- carved the font\n- built the wall at 2*3 Mill Lane\n\nThe end.";

    [Fact]
    public void NotesHaveBoldItalicBulletsAndHeadings()
    {
        var p = NoteFormat.Parse(Note);
        Assert.Equal(new[] { NoteFormat.Kind.Heading, NoteFormat.Kind.Paragraph, NoteFormat.Kind.Bullet, NoteFormat.Kind.Bullet, NoteFormat.Kind.Paragraph },
                     p.Select(x => x.Kind));
        Assert.Equal("His trade", p[0].Runs.Single().Text);
        Assert.Contains(p[1].Runs, r => r.Text == "stonemason" && r.Bold);
        Assert.Contains(p[1].Runs, r => r.Text == "very" && r.Italic);
        Assert.Contains("\nHe signed", string.Concat(p[1].Runs.Select(r => r.Text)));   // a single line break stays
        Assert.Equal("built the wall at 2*3 Mill Lane", string.Concat(p[3].Runs.Select(r => r.Text)));  // a lone * isn't italic
        Assert.DoesNotContain("**", NoteFormat.Plain(Note));
    }

    [Fact]
    public void ReportsWebPagesAndPlainNotesAllWork()
    {
        var blocks = NoteFormat.Blocks(Note).ToList();
        Assert.IsType<Heading>(blocks[0]);
        Assert.Equal(2, Assert.IsType<BulletList>(blocks[2]).Items.Count);

        var html = NoteFormat.Html(Note + "\n\n<script>alert(1)</script>");
        Assert.Contains("<strong>stonemason</strong>", html);
        Assert.Contains("<em>very</em>", html);
        Assert.Contains("<ul><li>carved the font</li>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);

        // An old note with no marks reads exactly as before.
        var plain = NoteFormat.Parse("Born in a snowstorm.\n\nMoved west in 1880.");
        Assert.Equal(2, plain.Count);
        Assert.All(plain, x => Assert.False(x.Runs.Single().Bold));
    }
}
