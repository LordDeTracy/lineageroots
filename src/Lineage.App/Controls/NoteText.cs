using Avalonia.Controls;

namespace Lineage.App.Controls;

/// <summary>The formatting buttons for a note box: marks around the selection, or at the start of the selected lines.</summary>
public static class NoteText
{
    /// <summary>Puts <paramref name="mark"/> around the selection (or the word at the caret), or takes it off again.</summary>
    public static void Wrap(TextBox box, string mark)
    {
        var text = box.Text ?? "";
        int start = Math.Min(box.SelectionStart, box.SelectionEnd), end = Math.Max(box.SelectionStart, box.SelectionEnd);
        if (start == end)
        {
            // No selection: the word under the caret.
            while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        }
        var inner = text[start..end];
        string result;
        if (inner.Length > 2 * mark.Length && inner.StartsWith(mark) && inner.EndsWith(mark))
            result = inner[mark.Length..^mark.Length];
        else
            result = mark + inner + mark;
        box.Text = text[..start] + result + text[end..];
        box.SelectionStart = start;
        box.SelectionEnd = start + result.Length;
        box.Focus();
    }

    /// <summary>Starts every selected line (or the caret's line) with <paramref name="prefix"/>, or removes it if all have it.</summary>
    public static void StartLines(TextBox box, string prefix)
    {
        var text = (box.Text ?? "").Replace("\r\n", "\n");
        int start = Math.Min(box.SelectionStart, box.SelectionEnd), end = Math.Max(box.SelectionStart, box.SelectionEnd);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        if (start == 0) lineStart = 0;
        var lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;
        var lines = text[lineStart..lineEnd].Split('\n');
        var all = lines.All(l => l.StartsWith(prefix));
        var changed = string.Join("\n", lines.Select(l => all ? l[prefix.Length..] : l.Length == 0 ? l : prefix + l));
        if (lines.Length == 1 && lines[0].Length == 0) changed = prefix;
        box.Text = text[..lineStart] + changed + text[lineEnd..];
        box.SelectionStart = lineStart;
        box.SelectionEnd = lineStart + changed.Length;
        box.Focus();
    }
}
