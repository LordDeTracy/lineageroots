using Lineage.Core.Model;

namespace Lineage.Core.Reports;

/// <summary>
/// Numbers citations as endnotes. The same source and page cited twice share one number, so a
/// census quoted for five facts doesn't fill the notes with five identical lines.
/// </summary>
internal sealed class NoteBook
{
    private readonly FamilyTree _tree;
    private readonly ReportDocument _doc;
    private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);

    public NoteBook(FamilyTree tree, ReportDocument doc) { _tree = tree; _doc = doc; }

    /// <summary>Sources cited so far, in order of first citation.</summary>
    public List<long> SourceIds { get; } = new();

    public int? Cite(Citation c)
    {
        var text = CitationText(_tree, c);
        if (text.Length == 0) return null;
        var key = $"{c.SourceId}|{c.Page.Trim()}|{text}";
        if (_numbers.TryGetValue(key, out var n)) return n;
        n = _doc.Notes.Count + 1;
        _doc.Notes.Add(new Note(n, text));
        _numbers[key] = n;
        if (_tree.Sources.ContainsKey(c.SourceId) && !SourceIds.Contains(c.SourceId)) SourceIds.Add(c.SourceId);
        return n;
    }

    /// <summary>"Church of England, Parish registers of St Mary, Harrow (Harrow, 1842), Baptisms 1842, entry 118."</summary>
    public static string CitationText(FamilyTree tree, Citation c)
    {
        var s = tree.Sources.GetValueOrDefault(c.SourceId);
        var title = s == null ? "" : s.Title.Trim().Length > 0 ? s.Title.Trim() : s.ShortTitle.Trim();
        if (s != null && s.Publication.Trim().Length > 0) title = title.Length > 0 ? $"{title} ({s.Publication.Trim()})" : s.Publication.Trim();
        var parts = new[] { s?.Author.Trim() ?? "", title, c.Page.Trim() }.Where(x => x.Length > 0).ToList();
        if (parts.Count == 0 && c.Text.Trim().Length > 0) parts.Add($"“{c.Text.Trim()}”");
        return parts.Count == 0 ? "" : EndSentence(string.Join(", ", parts));
    }

    /// <summary>A bibliography line: "Author. Title. Publication. Held at Repository."</summary>
    public static string Bibliography(FamilyTree tree, Source s)
    {
        var repo = s.RepositoryId is { } r && tree.Repositories.TryGetValue(r, out var rep) && rep.Name.Trim().Length > 0 ? $"Held at {rep.Name.Trim()}" : "";
        var title = s.Title.Trim().Length > 0 ? s.Title.Trim() : s.ShortTitle.Trim().Length > 0 ? s.ShortTitle.Trim() : "Untitled source";
        var parts = new[] { s.Author, title, s.Publication, s.CallNumber.Trim().Length > 0 ? "Call number " + s.CallNumber.Trim() : "", repo }
            .Select(x => x.Trim().TrimEnd('.')).Where(x => x.Length > 0);
        return string.Join(". ", parts) + ".";
    }

    private static string EndSentence(string s) => s.EndsWith('.') || s.EndsWith('”') || s.EndsWith('?') || s.EndsWith('!') ? s : s + ".";
}

/// <summary>Collects every place a person is mentioned, for a book's index of names.</summary>
internal sealed class IndexBook
{
    private readonly Dictionary<long, List<IndexRef>> _refs = new();
    private readonly Dictionary<long, Person> _people = new();

    public void Mention(Person p, IndexRef where)
    {
        if (!_refs.TryGetValue(p.Id, out var list)) { _refs[p.Id] = list = new(); _people[p.Id] = p; }
        if (list.All(r => r.Anchor != where.Anchor)) list.Add(where);
    }

    public IEnumerable<IndexEntry> Entries() =>
        _people.Values
            .OrderBy(p => p.PrimaryName.Surname.Length == 0 ? 1 : 0)
            .ThenBy(p => p.PrimaryName.Surname, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.PrimaryName.Given, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Birth?.Date.SortKey ?? 0)
            .Select(p => new IndexEntry(IndexName(p), _refs[p.Id]));

    public static string IndexName(Person p)
    {
        var n = p.PrimaryName;
        var name = n.Surname.Length > 0 && n.Given.Length > 0 ? $"{n.Surname}, {n.Given}" : n.Surname.Length > 0 ? n.Surname : n.Given.Length > 0 ? n.Given : "(no name)";
        if (n.Suffix.Length > 0) name += " " + n.Suffix;
        return p.Lifespan.Length > 0 ? $"{name} ({p.Lifespan})" : name;
    }
}

