using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lineage.App.Controls;

namespace Lineage.App.Tests;

public class NoteTextTests
{
    [AvaloniaFact]
    public void TheButtonsAddAndRemoveMarks()
    {
        var box = new TextBox { Text = "He was a stonemason." };
        box.SelectionStart = 9; box.SelectionEnd = 19;
        NoteText.Wrap(box, "**");
        Assert.Equal("He was a **stonemason**.", box.Text);
        NoteText.Wrap(box, "**");                                   // again takes it off
        Assert.Equal("He was a stonemason.", box.Text);

        box.SelectionStart = box.SelectionEnd = 4;                  // caret in "was": that word
        NoteText.Wrap(box, "*");
        Assert.Equal("He *was* a stonemason.", box.Text);

        box.Text = "font\nwall";
        box.SelectionStart = 0; box.SelectionEnd = box.Text.Length;
        NoteText.StartLines(box, "- ");
        Assert.Equal("- font\n- wall", box.Text);
        NoteText.StartLines(box, "- ");
        Assert.Equal("font\nwall", box.Text);
    }

    [AvaloniaFact]
    public void TheNoteViewShowsFormatting()
    {
        var view = new NoteView { Note = "**Bold** start\n\n- a point" };
        Assert.Equal(2, view.Children.Count);
        var first = Assert.IsType<SelectableTextBlock>(view.Children[0]);
        Assert.Equal(Avalonia.Media.FontWeight.SemiBold, ((Avalonia.Controls.Documents.Run)first.Inlines![0]).FontWeight);
    }
}
