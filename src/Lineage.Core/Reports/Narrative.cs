using Lineage.Core.Model;

namespace Lineage.Core.Reports;

/// <summary>Choices for <see cref="Narrative.FamilyBook"/>. Empty strings mean "use a sensible default".</summary>
public sealed class FamilyBookOptions
{
    public const string PlaceholderIntroduction =
        "This book gathers what we know about our family: where they lived, the work they did, the people they married " +
        "and the children they raised. Replace this text with your own introduction — why you made the book, who it is for, " +
        "and the relatives and archives that helped along the way.";

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Author { get; set; } = "";
    /// <summary>The introduction; blank lines separate paragraphs.</summary>
    public string Introduction { get; set; } = PlaceholderIntroduction;
    public int AncestorGenerations { get; set; } = 5;
    public int DescendantGenerations { get; set; } = 4;
    /// <summary>Numbered source notes and a bibliography.</summary>
    public bool IncludeSources { get; set; } = true;
    public bool IncludeIndex { get; set; } = true;
}

/// <summary>
/// Reports written as prose and lists rather than charts: the register (descendants) report,
/// individual summaries, place and surname lists, a calendar of anniversaries and a whole family book.
/// Each returns a <see cref="ReportDocument"/> for a renderer to lay out. Private events are always left out;
/// any other privacy filtering (living people, say) is up to the caller.
/// </summary>
public static class Narrative
{
    // ---- register ---------------------------------------------------------------------------------

    /// <summary>
    /// A register-style descendants report: one numbered entry per person with a family of their own,
    /// a paragraph telling their life, and their children listed i, ii, iii — "+" marks a child described later.
    /// </summary>
    public static ReportDocument Register(FamilyTree tree, Person root, int generations = 4)
    {
        generations = Math.Clamp(generations, 1, 50);
        var doc = new ReportDocument
        {
            Title = $"Descendants of {root.DisplayName}",
            Subtitle = Join(" · ", root.Lifespan, generations == 1 ? "1 generation" : $"{generations} generations"),
        };
        var notes = new NoteBook(tree, doc);
        doc.Add(new Paragraph(
            "Everyone with a family of their own has a numbered entry. In the lists of children, a plus sign (+) and a number " +
            "mean that child’s own family is described later under that number.", ParagraphStyle.Muted));
        WriteRegister(doc, new StoryWriter(tree, notes, null), tree, root, generations, "d", "", headingLevel: 1);
        return doc;
    }

    private static void WriteRegister(ReportDocument doc, StoryWriter w, FamilyTree tree, Person root, int generations,
                                      string prefix, string labelPrefix, int headingLevel)
    {
        // Numbers first, generation by generation, so each list of children can point forward.
        var numbers = new Dictionary<long, int> { [root.Id] = 1 };
        var order = new List<(Person Person, int Generation)> { (root, 1) };
        for (var i = 0; i < order.Count; i++)
        {
            var (p, gen) = order[i];
            if (gen >= generations) continue;
            foreach (var (_, _, children) in tree.FamiliesOf(p))
                foreach (var c in children)
                    if (!numbers.ContainsKey(c.Id) && HasFamily(tree, c))
                    {
                        numbers[c.Id] = order.Count + 1;
                        order.Add((c, gen + 1));
                    }
        }
        StoryWriter.Entry Entry(int n) => new($"{prefix}-{n}", n, labelPrefix + n);
        w.EntryOf = p => numbers.TryGetValue(p.Id, out var n) ? Entry(n) : null;

        foreach (var group in order.Select((x, i) => (x.Person, x.Generation, Number: i + 1)).GroupBy(x => x.Generation))
        {
            var g = group.Key;
            var about = g switch
            {
                1 => "",
                2 => "children",
                3 => "grandchildren",
                4 => "great-grandchildren",
                _ => $"{ReportText.Ordinal(g - 3)} great-grandchildren",
            };
            doc.Add(new Heading(headingLevel, about.Length > 0 ? $"Generation {g} — {about}" : $"Generation {g}", $"{prefix}-gen-{g}"));
            foreach (var (person, _, n) in group)
            {
                var entry = Entry(n);
                w.Current = new IndexRef(entry.Anchor, entry.Label);
                var runs = new List<Run> { Run.B($"{n}. ") };
                runs.AddRange(w.Story(person));
                doc.Add(new Paragraph(StoryWriter.Compact(runs), entry.Anchor));
                foreach (var para in StoryWriter.NoteParagraphs(person.Note)) doc.Add(new Paragraph(para));

                foreach (var (_, spouse, children) in tree.FamiliesOf(person))
                {
                    if (children.Count == 0) continue;
                    var intro = new List<Run> { new("Children of ") };
                    intro.AddRange(w.Name(person, self: person));
                    if (spouse != null) { intro.Add(new Run(" and ")); intro.AddRange(w.Name(spouse, self: person)); }
                    intro.Add(new Run(":"));
                    doc.Add(new Paragraph(StoryWriter.Compact(intro)));

                    var items = new List<ListItem>();
                    for (var i = 0; i < children.Count; i++)
                    {
                        var child = children[i];
                        var roman = ReportText.Roman(i + 1) + ".";
                        var own = numbers.TryGetValue(child.Id, out var cn) && cn != n;
                        var line = new List<Run>();
                        w.Mention(child);
                        line.Add(own ? Run.To(child.DisplayName, Entry(cn).Anchor, bold: true) : Run.B(child.DisplayName));
                        line.AddRange(ChildVitals(w, tree, child, withSpouses: !own));
                        items.Add(own ? new ListItem(StoryWriter.Compact(line), $"+ {cn}  {roman}", Entry(cn).Anchor)
                                      : new ListItem(StoryWriter.Compact(line), roman));
                    }
                    doc.Add(new BulletList(items));
                }
            }
        }
        w.Current = null;
    }

