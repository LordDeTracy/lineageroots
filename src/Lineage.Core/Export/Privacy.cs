using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Export;

/// <summary>What to do with people who may still be alive when exporting or sharing.</summary>
public enum LivingMode
{
    /// <summary>Export them in full.</summary>
    Include,
    /// <summary>Keep them in the tree as "Living" plus surname, with no details, so family links stay.</summary>
    Privatize,
    /// <summary>Leave them out, with their links.</summary>
    Exclude,
}

/// <summary>Choices for <see cref="Privacy.Filter"/>. The defaults suit sharing a tree with other people.</summary>
public sealed class PrivacyOptions
{
    public LivingMode Living { get; set; } = LivingMode.Privatize;
    /// <summary>Leave out events marked private.</summary>
    public bool ExcludePrivateEvents { get; set; } = true;
    public bool ExcludeNotes { get; set; }
    public bool ExcludeMedia { get; set; }
    /// <summary>Anyone born more than this many years ago is treated as no longer living.</summary>
    public int AssumeDeadAfterYears { get; set; } = 110;
}

/// <summary>
/// Removes private information before a tree is exported or shared. Works on a copy: the tree
/// being edited is never changed, and record ids are kept so exports stay consistent.
/// </summary>
public static class Privacy
{
    private static readonly HashSet<string> DeathTags = new() { "DEAT", "BURI", "CREM", "PROB" };

    /// <summary>
    /// Whether a person may still be alive. The rule, in order:
    /// <list type="number">
    /// <item>Any death, burial, cremation or probate record (dated or not), or a custom obituary
    /// or funeral event, means not living.</item>
    /// <item>With a birth (or christening/baptism) year: living only if born at most
    /// <paramref name="assumeDeadAfterYears"/> (N) years ago.</item>
    /// <item>Without one: not living if any of their own events is more than N years ago; or if a
    /// child was born more than N−15 years ago; a parent born more than N+15 years ago; or a
    /// marriage (or other couple event) more than N−15 years ago. Relatives' "born" years use their
    /// birth year, or failing that their earliest dated event.</item>
    /// <item>Otherwise living — when in doubt, protect.</item>
    /// </list>
    /// </summary>
    public static bool IsLiving(FamilyTree tree, Person person, int assumeDeadAfterYears, int currentYear)
    {
        if (HasDeathEvidence(person)) return false;
        var n = assumeDeadAfterYears;
        bool Before(int year, int years) => year > 0 && currentYear - year > years;

        var born = BirthYear(person);
        if (born > 0) return !Before(born, n);
        if (Before(EarliestYear(person), n)) return false;

        foreach (var family in person.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>())
        {
            if (Before(Earliest(family.Events), n - 15)) return false;
            if (tree.ChildrenOf(family).Any(c => Before(KnownYear(c), n - 15))) return false;
        }
        if (tree.ParentsOf(person).Any(p => Before(KnownYear(p), n + 15))) return false;
        return true;
    }

    private static bool HasDeathEvidence(Person p) =>
        p.Events.Any(e => DeathTags.Contains(e.Tag) ||
            (e.Tag == "EVEN" && (e.TypeName.Contains("obituary", StringComparison.OrdinalIgnoreCase) ||
                                 e.TypeName.Contains("funeral", StringComparison.OrdinalIgnoreCase))));

    private static int BirthYear(Person p) =>
        new[] { "BIRT", "CHR", "BAPM" }.Select(t => p.Find(t)?.Date.Year ?? 0).FirstOrDefault(y => y > 0);

    private static int Earliest(IEnumerable<Event> events) =>
        events.Select(e => e.Date.Year).Where(y => y > 0).DefaultIfEmpty(0).Min();

    private static int EarliestYear(Person p) => Earliest(p.Events);

    private static int KnownYear(Person p) => BirthYear(p) is > 0 and var y ? y : EarliestYear(p);

