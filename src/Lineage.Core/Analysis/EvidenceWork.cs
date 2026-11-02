using System.Text.RegularExpressions;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.Core.Analysis;

/// <summary>Where a fact stands in the evidence worklist.</summary>
public enum WorkKind { Conflict, Unsourced, Weak, ReadyToProve, Concluded }

/// <summary>One of the five parts of the Genealogical Proof Standard, met or not, with what's missing.</summary>
public sealed record GpsCheck(string Label, bool Met, string Detail);

/// <summary>A vital fact in the worklist: whose it is, what it says, how well it's proven and what's left to do.</summary>
public sealed record FactWork(Entity Owner, Event Event, string Who, string What, FactEvidence Evidence,
                              IReadOnlyList<GpsCheck> Gps, WorkKind Kind)
{
    public int GpsMet => Gps.Count(g => g.Met);
    public long? PersonId => Owner is Person p ? p.Id : null;
}

/// <summary>
/// One record's say on a fact: which source, what it says, the years it allows (a census age gives a two-year
/// window), how it's rated, and whether it disagrees with the fact being analysed.
/// </summary>
public sealed record Claim(string Source, long? SourceId, string Says, int From, int To, InformationKind Information,
                           EvidenceKind Evidence, SourceKind Kind, double Weight, bool Disagrees, string Basis);

/// <summary>
/// The evidence workspace: a worklist of the vital facts in the tree by what they need, every record that speaks to
/// one fact side by side, and the Genealogical Proof Standard's five parts checked for it.
/// </summary>
public static class EvidenceWork
{
    private static readonly string[] BirthTags = { "BIRT", "CHR", "BAPM" };
    private static readonly string[] DeathTags = { "DEAT", "BURI", "CREM" };
    private static readonly Regex Age = new(@"\bage\s*:?\s*(\d{1,3})\b", RegexOptions.IgnoreCase);

