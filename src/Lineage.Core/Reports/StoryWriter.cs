using System.Globalization;
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
        // A source with a template is cited in full the first time, then in its short form.
        if (_tree.Sources.GetValueOrDefault(c.SourceId) is { Template.Length: > 0 } s && SourceIds.Contains(s.Id)
            && Lineage.Core.Sources.SourceTemplates.ShortNote(s, c.Page) is { Length: > 0 } brief)
            text = EndSentence(brief);
        _doc.Notes.Add(new Note(n, text));
        _numbers[key] = n;
        if (_tree.Sources.ContainsKey(c.SourceId) && !SourceIds.Contains(c.SourceId)) SourceIds.Add(c.SourceId);
        return n;
    }

    /// <summary>"Church of England, Parish registers of St Mary, Harrow (Harrow, 1842), Baptisms 1842, entry 118."</summary>
    public static string CitationText(FamilyTree tree, Citation c)
    {
        var s = tree.Sources.GetValueOrDefault(c.SourceId);
        if (s is { Template.Length: > 0 } && Lineage.Core.Sources.SourceTemplates.FullNote(s, c.Page) is { Length: > 0 } full) return EndSentence(full);
        var title = s == null ? "" : s.Title.Trim().Length > 0 ? s.Title.Trim() : s.ShortTitle.Trim();
        if (s != null && s.Publication.Trim().Length > 0) title = title.Length > 0 ? $"{title} ({s.Publication.Trim()})" : s.Publication.Trim();
        var parts = new[] { s?.Author.Trim() ?? "", title, c.Page.Trim() }.Where(x => x.Length > 0).ToList();
        if (parts.Count == 0 && c.Text.Trim().Length > 0) parts.Add($"“{c.Text.Trim()}”");
        return parts.Count == 0 ? "" : EndSentence(string.Join(", ", parts));
    }

    /// <summary>A bibliography line: "Author. Title. Publication. Held at Repository."</summary>
    public static string Bibliography(FamilyTree tree, Source s)
    {
        if (s.Template.Length > 0 && Lineage.Core.Sources.SourceTemplates.Bibliography(s) is { Length: > 0 } entry) return EndSentence(entry);
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
            if (showNumber && entry.Number > 0) yield return new Run($" ({entry.Number})");
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
        foreach (var n in citations.Select(_notes.Cite).OfType<int>().Distinct().OrderBy(x => x))
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
            var who = s.Subject();
            var when = s.When(birth);
            s.Say(parents.Count > 0 ? "born.parents" : "born", ("S", who), ("W", when), ("P", s.People(parents)));
        }
        else if (parents.Count > 0)
        {
            var who = s.Subject();
            s.Say("child", ("S", who), ("P", s.People(parents)));
        }

        foreach (var tag in new[] { "CHR", "BAPM" })
            if (Find(p, tag) is { } e && Has(e))
            {
                var who = s.Subject();
                s.Say(tag == "CHR" ? "christened" : "baptized", ("S", who), ("W", s.When(e)));
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
        if (!s.Named) s.Say("name", ("S", s.Subject()));   // nothing recorded: the name alone
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
            var who = s.Subject();
            if (family.Status == CoupleStatus.Unmarried && marriage == null && spouse != null)
                s.Say("partners", ("S", who), ("X", Name(spouse, self: p).ToList()));
            else
            {
                var nth = count > 1 ? Text(Lang.Format("married.nth", Lang.Ordinal(i))) : new List<Run>();
                if (spouse != null)
                {
                    var name = Name(spouse, self: p).ToList();
                    var about = new List<Run>();
                    var (sf, sm) = _tree.PrimaryParents(spouse);
                    var sp = new[] { sf, sm }.OfType<Person>().ToList();
                    if (sp.Count > 0 && EntryOf(spouse) == null)
                    {
                        about = Fill(Lang.Template("spouse.parents", StoryLanguage.Form(spouse.Sex)), ("P", s.People(sp)));
                    }
                    var comma = about.Count > 0 && marriage != null && Has(marriage) ? Text(",") : new List<Run>();
                    s.Say("married", ("S", who), ("O", nth), ("X", name), ("XP", about), ("K", comma), ("W", marriage == null ? new() : s.When(marriage)));
                }
                else s.Say("married.alone", ("S", who), ("O", nth), ("W", s.When(marriage!)));
            }

            var divorce = Visible(family.Events).FirstOrDefault(e => e.Tag is "DIV" or "ANUL");
            if (divorce != null || family.Status == CoupleStatus.Divorced)
                s.Say(divorce?.Tag == "ANUL" ? "annulled" : "divorced", ("W", divorce == null ? new() : s.When(divorce)));

            if (spouseDetails && spouse != null && EntryOf(spouse) == null) SpouseSentence(s, spouse);
        }
    }

    private void SpouseSentence(Sentences s, Person spouse)
    {
        var born = Find(spouse, "BIRT") ?? Find(spouse, "CHR");
        var died = Find(spouse, "DEAT");
        var buried = Find(spouse, "BURI");
        var form = StoryLanguage.Form(spouse.Sex);
        var clauses = new List<Func<List<Run>>>();
        var given = spouse.PrimaryName.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? spouse.DisplayName;
        if (born != null && Has(born))
            clauses.Add(() => Fill(Lang.Template(born.Tag == "CHR" ? "clause.christened" : "clause.born", form), ("W", s.When(born))));
        if (died != null)
            clauses.Add(() =>
            {
                var when = s.When(died);
                var age = Lang.Age(Find(spouse, "BIRT") ?? born, died.Date);
                return Fill(Lang.Template("clause.died", form), ("W", when), ("A", age.Length > 0 ? Text(Lang.Format("died.age", age)) : new()));
            });
        else if (buried != null && Has(buried)) clauses.Add(() => Fill(Lang.Template("clause.buried", form), ("W", s.When(buried))));
        if (clauses.Count == 0) return;
        var runs = new List<Run> { new(given) };
        for (var i = 0; i < clauses.Count; i++)
        {
            if (i > 0) runs.Add(new Run(Lang.Template("clause.and")));
            runs.AddRange(clauses[i]());
        }
        s.Sentence(runs);
    }

    private void Occupation(Sentences s, List<Event> jobs, bool first)
    {
        var what = ReportText.LowerFirst(Value(jobs[0]));
        var places = jobs.Select(j => j.Place.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        var dates = jobs.Select(j => Lang.Date(j.Date)).Where(x => x.Length > 0).Distinct().ToList();
        var who = s.Subject();
        var job = new List<Run>();
        if (what.Length > 0) job.Add(new Run(Lang.As(what)));
        if (places.Count == 1) job.Add(new Run(Lang.Format("place.in", places[0])));
        if (dates.Count > 0) job.Add(new Run($" ({string.Join(", ", dates)})"));
        job.AddRange(Refs(jobs.SelectMany(j => j.Citations)));
        s.Say(first ? "work" : "work.also", ("S", who), ("J", job));
    }

    /// <summary>"He lived in Leeds (1871), York (1881) and Boston (1890)."</summary>
    private void Gathered(Sentences s, List<Event> events)
    {
        var bits = new List<(string Text, IEnumerable<Citation> Cites)>();
        foreach (var e in events)
        {
            var where = e.Place.Trim().Length > 0 ? e.Place.Trim() : Value(e);
            var when = Lang.Date(e.Date);
            if (e.Tag == "CENS")
            {
                var text = when.Length > 0 ? (where.Length > 0 ? $"{when} ({where})" : when) : where;
                if (text.Length > 0) bits.Add((text, e.Citations));
            }
            else if (where.Length > 0) bits.Add((when.Length > 0 ? $"{where} ({when})" : where, e.Citations));
        }
        if (bits.Count == 0) return;
        var who = s.Subject();
        var list = new List<Run>();
        for (var i = 0; i < bits.Count; i++)
        {
            if (i > 0) list.Add(new Run(i == bits.Count - 1 ? Lang.Template("and") : ", "));
            list.Add(new Run(bits[i].Text));
            list.AddRange(Refs(bits[i].Cites));
        }
        s.Say(events[0].Tag == "CENS" ? "census" : "lived", ("S", who), ("L", list));
    }

    private static readonly HashSet<string> Verbs = new()
        { "ADOP", "CONF", "FCOM", "ORDN", "BARM", "BASM", "BLES", "CHRA", "EMIG", "IMMI", "NATU", "EDUC", "GRAD", "RETI", "WILL", "MILI", "PROP" };

    private void Other(Sentences s, Event e)
    {
        var value = Value(e);
        var lowered = ReportText.LowerFirst(value);
        var id = Verbs.Contains(e.Tag)
            ? (value.Length > 0 && e.Tag is "EDUC" or "GRAD" or "MILI" or "PROP" ? $"ev.{e.Tag}.v" : $"ev.{e.Tag}")
            : value.Length > 0 && e.Tag is "DSCR" or "NCHI" ? $"ev.{e.Tag}.v" : null;
        var place = e.Tag switch { "EMIG" => "place.from", "IMMI" => "place.to", _ => "place.in" };
        if (id != null)
        {
            var who = s.Subject();
            s.Say(id, ("S", who), ("V", Text(e.Tag == "DSCR" ? lowered : value)), ("W", s.When(e, place)));
            return;
        }
        var first = !s.Named;
        var (whose, plain) = s.PossessiveSubject();
        var label = e.Tag switch { "PROB" => "Will", "RELI" => "Religion", "NATI" => "Nationality", "CAST" => "Caste", _ => EventTypes.Label(e) };
        var recordedAs = e.Tag is "RELI" or "NATI" or "CAST" && value.Length > 0;
        var shown = recordedAs ? Text(value) : value.Length > 0 && e.Tag != "DSCR" ? Text(Lang.Format("value", value)) : new List<Run>();
        id = recordedAs ? "ev.recorded.as" : e.Tag == "PROB" ? "ev.PROB" : "ev.recorded";
        // Languages that word these without "his"/"her" name the person in a form of their own the first time.
        s.Say(first && Lang.Has(id + ".named") ? id + ".named" : id,
              ("S", whose), ("N", plain), ("L", Text(Lang.Noun(label))), ("V", shown), ("W", s.When(e, place)));
    }

    private void Ending(Sentences s, Person p, Event? birth)
    {
        var death = Find(p, "DEAT");
        var burial = Find(p, "BURI") ?? Find(p, "CREM");
        if (death == null && (burial == null || !Has(burial))) return;
        var who = s.Subject();
        var form = s.Form;
        var buriedId = burial?.Tag == "CREM" ? "cremated" : "buried";
        if (death != null)
        {
            var cause = death.Cause.Trim().Length > 0 ? Text(Lang.Format("died.of", ReportText.LowerFirst(death.Cause.Trim()))) : new List<Run>();
            var when = s.When(death);
            var age = Lang.Age(birth ?? Find(p, "CHR") ?? Find(p, "BAPM"), death.Date);
            var after = burial != null && Has(burial) ? Fill(Lang.Template("died." + buriedId, form), ("W", s.When(burial))) : new List<Run>();
            s.Say("died", ("S", who), ("C", cause), ("W", when), ("A", age.Length > 0 ? Text(Lang.Format("died.age", age)) : new()), ("B", after));
        }
        else s.Say(buriedId, ("S", who), ("W", s.When(burial!)));
    }

    // ---- templates -------------------------------------------------------------------------------

    private StoryLanguage Lang => Language;

    /// <summary>The language the story is written in (normally <see cref="StoryLanguage.Current"/>).</summary>
    public StoryLanguage Language { get; set; } = StoryLanguage.Current;

    private static List<Run> Text(string text) => text.Length == 0 ? new() : new() { new Run(text) };

    private static readonly System.Text.RegularExpressions.Regex Gap = new(@"\{([A-Z]+)\}");

    /// <summary>A template with its gaps filled by runs (names stay links, note references stay references).</summary>
    private static List<Run> Fill(string template, params (string Name, List<Run> Runs)[] slots)
    {
        var result = new List<Run>();
        var at = 0;
        foreach (System.Text.RegularExpressions.Match m in Gap.Matches(template))
        {
            if (m.Index > at) result.Add(new Run(template[at..m.Index]));
            foreach (var (name, runs) in slots)
                if (name == m.Groups[1].Value) { result.AddRange(runs); break; }
            at = m.Index + m.Length;
        }
        if (at < template.Length) result.Add(new Run(template[at..]));
        return result;
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
    /// later ones use he/she/they, and <see cref="Form"/> picks the template forms to match.
    /// </summary>
    private sealed class Sentences
    {
        private readonly StoryWriter _w;
        private readonly Person _p;
        private bool _named, _they;

        public Sentences(StoryWriter w, Person p) { _w = w; _p = p; }

        public List<Run> Runs { get; } = new();
        public bool Named => _named;

        /// <summary>The template form for the subject just written: "m", "f", "u", or "they" after a plain "they".</summary>
        public string Form => _they ? "they" : StoryLanguage.Form(_p.Sex);

        /// <summary>The person as the subject: their name the first time, then he/she/they.</summary>
        public List<Run> Subject()
        {
            if (!_named)
            {
                _named = true;
                _they = false;
                _w.Mention(_p);
                return new List<Run> { Run.B(_p.DisplayName) }.Concat(_w.Refs(_p.Citations)).ToList();
            }
            _they = _p.Sex == Sex.Unknown;
            return new() { new Run(_w.Lang.Subject(_p.Sex)) };
        }

        /// <summary>"Thomas Whitfield’s" or "his" ({S}), with the plain form ({N}) for languages that word it differently.</summary>
        public (List<Run> Whose, List<Run> Plain) PossessiveSubject()
        {
            _they = false;
            if (!_named)
            {
                _named = true;
                _w.Mention(_p);
                var refs = _w.Refs(_p.Citations).ToList();
                return (new List<Run> { Run.B(_w.Lang.Possessive(_p.DisplayName)) }.Concat(refs).ToList(),
                        new List<Run> { Run.B(_p.DisplayName) }.Concat(refs).ToList());
            }
            return (new() { new Run(_w.Lang.PossessivePronoun(_p.Sex)) }, new() { new Run(_w.Lang.Subject(_p.Sex)) });
        }

        /// <summary>"George Whitfield and Mary Ann Cole", each name linked when they have an entry.</summary>
        public List<Run> People(IReadOnlyList<Person> people)
        {
            var runs = new List<Run>();
            for (var i = 0; i < people.Count; i++)
            {
                if (i > 0) runs.Add(new Run(i == people.Count - 1 ? _w.Lang.Template("and") : ", "));
                runs.AddRange(_w.Name(people[i], self: _p));
            }
            return runs;
        }

        /// <summary>" on 14 March 1842 in Harrow" plus its note references.</summary>
        public List<Run> When(Event e, string placeId = "place.in")
        {
            var runs = new List<Run>();
            var date = _w.Lang.DatePhrase(e.Date);
            if (date.Length > 0) runs.Add(new Run(" " + date));
            if (e.Place.Trim().Length > 0) runs.Add(new Run(_w.Lang.Format(placeId, e.Place.Trim())));
            runs.AddRange(_w.Refs(e.Citations));
            return runs;
        }

        /// <summary>Writes the sentence <paramref name="id"/> with its gaps filled.</summary>
        public void Say(string id, params (string Name, List<Run> Runs)[] slots) => Sentence(Fill(_w.Lang.Template(id, Form), slots), capital: true);

        /// <summary>Adds a sentence and its full stop, which goes before trailing note references, as books do: "...England.¹"</summary>
        public void Sentence(List<Run> runs, bool capital = false)
        {
            runs = runs.Where(r => r.NoteNumber != null || r.Text.Length > 0).ToList();
            if (runs.Count == 0) return;
            // Languages that leave out "he"/"she" start the sentence with the verb: "Se casó con…".
            if (capital && runs[0] is { Bold: false, Link: null, NoteNumber: null } first && first.Text.TrimStart() is { Length: > 0 } text)
                runs[0] = first with { Text = char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..] };
            if (Runs.Count > 0) Runs.Add(new Run(" "));
            Runs.AddRange(runs);
            var refs = 0;
            while (refs < Runs.Count && Runs[^(refs + 1)].NoteNumber != null) refs++;
            var at = Runs.Count - refs;
            if (at == 0 || !Runs[at - 1].Text.EndsWith('.')) Runs.Insert(at, new Run("."));
        }
    }
}
