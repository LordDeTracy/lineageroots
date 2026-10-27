using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>How well a fact is proven, weakest first (so the lowest of several is the one to worry about).</summary>
public enum ProofLevel { Conflicting, Unsourced, Weak, Good, Strong, Proven, Disproved }

/// <summary>
/// The assessment of one fact: its level, a 0–1 score from its citations, how many independent sources and
/// open conflicts it has, a one-line summary, and plain-English reasons and tips.
/// </summary>
public sealed record FactEvidence(ProofLevel Level, double Score, int Sources, int OpenConflicts, string Summary,
                                  IReadOnlyList<string> Reasons, IReadOnlyList<string> Tips)
{
    public string Label => Evidence.Label(Level);
}

/// <summary>Another record of the same kind of fact for the same person that disagrees (two births a few years apart).</summary>
public sealed record AutoConflict(Event Other, string Message);

/// <summary>
/// Evidence analysis in the spirit of the Genealogical Proof Standard. Each citation is weighed by the kind of
/// source (original, derivative or authored), the information (primary or secondary) and the evidence (direct,
/// indirect or negative); the GEDCOM quality rating stands in when those aren't set. Independent sources
/// corroborate one another, so a fact's score is the chance that at least one of its sources is right, treating
/// each source's weight as its reliability. Open conflicts — written down, or two records of the same fact that
/// disagree — mark it as conflicting until resolved. The researcher's own conclusion (proven or disproved) wins.
/// </summary>
public static class Evidence
{
    public const double StrongScore = 0.85, GoodScore = 0.55;
    /// <summary>A source cited for the whole person or family counts this much towards each of their vital facts.</summary>
    public const double GeneralWeight = 0.5;

    private static bool Vital(string tag) => tag is "BIRT" or "CHR" or "BAPM" or "DEAT" or "BURI" or "CREM" or "MARR";

    public static string Label(ProofLevel level) => level switch
    {
        ProofLevel.Proven => "Proven",
        ProofLevel.Strong => "Strong",
        ProofLevel.Good => "Good",
        ProofLevel.Weak => "Weak",
        ProofLevel.Unsourced => "No source",
        ProofLevel.Conflicting => "Conflicting",
        _ => "Disproved",
    };

    /// <summary>How much one citation can be trusted for its fact, 0–1. <paramref name="kindOf"/> overrides the sources' kinds (for previews).</summary>
    public static double Weight(FamilyTree tree, Citation c, Func<long, SourceKind>? kindOf = null)
    {
        var kind = KindOf(tree, c.SourceId, kindOf);
        if (kind == SourceKind.Unknown && c.Information == InformationKind.Unknown && c.Evidence == EvidenceKind.Unknown && c.Quality is { } q)
            return q switch { >= 3 => 0.75, 2 => 0.5, 1 => 0.25, _ => 0.05 };
        var source = kind switch
        {
            SourceKind.Original => 0.8,
            SourceKind.Derivative => 0.55,
            SourceKind.Authored => 0.35,
            _ => 0.45,
        };
        var information = c.Information switch { InformationKind.Primary => 1.0, InformationKind.Secondary => 0.7, _ => 0.85 };
        var evidence = c.Evidence switch { EvidenceKind.Direct => 1.0, EvidenceKind.Indirect => 0.7, EvidenceKind.Negative => 0.5, _ => 0.9 };
        return source * information * evidence;
    }

    /// <summary>
    /// The GEDCOM quality rating (QUAY 0–3) that matches a citation's evidence analysis, for other programs;
    /// null when nothing has been rated.
    /// </summary>
    public static int? QualityFor(FamilyTree tree, Citation c)
    {
        var kind = tree.Sources.TryGetValue(c.SourceId, out var s) ? s.Kind : SourceKind.Unknown;
        if (kind == SourceKind.Unknown && c.Information == InformationKind.Unknown && c.Evidence == EvidenceKind.Unknown) return c.Quality;
        var w = Weight(tree, c);
        return w >= 0.7 ? 3 : w >= 0.45 ? 2 : w >= 0.2 ? 1 : 0;
    }

    private static SourceKind KindOf(FamilyTree tree, long sourceId, Func<long, SourceKind>? kindOf) =>
        kindOf?.Invoke(sourceId) ?? (tree.Sources.TryGetValue(sourceId, out var s) ? s.Kind : SourceKind.Unknown);