/// <summary>
/// Writes one person's life as a paragraph of plain English: birth and parents, christening,
/// marriages, the events of their life in date order, death and burial. Private events are left out.
/// Names of people who have their own numbered entry become links to it.
/// </summary>
internal sealed class StoryWriter
{
    /// <summary>A numbered entry in the current report part.</summary>
    public sealed record Entry(string Anchor, int Number, string Label);

    private static readonly HashSet<string> Opening = new() { "BIRT", "CHR", "BAPM" };
    private static readonly HashSet<string> Closing = new() { "DEAT", "BURI", "CREM" };
    private static readonly HashSet<string> Never = new() { "SSN", "IDNO", "_SSN" }; // identity numbers stay out of shared reports

    private readonly FamilyTree _tree;
    private readonly NoteBook? _notes;
    private readonly IndexBook? _index;

    public StoryWriter(FamilyTree tree, NoteBook? notes, IndexBook? index) { _tree = tree; _notes = notes; _index = index; }

    public Func<Person, Entry?> EntryOf { get; set; } = _ => null;
    /// <summary>The entry being written, recorded against every name mentioned (for the index).</summary>
    public IndexRef? Current { get; set; }

    public static IEnumerable<Event> Visible(IEnumerable<Event> events) =>
        events.Where(e => !e.Private && !Never.Contains(e.Tag));

    public static Event? Find(Person p, string tag) => Visible(p.Events).FirstOrDefault(e => e.Tag == tag);

    // ---- names and notes -------------------------------------------------------------------------

    /// <summary>A person's name, linked (with their number) when they have an entry of their own.</summary>
    public IEnumerable<Run> Name(Person p, bool bold = false, bool showNumber = true, Person? self = null)
    {
        Mention(p);
        if (p != self && EntryOf(p) is { } entry)
        {
            yield return Run.To(p.DisplayName, entry.Anchor, bold);
            if (showNumber) yield return new Run($" ({entry.Number})");
        }
        else yield return new Run(p.DisplayName, bold);
    }

    public void Mention(Person p)
    {
        if (_index != null && Current != null) _index.Mention(p, Current);
    }

    public IEnumerable<Run> Refs(IEnumerable<Citation> citations)
    {
        if (_notes == null) yield break;
        foreach (var n in citations.Select(_notes.Cite).OfType<int>().Distinct().Order())
            yield return Run.Ref(n);
    }

    // ---- the story -------------------------------------------------------------------------------

    /// <summary>The narrative for one person. <paramref name="spouseDetails"/> adds a sentence on spouses who have no entry of their own.</summary>
    public List<Run> Story(Person p, bool spouseDetails = true)
    {
        var s = new Sentences(this, p);

        // Birth, with the parents.
        var birth = Find(p, "BIRT");
        var (father, mother) = _tree.PrimaryParents(p);
        var parents = new List<Person>();
        if (father != null) parents.Add(father);
        if (mother != null) parents.Add(mother);
        if (birth != null && Has(birth))
        {
            s.Begin();
            s.Text($" {s.Be} born").When(birth);
            if (parents.Count > 0) { s.Text($", the {ReportText.ChildWord(p.Sex)} of "); s.People(parents); }
            s.End();
        }
        else if (parents.Count > 0)
        {
            s.Begin();
            s.Text($" {s.Be} the {ReportText.ChildWord(p.Sex)} of ");
            s.People(parents);
            s.End();
        }

        foreach (var tag in new[] { "CHR", "BAPM" })
            if (Find(p, tag) is { } e && Has(e))
            {
                s.Begin();
                s.Text(tag == "CHR" ? $" {s.Be} christened" : $" {s.Be} baptized").When(e);
                s.End();
            }

        Marriages(s, p, spouseDetails);

        // The rest of the life, in date order; occupations, homes and censuses are gathered up.
        var middle = Visible(p.Events).Where(e => !Opening.Contains(e.Tag) && !Closing.Contains(e.Tag))
            .Select((e, i) => (e, i)).OrderBy(x => x.e.Date.SortKey == 0 ? int.MaxValue : x.e.Date.SortKey).ThenBy(x => x.i)
            .Select(x => x.e).ToList();
        var done = new HashSet<string>();
        var firstJob = true;
        foreach (var e in middle)
        {
            if (e.Tag == "OCCU")
            {
                var key = Value(e).ToLowerInvariant();
                if (!done.Add("OCCU|" + key)) continue;
                var same = middle.Where(x => x.Tag == "OCCU" && Value(x).ToLowerInvariant() == key).ToList();
                Occupation(s, same, firstJob);
                firstJob = false;
            }
            else if (e.Tag is "RESI" or "CENS")
            {
                if (!done.Add(e.Tag)) continue;
                Gathered(s, middle.Where(x => x.Tag == e.Tag).ToList());
            }
            else Other(s, e);
        }

        Ending(s, p, birth);
        if (!s.Named) { s.Begin(); s.End(); }   // nothing recorded: the name alone
        return Compact(s.Runs);
    }