    private static bool HasFamily(FamilyTree tree, Person p) =>
        tree.FamiliesOf(p).Any(f => f.Spouse != null || f.Children.Count > 0);

    /// <summary>", born 3 May 1874 in Leeds; died 1875; married Jane Doe".</summary>
    private static IEnumerable<Run> ChildVitals(StoryWriter w, FamilyTree tree, Person c, bool withSpouses)
    {
        var parts = new List<List<Run>>();
        foreach (var (tag, word) in new[] { ("BIRT", "born"), ("CHR", "christened"), ("DEAT", "died"), ("BURI", "buried") })
        {
            if (tag == "CHR" && StoryWriter.Find(c, "BIRT") is { } b && (!b.Date.IsEmpty || b.Place.Length > 0)) continue;
            if (StoryWriter.Find(c, tag) is not { } e) continue;
            var text = Join(" ", ReportText.Date(e.Date), e.Place.Trim().Length > 0 ? "in " + e.Place.Trim() : "");
            if (text.Length == 0 && tag != "DEAT") continue;
            var bit = new List<Run> { new(text.Length > 0 ? $"{word} {text}" : word) };
            bit.AddRange(w.Refs(e.Citations));
            parts.Add(bit);
        }
        if (withSpouses)
            foreach (var (family, spouse, _) in tree.FamiliesOf(c))
            {
                if (spouse == null) continue;
                var bit = new List<Run> { new("married ") };
                bit.AddRange(w.Name(spouse));
                if (StoryWriter.Visible(family.Events).FirstOrDefault(e => e.Tag == "MARR") is { } m && !m.Date.IsEmpty)
                {
                    bit.Add(new Run(" " + ReportText.DatePhrase(m.Date)));
                    bit.AddRange(w.Refs(m.Citations));
                }
                parts.Add(bit);
            }
        for (var i = 0; i < parts.Count; i++)
        {
            yield return new Run(i == 0 ? ", " : "; ");
            foreach (var r in parts[i]) yield return r;
        }
    }

    // ---- individual summary -------------------------------------------------------------------------

