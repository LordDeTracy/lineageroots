using System.Text;
using Lineage.Core.Model;

namespace Lineage.Core.Export;

/// <summary>
/// Writes a tree as GEDCOM 5.5.1 (UTF-8). Standard tags where they exist; Lineage/AQ custom tags
/// (_UID, _TAG, _COLOR, _DNA, _FSFTID, _TODO, _PLAC2, PEDI _HUSB/_WIFE, OBJE _REGION) for the rest.
/// Media links (OBJE) are written under people, families, events and source records.
/// Long values are split with CONC without ever cutting next to a space.
/// </summary>
public sealed class GedcomExporter
{
    private const int LineMax = 248;
    private readonly StringBuilder _sb = new();
    private readonly FamilyTree _tree;

    private GedcomExporter(FamilyTree tree) => _tree = tree;

    public static string Export(FamilyTree tree) => new GedcomExporter(tree).Run();

    public static void ExportFile(FamilyTree tree, string path) =>
        File.WriteAllText(path, Export(tree), new UTF8Encoding(false));

    private string Run()
    {
        L(0, "HEAD");
        L(1, "SOUR", "LINEAGE_ROOTS");
        L(2, "NAME", "Lineage Roots");
        L(2, "VERS", typeof(GedcomExporter).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
        L(1, "DATE", DateTime.Now.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant());
        L(1, "GEDC");
        L(2, "VERS", "5.5.1");
        L(2, "FORM", "LINEAGE-LINKED");
        L(1, "CHAR", "UTF-8");
        O(1, "FILE", _tree.Name);
        // A copy made for a relative remembers where it came from, so their changes can be merged back.
        if (_tree.SharedFrom is { } origin) { L(1, "_SHARE", origin.Id.ToString("D")); O(2, "_FROM", origin.From); }

        foreach (var p in _tree.People.Values.OrderBy(p => p.Id)) WritePerson(p);
        foreach (var f in _tree.Families.Values.OrderBy(f => f.Id)) WriteFamily(f);
        foreach (var s in _tree.Sources.Values.OrderBy(s => s.Id)) WriteSource(s);
        foreach (var r in _tree.Repositories.Values.OrderBy(r => r.Id))
        {
            L(0, $"@R{r.Id}@", "REPO");
            L(1, "NAME", r.Name.Length > 0 ? r.Name : "(unnamed repository)");
            O(1, "ADDR", r.Address);
            O(1, "PHON", r.Phone);
            O(1, "NOTE", r.Note);
        }
        foreach (var t in _tree.Research.Values.OrderBy(t => t.Id))
        {
            L(0, $"@TODO{t.Id}@", "_TODO");
            O(1, "DESC", t.Description);
            L(1, "_PRIORITY", t.Priority.ToString());
            L(1, "STAT", t.Done ? "1" : "0");
            O(1, "DATE", t.Date.ToGedcom());
            O(1, "_CAT", t.Category);
            if (t.RepositoryId is { } rid) L(1, "REPO", $"@R{rid}@");
            if (t.SourceId is { } sid) L(1, "SOUR", $"@S{sid}@");
            if (t.Result != SearchResult.None) L(1, "_RESULT", t.Result.ToString().ToUpperInvariant());
            O(1, "_HINT", t.HintKey);
            O(1, "NOTE", t.Note);
        }
        L(0, "TRLR");
        return _sb.ToString();
    }

    private void WritePerson(Person p)
    {
        L(0, $"@I{p.Id}@", "INDI");
        foreach (var n in p.Names)
        {
            var full = $"{n.Given} /{n.Surname}/ {n.Suffix}".Trim();
            L(1, "NAME", n.Type is "aka" or "married" or "phonetic" or "romanized" ? n.Given : full);
            if (n.Type.Length > 0) L(2, "TYPE", n.Type);
            O(2, "GIVN", n.Type.Length == 0 ? n.Given : "");
            O(2, "SURN", n.Surname);
            O(2, "NPFX", n.Prefix);
            O(2, "NSFX", n.Suffix);
            O(2, "NICK", n.Nickname);
        }
        if (p.Sex != Sex.Unknown) L(1, "SEX", p.Sex == Sex.Male ? "M" : "F");
        O(1, "TITL", p.Title);
        O(1, "DSCR", p.Description);
        foreach (var e in p.Events) WriteEvent(1, e);
        foreach (var c in p.Citations) WriteCitation(1, c);
        foreach (var fid in p.ParentFamilyIds)
        {
            var f = _tree.Family(fid);
            var link = f?.Children.FirstOrDefault(c => c.ChildId == p.Id);
            if (link?.Sealing is { } seal) { WriteEvent(1, seal); L(2, "FAMC", $"@F{fid}@"); }
        }
        for (var i = 0; i < p.ParentFamilyIds.Count; i++)
        {
            var fid = p.ParentFamilyIds[i];
            L(1, "FAMC", $"@F{fid}@");
            var link = _tree.Family(fid)?.Children.FirstOrDefault(c => c.ChildId == p.Id);
            if (link != null) WritePedigree(link);
            if (i == 0 && p.ParentFamilyIds.Count > 1) L(2, "_PRIMARY", "Y");
        }
        foreach (var fid in p.SpouseFamilyIds) L(1, "FAMS", $"@F{fid}@");
        if (p.Address.Length > 0) L(1, "ADDR", p.Address);
        O(1, "PHON", p.Phone);
        O(1, "_EMAIL", p.Email);
        O(1, "_URL", p.Url);
        O(1, "REFN", p.UserId);
        O(1, "_FSFTID", p.FamilySearchId);
        O(1, "_COLOR", p.Colour);
        foreach (var t in p.Tags) L(1, "_TAG", t);
        foreach (var d in p.DnaTests) WriteDna(d);
        foreach (var m in p.DnaMatches) WriteMatch(m);
        foreach (var rid in p.ResearchIds) L(1, "_TODO", $"@TODO{rid}@");
        foreach (var m in p.Media) WriteMedia(1, m);
        O(1, "NOTE", p.Note);
        L(1, "_UID", p.Uid.ToString("N").ToUpperInvariant());
        L(1, "RIN", p.Id.ToString());
    }

    private void WritePedigree(ChildLink c)
    {
        if (c.Status != RelationStatus.Verified) { L(2, "PEDI", c.Status.ToString().ToLowerInvariant()); return; }
        string Name(ParentRelation r) => r.ToString().ToLowerInvariant();
        if (c.ToFather == c.ToMother)
        {
            if (c.ToFather != ParentRelation.Birth) L(2, "PEDI", Name(c.ToFather));
            return;
        }
        L(2, "PEDI", Name(c.ToMother));
        L(3, "_HUSB", Name(c.ToFather));
        L(3, "_WIFE", Name(c.ToMother));
    }

    private void WriteFamily(Family f)
    {
        L(0, $"@F{f.Id}@", "FAM");
        // Order partners by sex where the data allows; GEDCOM 5.5.1 only has HUSB/WIFE slots.
        var a = _tree.Person(f.Partner1Id);
        var b = _tree.Person(f.Partner2Id);
        if (a?.Sex == Sex.Female || b?.Sex == Sex.Male) (a, b) = (b, a);
        if (a != null) L(1, "HUSB", $"@I{a.Id}@");
        if (b != null) L(1, "WIFE", $"@I{b.Id}@");
        foreach (var c in f.Children) L(1, "CHIL", $"@I{c.ChildId}@");
        foreach (var e in f.Events) WriteEvent(1, e);
        if (f.Status == CoupleStatus.Divorced) L(1, "DIV", "Y");
        if (f.NoChildren) L(1, "_NONE");
        if (f.Plural) L(1, "_PLURAL", "Y");
        foreach (var c in f.Citations) WriteCitation(1, c);
        foreach (var rid in f.ResearchIds) L(1, "_TODO", $"@TODO{rid}@");
        foreach (var m in f.Media) WriteMedia(1, m);
        O(1, "NOTE", f.Note);
        L(1, "_UID", f.Uid.ToString("N").ToUpperInvariant());
        L(1, "RIN", f.Id.ToString());
    }

    private void WriteEvent(int level, Event e)
    {
        L(level, e.Tag, e.Value);
        O(level + 1, "TYPE", e.TypeName);
        if (e.Status.Length > 0) L(level + 1, "STAT", e.Status);
        O(level + 1, "DATE", e.Date.ToGedcom());
        O(level + 1, "PLAC", e.Place);
        if (e.Location is { } at)
        {
            if (e.Place.Length == 0) L(level + 1, "PLAC");
            WriteMap(level + 2, "MAP", at);
        }
        O(level + 1, "_PLAC2", e.Place2);
        O(level + 1, "TEMP", e.Temple);
        O(level + 1, "CAUS", e.Cause);
        O(level + 1, "_Description2", e.Description);
        if (e.Private) L(level + 1, "_CONF");
        foreach (var c in e.Citations) WriteCitation(level + 1, c);
        foreach (var m in e.Media) WriteMedia(level + 1, m);
        O(level + 1, "NOTE", e.Note);
        WriteProof(level + 1, e);
        L(level + 1, "_UID", e.Id.ToString("N").ToUpperInvariant());   // so a relative's copy can be merged back fact by fact
    }

    /// <summary>
    /// Evidence analysis on an event: n _PROOF PROVEN|DISPROVED, n _PROOFNOTE reasoning, and one
    /// n _CONFLICT claim per conflicting record (n+1 SOUR @S1@ / n+2 PAGE, n+1 _RESOLUTION text, n+1 _RESOLVED Y).
    /// </summary>
    private void WriteProof(int level, Event e)
    {
        if (e.Conclusion != FactConclusion.NotDecided) L(level, "_PROOF", e.Conclusion == FactConclusion.Proven ? "PROVEN" : "DISPROVED");
        O(level, "_PROOFNOTE", e.Reasoning);
        foreach (var c in e.Conflicts)
        {
            L(level, "_CONFLICT", c.Claim);
            if (c.SourceId is { } sid && _tree.Sources.ContainsKey(sid)) { L(level + 1, "SOUR", $"@S{sid}@"); O(level + 2, "PAGE", c.Page); }
            O(level + 1, "_RESOLUTION", c.Resolution);
            if (c.Resolved) L(level + 1, "_RESOLVED", "Y");
        }
    }

    private void WriteCitation(int level, Citation c)
    {
        L(level, "SOUR", _tree.Sources.ContainsKey(c.SourceId) ? $"@S{c.SourceId}@" : "(unknown source)");
        O(level + 1, "PAGE", c.Page);
        if (c.Text.Length > 0) { L(level + 1, "DATA"); L(level + 2, "TEXT", c.Text); }
        // Evidence analysis as custom tags, and as the standard quality rating other programs understand.
        if (Analysis.Evidence.QualityFor(_tree, c) is { } q) L(level + 1, "QUAY", q.ToString());
        if (c.Information != InformationKind.Unknown) L(level + 1, "_INFO", c.Information == InformationKind.Primary ? "PRIMARY" : "SECONDARY");
        if (c.Evidence != EvidenceKind.Unknown) L(level + 1, "_EVID", c.Evidence.ToString().ToUpperInvariant());
        O(level + 1, "NOTE", c.Note);
    }

    private void WriteSource(Source s)
    {
        L(0, $"@S{s.Id}@", "SOUR");
        L(1, "_UID", s.Uid.ToString("N").ToUpperInvariant());
        O(1, "TITL", s.Title);
        O(1, "ABBR", s.ShortTitle);
        O(1, "AUTH", s.Author);
        O(1, "PUBL", s.Publication);
        O(1, "TEXT", s.Text);
        if (s.RepositoryId is { } rid) { L(1, "REPO", $"@R{rid}@"); O(2, "CALN", s.CallNumber); }
        else O(1, "CALN", s.CallNumber);
        O(1, "_TYPE", s.Type);
        if (s.Quality is { } q) L(1, "QUAY", q.ToString());
        if (s.Kind != SourceKind.Unknown) L(1, "_SRCKIND", s.Kind.ToString().ToUpperInvariant());
        foreach (var m in s.Media) WriteMedia(1, m);
        O(1, "NOTE", s.Note);
    }

    /// <summary>
    /// One OBJE link. People tagged in a photo use Lineage custom tags, one _REGION per person:
    /// <code>
    /// n   OBJE
    /// n+1 _REGION
    /// n+2 _PERSON @I12@
    /// n+2 _RECT 0.1250 0.2000 0.1500 0.2250
    /// </code>
    /// _RECT is x, y, width and height as fractions (0..1) of the picture, origin top-left,
    /// invariant culture with 4 decimals. Tags of people no longer in the tree are left out.
    /// </summary>
    private void WriteMedia(int level, MediaLink m)
    {
        L(level, "OBJE");
        O(level + 1, "FORM", m.Format);
        O(level + 1, "FILE", m.File);
        O(level + 1, "TITL", m.Title);
        O(level + 1, "_TYPE", m.MediaType);
        if (m.Primary) L(level + 1, "_PRIM", "Y");
        O(level + 1, "_DATE", m.Taken);
        if (m.Location is { } at) WriteMap(level + 1, "_MAP", at);
        foreach (var r in m.Regions.Where(r => _tree.People.ContainsKey(r.PersonId)))
        {
            L(level + 1, "_REGION");
            L(level + 2, "_PERSON", $"@I{r.PersonId}@");
            L(level + 2, "_RECT", string.Join(' ', new[] { r.X, r.Y, r.Width, r.Height }
                .Select(v => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))));
        }
        O(level + 1, "NOTE", m.Note);
    }

