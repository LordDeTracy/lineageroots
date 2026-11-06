using Lineage.Core.Export;
using Lineage.Core.Model;

namespace Lineage.Core.Reports;

/// <summary>How a custom report puts its people in order.</summary>
public enum CustomReportOrder { ByName, ByGeneration }

/// <summary>The choices for a report put together by the user: who is in it and what it says about each of them.</summary>
public sealed class CustomReportOptions
{
    public string Title { get; set; } = "";
    public BranchKind People { get; set; } = BranchKind.Ancestors;
    /// <summary>How far the ancestors and descendants reach (the person is generation 1).</summary>
    public int Generations { get; set; } = 4;
    public CustomReportOrder Order { get; set; } = CustomReportOrder.ByGeneration;
    public bool Story { get; set; } = true;
    public bool Facts { get; set; } = true;
    public bool Family { get; set; } = true;
    public bool Notes { get; set; } = true;
    public bool Photos { get; set; }
    /// <summary>Numbered notes saying where each fact was found.</summary>
    public bool Sources { get; set; } = true;
    public bool Index { get; set; }
    /// <summary>One person to a page.</summary>
    public bool PagePerPerson { get; set; }
    /// <summary>Finds a photo's file on disk (needed for <see cref="Photos"/>).</summary>
    public Func<string, string?>? FindMedia { get; set; }
}

public static partial class Narrative
{
    /// <summary>
    /// A report made to order: the person alone, their ancestors, descendants or the whole tree, and for each person
    /// whichever of their story, facts, family, notes and photos were chosen. Names of people in the report link to
    /// their own entries.
    /// </summary>
    public static ReportDocument Custom(FamilyTree tree, Person root, CustomReportOptions o)
    {
        var ids = CustomPeople(tree, root, o);
        var level = Levels(tree, root, ids);
        var people = (o.Order == CustomReportOrder.ByName
                ? ids.Select(id => tree.People[id]).OrderBy(p => p.PrimaryName.Surname, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(p => p.PrimaryName.Given, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Birth?.Date.SortKey ?? 0)
                : ids.Select(id => tree.People[id]).OrderBy(p => Math.Abs(level.GetValueOrDefault(p.Id, 999)))   // nearest generations first
                    .ThenBy(p => level.GetValueOrDefault(p.Id, 999)).ThenBy(p => p.Birth?.Date.SortKey ?? 0))
            .ToList();

        var who = o.People switch
        {
            BranchKind.Ancestors when o.Generations <= 1 => root.DisplayName,
            BranchKind.Ancestors => $"Ancestors of {root.DisplayName}",
            BranchKind.Descendants => $"Descendants of {root.DisplayName}",
            BranchKind.AncestorsAndDescendants => $"Ancestors and descendants of {root.DisplayName}",
            _ => tree.Name.Trim().Length > 0 ? tree.Name.Trim() : "The whole tree",
        };
        var doc = new ReportDocument
        {
            Title = o.Title.Trim().Length > 0 ? o.Title.Trim() : who,
            Subtitle = Join(" · ", o.Title.Trim().Length > 0 ? who : "", people.Count == 1 ? "1 person" : $"{people.Count} people"),
            NotesHeading = "Sources",
        };
        var notes = o.Sources ? new NoteBook(tree, doc) : null;
        var index = o.Index ? new IndexBook() : null;
        var w = new StoryWriter(tree, notes, index)
        {
            EntryOf = p => ids.Contains(p.Id) ? new StoryWriter.Entry($"p-{p.Id}", 0, "") : null,
        };

        var heading = 0;
        foreach (var p in people)
        {
            if (o.PagePerPerson && doc.Blocks.Count > 0) doc.Add(new PageBreak());
            // People in generation order get a heading per generation, so the reader knows where they are.
            if (o.Order == CustomReportOrder.ByGeneration && o.People != BranchKind.Everyone && level.TryGetValue(p.Id, out var gen) && gen != heading)
            {
                heading = gen;
                doc.Add(new Heading(1, GenerationTitle(gen)));
            }
            var anchor = $"p-{p.Id}";
            w.Current = new IndexRef(anchor, p.DisplayName);
            doc.Add(new Heading(2, Join(" · ", p.DisplayName, p.Lifespan), anchor));
            index?.Mention(p, w.Current);
            if (o.Photos && o.FindMedia is { } find && PortraitOf(p, find) is { } photo) doc.Add(photo);
            if (o.Story) doc.Add(new Paragraph(w.Story(p, spouseDetails: !o.Family)));
            if (o.Facts)
            {
                var facts = StoryWriter.Visible(p.Events).Select((e, i) => (e, i))
                    .OrderBy(x => x.e.Date.SortKey == 0 ? int.MaxValue : x.e.Date.SortKey).ThenBy(x => x.i).Select(x => x.e).ToList();
                if (facts.Count > 0) doc.Add(FactTable(w, facts));
            }
            if (o.Family) FamilyLines(doc, tree, w, p);
            if (o.Notes) foreach (var block in NoteFormat.Blocks(p.SharedNote)) doc.Add(block);
        }
        w.Current = null;
        if (people.Count == 0) doc.Add(new Paragraph("Nobody matches these choices.", ParagraphStyle.Muted));
        if (index != null) doc.Index.AddRange(index.Entries());
        return doc;
    }

    /// <summary>Who a custom report with these choices takes in.</summary>
    public static HashSet<long> CustomPeople(FamilyTree tree, Person root, CustomReportOptions o) =>
        o.People == BranchKind.Everyone ? tree.People.Keys.ToHashSet() : Within(tree, root, o.People, Math.Clamp(o.Generations, 1, 30));

    /// <summary>The branch, but only as far as <paramref name="generations"/> reaches up and down.</summary>
    private static HashSet<long> Within(FamilyTree tree, Person root, BranchKind kind, int generations)
    {
        var ids = new HashSet<long> { root.Id };
        if (kind is BranchKind.Ancestors or BranchKind.AncestorsAndDescendants)
            foreach (var p in tree.Ancestors(root, generations).Values) ids.Add(p.Id);
        if (kind is BranchKind.Descendants or BranchKind.AncestorsAndDescendants)
        {
            var current = new List<Person> { root };
            for (var g = 1; g < generations && current.Count > 0; g++)
            {
                var next = new List<Person>();
                foreach (var p in current)
                    foreach (var (_, spouse, children) in tree.FamiliesOf(p))
                    {
                        if (spouse != null) ids.Add(spouse.Id);
                        foreach (var c in children) if (ids.Add(c.Id)) next.Add(c);
                    }
                current = next;
            }
            // The last generation's husbands and wives belong with them.
            foreach (var p in current)
                foreach (var (_, spouse, _) in tree.FamiliesOf(p))
                    if (spouse != null) ids.Add(spouse.Id);
        }
        return ids;
    }

    /// <summary>Generations relative to the person: 0 for them (and their spouses), -1 parents, +1 children…</summary>
    private static Dictionary<long, int> Levels(FamilyTree tree, Person root, HashSet<long> ids)
    {
        var level = new Dictionary<long, int> { [root.Id] = 0 };
        var queue = new Queue<Person>(new[] { root });
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var here = level[p.Id];
            void Visit(Person other, int at)
            {
                if (!ids.Contains(other.Id) || level.ContainsKey(other.Id)) return;
                level[other.Id] = at;
                queue.Enqueue(other);
            }
            foreach (var parent in tree.ParentsOf(p)) Visit(parent, here - 1);
            foreach (var (_, spouse, children) in tree.FamiliesOf(p))
            {
                if (spouse != null) Visit(spouse, here);
                foreach (var c in children) Visit(c, here + 1);
            }
        }
        return level;
    }