    /// <summary>
    /// Assesses a fact. <paramref name="owner"/> (the person or family it belongs to) lets it find disagreeing records
    /// of the same fact. For a preview of an edit, <paramref name="replacing"/> is the event being edited (so it isn't
    /// compared with itself) and <paramref name="kindOf"/> gives the sources' kinds as edited.
    /// </summary>
    public static FactEvidence Assess(FamilyTree tree, Event e, Entity? owner = null, Func<long, SourceKind>? kindOf = null, Event? replacing = null)
    {
        var reasons = new List<string>();
        var tips = new List<string>();

        // Corroboration: the best citation of each source, combined. Sources cited for the whole person or family
        // record (not this fact) support it too, at half weight.
        var bySource = e.Citations.GroupBy(c => c.SourceId).Select(g => g.Max(c => Weight(tree, c, kindOf))).ToList();
        var general = (owner switch { Person op => op.Citations, Family of => of.Citations, _ => new List<Citation>() })
            .Where(c => e.Citations.All(x => x.SourceId != c.SourceId))
            .GroupBy(c => c.SourceId).Select(g => g.Max(c => Weight(tree, c, kindOf)) * GeneralWeight).ToList();
        if (Vital(e.Tag)) bySource.AddRange(general);
        else general.Clear();
        var score = 1 - bySource.Aggregate(1.0, (miss, w) => miss * (1 - w));
        var autos = owner != null ? AutoConflicts(e, owner, replacing) : new List<AutoConflict>();
        var open = e.Conflicts.Count(c => !c.Resolved) + autos.Count;

        if (bySource.Count == 0) reasons.Add("No source is cited.");
        else reasons.Add(bySource.Count == 1 ? "One source." : $"{bySource.Count} independent sources.");
        if (general.Count > 0)
        {
            reasons.Add(general.Count == 1 ? "One of them is cited for the whole record rather than this fact (counted at half weight)."
                                           : $"{general.Count} of them are cited for the whole record rather than this fact (counted at half weight).");
            tips.Add("Attach the sources to the facts they prove.");
        }
        if (e.Citations.Count > 0)
        {
            var best = e.Citations.MaxBy(c => Weight(tree, c, kindOf))!;
            reasons.Add("Best: " + Describe(KindOf(tree, best.SourceId, kindOf), best) + ".");
            var kinds = e.Citations.Select(c => KindOf(tree, c.SourceId, kindOf)).ToList();
            if (kinds.All(k => k is SourceKind.Derivative or SourceKind.Authored))
                tips.Add("Every source is a copy, index or compiled work — look at the original record.");
            if (e.Citations.All(c => c.Information == InformationKind.Secondary))
                tips.Add("All the information is second-hand — look for a record made by someone who was there.");
            if (e.Citations.All(c => c.Evidence is EvidenceKind.Indirect or EvidenceKind.Negative))
                tips.Add("Nothing states this outright — write down how the indirect evidence adds up.");
            if (bySource.Count == 1) tips.Add("Look for a second, independent record that agrees.");
            if (e.Citations.All(c => c.Information == InformationKind.Unknown && c.Evidence == EvidenceKind.Unknown && c.Quality == null)
                && kinds.All(k => k == SourceKind.Unknown))
                tips.Add("Rate the sources (original or copy, first-hand or not) for a truer picture.");
        }
        else tips.Add("Cite where this fact came from.");
        foreach (var c in e.Conflicts.Where(c => !c.Resolved)) reasons.Add($"Unresolved conflict: {c.Claim}");
        foreach (var a in autos) reasons.Add(a.Message);
        if (open > 0) tips.Add("Settle the conflict: weigh the records against each other and record your reasoning.");

        var level = e.Conclusion == FactConclusion.Disproved ? ProofLevel.Disproved
            : open > 0 ? ProofLevel.Conflicting
            : e.Conclusion == FactConclusion.Proven ? ProofLevel.Proven
            : bySource.Count == 0 ? ProofLevel.Unsourced
            : score >= StrongScore ? ProofLevel.Strong
            : score >= GoodScore ? ProofLevel.Good
            : ProofLevel.Weak;
        if (level is ProofLevel.Proven or ProofLevel.Disproved)
        {
            tips.Clear();   // the researcher has decided; the only thing left is to say why
            if (e.Reasoning.Trim().Length == 0) tips.Add(level == ProofLevel.Proven ? "Write down the reasoning that proves it." : "Write down why it's wrong.");
        }
        var summary = level switch
        {
            ProofLevel.Disproved => "Marked as disproved",
            ProofLevel.Conflicting => open == 1 ? "1 unresolved conflict" : $"{open} unresolved conflicts",
            ProofLevel.Proven => "Marked as proven" + (bySource.Count > 0 ? $" ({Plural(bySource.Count, "source")})" : ""),
            ProofLevel.Unsourced => "No source cited",
            _ => $"{Label(level)} evidence: {Plural(bySource.Count, "source")}",
        };
        return new FactEvidence(level, score, bySource.Count, open, summary, reasons, tips);
    }