    /// <summary>The person's own note, one paragraph per blank-line-separated block.</summary>
    public static IEnumerable<string> NoteParagraphs(string note) =>
        note.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => string.Join(' ', x.Split('\n', StringSplitOptions.TrimEntries)));

    private void Marriages(Sentences s, Person p, bool spouseDetails)
    {
        var families = _tree.FamiliesOf(p).ToList();
        var count = families.Count(f => f.Spouse != null || f.Family.Marriage is { Private: false });
        var i = 0;
        foreach (var (family, spouse, _) in families)
        {
            var marriage = Visible(family.Events).FirstOrDefault(e => e.Tag == "MARR");
            if (spouse == null && marriage == null) continue;
            i++;
            s.Begin();
            if (family.Status == CoupleStatus.Unmarried && marriage == null && spouse != null)
            {
                s.Text(" and ");
                s.Add(Name(spouse, self: p));
                s.Text(" were partners");
            }
            else
            {
                s.Text(" married");
                if (count > 1) s.Text($", {ReportText.OrdinalWord(i)},");
                if (spouse != null)
                {
                    s.Text(" ");
                    s.Add(Name(spouse, self: p));
                    var (sf, sm) = _tree.PrimaryParents(spouse);
                    var sp = new[] { sf, sm }.OfType<Person>().ToList();
                    if (sp.Count > 0 && EntryOf(spouse) == null)
                    {
                        s.Text($", the {ReportText.ChildWord(spouse.Sex)} of ");
                        s.People(sp);
                        if (marriage != null && Has(marriage)) s.Text(",");
                    }
                }
                if (marriage != null) s.When(marriage);
            }
            s.End();

            var divorce = Visible(family.Events).FirstOrDefault(e => e.Tag is "DIV" or "ANUL");
            if (divorce != null || family.Status == CoupleStatus.Divorced)
            {
                s.Raw(divorce?.Tag == "ANUL" ? "The marriage was annulled" : "They divorced");
                if (divorce != null) s.When(divorce);
                s.End();
            }

            if (spouseDetails && spouse != null && EntryOf(spouse) == null) SpouseSentence(s, spouse);
        }
    }

    private void SpouseSentence(Sentences s, Person spouse)
    {
        var born = Find(spouse, "BIRT") ?? Find(spouse, "CHR");
        var died = Find(spouse, "DEAT");
        var buried = Find(spouse, "BURI");
        var clauses = new List<Action>();
        var given = spouse.PrimaryName.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? spouse.DisplayName;
        if (born != null && Has(born)) clauses.Add(() => s.Text(born.Tag == "CHR" ? " was christened" : " was born").When(born));
        if (died != null)
        {
            clauses.Add(() =>
            {
                s.Text(" died").When(died);
                var age = ReportText.Age(Find(spouse, "BIRT") ?? born, died.Date);
                if (age.Length > 0) s.Text(", " + age);
            });
        }
        else if (buried != null && Has(buried)) clauses.Add(() => s.Text(" was buried").When(buried));
        if (clauses.Count == 0) return;
        s.Raw(given);
        for (var i = 0; i < clauses.Count; i++)
        {
            if (i > 0) s.Text(" and");
            clauses[i]();
        }
        s.End();
    }

    private void Occupation(Sentences s, List<Event> jobs, bool first)
    {
        var what = ReportText.LowerFirst(Value(jobs[0]));
        var places = jobs.Select(j => j.Place.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        var dates = jobs.Select(j => ReportText.Date(j.Date)).Where(x => x.Length > 0).Distinct().ToList();
        s.Begin();
        s.Text(first ? " worked" : " also worked");
        if (what.Length > 0) s.Text(" as " + ReportText.WithArticle(what));
        if (places.Count == 1) s.Text(" in " + places[0]);
        if (dates.Count > 0) s.Text($" ({string.Join(", ", dates)})");
        s.Add(Refs(jobs.SelectMany(j => j.Citations)));
        s.End();
    }

    /// <summary>"He lived in Leeds (1871), York (1881) and Boston (1890)."</summary>
    private void Gathered(Sentences s, List<Event> events)
    {
        var bits = new List<(string Text, IEnumerable<Citation> Cites)>();
        foreach (var e in events)
        {
            var where = e.Place.Trim().Length > 0 ? e.Place.Trim() : Value(e);
            var when = ReportText.Date(e.Date);
            if (e.Tag == "CENS")
            {
                var text = when.Length > 0 ? (where.Length > 0 ? $"{when} ({where})" : when) : where;
                if (text.Length > 0) bits.Add((text, e.Citations));
            }
            else if (where.Length > 0) bits.Add((when.Length > 0 ? $"{where} ({when})" : where, e.Citations));
        }
        if (bits.Count == 0) return;
        s.Begin();
        s.Text(events[0].Tag == "CENS" ? $" {s.Be} recorded in the census in " : " lived in ");
        for (var i = 0; i < bits.Count; i++)
        {
            if (i > 0) s.Text(i == bits.Count - 1 ? " and " : ", ");
            s.Text(bits[i].Text);
            s.Add(Refs(bits[i].Cites));
        }
        s.End();
    }

    private void Other(Sentences s, Event e)
    {
        var value = Value(e);
        var lowered = ReportText.LowerFirst(value);
        string? verb = e.Tag switch
        {
            "ADOP" => $"{s.BeNext} adopted",
            "CONF" => $"{s.BeNext} confirmed",
            "FCOM" => $"made {s.His} first communion",
            "ORDN" => $"{s.BeNext} ordained",
            "BARM" => $"celebrated {s.His} bar mitzvah",
            "BASM" => $"celebrated {s.His} bat mitzvah",
            "BLES" => $"{s.BeNext} blessed",
            "CHRA" => $"{s.BeNext} christened as an adult",
            "EMIG" => "emigrated",
            "IMMI" => "immigrated",
            "NATU" => $"{s.BeNext} naturalized",
            "EDUC" => value.Length > 0 ? $"{s.BeNext} educated at {value}" : $"{s.BeNext} educated",
            "GRAD" => value.Length > 0 ? $"graduated from {value}" : "graduated",
            "RETI" => "retired",
            "WILL" => $"made {s.His} will",
            "MILI" => value.Length > 0 ? $"served in the military ({value})" : "served in the military",
            "PROP" => value.Length > 0 ? $"owned property ({value})" : "owned property",
            "DSCR" => value.Length > 0 ? $"{s.BeNext} described as {lowered}" : null,
            "NCHI" => value.Length > 0 ? $"had {value} children" : null,
            _ => null,
        };
        var place = e.Tag switch { "EMIG" => "from", "IMMI" => "to", _ => "in" };
        s.Begin(possessive: verb == null);
        if (verb != null) s.Text(" " + verb);
        else
        {
            var label = e.Tag switch { "PROB" => "will", "RELI" => "religion", "NATI" => "nationality", "CAST" => "caste", _ => ReportText.LowerFirst(EventTypes.Label(e)) };
            if (e.Tag is "RELI" or "NATI" or "CAST" && value.Length > 0) { s.Text($" {label} was recorded as {value}"); value = ""; }
            else s.Text($" {label} was {(e.Tag == "PROB" ? "proved" : "recorded")}");
            if (value.Length > 0 && e.Tag != "DSCR") s.Text($" ({value})");
        }
        s.When(e, place);
        s.End();
    }

    private void Ending(Sentences s, Person p, Event? birth)
    {
        var death = Find(p, "DEAT");
        var burial = Find(p, "BURI") ?? Find(p, "CREM");
        if (death == null && (burial == null || !Has(burial))) return;
        s.Begin();
        if (death != null)
        {
            s.Text(" died");
            if (death.Cause.Trim().Length > 0) s.Text(" of " + ReportText.LowerFirst(death.Cause.Trim()));
            s.When(death);
            var age = ReportText.Age(birth ?? Find(p, "CHR") ?? Find(p, "BAPM"), death.Date);
            if (age.Length > 0) s.Text(", " + age);
            if (burial != null && Has(burial)) s.Text($", and {s.Be} {(burial.Tag == "CREM" ? "cremated" : "buried")}").When(burial);
        }
        else s.Text($" {s.Be} {(burial!.Tag == "CREM" ? "cremated" : "buried")}").When(burial);
        s.End();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static bool Has(Event e) => !e.Date.IsEmpty || e.Place.Trim().Length > 0;

    public static string Value(Event e)
    {
        var v = e.Value.Trim();
        if (v is "Y" or "y") v = "";
        if (v.Length == 0) v = e.Description.Trim();
        return v;
    }

    /// <summary>Merges neighbouring plain runs so renderers see fewer, longer pieces of text.</summary>
    public static List<Run> Compact(List<Run> runs)
    {
        var result = new List<Run>(runs.Count);
        foreach (var r in runs)
        {
            if (r.NoteNumber == null && r.Text.Length == 0) continue;
            if (result.Count > 0 && result[^1] is { NoteNumber: null, Link: null } last && r.NoteNumber == null && r.Link == null
                && last.Bold == r.Bold && last.Italic == r.Italic)
                result[^1] = last with { Text = last.Text + r.Text };
            else result.Add(r);
        }
        return result;
    }

    /// <summary>
    /// Builds the paragraph sentence by sentence. The first sentence names the person in full;
    /// later ones use he/she/they, with "was"/"were" to match.
    /// </summary>
    private sealed class Sentences
    {
        private readonly StoryWriter _w;
        private readonly Person _p;
        private bool _named, _they, _open;

        public Sentences(StoryWriter w, Person p) { _w = w; _p = p; }

        public List<Run> Runs { get; } = new();
        public bool Named => _named;

        /// <summary>"was" or "were" for the subject just written.</summary>
        public string Be => _they ? "were" : "was";
        /// <summary>The verb for the subject about to be written by <see cref="Begin"/> (non-possessive).</summary>
        public string BeNext => _named && _p.Sex == Sex.Unknown ? "were" : "was";
        public string His => ReportText.Possessive(_p.Sex);

        public void Begin(bool possessive = false)
        {
            if (Runs.Count > 0) Runs.Add(new Run(" "));
            _open = true;
            if (!_named)
            {
                _named = true;
                _they = false;
                _w.Mention(_p);
                Runs.Add(Run.B(possessive ? ReportText.Possessive(_p.DisplayName) : _p.DisplayName));
                Runs.AddRange(_w.Refs(_p.Citations));
                return;
            }
            _they = !possessive && _p.Sex == Sex.Unknown;
            Runs.Add(new Run(possessive ? Capital(ReportText.Possessive(_p.Sex)) : ReportText.Subject(_p.Sex)));
        }

        /// <summary>Starts a sentence with other words ("They divorced").</summary>
        public void Raw(string text)
        {
            if (Runs.Count > 0) Runs.Add(new Run(" "));
            _open = true;
            Runs.Add(new Run(text));
        }

        public Sentences Text(string text) { Runs.Add(new Run(text)); return this; }
        public void Add(IEnumerable<Run> runs) => Runs.AddRange(runs);

        public void People(IReadOnlyList<Person> people)
        {
            for (var i = 0; i < people.Count; i++)
            {
                if (i > 0) Text(i == people.Count - 1 ? " and " : ", ");
                Add(_w.Name(people[i], self: _p));
            }
        }

        /// <summary>" on 14 March 1842 in Harrow" plus its note references.</summary>
        public Sentences When(Event e, string placeWord = "in")
        {
            var date = ReportText.DatePhrase(e.Date);
            if (date.Length > 0) Text(" " + date);
            if (e.Place.Trim().Length > 0) Text($" {placeWord} {e.Place.Trim()}");
            Add(_w.Refs(e.Citations));
            return this;
        }

        public void End()
        {
            if (!_open) return;
            _open = false;
            // put the full stop before trailing note references, as books do: "...England.¹"
            var refs = 0;
            while (refs < Runs.Count && Runs[^(refs + 1)].NoteNumber != null) refs++;
            var at = Runs.Count - refs;
            if (at == 0 || !Runs[at - 1].Text.EndsWith('.')) Runs.Insert(at, new Run("."));
        }

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