    /// <summary>Everything about one person: names, a table of facts, parents, each family, notes and sources.</summary>
    public static ReportDocument IndividualSummary(FamilyTree tree, Person p)
    {
        var doc = new ReportDocument { Title = p.DisplayName, Subtitle = Join(" · ", p.Lifespan, SexWord(p.Sex)), NotesHeading = "Sources" };
        var notes = new NoteBook(tree, doc);
        var w = new StoryWriter(tree, notes, null);

        doc.Add(new Heading(1, "Names", "names"));
        var names = new List<ListItem>();
        for (var i = 0; i < p.Names.Count; i++)
        {
            var n = p.Names[i];
            if (n.Full.Length == 0 && n.Nickname.Length == 0) continue;
            var kind = i == 0 ? "" : n.Type.ToLowerInvariant() switch
            {
                "" or "aka" => "also known as",
                "birth" => "birth name",
                "married" => "married name",
                "immigrant" => "name after immigrating",
                "maiden" => "maiden name",
                var other => other,
            };
            var runs = new List<Run> { i == 0 ? Run.B(n.Full.Length > 0 ? n.Full : p.DisplayName) : new Run(n.Full) };
            if (kind.Length > 0) runs.Add(new Run($" ({kind})"));
            if (n.Nickname.Length > 0) runs.Add(new Run($", called “{n.Nickname}”"));
            if (i == 0) runs.AddRange(w.Refs(p.Citations));
            names.Add(new ListItem(StoryWriter.Compact(runs)));
        }
        if (names.Count == 0) names.Add(new ListItem(p.DisplayName));
        doc.Add(new BulletList(names));

        var facts = StoryWriter.Visible(p.Events).Select((e, i) => (e, i))
            .OrderBy(x => x.e.Date.SortKey == 0 ? int.MaxValue : x.e.Date.SortKey).ThenBy(x => x.i).Select(x => x.e).ToList();
        doc.Add(new Heading(1, "Facts and events", "facts"));
        if (facts.Count == 0) doc.Add(new Paragraph("No events are recorded.", ParagraphStyle.Muted));
        else doc.Add(FactTable(w, facts));

        doc.Add(new Heading(1, "Parents", "parents"));
        var parentFamilies = p.ParentFamilyIds.Select(id => tree.Family(id)).OfType<Family>().ToList();
        var parentItems = new List<ListItem>();
        foreach (var f in parentFamilies)
        {
            var link = f.Children.FirstOrDefault(c => c.ChildId == p.Id);
            foreach (var (pid, slot) in new[] { (f.Partner1Id, 1), (f.Partner2Id, 2) })
            {
                if (tree.Person(pid) is not { } parent) continue;
                var role = parent.Sex switch { Sex.Male => "Father", Sex.Female => "Mother", _ => "Parent" };
                var relation = link == null ? ParentRelation.Birth : slot == 1 ? link.ToFather : link.ToMother;
                if (relation != ParentRelation.Birth) role += $" ({relation.ToString().ToLowerInvariant()})";
                parentItems.Add(new ListItem(new[] { new Run(role + ": "), Run.B(parent.DisplayName), new Run(Life(parent)) }));
            }
        }
        if (parentItems.Count == 0) doc.Add(new Paragraph("No parents are recorded.", ParagraphStyle.Muted));
        else doc.Add(new BulletList(parentItems));

        var families = tree.FamiliesOf(p).ToList();
        var number = 0;
        foreach (var (family, spouse, children) in families)
        {
            number++;
            doc.Add(new Heading(1, spouse != null ? $"Family with {spouse.DisplayName}" : families.Count > 1 ? $"Family {number}" : "Family", $"family-{number}"));
            if (spouse != null)
            {
                var role = spouse.Sex switch { Sex.Male => "Husband", Sex.Female => "Wife", _ => "Spouse" };
                if (family.Status == CoupleStatus.Unmarried) role = "Partner";
                doc.Add(new Paragraph(new[] { new Run(role + ": "), Run.B(spouse.DisplayName), new Run(Life(spouse)) }));
            }
            var familyFacts = StoryWriter.Visible(family.Events).OrderBy(e => e.Date.SortKey == 0 ? int.MaxValue : e.Date.SortKey).ToList();
            if (familyFacts.Count > 0) doc.Add(FactTable(w, familyFacts));
            if (family.Status == CoupleStatus.Divorced && familyFacts.All(e => e.Tag != "DIV"))
                doc.Add(new Paragraph("The couple divorced.", ParagraphStyle.Muted));
            if (children.Count > 0)
            {
                doc.Add(new Heading(2, children.Count == 1 ? "Child" : $"Children ({children.Count})"));
                doc.Add(new BulletList(children.Select((c, i) =>
                    new ListItem(new[] { Run.B(c.DisplayName), new Run(Life(c)) }, ReportText.Roman(i + 1) + ".")).ToList()));
            }
            foreach (var para in StoryWriter.NoteParagraphs(family.Note)) doc.Add(new Paragraph(para));
        }

        var personNotes = StoryWriter.NoteParagraphs(p.Note).ToList();
        if (personNotes.Count > 0)
        {
            doc.Add(new Heading(1, "Notes", "notes-text"));
            foreach (var para in personNotes) doc.Add(new Paragraph(para));
        }
        return doc;
    }

