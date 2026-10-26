using System.Globalization;
using System.Text;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Analysis;

/// <summary>How two people from different trees were matched.</summary>
public enum MatchKind { SameRecord, SameFamilySearchId, Likely }

/// <summary>One fact that differs between my copy of a person and the other tree's.</summary>
public sealed record FieldDifference(string Key, string Label, string Mine, string Theirs);

/// <summary>A person in my tree and the person in the other tree we believe is the same.</summary>
/// <param name="Score">0–100; 100 for matches by record or FamilySearch ID.</param>
/// <param name="Differences">Only the facts that differ (one side may be empty).</param>
public sealed record PersonMatch(Person Mine, Person Other, int Score, MatchKind Kind, IReadOnlyList<FieldDifference> Differences)
{
    /// <summary>How the match was made, for display.</summary>
    public string How => Kind switch
    {
        MatchKind.SameRecord => "Same record",
        MatchKind.SameFamilySearchId => "Same FamilySearch ID",
        _ => "Likely match",
    };
}

/// <summary>The outcome of <see cref="TreeCompare.Compare"/>: who is in both trees and who is in only one.</summary>
public sealed class CompareResult
{
    private readonly Dictionary<long, PersonMatch> _byMine = new();
    private readonly Dictionary<long, PersonMatch> _byOther = new();

    public CompareResult(IEnumerable<PersonMatch> matches, IEnumerable<Person> onlyInOther, IEnumerable<Person> onlyInMine)
    {
        Matches = matches.ToList();
        OnlyInOther = onlyInOther.OrderBy(p => p.PrimaryName.SortKey).ThenBy(p => p.Id).ToList();
        OnlyInMine = onlyInMine.OrderBy(p => p.PrimaryName.SortKey).ThenBy(p => p.Id).ToList();
        foreach (var m in Matches)
        {
            _byMine[m.Mine.Id] = m;
            _byOther[m.Other.Id] = m;
        }
    }

    public IReadOnlyList<PersonMatch> Matches { get; }
    public IReadOnlyList<Person> OnlyInOther { get; }
    public IReadOnlyList<Person> OnlyInMine { get; }

    public PersonMatch? ForMine(long mineId) => _byMine.GetValueOrDefault(mineId);
    public PersonMatch? ForOther(long otherId) => _byOther.GetValueOrDefault(otherId);

    /// <summary>The same result with a match the user rejected: both people become "only in" one tree.</summary>
    public CompareResult Without(PersonMatch rejected) =>
        new(Matches.Where(m => m != rejected),
            OnlyInOther.Append(rejected.Other), OnlyInMine.Append(rejected.Mine));
}

/// <summary>
/// Compares two trees (for example a cousin's GEDCOM against yours) and copies chosen people
/// across. People are matched first by record id (_UID — both trees came from the same file),
/// then by FamilySearch ID, then by how alike they are: names, birth and death years, birthplace,
/// and the names of parents and spouses. Each person matches at most one other, best match first.
/// Candidate pairs are grouped by the sound of the surname and the first initial, so even two
/// 15,000-person trees compare in seconds.
/// </summary>
public static class TreeCompare
{
    public const int DefaultMinScore = 60;

