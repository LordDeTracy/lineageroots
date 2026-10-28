using Lineage.Core.Model;

namespace Lineage.Core.Dna;

/// <summary>
/// Reads a segment download — FamilyTreeDNA's chromosome browser, MyHeritage's shared segments, GEDmatch's one-to-one
/// or a download tool's — into the tester's matches: a chromosome, start and end column, cM, and usually the match's
/// name. A file without names (GEDmatch) is one match's segments, named by the caller. Each match in the file has its
/// segments replaced, so downloading again doesn't double them; a match not yet in the list is added.
/// </summary>
public static class SegmentImport
{
    public sealed record SegmentColumns(int Chromosome, int Start, int End, int Cm, int Snps, int Name);

    /// <summary>The columns of a segment file, or null when it isn't one.</summary>
    internal static SegmentColumns? Columns(string[] header)
    {
        var chr = MatchImport.Find(header, h => h is "chr" or "chrom" or "chromosome" or "chromosomenumber");
        var start = MatchImport.Find(header, h => h.Contains("start") && !h.Contains("rsid") && !h.Contains("snp"));
        var end = MatchImport.Find(header, h => (h.StartsWith("end") || h.Contains("endpos") || h.Contains("endlocation") || h.Contains("stop"))
                                                && !h.Contains("rsid") && !h.Contains("snp"));
        var cm = MatchImport.Find(header, h => h.Contains("cm") || h.Contains("centimorgan"));
        if (chr < 0 || start < 0 || end < 0 || cm < 0) return null;
        var snps = MatchImport.Find(header, h => h.Contains("snp") && !h.Contains("start") && !h.Contains("end"));
        // MyHeritage puts the tester in "Name" and the match in "Match name".
        var name = MatchImport.Find(header, h => h is "matchname" or "match");
        if (name < 0) name = MatchImport.Find(header, h => h is "name" or "displayname" or "fullname");
        return new SegmentColumns(chr, start, end, cm, snps, name);
    }

    /// <summary>True when the file is a segment list that doesn't say whose segments they are.</summary>
    public static bool NeedsMatchName(string csv)
    {
        var rows = MatchImport.ParseCsv(csv);
        return rows.Count > 0 && Columns(rows[0].Select(MatchImport.Key).ToArray()) is { Name: < 0 };
    }

    internal static MatchImportResult Import(Person tester, List<string[]> rows, SegmentColumns c, string company, string matchName)
    {
        var problems = new List<string>();
        if (c.Name < 0 && matchName.Trim().Length == 0)
            return new MatchImportResult("segments", 0, 0, 0, new[] { "This segment file doesn't name the match; say whose segments they are." });

        var replaced = new HashSet<DnaMatch>();
        int added = 0, segments = 0;
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var name = c.Name >= 0 ? MatchImport.Cell(row, c.Name) : matchName.Trim();
            if (name.Length == 0) { problems.Add($"Row {r + 1}: no match name"); continue; }
            if (Chromosome(MatchImport.Cell(row, c.Chromosome)) is not { } chr
                || MatchImport.Number(MatchImport.Cell(row, c.Start)) is not { } start
                || MatchImport.Number(MatchImport.Cell(row, c.End)) is not { } end || end <= start
                || MatchImport.Number(MatchImport.Cell(row, c.Cm)) is not { } cm)
            {
                problems.Add($"Row {r + 1}: not a segment");
                continue;
            }
            var match = MatchImport.Existing(tester, "", name, company);
            if (match == null)
            {
                match = new DnaMatch { Name = name, Company = company };
                tester.DnaMatches.Add(match);
                added++;
            }
            if (replaced.Add(match)) match.ChromosomeSegments.Clear();
            match.ChromosomeSegments.Add(new DnaSegment
            {
                Chromosome = chr, Start = (long)start, End = (long)end, Cm = cm,
                Snps = MatchImport.Number(MatchImport.Cell(row, c.Snps)) is { } snps ? (int)snps : 0,
            });
            segments++;
        }
        // A match added from segments alone gets its totals from them.
        foreach (var m in replaced.Where(m => m.SharedCm == 0))
        {
            m.SharedCm = Math.Round(m.ChromosomeSegments.Sum(s => s.Cm), 1);
            m.Segments = m.ChromosomeSegments.Count;
            m.LongestCm = m.ChromosomeSegments.Max(s => s.Cm);
        }
        return new MatchImportResult("segments", added, replaced.Count - added, segments, problems);
    }

    /// <summary>A chromosome from "7", "chr7", "X" or "23".</summary>
    public static int? Chromosome(string cell)
    {
        var s = cell.Trim().ToUpperInvariant();
        if (s.StartsWith("CHR")) s = s[3..].Trim();
        if (s == "X") return ChromosomePainter.X;
        return int.TryParse(s, out var n) && n is >= 1 and <= ChromosomePainter.X ? n : null;
    }
}
