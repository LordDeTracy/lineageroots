using System.Text;

namespace Lineage.Core.Import.Gedcom;

/// <summary>One GEDCOM line with its sub-lines; CONC/CONT are folded into <see cref="Value"/>.</summary>
public sealed class GedcomNode
{
    public string Tag { get; init; } = "";
    public string? Xref { get; init; }
    public string Value { get; set; } = "";
    public List<GedcomNode> Children { get; } = new();

    public GedcomNode? First(string tag) => Children.FirstOrDefault(c => c.Tag == tag);
    public IEnumerable<GedcomNode> All(string tag) => Children.Where(c => c.Tag == tag);
    public string Text(string tag) => First(tag)?.Value ?? "";

    /// <summary>Parses GEDCOM text into level-0 records.</summary>
    public static List<GedcomNode> Parse(string text)
    {
        var roots = new List<GedcomNode>();
        var stack = new List<GedcomNode>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart(' ', '\t', '﻿');
            if (line.Length == 0) continue;
            var sp = line.IndexOf(' ');
            if (sp <= 0 || !int.TryParse(line.AsSpan(0, sp), out var level)) continue;
            var rest = line[(sp + 1)..];
            string? xref = null;
            if (rest.StartsWith('@'))
            {
                var end = rest.IndexOf("@ ", 1, StringComparison.Ordinal);
                if (end > 0) { xref = rest[..(end + 1)]; rest = rest[(end + 2)..]; }
            }
            var sp2 = rest.IndexOf(' ');
            var tag = sp2 < 0 ? rest : rest[..sp2];
            if (!tag.StartsWith('_')) tag = tag.ToUpperInvariant();   // custom tags keep their case (_Marker)
            var value = sp2 < 0 ? "" : rest[(sp2 + 1)..];

            if ((tag == "CONC" || tag == "CONT") && level > 0 && stack.Count >= level)
            {
                var owner = stack[level - 1];
                owner.Value += (tag == "CONT" ? "\n" : "") + value;
                continue;
            }
            var node = new GedcomNode { Tag = tag, Xref = xref, Value = value };
            if (level == 0) { roots.Add(node); stack.Clear(); stack.Add(node); continue; }
            if (level > stack.Count) continue;            // malformed jump; skip line
            stack.RemoveRange(level, stack.Count - level);
            stack[level - 1].Children.Add(node);
            stack.Add(node);
        }
        return roots;
    }

    /// <summary>Reads a GEDCOM file as UTF-8 (BOM or not), falling back to Windows-1252.</summary>
    public static string ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
}