    public static CompareResult Compare(FamilyTree mine, FamilyTree other, int minScore = DefaultMinScore)
    {
        var matches = new List<PersonMatch>();
        var usedMine = new HashSet<long>();
        var usedOther = new HashSet<long>();
        void Add(Person a, Person b, int score, MatchKind kind)
        {
            usedMine.Add(a.Id);
            usedOther.Add(b.Id);
            matches.Add(new PersonMatch(a, b, score, kind, Differences(mine, a, other, b)));
        }

        var byUid = new Dictionary<Guid, Person>();
        foreach (var p in mine.People.Values) byUid.TryAdd(p.Uid, p);
        foreach (var o in other.People.Values)
            if (byUid.TryGetValue(o.Uid, out var m) && !usedMine.Contains(m.Id)) Add(m, o, 100, MatchKind.SameRecord);

        var byFs = mine.People.Values
            .Where(p => !usedMine.Contains(p.Id) && FsKey(p).Length > 0)
            .GroupBy(FsKey).ToDictionary(g => g.Key, g => new Queue<Person>(g));
        foreach (var o in other.People.Values)
            if (!usedOther.Contains(o.Id) && FsKey(o) is { Length: > 0 } key &&
                byFs.TryGetValue(key, out var queue) && queue.TryDequeue(out var m))
                Add(m, o, 100, MatchKind.SameFamilySearchId);

        // Likely matches among the rest.
        var blocks = new Dictionary<string, List<Facts>>();
        foreach (var p in mine.People.Values)
        {
            if (usedMine.Contains(p.Id)) continue;
            var f = new Facts(mine, p);
            foreach (var k in f.Keys)
            {
                if (!blocks.TryGetValue(k, out var list)) blocks[k] = list = new List<Facts>();
                list.Add(f);
            }
        }
        var candidates = new List<(int Score, Facts A, Facts B)>();
        var seen = new HashSet<Facts>();
        foreach (var o in other.People.Values)
        {
            if (usedOther.Contains(o.Id)) continue;
            var b = new Facts(other, o);
            seen.Clear();
            foreach (var k in b.Keys)
            {
                if (!blocks.TryGetValue(k, out var list)) continue;
                foreach (var a in list)
                    if (seen.Add(a) && Score(a, b) is { } s && s >= minScore) candidates.Add((s, a, b));
            }
        }
        foreach (var (score, a, b) in candidates.OrderByDescending(c => c.Score)
                     .ThenBy(c => c.A.Person.Id).ThenBy(c => c.B.Person.Id))
            if (!usedMine.Contains(a.Person.Id) && !usedOther.Contains(b.Person.Id))
                Add(a.Person, b.Person, score, MatchKind.Likely);

        return new CompareResult(
            matches.OrderBy(m => m.Mine.PrimaryName.SortKey).ThenBy(m => m.Mine.Id),
            other.People.Values.Where(p => !usedOther.Contains(p.Id)),
            mine.People.Values.Where(p => !usedMine.Contains(p.Id)));
    }

    private static string FsKey(Person p) => p.FamilySearchId.Trim().ToUpperInvariant();

    // ---- Scoring ----

    /// <summary>What we compare about a person, worked out once so each pair is cheap to score.</summary>
    private sealed class Facts
    {
        public readonly Person Person;
        public readonly (string[] Given, string Surname, string Sound)[] Names;
        public readonly List<string> Keys = new();
        public readonly GenDate? Birth, Death;
        public readonly string BirthPlace, Father, Mother;
        public readonly string[] Spouses;
        public readonly string FsId;

        public Facts(FamilyTree tree, Person p)
        {
            Person = p;
            Names = p.Names.Select(n => (Words(n.Given), Duplicates.Simplify(n.Surname), Duplicates.Soundex(n.Surname)))
                           .Where(n => n.Item1.Length > 0 && n.Item3.Length > 0).ToArray();
            foreach (var n in Names)
            {
                var key = n.Sound + n.Given[0][0];
                if (!Keys.Contains(key)) Keys.Add(key);
            }
            Birth = p.Birth?.Date;
            Death = p.Death?.Date;
            BirthPlace = Duplicates.Simplify((p.Birth?.Place ?? "").Split(',')[0]);
            var (father, mother) = tree.PrimaryParents(p);
            Father = FirstGiven(father);
            Mother = FirstGiven(mother);
            Spouses = p.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>()
                       .Select(f => FirstGiven(tree.SpouseIn(f, p))).Where(s => s.Length > 0).Distinct().ToArray();
            FsId = FsKey(p);
        }

        private static string FirstGiven(Person? p) => p == null ? "" : Words(p.PrimaryName.Given) is [var first, ..] ? first : "";
    }

    private static string[] Words(string s) => Duplicates.Simplify(s).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// How alike two people from different trees are, 0–100, or null when they can't be the same
    /// person (different sex, unrelated names, birth or death years more than 10 apart).
    /// Mirrors <see cref="Duplicates.Score"/>, which only works within one tree.
    /// </summary>
    private static int? Score(Facts a, Facts b)
    {
        if (a.Person.Sex != Sex.Unknown && b.Person.Sex != Sex.Unknown && a.Person.Sex != b.Person.Sex) return null;

        int? names = null;
        foreach (var na in a.Names)
            foreach (var nb in b.Names)
                if (NameScore(na, nb) is { } s && (names == null || s > names)) names = s;
        if (names is not { } score) return null;

        score += Years(a.Birth, b.Birth, 25, out var conflict);
        if (conflict) return null;
        score += Years(a.Death, b.Death, 15, out conflict);
        if (conflict) return null;

        if (a.BirthPlace.Length > 0 && a.BirthPlace == b.BirthPlace) score += 10;
        if (a.Father.Length > 0 && a.Father == b.Father) score += 10;
        if (a.Mother.Length > 0 && a.Mother == b.Mother) score += 10;
        if (a.Spouses.Any(s => b.Spouses.Contains(s))) score += 15;
        // Both linked to FamilySearch, but to different people there.
        if (a.FsId.Length > 0 && b.FsId.Length > 0 && a.FsId != b.FsId) score -= 20;
        return Math.Clamp(score, 0, 100);
    }