    /// <summary>
    /// A filtered deep copy of the tree for export. Living people are kept, privatized ("Living"
    /// plus surname; no events, notes, media, citations, contact details, FamilySearch ID or DNA,
    /// but still linked to their families) or excluded (removed with their links; families left
    /// with no partners and no children are removed too). Couple events of a privatized partner's
    /// family, and a privatized child's sealing, are dropped as well. Sources, repositories and
    /// research items are always kept.
    /// </summary>
    /// <param name="currentYear">The year to judge "living" by; this year when null.</param>
    public static FamilyTree Filter(FamilyTree tree, PrivacyOptions options, int? currentYear = null)
    {
        var year = currentYear ?? DateTime.Today.Year;
        var living = options.Living == LivingMode.Include
            ? new HashSet<long>()
            : tree.People.Values.Where(p => IsLiving(tree, p, options.AssumeDeadAfterYears, year)).Select(p => p.Id).ToHashSet();

        var copy = new FamilyTree { Name = tree.Name };
        foreach (var p in tree.People.Values) copy.AddPerson(Clone(p));
        foreach (var f in tree.Families.Values) copy.AddFamily(Clone(f));
        foreach (var s in tree.Sources.Values) copy.AddSource(Clone(s));
        foreach (var r in tree.Repositories.Values) copy.AddRepository(Clone(r));
        foreach (var r in tree.Research.Values) copy.AddResearch(Clone(r));
        copy.NotDuplicates.UnionWith(tree.NotDuplicates);

        if (options.Living == LivingMode.Exclude) Exclude(copy, living);
        else if (options.Living == LivingMode.Privatize) Privatize(copy, living);
        // DNA match lists name living people (the matches themselves), so they only go out when living people do.
        if (options.Living != LivingMode.Include)
            foreach (var p in copy.People.Values) p.DnaMatches.Clear();

        if (options.ExcludePrivateEvents)
        {
            foreach (var p in copy.People.Values) p.Events.RemoveAll(e => e.Private);
            foreach (var f in copy.Families.Values)
            {
                f.Events.RemoveAll(e => e.Private);
                foreach (var c in f.Children)
                    if (c.Sealing is { Private: true }) c.Sealing = null;
            }
        }
        if (options.ExcludeNotes) RemoveNotes(copy);
        if (options.ExcludeMedia)
        {
            foreach (var p in copy.People.Values) p.Media.Clear();
            foreach (var f in copy.Families.Values) f.Media.Clear();
        }
        return copy;
    }

    private static T Clone<T>(T record) where T : Entity => LineageStore.FromJson<T>(LineageStore.ToJson(record));

    private static void Privatize(FamilyTree tree, HashSet<long> living)
    {
        foreach (var id in living)
        {
            var p = tree.People[id];
            var surname = p.PrimaryName.Surname;
            p.Names.Clear();
            p.Names.Add(new Name { Given = "Living", Surname = surname });
            p.Events.Clear();
            p.Citations.Clear();
            p.Media.Clear();
            p.DnaTests.Clear();
            p.Tags.Clear();
            p.ResearchIds.Clear();
            p.Note = p.Email = p.Phone = p.Address = p.Url = "";
            p.FamilySearchId = p.Title = p.Description = p.UserId = "";
        }
        foreach (var f in tree.Families.Values)
        {
            // A marriage date and place say as much about a living person as their own events.
            if (f.PartnerIds.Any(living.Contains))
            {
                f.Events.Clear();
                f.Citations.Clear();
                f.Media.Clear();
                f.Note = "";
            }
            foreach (var c in f.Children)
                if (living.Contains(c.ChildId)) c.Sealing = null;
        }
    }

    private static void Exclude(FamilyTree tree, HashSet<long> living)
    {
        foreach (var f in tree.Families.Values)
        {
            if (f.Partner1Id is { } a && living.Contains(a)) f.Partner1Id = null;
            if (f.Partner2Id is { } b && living.Contains(b)) f.Partner2Id = null;
            f.Children.RemoveAll(c => living.Contains(c.ChildId));
        }
        foreach (var id in living) tree.People.Remove(id);

        var empty = tree.Families.Values.Where(f => !f.PartnerIds.Any() && f.Children.Count == 0).Select(f => f.Id).ToHashSet();
        foreach (var id in empty) tree.Families.Remove(id);
        foreach (var p in tree.People.Values)
        {
            p.ParentFamilyIds.RemoveAll(id => !tree.Families.ContainsKey(id));
            p.SpouseFamilyIds.RemoveAll(id => !tree.Families.ContainsKey(id));
        }
        tree.NotDuplicates.RemoveWhere(k => k.Split(':').Any(x => long.TryParse(x, out var id) && living.Contains(id)));
    }

    private static void RemoveNotes(FamilyTree tree)
    {
        static void Events(IEnumerable<Event> events)
        {
            foreach (var e in events)
            {
                e.Note = "";
                e.Reasoning = "";      // research notes on the evidence
                e.Conflicts.Clear();
                Citations(e.Citations);
            }
        }
        static void Citations(IEnumerable<Citation> citations)
        {
            foreach (var c in citations) c.Note = "";
        }
        foreach (var p in tree.People.Values)
        {
            p.Note = "";
            Events(p.Events);
            Citations(p.Citations);
            foreach (var m in p.Media) m.Note = "";
            foreach (var d in p.DnaTests) d.Note = "";
            foreach (var m in p.DnaMatches) m.Note = "";
        }
        foreach (var f in tree.Families.Values)
        {
            f.Note = "";
            Events(f.Events);
            Citations(f.Citations);
            foreach (var m in f.Media) m.Note = "";
            Events(f.Children.Select(c => c.Sealing).OfType<Event>());
        }
        foreach (var s in tree.Sources.Values) s.Note = "";
        foreach (var r in tree.Repositories.Values) r.Note = "";
        foreach (var r in tree.Research.Values) r.Note = "";
    }
}