    /// <summary>Every birth, christening, death, burial and marriage in the tree, assessed.</summary>
    public static List<FactWork> Worklist(FamilyTree tree)
    {
        var list = new List<FactWork>();
        foreach (var p in tree.People.Values)
            foreach (var e in p.Events.Where(e => BirthTags.Contains(e.Tag) || DeathTags.Contains(e.Tag)))
                list.Add(Work(tree, e, p));
        foreach (var f in tree.Families.Values)
            foreach (var e in f.Events.Where(e => e.Tag == "MARR"))
                list.Add(Work(tree, e, f));
        return list.OrderBy(w => w.Kind).ThenBy(w => w.Who, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static FactWork Work(FamilyTree tree, Event e, Entity owner)
    {
        var evidence = Evidence.Assess(tree, e, owner);
        var kind = evidence.Level switch
        {
            ProofLevel.Conflicting => WorkKind.Conflict,
            ProofLevel.Unsourced => WorkKind.Unsourced,
            ProofLevel.Weak => WorkKind.Weak,
            ProofLevel.Proven or ProofLevel.Disproved => WorkKind.Concluded,
            _ => WorkKind.ReadyToProve,
        };
        var says = string.Join(", ", new[] { e.Date.IsEmpty ? "" : ReportText.Date(e.Date), e.Place.Trim() }.Where(x => x.Length > 0));
        return new FactWork(owner, e, Who(tree, owner), says.Length > 0 ? $"{EventTypes.Label(e)}: {says}" : EventTypes.Label(e),
                            evidence, Gps(tree, e, owner, evidence), kind);
    }

    /// <summary>The research question the fact answers ("When and where was Thomas born?").</summary>
    public static string Question(FamilyTree tree, Event e, Entity owner)
    {
        var name = owner is Person p ? FirstName(p) : Who(tree, owner);
        return e.Tag switch
        {
            "BIRT" => $"When and where was {name} born?",
            "CHR" or "BAPM" => $"When and where was {name} christened?",
            "DEAT" => $"When and where did {name} die?",
            "BURI" or "CREM" => $"When and where was {name} buried?",
            "MARR" => $"When and where did {name} marry?",
            _ => $"Is this {EventTypes.Label(e).ToLowerInvariant()} right?",
        };
    }

    /// <summary>
    /// Every record that speaks to the fact: each citation of it and of other records of the same kind (a second
    /// birth date, a christening), and — for a birth — the ages given in census records, as the years they allow.
    /// </summary>
    public static List<Claim> Claims(FamilyTree tree, Event e, Entity owner)
    {
        var claims = new List<Claim>();
        var events = owner switch { Person p => p.Events, Family f => f.Events, _ => new List<Event>() };
        var group = BirthTags.Contains(e.Tag) ? BirthTags : DeathTags.Contains(e.Tag) ? DeathTags : new[] { e.Tag };
        var (lo, hi) = Years(e.Date);
        foreach (var other in events.Where(x => group.Contains(x.Tag)))
        {
            var (from, to) = Years(other.Date);
            var says = string.Join(", ", new[] { EventTypes.Label(other), other.Date.IsEmpty ? "" : ReportText.Date(other.Date), other.Place.Trim() }
                .Where(x => x.Length > 0));
            var disagrees = !ReferenceEquals(other, e) && other.Conclusion != FactConclusion.Disproved
                && ((lo > 0 && from > 0 && (to < lo - 1 || from > hi + 1))
                    || (other.Tag == e.Tag && e.Place.Trim().Length > 0 && other.Place.Trim().Length > 0 && !Evidence.SamePlace(e.Place, other.Place)));
            if (other.Citations.Count == 0)
                claims.Add(new Claim("No source cited", null, says, from, to, InformationKind.Unknown, EvidenceKind.Unknown,
                                     SourceKind.Unknown, 0, disagrees, ReferenceEquals(other, e) ? "This fact" : "Another record"));
            foreach (var c in other.Citations)
                claims.Add(ClaimFrom(tree, c, says, from, to, disagrees, ReferenceEquals(other, e) ? "This fact" : "Another record"));
        }
        if (BirthTags.Contains(e.Tag) && owner is Person person)
        {
            // A census gives an age on census day: born that many years earlier, give or take a birthday.
            foreach (var cens in person.Events.Where(x => x.Tag == "CENS" && x.Date.Year > 0))
            {
                if (Age.Match(cens.Description + " " + cens.Value) is not { Success: true } m || !int.TryParse(m.Groups[1].Value, out var age)) continue;
                var year = cens.Date.NewStyleYear;
                var (from, to) = (year - age - 1, year - age);
                var says = $"Age {age} in {year}: born {from}–{to}";
                var disagrees = lo > 0 && (to < lo - 1 || from > hi + 1);
                if (cens.Citations.Count == 0)
                    claims.Add(new Claim("Census (no source cited)", null, says, from, to, InformationKind.Unknown, EvidenceKind.Indirect,
                                         SourceKind.Unknown, 0, disagrees, "Census age"));
                foreach (var c in cens.Citations) claims.Add(ClaimFrom(tree, c, says, from, to, disagrees, "Census age") with { Evidence = EvidenceKind.Indirect });
            }
        }
        return claims.OrderBy(c => c.Disagrees).ThenByDescending(c => c.Weight).ToList();
    }

    /// <summary>The Genealogical Proof Standard's five parts, as far as the tree shows them.</summary>
    public static List<GpsCheck> Gps(FamilyTree tree, Event e, Entity owner, FactEvidence? evidence = null)
    {
        evidence ??= Evidence.Assess(tree, e, owner);
        var searches = owner is Person p
            ? p.ResearchIds.Select(id => tree.Research.GetValueOrDefault(id)).OfType<ResearchItem>().Count(r => r.Result != SearchResult.None)
            : 0;
        var cited = e.Citations.Where(c => tree.Sources.ContainsKey(c.SourceId)).ToList();
        var complete = cited.Count > 0 && cited.Count == e.Citations.Count && cited.All(c => c.Page.Trim().Length > 0 || c.Text.Trim().Length > 0);
        var rated = cited.Count > 0 && cited.All(c => c.Information != InformationKind.Unknown && c.Evidence != EvidenceKind.Unknown
                                                      && tree.Sources[c.SourceId].Kind != SourceKind.Unknown);
        var written = e.Conclusion != FactConclusion.NotDecided && e.Reasoning.Trim().Length > 0;
        return new List<GpsCheck>
        {
            new("Reasonably exhaustive search", evidence.Sources >= 2 || searches > 0,
                evidence.Sources >= 2 ? $"{evidence.Sources} independent sources."
                : searches > 0 ? $"{searches} search{(searches == 1 ? "" : "es")} logged for them."
                : evidence.Sources == 0 ? "No source yet, and no searches logged. Look for records, and log where you looked."
                : "Only one source, and no searches logged. Look further, and log where you looked."),
            new("Complete citations", complete,
                complete ? "Every source says where in it (page, entry or folio)."
                : cited.Count == 0 ? "No source is cited." : "Add the page, entry or folio to each citation."),
            new("Analysis and correlation", rated,
                rated ? "Every source is rated: original or copy, first-hand or not, direct or indirect."
                : "Rate each source (original or copy, first-hand or not, direct or indirect)."),
            new("Conflicts resolved", evidence.OpenConflicts == 0,
                evidence.OpenConflicts == 0 ? "No records disagree, or the disagreements are settled."
                : $"{evidence.OpenConflicts} disagreement{(evidence.OpenConflicts == 1 ? "" : "s")} to settle."),
            new("Written conclusion", written,
                written ? "A conclusion with the reasoning behind it."
                : e.Conclusion == FactConclusion.NotDecided ? "Decide (proven or disproved) and write down why." : "Write down the reasoning."),
        };
    }

    private static Claim ClaimFrom(FamilyTree tree, Citation c, string says, int from, int to, bool disagrees, string basis)
    {
        var source = tree.Sources.GetValueOrDefault(c.SourceId);
        var title = source?.Title is { Length: > 0 } t ? t : "Unknown source";
        if (c.Page.Trim().Length > 0) title += $", {c.Page.Trim()}";
        return new Claim(title, source?.Id, says, from, to, c.Information, c.Evidence, source?.Kind ?? SourceKind.Unknown,
                         Evidence.Weight(tree, c), disagrees, basis);
    }

    /// <summary>The years a date allows: "abt 1842" two either side, "1600/01" both, a range its span; 0 when unknown.</summary>
    private static (int From, int To) Years(GenDate d)
    {
        if (d.Kind == DateKind.Text || d.Year <= 0) return (0, 0);
        var (from, to) = (d.Year, d.NewStyleYear);
        if (d.End is { Year: > 0 } end) to = Math.Max(to, end.Year);
        if (d.Qualifier is DateQualifier.About or DateQualifier.Estimated or DateQualifier.Calculated) { from -= 2; to += 2; }
        if (d.Qualifier == DateQualifier.Before) from -= 50;
        if (d.Qualifier == DateQualifier.After) to += 50;
        if (d.DualYear == 0 && GenDate.MaybeOldStyle(d)) to = Math.Max(to, d.Year + 1);
        return (from, to);
    }

    private static string Who(FamilyTree tree, Entity owner) => owner switch
    {
        Person p => p.DisplayName,
        Family f => string.Join(" & ", f.PartnerIds.Select(id => tree.Person(id)?.DisplayName).OfType<string>()),
        _ => "",
    };

    private static string FirstName(Person p) =>
        p.PrimaryName.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? p.DisplayName;
}
