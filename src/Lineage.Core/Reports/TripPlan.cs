using Lineage.Core.Model;

namespace Lineage.Core.Reports;

public static partial class Narrative
{
    /// <summary>
    /// A checklist for a visit to an archive or library: where it is, the open research tasks to do there (with the
    /// people they're about), the records already cited from it (worth a second look), and letters still waiting on them.
    /// </summary>
    public static ReportDocument TripPlan(FamilyTree tree, Repository repo)
    {
        var doc = new ReportDocument
        {
            Title = $"Visit to {(repo.Name.Trim().Length > 0 ? repo.Name.Trim() : "the archive")}",
            Subtitle = "Research trip checklist",
        };
        var contact = new[] { repo.Address.Trim(), repo.Phone.Trim() }.Where(x => x.Length > 0).ToList();
        if (contact.Count > 0) foreach (var line in contact) doc.Add(new Paragraph(line, ParagraphStyle.Muted));

        var held = tree.Sources.Values.Where(s => s.RepositoryId == repo.Id).Select(s => s.Id).ToHashSet();
        var people = new Dictionary<long, List<string>>();
        foreach (var p in tree.People.Values)
            foreach (var id in p.ResearchIds)
            {
                if (!people.TryGetValue(id, out var list)) people[id] = list = new();
                list.Add(p.DisplayName);
            }

        var tasks = tree.Research.Values
            .Where(r => !r.Done && r.Letter == null && r.Result == SearchResult.None
                        && (r.RepositoryId == repo.Id || r.SourceId is { } sid && held.Contains(sid)))
            .OrderBy(r => r.Priority == 0 ? 9 : r.Priority).ThenBy(r => r.Description, StringComparer.CurrentCultureIgnoreCase).ToList();
        doc.Add(new Heading(1, "To look for"));
        if (tasks.Count == 0) doc.Add(new Paragraph("No open research tasks name this archive or a record held there.", ParagraphStyle.Muted));
        else
            doc.Add(new BulletList(tasks.Select(r =>
            {
                var runs = new List<Run> { new("☐  "), Run.B(r.Description.Trim().Length > 0 ? r.Description.Trim() : "(no description)") };
                var about = people.GetValueOrDefault(r.Id) ?? new();
                var source = r.SourceId is { } s && tree.Sources.TryGetValue(s, out var src) ? src.Title : "";
                var detail = Join(" · ", about.Count > 0 ? string.Join(", ", about) : "", source, r.Category);
                if (detail.Length > 0) runs.Add(new Run($" — {detail}"));
                if (r.Note.Trim().Length > 0) runs.Add(new Run($". {r.Note.Trim()}", Italic: true));
                return new ListItem(runs);
            }).ToList()));

        // Who each record is cited for: people, and both partners for a family's events.
        var uses = tree.People.Values.SelectMany(p => p.Citations.Concat(p.Events.SelectMany(e => e.Citations)).Select(c => (c.SourceId, Name: p.DisplayName)))
            .Concat(tree.Families.Values.SelectMany(f => f.Citations.Concat(f.Events.SelectMany(e => e.Citations))
                .SelectMany(c => f.PartnerIds.Select(id => tree.Person(id)).OfType<Person>().Select(p => (c.SourceId, Name: p.DisplayName)))));
        var cited = uses.Where(x => held.Contains(x.SourceId))
            .GroupBy(x => x.SourceId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().ToList());
        doc.Add(new Heading(1, "Records held here"));
        if (held.Count == 0) doc.Add(new Paragraph("No sources in the tree are marked as held here yet.", ParagraphStyle.Muted));
        else
            doc.Add(new Table(new[] { "Record", "Call number", "Cited for" },
                held.Select(id => tree.Sources[id]).OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(s => (IReadOnlyList<Cell>)new Cell[]
                    {
                        s.Title, s.CallNumber,
                        cited.TryGetValue(s.Id, out var who) ? string.Join(", ", who.Take(8)) + (who.Count > 8 ? $" and {who.Count - 8} more" : "") : "not cited yet",
                    }).ToList(),
                new[] { 3f, 1.2f, 3f }));

        var name = repo.Name.Trim();
        var waiting = tree.Research.Values.Where(r => r.Letter is { } l && !r.Done && name.Length > 0
                                                       && string.Equals(l.With.Trim(), name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (waiting.Count > 0)
        {
            doc.Add(new Heading(1, "Letters waiting for a reply"));
            doc.Add(new BulletList(waiting.Select(r => new ListItem(Join(" · ", r.Description, r.Letter!.Method, ReportText.Date(r.Date)))).ToList()));
        }

        doc.Add(new Heading(1, "Notes from the visit"));
        doc.Add(new Paragraph("Record every search you make, including those that find nothing, and enter them in the search log when you get home.", ParagraphStyle.Muted));
        return doc;
    }
}