    private void WriteMap(int level, string tag, GeoPoint at)
    {
        L(level, tag);
        L(level + 1, "LATI", at.GedcomLatitude);
        L(level + 1, "LONG", at.GedcomLongitude);
    }

    private void WriteDna(DnaTest d)
    {
        L(1, "_DNA");
        O(2, "TYPE", d.Kind);
        O(2, "_Company", d.Company);
        O(2, "REFN", d.Reference);
        if (d.Date is { } date) L(2, "DATE", date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
        if (d.Haplogroup.Length > 0) { L(2, "_Haplogroup", d.Haplogroup); if (d.HaplogroupEstimated) L(3, "_Est", "Y"); }
        foreach (var (name, value) in d.Markers) { L(2, "_Marker", name); L(3, "_Value", value.ToString()); }
        O(2, "NOTE", d.Note);
    }

    /// <summary>
    /// A DNA match: 1 _MATCH name, 2 _UID, 2 _CM, 2 _SEGMENTS, 2 _LONGEST, 2 _Company, 2 REFN, 2 _PERSON @I12@,
    /// 2 _SHARED uid (one per shared match), 2 NOTE.
    /// </summary>
    private void WriteMatch(DnaMatch m)
    {
        L(1, "_MATCH", m.Name);
        L(2, "_UID", m.Id.ToString("D"));
        L(2, "_CM", Num(m.SharedCm));
        if (m.Segments > 0) L(2, "_SEGMENTS", m.Segments.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (m.LongestCm > 0) L(2, "_LONGEST", Num(m.LongestCm));
        O(2, "_Company", m.Company);
        O(2, "REFN", m.Reference);
        if (m.PersonId is { } pid && _tree.Person(pid) != null) L(2, "_PERSON", $"@I{pid}@");
        foreach (var s in m.SharedWith) L(2, "_SHARED", s.ToString("D"));
        foreach (var g in m.ChromosomeSegments)
            L(2, "_SEG", string.Join(' ', g.Chromosome, g.Start, g.End, Num(g.Cm), g.Snps));
        O(2, "NOTE", m.Note);
    }

    private static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private void O(int level, string tag, string? value)
    {
        if (!string.IsNullOrEmpty(value)) L(level, tag, value);
    }

    private void L(int level, string tag, string? value = null)
    {
        var lines = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var chunks = Split(lines[i]);
            for (var j = 0; j < chunks.Count; j++)
            {
                var (lv, tg) = i == 0 && j == 0 ? (level, tag) : (level + 1, j == 0 ? "CONT" : "CONC");
                _sb.Append(lv).Append(' ').Append(tg);
                if (chunks[j].Length > 0) _sb.Append(' ').Append(chunks[j]);
                _sb.Append('\n');
            }
        }
    }

    private static List<string> Split(string text)
    {
        var chunks = new List<string>();
        while (text.Length > LineMax)
        {
            var cut = LineMax;
            while (cut > LineMax / 2 && (text[cut - 1] == ' ' || text[cut] == ' ')) cut--;
            chunks.Add(text[..cut]);
            text = text[cut..];
        }
        chunks.Add(text);
        return chunks;
    }
}
