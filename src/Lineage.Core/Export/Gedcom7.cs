using System.Text;
using System.Text.RegularExpressions;
using Lineage.Core.Import.Gedcom;
using Lineage.Core.Model;

namespace Lineage.Core.Export;

/// <summary>
/// Writes a tree as GEDCOM 7.0. The tree is written as for 5.5.1 and then brought into line with version 7: no CONC or
/// CHAR, photos as multimedia records (OBJE) with MIME types, associations with ROLE and PHRASE, name types and
/// pedigree words in capitals, FamilySearch IDs as EXID, dual-dated years as a date with a PHRASE. Lineage's own
/// extension tags (starting with _) stay, as version 7 allows.
/// </summary>
public static class Gedcom7
{
    public const string FamilySearchExid = "https://gedcom.io/exid-type/FamilySearch-PersonId";

    public static void ExportFile(FamilyTree tree, string path) => File.WriteAllText(path, Export(tree), new UTF8Encoding(false));

    public static string Export(FamilyTree tree) => Convert(GedcomExporter.Export(tree));

    private static readonly HashSet<string> Roles = new() { "CHIL", "CLERGY", "FATH", "FRIEND", "GODP", "HUSB", "MOTH", "MULTIPLE", "NGHBR", "OFFICIATOR", "PARENT", "SPOU", "WIFE", "WITN" };
    private static readonly Dictionary<string, string> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Witness"] = "WITN", ["Godparent"] = "GODP", ["Friend"] = "FRIEND", ["Neighbour"] = "NGHBR", ["Neighbor"] = "NGHBR",
        ["Minister"] = "CLERGY", ["Clergy"] = "CLERGY", ["Officiant"] = "OFFICIATOR",
    };
    private static readonly HashSet<string> NameTypes = new() { "AKA", "BIRTH", "IMMIGRANT", "MAIDEN", "MARRIED", "PROFESSIONAL" };
    private static readonly HashSet<string> Pedigrees = new() { "BIRTH", "ADOPTED", "FOSTER", "SEALING" };
    private static readonly HashSet<string> ChildStatus = new() { "CHALLENGED", "DISPROVEN", "PROVEN" };
    private static readonly Regex DualYear = new(@"(\d{3,4})/(\d{2,4})\b");

    /// <summary>Turns GEDCOM 5.5.1 as Lineage writes it into GEDCOM 7.0.</summary>
    public static string Convert(string gedcom551)
    {
        var roots = GedcomNode.Parse(gedcom551);
        var media = new List<GedcomNode>();
        foreach (var r in roots)
        {
            if (r.Tag == "HEAD") Header(r);
            else Walk(r, media);
        }
        var trailer = roots.FindIndex(r => r.Tag == "TRLR");
        roots.InsertRange(trailer < 0 ? roots.Count : trailer, media);

        var sb = new StringBuilder();
        foreach (var r in roots) Write(sb, r, 0);
        return sb.ToString();
    }

    private static void Header(GedcomNode head)
    {
        head.Children.RemoveAll(c => c.Tag is "CHAR" or "FILE");
        if (head.First("GEDC") is { } gedc)
        {
            gedc.Children.Clear();
            gedc.Children.Add(new GedcomNode { Tag = "VERS", Value = "7.0" });
        }
    }

    private static void Walk(GedcomNode node, List<GedcomNode> media)
    {
        for (var i = 0; i < node.Children.Count; i++)
        {
            var c = node.Children[i];
            switch (c.Tag)
            {
                case "OBJE" when !c.Value.StartsWith('@'):
                    node.Children[i] = MediaRecord(c, media);
                    continue;
                case "ASSO":
                    if (c.First("RELA") is { } rela)
                    {
                        c.Children.Remove(rela);
                        var code = RoleWords.GetValueOrDefault(rela.Value.Trim(), Roles.Contains(rela.Value.Trim().ToUpperInvariant()) ? rela.Value.Trim().ToUpperInvariant() : "OTHER");
                        var role = new GedcomNode { Tag = "ROLE", Value = code };
                        if (code == "OTHER" || !RoleWords.ContainsKey(rela.Value.Trim())) role.Children.Add(new GedcomNode { Tag = "PHRASE", Value = rela.Value.Trim() });
                        c.Children.Insert(0, role);
                    }
                    break;
                case "TYPE" when node.Tag == "NAME":
                    var type = c.Value.Trim().ToUpperInvariant();
                    if (!NameTypes.Contains(type))
                    {
                        c.Children.Add(new GedcomNode { Tag = "PHRASE", Value = c.Value.Trim() });
                        type = "OTHER";
                    }
                    c.Value = type;
                    break;
                case "PEDI":
                    var word = c.Value.Trim().ToUpperInvariant();
                    if (word == "DISPROVED") word = "DISPROVEN";
                    if (ChildStatus.Contains(word)) { node.Children[i] = new GedcomNode { Tag = "STAT", Value = word }; continue; }
                    if (!Pedigrees.Contains(word)) { c.Children.Add(new GedcomNode { Tag = "PHRASE", Value = c.Value.Trim() }); word = "OTHER"; }
                    c.Value = word;
                    break;
                case "_FSFTID":
                    var exid = new GedcomNode { Tag = "EXID", Value = c.Value };
                    exid.Children.Add(new GedcomNode { Tag = "TYPE", Value = FamilySearchExid });
                    node.Children[i] = exid;
                    continue;
                case "RIN":
                    node.Children.RemoveAt(i--);
                    continue;
                case "DATE" when DualYear.IsMatch(c.Value):
                    // 7.0 has no "1741/42"; the date takes the new-style year and keeps the original as a phrase.
                    var original = c.Value;
                    c.Value = DualYear.Replace(c.Value, m => NewYear(m.Groups[1].Value, m.Groups[2].Value));
                    c.Children.Add(new GedcomNode { Tag = "PHRASE", Value = original });
                    break;
            }
            Walk(c, media);
        }
    }

    private static string NewYear(string year, string dual)
    {
        if (dual.Length >= year.Length) return dual;
        return year[..(year.Length - dual.Length)] + dual;
    }

    /// <summary>Moves an embedded photo into a multimedia record and returns the pointer that replaces it.</summary>
    private static GedcomNode MediaRecord(GedcomNode embedded, List<GedcomNode> media)
    {
        var xref = $"@O{media.Count + 1}@";
        var record = new GedcomNode { Tag = "OBJE", Xref = xref };
        var file = new GedcomNode { Tag = "FILE", Value = embedded.Text("FILE") };
        file.Children.Add(new GedcomNode { Tag = "FORM", Value = Mime(embedded.Text("FILE"), embedded.Text("FORM")) });
        if (embedded.Text("TITL").Length > 0) file.Children.Add(new GedcomNode { Tag = "TITL", Value = embedded.Text("TITL") });
        record.Children.Add(file);
        foreach (var note in embedded.All("NOTE")) record.Children.Add(note);
        media.Add(record);

        var pointer = new GedcomNode { Tag = "OBJE", Value = xref };
        pointer.Children.AddRange(embedded.Children.Where(c => c.Tag.StartsWith('_')));   // profile mark, tags, place, privacy
        return pointer;
    }

    private static string Mime(string file, string form) => (Path.GetExtension(file).TrimStart('.').Length > 0 ? Path.GetExtension(file).TrimStart('.') : form).ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg", "png" => "image/png", "gif" => "image/gif", "bmp" => "image/bmp", "tif" or "tiff" => "image/tiff",
        "webp" => "image/webp", "pdf" => "application/pdf", "mp3" => "audio/mpeg", "wav" => "audio/wav", "m4a" => "audio/mp4",
        "mp4" or "m4v" => "video/mp4", "mov" => "video/quicktime", "txt" => "text/plain", "htm" or "html" => "text/html",
        var other => other.Contains('/') ? other : "application/octet-stream",
    };

    private static void Write(StringBuilder sb, GedcomNode node, int level)
    {
        var lines = node.Value.Replace("\r\n", "\n").Split('\n');
        sb.Append(level).Append(' ');
        if (node.Xref != null) sb.Append(node.Xref).Append(' ');
        sb.Append(node.Tag);
        // A value starting with @ that isn't a pointer is written @@ (7.0 escapes only the first one).
        var first = lines[0];
        if (first.StartsWith('@') && !Regex.IsMatch(first, "^@[^@ ]+@$")) first = "@" + first;
        if (first.Length > 0) sb.Append(' ').Append(first);
        sb.Append('\n');
        foreach (var more in lines.Skip(1))
        {
            sb.Append(level + 1).Append(" CONT");
            if (more.Length > 0) sb.Append(' ').Append(more.StartsWith('@') ? "@" + more : more);
            sb.Append('\n');
        }
        foreach (var c in node.Children) Write(sb, c, level + 1);
    }
}