    private static string GenerationTitle(int level) => level switch
    {
        0 => "The person and their husbands or wives",
        -1 => "Parents",
        -2 => "Grandparents",
        -3 => "Great-grandparents",
        < -3 => $"{ReportText.Ordinal(-level - 2)} great-grandparents",
        1 => "Children and their husbands or wives",
        2 => "Grandchildren",
        3 => "Great-grandchildren",
        _ => $"{ReportText.Ordinal(level - 2)} great-grandchildren",
    };

    /// <summary>Parents, then each family's spouse and children, each name linked when they're in the report.</summary>
    private static void FamilyLines(ReportDocument doc, FamilyTree tree, StoryWriter w, Person p)
    {
        var items = new List<ListItem>();
        var parents = tree.ParentsOf(p).Distinct().ToList();
        if (parents.Count > 0)
        {
            var runs = new List<Run> { new("Parents: ") };
            for (var i = 0; i < parents.Count; i++)
            {
                if (i > 0) runs.Add(new Run(" and "));
                runs.AddRange(w.Name(parents[i], showNumber: false, self: p));
            }
            items.Add(new ListItem(runs));
        }
        foreach (var (family, spouse, children) in tree.FamiliesOf(p))
        {
            var runs = new List<Run> { new(spouse == null ? "Family" : family.Status == CoupleStatus.Unmarried ? "Partner: " : "Married: ") };
            if (spouse != null) runs.AddRange(w.Name(spouse, showNumber: false, self: p));
            if (StoryWriter.Visible(family.Events).FirstOrDefault(e => e.Tag == "MARR") is { } m && !m.Date.IsEmpty)
                runs.Add(new Run(", " + ReportText.Date(m.Date)));
            if (children.Count > 0)
            {
                runs.Add(new Run(spouse == null ? ": " : ". Children: "));
                for (var i = 0; i < children.Count; i++)
                {
                    if (i > 0) runs.Add(new Run(", "));
                    runs.AddRange(w.Name(children[i], showNumber: false, self: p));
                }
            }
            items.Add(new ListItem(StoryWriter.Compact(runs)));
        }
        if (items.Count > 0) doc.Add(new BulletList(items));
    }
}
