using Lineage.Core.Analysis;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.Core.Reports;

/// <summary>Who the report is for and why: the client, the researcher and the research question.</summary>
public sealed record ClientReportOptions(string Client = "", string Researcher = "", string Objective = "");

/// <summary>
/// A research report for a client, laid out the way professional genealogists hand in their work: the objective,
/// a summary of findings, the conclusions with their reasoning, the evidence for each fact weighed record by record
/// against the Genealogical Proof Standard, the searches made (including those that found nothing), what to do next,
/// and the sources consulted. Everything comes from the tree, so the report is as good as the research recorded.
/// </summary>
public static class ClientReport
{
    public static ReportDocument Build(FamilyTree tree, Person subject, ClientReportOptions options)
    {
        var name = subject.DisplayName;
        var doc = new ReportDocument
        {
            Title = $"Research report: {name}",
            Subtitle = string.Join(" · ", new[]
            {
                options.Client.Trim().Length > 0 ? $"Prepared for {options.Client.Trim()}" : "",
                options.Researcher.Trim().Length > 0 ? $"by {options.Researcher.Trim()}" : "",
                DateTime.Today.ToString("d MMMM yyyy"),
            }.Where(x => x.Length > 0)),
        };

        // The facts the report is about: the subject's vital events and their marriages.
        var facts = subject.Events.Where(e => e.Tag is "BIRT" or "CHR" or "BAPM" or "DEAT" or "BURI" or "CREM")
            .Select(e => (Event: e, Owner: (Entity)subject))
            .Concat(subject.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>()
                .SelectMany(f => f.Events.Where(e => e.Tag == "MARR").Select(e => (Event: e, Owner: (Entity)f))))
            .Select(x => EvidenceWork.Work(tree, x.Event, x.Owner))
            .ToList();

        doc.Add(new Heading(1, "Research objective", "objective"));
        doc.Add(new Paragraph(options.Objective.Trim().Length > 0
            ? options.Objective.Trim()
            : $"To identify and document the key events in the life of {name}: birth, marriage and death, with the evidence for each."));

        doc.Add(new Heading(1, "Summary of findings", "summary"));
        if (facts.Count == 0) doc.Add(new Paragraph("No births, marriages or deaths are recorded for them yet.", ParagraphStyle.Muted));
        else doc.Add(new BulletList(facts.Select(f => new ListItem(new[]
        {
            Run.B(f.What), new Run($" — {f.Evidence.Summary.ToLowerInvariant()}."),
        })).ToList()));

        var concluded = facts.Where(f => f.Event.Conclusion != FactConclusion.NotDecided).ToList();
        doc.Add(new Heading(1, "Conclusions", "conclusions"));
        if (concluded.Count == 0)
            doc.Add(new Paragraph("No conclusions have been reached yet; the evidence below shows where each question stands.", ParagraphStyle.Muted));
        foreach (var f in concluded)
        {
            doc.Add(new Heading(2, $"{f.What}: {(f.Event.Conclusion == FactConclusion.Proven ? "proven" : "disproved")}"));
            doc.Add(new Paragraph(f.Event.Reasoning.Trim().Length > 0 ? f.Event.Reasoning.Trim() : "(No reasoning written down.)"));
        }

        doc.Add(new Heading(1, "Evidence", "evidence"));
        foreach (var f in facts)
        {
            doc.Add(new Heading(2, EvidenceWork.Question(tree, f.Event, f.Owner)));
            var claims = EvidenceWork.Claims(tree, f.Event, f.Owner);
            doc.Add(new Table(new[] { "Record", "What it says", "Rating", "Agrees?" },
                claims.Select(c => (IReadOnlyList<Cell>)new Cell[] { c.Source, c.Says, Rating(c), c.Disagrees ? "No" : "Yes" }).ToList(),
                new[] { 3f, 3f, 2.2f, 1f }));
            doc.Add(new Paragraph($"Genealogical Proof Standard: {f.GpsMet} of 5 parts met.", ParagraphStyle.Muted));
            var unmet = f.Gps.Where(g => !g.Met).ToList();
            if (unmet.Count > 0)
                doc.Add(new BulletList(unmet.Select(g => new ListItem(new[] { Run.B(g.Label + ": "), new Run(g.Detail) })).ToList()));
        }

        var research = subject.ResearchIds.Select(id => tree.Research.GetValueOrDefault(id)).OfType<ResearchItem>().ToList();
        var searches = research.Where(r => r.Result != SearchResult.None).OrderBy(r => r.Date.SortKey).ToList();
        doc.Add(new Heading(1, "Searches made", "searches"));
        doc.Add(new Paragraph("Every search recorded, including those that found nothing: a negative search is evidence too.", ParagraphStyle.Muted));
        if (searches.Count == 0) doc.Add(new Paragraph("No searches have been logged for them.", ParagraphStyle.Muted));
        else doc.Add(new Table(new[] { "Search", "Where", "Date", "Result" },
            searches.Select(r => (IReadOnlyList<Cell>)new Cell[]
            {
                r.Description, r.Note.Trim(), ReportText.Date(r.Date),
                r.Result switch { SearchResult.Found => "Found", SearchResult.NotFound => "Not found", _ => "Unclear" },
            }).ToList(), new[] { 3f, 2.5f, 1.2f, 1f }));

        doc.Add(new Heading(1, "Recommendations for further research", "next"));
        var open = research.Where(r => r.Result == SearchResult.None && !r.Done).Select(r => r.Description).ToList();
        var suggested = ResearchSuggestions.For(tree, subject).Where(h => h.Priority <= 2 && !open.Contains(h.Task))
            .Select(h => h.Where.Length > 0 ? $"{h.Task}. {h.Where}" : $"{h.Task}.").Take(8).ToList();
        var next = open.Select(t => t.EndsWith('.') ? t : t + ".").Concat(suggested).ToList();
        if (next.Count == 0) doc.Add(new Paragraph("Nothing further is suggested at present.", ParagraphStyle.Muted));
        else doc.Add(new BulletList(next.Select(t => new ListItem(new[] { new Run(t) })).ToList()));

        doc.Add(new Heading(1, "Sources consulted", "sources"));
        var sourceIds = subject.Events.Concat(subject.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>().SelectMany(f => f.Events))
            .SelectMany(e => e.Citations).Concat(subject.Citations).Select(c => c.SourceId).Distinct();
        var sources = sourceIds.Select(id => tree.Sources.GetValueOrDefault(id)).OfType<Source>()
            .OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (sources.Count == 0) doc.Add(new Paragraph("No sources are cited yet.", ParagraphStyle.Muted));
        else doc.Add(new BulletList(sources.Select(s => new ListItem(new[]
        {
            new Run(string.Join(". ", new[] { s.Author.Trim(), s.Title.Trim(), s.Publication.Trim() }.Where(x => x.Length > 0)) + "."),
        })).ToList()));
        return doc;
    }

    private static string Rating(Claim c)
    {
        var parts = new[]
        {
            c.Kind switch { SourceKind.Original => "original", SourceKind.Derivative => "derivative", SourceKind.Authored => "authored", _ => "" },
            c.Information switch { InformationKind.Primary => "primary information", InformationKind.Secondary => "secondary information", _ => "" },
            c.Evidence switch { EvidenceKind.Direct => "direct evidence", EvidenceKind.Indirect => "indirect evidence", EvidenceKind.Negative => "negative evidence", _ => "" },
        }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "not rated" : string.Join(", ", parts);
    }
}