    private static Table FactTable(StoryWriter w, List<Event> facts)
    {
        var rows = new List<IReadOnlyList<Cell>>();
        foreach (var e in facts)
        {
            var details = new List<string>();
            var value = StoryWriter.Value(e);
            if (value.Length > 0) details.Add(value);
            if (e.Cause.Trim().Length > 0) details.Add("Cause: " + e.Cause.Trim());
            if (e.Place2.Trim().Length > 0) details.Add("To: " + e.Place2.Trim());
            if (e.Temple.Trim().Length > 0) details.Add("Temple: " + e.Temple.Trim());
            if (e.Note.Trim().Length > 0) details.Add(e.Note.Trim());
            var detailRuns = new List<Run> { new(string.Join(". ", details)) };
            detailRuns.AddRange(w.Refs(e.Citations));
            rows.Add(new Cell[] { EventTypes.Label(e), ReportText.Date(e.Date), e.Place.Trim(), new(StoryWriter.Compact(detailRuns)) });
        }
        return new Table(new[] { "Event", "Date", "Place", "Details" }, rows, new[] { 1.3f, 1.4f, 2.2f, 2.4f });
    }

    private static string Life(Person p)
    {
        var bits = new List<string>();
        if (p.Lifespan.Length > 0) bits.Add(p.Lifespan);
        if (StoryWriter.Find(p, "BIRT") is { } b && b.Place.Trim().Length > 0) bits.Add("born in " + b.Place.Trim());
        return bits.Count == 0 ? "" : " (" + string.Join(", ", bits) + ")";
    }

    private static string SexWord(Sex s) => s switch { Sex.Male => "Male", Sex.Female => "Female", _ => "" };

    // ---- lists --------------------------------------------------------------------------------------

    /// <summary>Every place in the tree with how many events and people it has and the years they span, alphabetical.</summary>
    public static ReportDocument PlaceList(FamilyTree tree)
    {
        var places = new Dictionary<string, (string Name, int Events, HashSet<long> People, int From, int To)>(StringComparer.OrdinalIgnoreCase);
        void Count(Event e, IEnumerable<long> people)
        {
            var name = e.Place.Trim();
            if (name.Length == 0) return;
            var (n, events, set, from, to) = places.TryGetValue(name, out var x) ? x : (name, 0, new HashSet<long>(), 0, 0);
            foreach (var id in people) set.Add(id);
            var y = ReportText.Year(e.Date);
            if (y > 0) { from = from == 0 ? y : Math.Min(from, y); to = Math.Max(to, y); }
            places[name] = (n, events + 1, set, from, to);
        }
        foreach (var p in tree.People.Values)
            foreach (var e in StoryWriter.Visible(p.Events)) Count(e, new[] { p.Id });
        foreach (var f in tree.Families.Values)
            foreach (var e in StoryWriter.Visible(f.Events)) Count(e, f.PartnerIds.Where(tree.People.ContainsKey));

        var sorted = places.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var doc = new ReportDocument { Title = "Places", Subtitle = Join(" · ", tree.Name, sorted.Count == 1 ? "1 place" : $"{sorted.Count} places") };
        doc.Add(new Paragraph("Every place named in the tree, with the number of events recorded there, the people involved, and the years from the earliest to the latest event.", ParagraphStyle.Muted));
        if (sorted.Count == 0) doc.Add(new Paragraph("No places are recorded yet."));
        else doc.Add(new Table(new[] { "Place", "Events", "People", "Years" },
            sorted.Select(x => (IReadOnlyList<Cell>)new Cell[] { x.Name, Num(x.Events), Num(x.People.Count), ReportText.Years(x.From, x.To) }).ToList(),
            new[] { 4f, 1f, 1f, 1.4f }));
        return doc;
    }