    private static int? NameScore((string[] Given, string Surname, string Sound) a, (string[] Given, string Surname, string Sound) b)
    {
        int score;
        if (a.Given.SequenceEqual(b.Given)) score = 30;
        else if (a.Given[0] == b.Given[0]) score = 20;
        else if (a.Given[0][0] == b.Given[0][0] && (a.Given[0].Length == 1 || b.Given[0].Length == 1)) score = 5;
        else return null;
        if (a.Surname == b.Surname) return score + 20;
        return a.Sound == b.Sound ? score + 10 : null;
    }

    private static int Years(GenDate? a, GenDate? b, int weight, out bool conflict)
    {
        conflict = false;
        if (a is not { Year: > 0 } || b is not { Year: > 0 }) return 0;
        var diff = Math.Abs(a.Year - b.Year);
        if (diff == 0) return a.Month > 0 && a.Month == b.Month && a.Day > 0 && a.Day == b.Day ? weight + 5 : weight;
        if (diff <= 2) return weight * 3 / 5;
        if (diff <= 5) return weight / 5;
        conflict = diff > 10;
        return 0;
    }

    // ---- Differences ----

    private static List<FieldDifference> Differences(FamilyTree mine, Person a, FamilyTree other, Person b)
    {
        var list = new List<FieldDifference>();
        void Add(string key, string label, string x, string y)
        {
            if (Loose(x) != Loose(y)) list.Add(new FieldDifference(key, label, x, y));
        }
        static string SexText(Person p) => p.Sex == Sex.Unknown ? "" : p.Sex.ToString();

        Add("name", "Name", a.PrimaryName.Full, b.PrimaryName.Full);
        Add("sex", "Sex", SexText(a), SexText(b));
        Add("birth.date", "Birth date", a.Birth?.Date.ToGedcom() ?? "", b.Birth?.Date.ToGedcom() ?? "");
        Add("birth.place", "Birth place", a.Birth?.Place ?? "", b.Birth?.Place ?? "");
        Add("death.date", "Death date", a.Death?.Date.ToGedcom() ?? "", b.Death?.Date.ToGedcom() ?? "");
        Add("death.place", "Death place", a.Death?.Place ?? "", b.Death?.Place ?? "");
        var (fa, ma) = mine.PrimaryParents(a);
        var (fb, mb) = other.PrimaryParents(b);
        Add("father", "Father", fa?.PrimaryName.Full ?? "", fb?.PrimaryName.Full ?? "");
        Add("mother", "Mother", ma?.PrimaryName.Full ?? "", mb?.PrimaryName.Full ?? "");
        Add("spouses", "Spouses", SpouseNames(mine, a), SpouseNames(other, b));
        return list;
    }

    private static string SpouseNames(FamilyTree tree, Person p) => string.Join("; ",
        p.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>()
         .Select(f => tree.SpouseIn(f, p)?.PrimaryName.Full ?? "").Where(n => n.Length > 0)
         .Distinct().OrderBy(Loose, StringComparer.Ordinal));

