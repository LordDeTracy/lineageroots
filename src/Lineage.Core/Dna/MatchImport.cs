using System.Globalization;
using System.Text;
using Lineage.Core.Model;

namespace Lineage.Core.Dna;

/// <summary>What an import did.</summary>
public sealed record MatchImportResult(string Kind, int Added, int Updated, int Links, IReadOnlyList<string> Problems)
{
    public string Summary => Kind == "segments"
        ? $"{Links:N0} segment{(Links == 1 ? "" : "s")} for {Updated + Added:N0} match{(Updated + Added == 1 ? "" : "es")}"
          + (Added > 0 ? $" ({Added:N0} new)" : "") + (Problems.Count > 0 ? $"; {Problems.Count:N0} rows skipped" : "")
        : Kind == "shared"
        ? $"{Links:N0} shared-match link{(Links == 1 ? "" : "s")} added" + (Problems.Count > 0 ? $"; {Problems.Count:N0} rows skipped" : "")
        : $"{Added:N0} match{(Added == 1 ? "" : "es")} added, {Updated:N0} updated" + (Problems.Count > 0 ? $"; {Problems.Count:N0} rows skipped" : "");
}

/// <summary>
/// Reads DNA match lists saved as CSV — from a testing company or a download tool — into a tester's matches.
/// Two kinds of file are understood, told apart by their columns:
/// <list type="bullet">
/// <item>a match list: a name column and a shared cM column, and optionally segments, longest segment and an ID;</item>
/// <item>a shared-matches ("in common with") list: two columns naming or identifying a match and one of their shared matches.</item>
/// </list>
/// Matches already present (same ID, or same name when there's no ID) are updated rather than added again.
/// </summary>
public static class MatchImport
{
    public static MatchImportResult Import(Person tester, string csv, string company = "", string matchName = "")
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return new MatchImportResult("matches", 0, 0, 0, new[] { "The file is empty." });
        var header = rows[0].Select(Key).ToArray();
        if (SegmentImport.Columns(header) is { } seg) return SegmentImport.Import(tester, rows, seg, company, matchName);
        // A shared-matches list may also give cM, so look for its pair of columns first.
        if (SharedColumns(header) is { } p) return ImportShared(tester, rows, p.A, p.B);
        var cm = Find(header, h => (h.Contains("cm") || h.Contains("centimorgan") || h == "shareddna")
                                   && !h.Contains("longest") && !h.Contains("largest"));
        if (cm >= 0) return ImportMatches(tester, rows, header, cm, company);
        return new MatchImportResult("matches", 0, 0, 0, new[]
        {
            "Couldn't find the columns: a match list needs a name and a shared cM column; a shared-matches list needs two columns naming a match and their shared match.",
        });
    }

    private static MatchImportResult ImportMatches(Person tester, List<string[]> rows, string[] header, int cmCol, string company)
    {
        var nameCol = Find(header, h => h is "name" or "matchname" or "displayname" or "fullname" or "username" or "match");
        if (nameCol < 0) nameCol = Find(header, h => h.Contains("name") && !h.Contains("tree") && !h.Contains("admin") && !h.Contains("test") && !h.Contains("kit"));
        var idCol = Find(header, h => h is "matchguid" or "matchid" or "guid" or "kit" or "kitnumber" or "id" or "uid" or "matchkit");
        var segCol = Find(header, h => h.Contains("segment") && !h.Contains("longest") && !h.Contains("largest"));
        var longCol = Find(header, h => h.Contains("longest") || h.Contains("largest"));
        var problems = new List<string>();
        if (nameCol < 0 && idCol < 0) return new MatchImportResult("matches", 0, 0, 0, new[] { "No name column found." });

        int added = 0, updated = 0;
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var name = Cell(row, nameCol);
            var reference = Cell(row, idCol);
            if (name.Length == 0) name = reference;
            if (name.Length == 0) continue;
            if (Number(Cell(row, cmCol)) is not { } cm) { problems.Add($"Row {r + 1}: no shared cM"); continue; }

            var match = Existing(tester, reference, name, company);
            if (match == null)
            {
                match = new DnaMatch { Name = name, Reference = reference, Company = company };
                tester.DnaMatches.Add(match);
                added++;
            }
            else
            {
                updated++;
                if (reference.Length > 0) match.Reference = reference;
                match.Name = name;
            }
            match.SharedCm = cm;
            if (Number(Cell(row, segCol)) is { } seg) match.Segments = (int)Math.Round(seg);
            if (Number(Cell(row, longCol)) is { } longest) match.LongestCm = longest;
        }
        return new MatchImportResult("matches", added, updated, 0, problems);
    }

    private static MatchImportResult ImportShared(Person tester, List<string[]> rows, int aCol, int bCol)
    {
        var problems = new List<string>();
        var links = 0;
        for (var r = 1; r < rows.Count; r++)
        {
            var a = Resolve(tester, Cell(rows[r], aCol));
            var b = Resolve(tester, Cell(rows[r], bCol));
            if (a == null || b == null || a == b) { problems.Add($"Row {r + 1}: match not in the list"); continue; }
            if (!a.SharedWith.Contains(b.Id)) { a.SharedWith.Add(b.Id); links++; }
            if (!b.SharedWith.Contains(a.Id)) b.SharedWith.Add(a.Id);
        }
        return new MatchImportResult("shared", 0, 0, links, problems);
    }

    /// <summary>The match this row updates: the same ID, or (when either has no ID) the same name at the same company.</summary>
    internal static DnaMatch? Existing(Person tester, string reference, string name, string company)
    {
        if (reference.Length > 0 && tester.DnaMatches.FirstOrDefault(m => m.Reference == reference) is { } byId) return byId;
        return tester.DnaMatches.FirstOrDefault(m =>
            (m.Reference.Length == 0 || reference.Length == 0)
            && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
            && (company.Length == 0 || m.Company.Length == 0 || string.Equals(m.Company, company, StringComparison.OrdinalIgnoreCase)));
    }

    private static DnaMatch? Resolve(Person tester, string key)
    {
        if (key.Length == 0) return null;
        return tester.DnaMatches.FirstOrDefault(m => m.Reference.Length > 0 && m.Reference == key)
            ?? tester.DnaMatches.FirstOrDefault(m => string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The two columns of a shared-matches list: the match, then their shared match ("icw", "shared", "in common").</summary>
    private static (int A, int B)? SharedColumns(string[] header)
    {
        static bool Identity(string h) => h.Contains("name") || h.EndsWith("id") || h.Contains("guid") || h.Contains("kit");
        static bool Second(string h) => h.Contains("icw") || h.Contains("sharedmatch") || h.Contains("common");
        var b = Find(header, h => Identity(h) && Second(h));
        if (b < 0) return null;
        // Prefer the same kind of column for both (IDs with IDs, names with names).
        var wantId = !header[b].Contains("name");
        var a = Find(header, h => Identity(h) && !Second(h) && !h.Contains("test") && (wantId ? !h.Contains("name") : h.Contains("name")));
        if (a < 0) a = Find(header, h => Identity(h) && !Second(h) && !h.Contains("test"));
        return a < 0 ? null : (a, b);
    }

    internal static int Find(string[] header, Func<string, bool> test)
    {
        for (var i = 0; i < header.Length; i++) if (test(header[i])) return i;
        return -1;
    }

    /// <summary>A header in a comparable form: lower case letters and digits only ("Shared cM" → "sharedcm", "# Segments" → "segments").</summary>
    internal static string Key(string header) =>
        new(header.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    internal static string Cell(string[] row, int col) => col >= 0 && col < row.Length ? row[col].Trim() : "";

    /// <summary>A number from a cell like "1,234.5 cM" (thousands separators and units ignored).</summary>
    internal static double? Number(string cell)
    {
        var digits = new string(cell.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Rows of a CSV file: commas (or tabs or semicolons, if that's what the header uses), quotes doubled inside quoted cells.</summary>
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var firstLine = text.Split('\n')[0];
        var sep = firstLine.Count(c => c == '\t') > firstLine.Count(c => c == ',') ? '\t'
                : firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(row.ToArray());
                row = new List<string>();
            }
            else cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(x => x.Length > 0)) rows.Add(row.ToArray());
        return rows;
    }
}