    /// <summary>Each surname with how many people carry it and the earliest and latest year recorded for them.</summary>
    public static ReportDocument SurnameList(FamilyTree tree)
    {
        var groups = tree.People.Values
            .GroupBy(p => p.PrimaryName.Surname.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g =>
            {
                var years = g.SelectMany(p => StoryWriter.Visible(p.Events)).Select(e => ReportText.Year(e.Date)).Where(y => y > 0).ToList();
                return (Name: g.Key, Count: g.Count(), From: years.Count > 0 ? years.Min() : 0, To: years.Count > 0 ? years.Max() : 0);
            })
            .OrderBy(x => x.Name.Length == 0 ? 1 : 0).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var doc = new ReportDocument { Title = "Surnames", Subtitle = Join(" · ", tree.Name, groups.Count == 1 ? "1 surname" : $"{groups.Count} surnames") };
        doc.Add(new Paragraph("Each family name in the tree, how many people carry it, and the span of years their records cover.", ParagraphStyle.Muted));
        if (groups.Count == 0) doc.Add(new Paragraph("No people are recorded yet."));
        else doc.Add(new Table(new[] { "Surname", "People", "Years" },
            groups.Select(x => (IReadOnlyList<Cell>)new Cell[] { x.Name.Length > 0 ? x.Name : "(no surname)", Num(x.Count), ReportText.Years(x.From, x.To) }).ToList(),
            new[] { 3f, 1f, 1.4f }));
        return doc;
    }

    /// <summary>Births, marriages and deaths whose day and month are known, month by month — for remembering anniversaries.</summary>
    public static ReportDocument BirthdayCalendar(FamilyTree tree)
    {
        var items = new List<(int Month, int Day, int Kind, int Year, string Text)>();
        static bool Exact(GenDate d) => d.Kind == DateKind.Simple && d.Qualifier == DateQualifier.None && d.Day > 0 && d.Month is >= 1 and <= 12;
        string WithYear(string s, int y) => y > 0 ? $"{s} {y}" : s;
        foreach (var p in tree.People.Values)
        {
            if (StoryWriter.Find(p, "BIRT") is { } b && Exact(b.Date)) items.Add((b.Date.Month, b.Date.Day, 0, b.Date.Year, WithYear($"{p.DisplayName} born", b.Date.Year)));
            if (StoryWriter.Find(p, "DEAT") is { } d && Exact(d.Date)) items.Add((d.Date.Month, d.Date.Day, 2, d.Date.Year, WithYear($"{p.DisplayName} died", d.Date.Year)));
        }
        foreach (var f in tree.Families.Values)
        {
            if (StoryWriter.Visible(f.Events).FirstOrDefault(e => e.Tag == "MARR") is not { } m || !Exact(m.Date)) continue;
            var couple = f.PartnerIds.Select(id => tree.Person(id)?.DisplayName).OfType<string>().ToList();
            if (couple.Count == 0) continue;
            items.Add((m.Date.Month, m.Date.Day, 1, m.Date.Year, WithYear($"{ReportText.List(couple)} married", m.Date.Year)));
        }

        var doc = new ReportDocument { Title = "Birthdays and anniversaries", Subtitle = tree.Name };
        doc.Add(new Paragraph("Births, marriages and deaths whose day and month are known, through the year.", ParagraphStyle.Muted));
        if (items.Count == 0) doc.Add(new Paragraph("No births, marriages or deaths with a full date are recorded yet."));
        foreach (var month in items.GroupBy(x => x.Month).OrderBy(g => g.Key))
        {
            var name = ReportText.MonthName(month.Key);
            doc.Add(new Heading(1, name, "month-" + month.Key));
            doc.Add(new BulletList(month.OrderBy(x => x.Day).ThenBy(x => x.Kind).ThenBy(x => x.Year).ThenBy(x => x.Text, StringComparer.CurrentCulture)
                .Select(x => new ListItem(new[] { Run.B($"{x.Day} {name}"), new Run(" — " + x.Text) })).ToList()));
        }
        return doc;
    }

    // ---- family book ------------------------------------------------------------------------------