    /// <summary>Lower case, no accents, letters and digits only, single spaces — so "Leeds,Yorks" equals "leeds, yorks".</summary>
    private static string Loose(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- Import ----

    /// <summary>
    /// Records in my tree that <see cref="Import"/> may change: the matched relatives of the people
    /// being imported, with their families. Pass to <c>UndoHistory.Begin</c> before importing —
    /// the new records are picked up by the undo history automatically.
    /// </summary>
    public static List<Entity> ImportTouched(FamilyTree mine, FamilyTree other, IEnumerable<long> otherIds, CompareResult result)
    {
        var touched = new HashSet<Entity>();
        foreach (var o in Chosen(other, otherIds, result))
            foreach (var f in o.ParentFamilyIds.Concat(o.SpouseFamilyIds).Select(id => other.Family(id)).OfType<Family>())
                foreach (var id in f.PartnerIds.Concat(f.Children.Select(c => c.ChildId)))
                    if (result.ForOther(id) is { } m && mine.People.ContainsKey(m.Mine.Id))
                        touched.UnionWith(mine.Around(m.Mine));
        return touched.ToList();
    }

    private static List<Person> Chosen(FamilyTree other, IEnumerable<long> otherIds, CompareResult result) =>
        otherIds.Distinct().Select(id => other.Person(id)).OfType<Person>()
                .Where(p => result.ForOther(p.Id) == null).ToList();

    /// <summary>
    /// Copies the chosen people from the other tree into mine, with new ids. People who already
    /// matched someone in my tree are skipped (they are already here). Family links are recreated
    /// where the relative is matched (linked to my existing person) or imported at the same time;
    /// children of a matched couple join that couple's existing family. Sources they cite are
    /// copied too, unless my tree has the same source (same record id or identical title).
    /// Returns the new people's ids, in the order chosen.
    /// </summary>
    public static List<long> Import(FamilyTree mine, FamilyTree other, IEnumerable<long> otherIds, CompareResult result)
    {
        var chosen = Chosen(other, otherIds, result);
        var sources = new SourceCopier(mine, other);
        var uids = mine.People.Values.Select(p => p.Uid).ToHashSet();
        var copies = new Dictionary<long, Person>();
        foreach (var o in chosen)
        {
            var p = Clone(o);
            p.Id = 0;
            if (!uids.Add(p.Uid)) p.Uid = Guid.NewGuid();
            p.ParentFamilyIds.Clear();
            p.SpouseFamilyIds.Clear();
            p.ResearchIds.Clear();
            sources.Remap(p.Citations);
            foreach (var e in p.Events) sources.Remap(e.Citations);
            copies[o.Id] = mine.AddPerson(p);
        }

        (Person? Person, bool IsNew) Resolve(long? id) =>
            id is not { } x ? (null, false)
            : copies.TryGetValue(x, out var c) ? (c, true)
            : result.ForOther(x) is { } m && mine.People.ContainsKey(m.Mine.Id) ? (m.Mine, false)
            : (null, false);

        var familyUids = mine.Families.Values.Select(f => f.Uid).ToHashSet();
        var families = chosen.SelectMany(o => o.ParentFamilyIds.Concat(o.SpouseFamilyIds)).Distinct()
                             .Select(id => other.Family(id)).OfType<Family>().ToList();
        foreach (var of in families)
        {
            var p1 = Resolve(of.Partner1Id);
            var p2 = Resolve(of.Partner2Id);
            var kids = of.Children.Select(c => (Link: c, Child: Resolve(c.ChildId))).Where(k => k.Child.Person != null).ToList();
            var members = new[] { p1, p2 }.Concat(kids.Select(k => k.Child)).Where(m => m.Person != null).ToList();
            if (members.Count < 2 || !members.Any(m => m.IsNew)) continue;   // nobody new to link

            var target = FindFamily(mine, p1, p2, kids.Select(k => k.Child).Where(k => !k.IsNew).Select(k => k.Person!).ToList());
            var created = target == null;
            var addedPartner = false;
            if (target == null)
            {
                var shell = Clone(of);
                shell.Id = 0;
                if (!familyUids.Add(shell.Uid)) shell.Uid = Guid.NewGuid();
                shell.Partner1Id = shell.Partner2Id = null;
                shell.Children.Clear();
                shell.ResearchIds.Clear();
                sources.Remap(shell.Citations);
                foreach (var e in shell.Events) sources.Remap(e.Citations);
                target = mine.AddFamily(shell);
            }
            if (p1.Person != null) addedPartner |= Place(mine, target, p1.Person, preferSecond: false);
            if (p2.Person != null) addedPartner |= Place(mine, target, p2.Person, preferSecond: true);
            // A couple new to my tree brings its marriage with it.
            if (!created && addedPartner && target.Events.Count == 0)
                foreach (var e in Clone(of).Events)
                {
                    sources.Remap(e.Citations);
                    target.Events.Add(e);
                }
            foreach (var (link, (child, _)) in kids)
            {
                if (target.Children.Any(c => c.ChildId == child!.Id)) continue;
                var copy = new ChildLink
                {
                    ChildId = child!.Id, ToFather = link.ToFather, ToMother = link.ToMother, Status = link.Status,
                    Sealing = link.Sealing == null ? null : CloneEvent(link.Sealing),
                };
                if (copy.Sealing != null) sources.Remap(copy.Sealing.Citations);
                mine.AddChild(target, child, copy);
            }
        }
        return chosen.Select(o => copies[o.Id].Id).ToList();
    }

    /// <summary>
    /// My existing family that the other tree's family corresponds to: the same couple, or the
    /// parents' family of matched children when it has room for the incoming partners.
    /// Null when a new family should be made.
    /// </summary>
    private static Family? FindFamily(FamilyTree mine, (Person? Person, bool IsNew) p1, (Person? Person, bool IsNew) p2,
                                      List<Person> existingKids)
    {
        var partners = new[] { p1, p2 }.Where(p => p.Person != null).ToList();
        var existing = partners.Where(p => !p.IsNew).Select(p => p.Person!.Id).ToList();
        var incoming = partners.Count(p => p.IsNew);
        if (existing.Count == 2)
            return mine.People[existing[0]].SpouseFamilyIds.Select(id => mine.Family(id)).OfType<Family>()
                       .FirstOrDefault(f => f.PartnerIds.Contains(existing[1]));
        if (existingKids.Count == 0) return null;
        return existingKids.SelectMany(k => k.ParentFamilyIds).Distinct().Select(id => mine.Family(id)).OfType<Family>()
            .Where(f => existing.All(id => f.PartnerIds.Contains(id)) &&
                        (f.Partner1Id == null ? 1 : 0) + (f.Partner2Id == null ? 1 : 0) >= incoming)
            .OrderByDescending(f => existingKids.Count(k => f.Children.Any(c => c.ChildId == k.Id)))
            .FirstOrDefault();
    }

    /// <summary>Puts a partner into a family, in their usual slot if it is free. Returns true if they were added.</summary>
    private static bool Place(FamilyTree mine, Family f, Person p, bool preferSecond)
    {
        if (f.PartnerIds.Contains(p.Id))
        {
            if (!p.SpouseFamilyIds.Contains(f.Id)) p.SpouseFamilyIds.Add(f.Id);
            return false;
        }
        bool second;
        if ((preferSecond ? f.Partner2Id : f.Partner1Id) == null) second = preferSecond;
        else if ((preferSecond ? f.Partner1Id : f.Partner2Id) == null) second = !preferSecond;
        else return false;
        mine.SetPartner(f, p, second);
        return true;
    }

    private static T Clone<T>(T record) where T : Entity => LineageStore.FromJson<T>(LineageStore.ToJson(record));

    private static Event CloneEvent(Event e)
    {
        // Events aren't records, so carry one inside a throwaway person to deep-copy it.
        var holder = new Person();
        holder.Events.Add(e);
        return Clone(holder).Events[0];
    }

    /// <summary>Maps the other tree's sources (and their repositories) to mine, copying the ones I don't have.</summary>
    private sealed class SourceCopier
    {
        private readonly FamilyTree _mine, _other;
        private readonly Dictionary<long, long?> _sources = new(), _repos = new();

        public SourceCopier(FamilyTree mine, FamilyTree other) => (_mine, _other) = (mine, other);

        public void Remap(List<Citation> citations)
        {
            citations.RemoveAll(c => Source(c.SourceId) == null);   // cites a source the other tree doesn't have
            foreach (var c in citations) c.SourceId = Source(c.SourceId)!.Value;
        }

        private long? Source(long otherId)
        {
            if (_sources.TryGetValue(otherId, out var known)) return known;
            long? id = null;
            if (_other.Sources.TryGetValue(otherId, out var s))
            {
                var title = s.Title.Trim();
                var same = _mine.Sources.Values.FirstOrDefault(m => m.Uid == s.Uid)
                    ?? (title.Length == 0 ? null : _mine.Sources.Values.FirstOrDefault(m =>
                           string.Equals(m.Title.Trim(), title, StringComparison.OrdinalIgnoreCase)));
                if (same == null)
                {
                    same = Clone(s);
                    same.Id = 0;
                    if (_mine.Sources.Values.Any(m => m.Uid == same.Uid)) same.Uid = Guid.NewGuid();
                    same.RepositoryId = s.RepositoryId is { } r ? Repository(r) : null;
                    _mine.AddSource(same);
                }
                id = same.Id;
            }
            return _sources[otherId] = id;
        }

        private long? Repository(long otherId)
        {
            if (_repos.TryGetValue(otherId, out var known)) return known;
            long? id = null;
            if (_other.Repositories.TryGetValue(otherId, out var r))
            {
                var name = r.Name.Trim();
                var same = _mine.Repositories.Values.FirstOrDefault(m => m.Uid == r.Uid)
                    ?? (name.Length == 0 ? null : _mine.Repositories.Values.FirstOrDefault(m =>
                           string.Equals(m.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)));
                if (same == null)
                {
                    same = Clone(r);
                    same.Id = 0;
                    if (_mine.Repositories.Values.Any(m => m.Uid == same.Uid)) same.Uid = Guid.NewGuid();
                    _mine.AddRepository(same);
                }
                id = same.Id;
            }
            return _repos[otherId] = id;
        }
    }
}