    /// <summary>
    /// Other records of the same vital fact for the same owner that disagree with this one: years more than one
    /// apart, or different places. A record the researcher has marked disproved — or settled by marking one proven —
    /// doesn't count.
    /// </summary>
    public static List<AutoConflict> AutoConflicts(Event e, Entity owner, Event? replacing = null)
    {
        var result = new List<AutoConflict>();
        if (e.Conclusion != FactConclusion.NotDecided || e.Tag is not ("BIRT" or "CHR" or "BAPM" or "DEAT" or "BURI" or "CREM" or "MARR")) return result;
        var events = owner switch { Person p => p.Events, Family f => f.Events, _ => new List<Event>() };
        foreach (var other in events)
        {
            if (ReferenceEquals(other, e) || ReferenceEquals(other, replacing) || other.Tag != e.Tag || other.Conclusion != FactConclusion.NotDecided) continue;
            var years = e.Date.Year > 0 && other.Date.Year > 0 && e.Date.Kind != DateKind.Text && other.Date.Kind != DateKind.Text
                        && Math.Abs(e.Date.Year - other.Date.Year) > 1;
            var places = e.Place.Trim().Length > 0 && other.Place.Trim().Length > 0
                         && !SamePlace(e.Place, other.Place);
            if (!years && !places) continue;
            var what = EventTypes.Label(other).ToLowerInvariant();
            var says = string.Join(", ", new[] { other.Date.Year > 0 ? Reports.ReportText.Date(other.Date) : "", other.Place }.Where(x => x.Length > 0));
            result.Add(new AutoConflict(other, $"Another {what} record says {says}."));
        }
        return result;
    }

    /// <summary>Places agree when one is a fuller form of the other ("Leeds" and "Leeds, Yorkshire, England").</summary>
    private static bool SamePlace(string a, string b)
    {
        var x = PlaceIndex.Normalize(a);
        var y = PlaceIndex.Normalize(b);
        var n = Math.Min(x.Count, y.Count);
        return n > 0 && x.Take(n).SequenceEqual(y.Take(n));
    }

    /// <summary>How well a person's life is pinned down: the weaker of their birth (or baptism) and death (or burial) facts.</summary>
    public static ProofLevel? PersonLevel(FamilyTree tree, Person p)
    {
        ProofLevel? weakest = null;
        foreach (var tags in new[] { new[] { "BIRT", "CHR", "BAPM" }, new[] { "DEAT", "BURI", "CREM" } })
        {
            var e = tags.Select(t => p.Events.FirstOrDefault(x => x.Tag == t && x.Conclusion != FactConclusion.Disproved)).OfType<Event>().FirstOrDefault();
            if (e == null) continue;
            var level = Assess(tree, e, p).Level;
            if (weakest == null || level < weakest) weakest = level;
        }
        return weakest;
    }

    /// <summary>How many of a person's facts (and their marriages) are at each level.</summary>
    public static Dictionary<ProofLevel, int> Tally(FamilyTree tree, Person p)
    {
        var tally = new Dictionary<ProofLevel, int>();
        void Add(Event e, Entity owner)
        {
            var level = Assess(tree, e, owner).Level;
            tally[level] = tally.GetValueOrDefault(level) + 1;
        }
        foreach (var e in p.Events) Add(e, p);
        foreach (var fid in p.SpouseFamilyIds)
            if (tree.Family(fid) is { } f)
                foreach (var e in f.Events) Add(e, f);
        return tally;
    }

    private static string Describe(SourceKind kind, Citation c)
    {
        if (kind == SourceKind.Unknown && c.Information == InformationKind.Unknown && c.Evidence == EvidenceKind.Unknown)
            return c.Quality switch
            {
                3 => "rated direct evidence",
                2 => "rated secondary evidence",
                1 => "rated questionable",
                0 => "rated unreliable",
                _ => "not rated yet",
            };
        var parts = new List<string>();
        parts.Add(kind switch
        {
            SourceKind.Original => "an original record",
            SourceKind.Derivative => "a copy or index",
            SourceKind.Authored => "a compiled work",
            _ => "a source",
        });
        if (c.Information != InformationKind.Unknown) parts.Add(c.Information == InformationKind.Primary ? "with first-hand information" : "with second-hand information");
        if (c.Evidence != EvidenceKind.Unknown)
            parts.Add(c.Evidence switch { EvidenceKind.Direct => "stating it directly", EvidenceKind.Indirect => "implying it", _ => "by what it leaves out" });
        return string.Join(" ", parts);
    }

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}