    /// <summary>
    /// A family history book: title page, introduction, the ancestors of <paramref name="root"/> as an
    /// Ahnentafel narrative, their descendants as a register, a bibliography of sources and an index of names.
    /// Headings carry anchors so renderers can build a table of contents.
    /// </summary>
    public static ReportDocument FamilyBook(FamilyTree tree, Person root, FamilyBookOptions? options = null)
    {
        options ??= new FamilyBookOptions();
        var surname = root.PrimaryName.Surname.Trim();
        var doc = new ReportDocument
        {
            IsBook = true,
            Title = options.Title.Trim().Length > 0 ? options.Title.Trim() : surname.Length > 0 ? $"The {surname} Family" : $"The Family of {root.DisplayName}",
            Subtitle = options.Subtitle.Trim().Length > 0 ? options.Subtitle.Trim() : $"Ancestors and descendants of {root.DisplayName}",
            Author = options.Author.Trim(),
            IndexNote = "Each name is followed by the entries that mention it: A for the Ancestors part, D for the Descendants part.",
        };
        var notes = options.IncludeSources ? new NoteBook(tree, doc) : null;
        var index = options.IncludeIndex ? new IndexBook() : null;

        doc.Add(new Heading(1, "Introduction", "introduction"));
        var intro = StoryWriter.NoteParagraphs(options.Introduction ?? "").ToList();
        if (intro.Count == 0) intro.Add(FamilyBookOptions.PlaceholderIntroduction);
        foreach (var para in intro) doc.Add(new Paragraph(para));

        // Ancestors, numbered the Ahnentafel way.
        var ancestorGenerations = Math.Clamp(options.AncestorGenerations, 1, 30);
        doc.Add(new PageBreak());
        doc.Add(new Heading(1, $"Ancestors of {root.DisplayName}", "ancestors"));
        doc.Add(new Paragraph("Each ancestor has a number: a person’s father is twice their number and their mother twice their number plus one.", ParagraphStyle.Muted));
        var people = tree.Ancestors(root, ancestorGenerations);
        var first = new Dictionary<long, int>();
        foreach (var (n, p) in people.OrderBy(kv => kv.Key)) first.TryAdd(p.Id, n);
        var w = new StoryWriter(tree, notes, index)
        {
            EntryOf = p => first.TryGetValue(p.Id, out var n) ? new StoryWriter.Entry($"a-{n}", n, $"A{n}") : null,
        };
        for (var gen = 1; gen <= ancestorGenerations; gen++)
        {
            var from = 1 << (gen - 1);
            var inGen = people.Where(kv => kv.Key >= from && kv.Key < from * 2).OrderBy(kv => kv.Key).ToList();
            if (inGen.Count == 0) break;
            var about = gen switch
            {
                1 => "",
                2 => "parents",
                3 => "grandparents",
                4 => "great-grandparents",
                _ => $"{ReportText.Ordinal(gen - 3)} great-grandparents",
            };
            doc.Add(new Heading(2, about.Length > 0 ? $"Generation {gen} — {about}" : $"Generation {gen}", $"a-gen-{gen}"));
            foreach (var (n, p) in inGen)
            {
                if (first[p.Id] != n)
                {
                    // the same ancestor reached by two lines (cousins who married)
                    doc.Add(new Paragraph(new[] { Run.B($"{n}. "), Run.B(p.DisplayName), new Run(" — see "), Run.To(first[p.Id].ToString(), $"a-{first[p.Id]}"), new Run(".") }, $"a-{n}"));
                    continue;
                }
                w.Current = new IndexRef($"a-{n}", $"A{n}");
                var runs = new List<Run> { Run.B($"{n}. ") };
                runs.AddRange(w.Story(p));
                doc.Add(new Paragraph(StoryWriter.Compact(runs), $"a-{n}"));
                foreach (var para in StoryWriter.NoteParagraphs(p.Note)) doc.Add(new Paragraph(para));
            }
        }
        w.Current = null;

        // Descendants, as a register.
        doc.Add(new PageBreak());
        doc.Add(new Heading(1, $"Descendants of {root.DisplayName}", "descendants"));
        doc.Add(new Paragraph(
            "Everyone with a family of their own has a numbered entry. In the lists of children, a plus sign (+) and a number " +
            "mean that child’s own family is described later under that number.", ParagraphStyle.Muted));
        WriteRegister(doc, new StoryWriter(tree, notes, index), tree, root, Math.Clamp(options.DescendantGenerations, 1, 50), "d", "D", headingLevel: 2);

        if (notes != null && notes.SourceIds.Count > 0)
        {
            doc.Add(new PageBreak());
            doc.Add(new Heading(1, "Sources", "sources"));
            doc.Add(new Paragraph("The records this book draws on. Numbered notes at the end of the book show exactly where each fact was found.", ParagraphStyle.Muted));
            doc.Add(new BulletList(notes.SourceIds.Select(id => NoteBook.Bibliography(tree, tree.Sources[id]))
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                .Select(s => new ListItem(s)).ToList()));
        }
        if (index != null) doc.Index.AddRange(index.Entries());
        return doc;
    }

    /// <summary>One person's life as a single plain paragraph, without source notes — for web pages and tooltips.</summary>
    public static string LifeSummary(FamilyTree tree, Person p) => Run.Join(new StoryWriter(tree, null, null).Story(p));

    private static string Num(int n) => n.ToString(System.Globalization.CultureInfo.CurrentCulture);

    private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Where(x => x.Length > 0));
}
