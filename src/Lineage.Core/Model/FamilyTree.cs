namespace Lineage.Core.Model;

/// <summary>
/// The whole tree in memory, with id-indexed collections and fast lookups.
/// Keeping everything in memory is deliberate: even 100k people fit comfortably and every view
/// (family, pedigree, lists, search) becomes a dictionary lookup instead of a query.
/// </summary>
public sealed class FamilyTree
{
    public string Name { get; set; } = "Untitled";
    public Dictionary<long, Person> People { get; } = new();
    public Dictionary<long, Family> Families { get; } = new();
    public Dictionary<long, Source> Sources { get; } = new();
    public Dictionary<long, Repository> Repositories { get; } = new();
    public Dictionary<long, ResearchItem> Research { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Pairs the user said are different people ("12:40", lower id first); duplicate search skips them.</summary>
    public HashSet<string> NotDuplicates { get; } = new();
    /// <summary>The person the tree opens on and the Home page starts from (often the user).</summary>
    public long? HomePersonId { get; set; }
    /// <summary>People the user starred for quick access, in the order they were added.</summary>
    public List<long> Bookmarks { get; } = new();

    public static string PairKey(long a, long b) => a < b ? $"{a}:{b}" : $"{b}:{a}";

    private long _nextPerson = 1, _nextFamily = 1, _nextSource = 1, _nextRepo = 1, _nextResearch = 1;

    public Person AddPerson(Person p) => Add(People, p, ref _nextPerson);
    public Family AddFamily(Family f) => Add(Families, f, ref _nextFamily);
    public Source AddSource(Source s) => Add(Sources, s, ref _nextSource);
    public Repository AddRepository(Repository r) => Add(Repositories, r, ref _nextRepo);
    public ResearchItem AddResearch(ResearchItem r) => Add(Research, r, ref _nextResearch);

    private static T Add<T>(Dictionary<long, T> map, T item, ref long next) where T : Entity
    {
        if (item.Id <= 0) item.Id = next;
        map[item.Id] = item;
        if (item.Id >= next) next = item.Id + 1;
        return item;
    }

    public Person? Person(long? id) => id.HasValue && People.TryGetValue(id.Value, out var p) ? p : null;
    public Family? Family(long? id) => id.HasValue && Families.TryGetValue(id.Value, out var f) ? f : null;

    /// <summary>Father and mother from the primary parent family (by GEDCOM HUSB/WIFE slot).</summary>
    public (Person? Father, Person? Mother) PrimaryParents(Person p) =>
        Family(p.ParentFamilyIds.FirstOrDefault()) is { } f ? (Person(f.Partner1Id), Person(f.Partner2Id)) : (null, null);

    /// <summary>
    /// Ancestors by Ahnentafel number: 1 is the person, 2n the father of n, 2n+1 the mother.
    /// Pedigree charts, fan charts and ancestor reports all read this.
    /// </summary>
    public Dictionary<int, Person> Ancestors(Person root, int generations)
    {
        var result = new Dictionary<int, Person> { [1] = root };
        var max = (1 << generations) - 1;
        for (var n = 1; n <= max / 2; n++)
        {
            if (!result.TryGetValue(n, out var p)) continue;
            var (father, mother) = PrimaryParents(p);
            if (father != null) result[2 * n] = father;
            if (mother != null) result[2 * n + 1] = mother;
        }
        return result;
    }

    /// <summary>The person's families as a partner, each with the spouse and children.</summary>
    public IEnumerable<(Family Family, Person? Spouse, List<Person> Children)> FamiliesOf(Person p) =>
        p.SpouseFamilyIds.Select(id => Family(id)).OfType<Family>()
         .Select(f => (f, SpouseIn(f, p), ChildrenOf(f).ToList()));

    /// <summary>
    /// Everything an edit to this person can change: the person, their families, and everyone in
    /// those families (whose family lists may change too). Undo snapshots exactly this.
    /// </summary>
    public IEnumerable<Entity> Around(Person p)
    {
        var seen = new HashSet<Entity> { p };
        foreach (var f in p.ParentFamilyIds.Concat(p.SpouseFamilyIds).Select(id => Family(id)).OfType<Family>())
        {
            seen.Add(f);
            foreach (var id in f.PartnerIds.Concat(f.Children.Select(c => c.ChildId)))
                if (Person(id) is { } other) seen.Add(other);
        }
        return seen;
    }

    public IEnumerable<Person> ParentsOf(Person p) =>
        p.ParentFamilyIds.Select(id => Family(id)).Where(f => f != null)
         .SelectMany(f => f!.PartnerIds).Select(id => Person(id)).Where(x => x != null)!;

    public IEnumerable<Person> ChildrenOf(Family f) =>
        f.Children.Select(c => Person(c.ChildId)).Where(x => x != null)!;

    public Person? SpouseIn(Family f, Person p) =>
        Person(f.Partner1Id == p.Id ? f.Partner2Id : f.Partner1Id);

    /// <summary>Links a child into a family on both sides.</summary>
    public void AddChild(Family f, Person child, ChildLink? link = null)
    {
        if (f.Children.All(c => c.ChildId != child.Id))
            f.Children.Add(link ?? new ChildLink { ChildId = child.Id });
        if (!child.ParentFamilyIds.Contains(f.Id)) child.ParentFamilyIds.Add(f.Id);
    }

    /// <summary>Sets a partner on a family and records the family on the person.</summary>
    public void SetPartner(Family f, Person p, bool second)
    {
        if (second) f.Partner2Id = p.Id; else f.Partner1Id = p.Id;
        if (!p.SpouseFamilyIds.Contains(f.Id)) p.SpouseFamilyIds.Add(f.Id);
    }

    /// <summary>Removes a person and every link to them.</summary>
    public void RemovePerson(Person p)
    {
        foreach (var f in Families.Values)
        {
            if (f.Partner1Id == p.Id) f.Partner1Id = null;
            if (f.Partner2Id == p.Id) f.Partner2Id = null;
            f.Children.RemoveAll(c => c.ChildId == p.Id);
        }
        People.Remove(p.Id);
    }

    /// <summary>Removes a source and every citation of it.</summary>
    public void RemoveSource(Source s)
    {
        foreach (var c in CitationLists()) c.RemoveAll(x => x.SourceId == s.Id);
        Sources.Remove(s.Id);
    }

    /// <summary>Every citation list in the tree (people, families and their events).</summary>
    public IEnumerable<List<Citation>> CitationLists()
    {
        foreach (var p in People.Values)
        {
            yield return p.Citations;
            foreach (var e in p.Events) yield return e.Citations;
        }
        foreach (var f in Families.Values)
        {
            yield return f.Citations;
            foreach (var e in f.Events) yield return e.Citations;
        }
    }

    /// <summary>People and families that cite a source (directly or on one of their events).</summary>
    public IEnumerable<Entity> CitersOf(long sourceId)
    {
        foreach (var p in People.Values)
            if (p.Citations.Concat(p.Events.SelectMany(e => e.Citations)).Any(c => c.SourceId == sourceId)) yield return p;
        foreach (var f in Families.Values)
            if (f.Citations.Concat(f.Events.SelectMany(e => e.Citations)).Any(c => c.SourceId == sourceId)) yield return f;
    }

    /// <summary>Case-insensitive search over all names; matches every word of the query.</summary>
    public IEnumerable<Person> Search(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return People.Values;
        return People.Values.Where(p => words.All(w =>
            p.Names.Any(n => n.Full.Contains(w, StringComparison.OrdinalIgnoreCase))));
    }
}
