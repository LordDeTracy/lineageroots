using System.Globalization;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>
/// How serious a problem is. <see cref="Error"/>: impossible as recorded. <see cref="Warning"/>: unlikely, or
/// impossible only on approximate dates. <see cref="Info"/>: worth a look but not wrong.
/// </summary>
public enum ProblemSeverity { Error, Warning, Info }

/// <summary>
/// Something in the tree that looks wrong. <see cref="Code"/> is one of <see cref="ProblemCodes"/> and never
/// changes, so the UI can group, filter or hide a kind of problem; <see cref="Message"/> is for the family historian.
/// <see cref="PersonId"/> is the person the message is about (0 for a family with nobody in it).
/// </summary>
public sealed record Problem(string Code, ProblemSeverity Severity, long PersonId, long? FamilyId, string Message);

/// <summary>Stable identifiers for each kind of problem the finder reports.</summary>
public static class ProblemCodes
{
    public const string DeathBeforeBirth = "death-before-birth";
    public const string BurialBeforeDeath = "burial-before-death";
    public const string EventBeforeBirth = "event-before-birth";
    public const string EventAfterDeath = "event-after-death";
    public const string LivedTooLong = "lived-too-long";
    public const string MotherTooYoung = "mother-too-young";
    public const string MotherTooOld = "mother-too-old";
    public const string FatherTooYoung = "father-too-young";
    public const string FatherTooOld = "father-too-old";
    public const string BornAfterMotherDied = "born-after-mother-died";
    public const string BornAfterFatherDied = "born-after-father-died";
    public const string MarriedTooYoung = "married-too-young";
    public const string MarriedAfterDeath = "married-after-death";
    public const string SiblingsTooClose = "siblings-too-close";
    public const string ParentYoungerThanChild = "parent-younger-than-child";
    public const string OwnAncestor = "own-ancestor";
    public const string UnreadableDate = "unreadable-date";
    public const string NoFamily = "no-family";
    public const string ConflictingEvidence = "conflicting-evidence";
}

/// <summary>
/// The limits the problem finder works to. The defaults suit most records; a researcher can loosen them,
/// for example where very young marriages were usual.
/// </summary>
public sealed class ProblemOptions
{
    /// <summary>Anyone recorded as living longer than this many years is flagged.</summary>
    public int MaxLifespan { get; set; } = 110;
    public int MinMotherAge { get; set; } = 12;
    public int MaxMotherAge { get; set; } = 55;
    public int MinFatherAge { get; set; } = 13;
    public int MaxFatherAge { get; set; } = 80;
    public int MinMarriageAge { get; set; } = 13;
    /// <summary>A child born more than this many months after the father's death is flagged (pregnancy plus a margin).</summary>
    public int MaxMonthsAfterFatherDeath { get; set; } = 10;
    /// <summary>Brothers and sisters born closer together than this are flagged, unless they are twins.</summary>
    public int MinMonthsBetweenSiblings { get; set; } = 9;
    /// <summary>Brothers and sisters born this many days apart or fewer are treated as twins.</summary>
    public int TwinDays { get; set; } = 2;
    /// <summary>Grace period for events recorded after death, e.g. a census taken soon after.</summary>
    public int YearsAfterDeath { get; set; } = 3;
    /// <summary>How far an "about", "estimated" or "calculated" date may be from the year it gives.</summary>
    public int ApproximateYears { get; set; } = 5;
    /// <summary>
    /// Event tags (or custom event type names) that normally come after death and are never flagged for it.
    /// LDS temple ordinances are always allowed too, as they are often done long afterwards.
    /// </summary>
    public ISet<string> AfterDeathEvents { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "BURI", "CREM", "PROB", "WILL", "_FUNE", "Funeral", "Obituary", "Memorial", "Probate" };
    /// <summary>Also report <see cref="ProblemSeverity.Info"/> items (unreadable dates, people with no family links).</summary>
    public bool IncludeInfo { get; set; } = true;
}

/// <summary>
/// Finds dates and links that can't be right (died before being born, a mother of 70, a person who is their own
/// grandparent) so the researcher can fix them. Approximate dates (ABT, EST, CAL, BEF, AFT, ranges, or a christening
/// standing in for a birth) are given the benefit of the doubt: they are only flagged when wrong even at their most
/// generous reading, and then as a warning rather than an error. One pass over people and families, so it is quick
/// on large trees.
/// </summary>
public static class Problems
{
    private const double DaysPerYear = 365.2425;
    private const double DaysPerMonth = DaysPerYear / 12;
    private const int Open = 5_000_000;   // days; the open end of a "before" or "after" date

    private static readonly string[] BirthTags = { "BIRT", "CHR", "BAPM" };
    private static readonly string[] DeathTags = { "DEAT", "BURI", "CREM" };
    private static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public static List<Problem> Find(FamilyTree tree, ProblemOptions? options = null) =>
        new Checker(tree, options ?? new ProblemOptions()).Run();

    /// <summary>The first and last day (as day numbers) a recorded date could mean.</summary>
    private readonly record struct When(int Lo, int Hi, bool Rough, GenDate Date);

    private sealed class Life
    {
        public Event? BirthEvent, DeathEvent;
        public When? Birth, Death;
    }

    private sealed class Checker
    {
        private readonly FamilyTree _tree;
        private readonly ProblemOptions _o;
        private readonly Dictionary<long, Life> _lives = new();
        private readonly List<Problem> _found = new();

        public Checker(FamilyTree tree, ProblemOptions options) { _tree = tree; _o = options; }

        public List<Problem> Run()
        {
            foreach (var p in _tree.People.Values) _lives[p.Id] = LifeOf(p);
            foreach (var p in _tree.People.Values) CheckPerson(p);
            foreach (var f in _tree.Families.Values) CheckFamily(f);
            CheckLoops();
            CheckEvidence();
            if (_o.IncludeInfo)
            {
                CheckTextDates();
                CheckUnlinked();
            }
            return Sorted();
        }

        // ---- dates ----

        private When? Span(GenDate d, bool standIn = false)
        {
            if (d.Kind == DateKind.Text || !Valid(d.Year)) return null;
            var (lo, hi) = Bounds(d);
            if (d.Kind is DateKind.Range or DateKind.Period && d.End is { } end && Valid(end.Year))
            {
                var (elo, ehi) = Bounds(end);
                lo = Math.Min(lo, elo);
                hi = Math.Max(hi, ehi);
            }
            if (d.DualYear > d.Year) hi += 366;   // "1691/92": the later, new-style year is the real one
            switch (d.Qualifier)
            {
                case DateQualifier.About or DateQualifier.Estimated or DateQualifier.Calculated:
                    lo -= Days(_o.ApproximateYears);
                    hi += Days(_o.ApproximateYears);
                    break;
                case DateQualifier.Before: lo = -Open; break;
                case DateQualifier.After: hi = Open; break;
            }
            // A christening or burial comes some time after the birth or death it stands in for.
            if (standIn) lo -= Days(1);
            var rough = standIn || d.Qualifier != DateQualifier.None || d.Kind != DateKind.Simple;
            return new When(lo, hi, rough, d);
        }

        private static bool Valid(int year) => year is > 0 and < 9999;

        private static (int Lo, int Hi) Bounds(GenDate d)
        {
            var y = d.Year;
            if (d.Month is < 1 or > 12) return (DayNumber(y, 1, 1), DayNumber(y, 12, 31));
            var last = DateTime.DaysInMonth(y, d.Month);
            if (d.Day < 1) return (DayNumber(y, d.Month, 1), DayNumber(y, d.Month, last));
            var day = DayNumber(y, d.Month, Math.Min(d.Day, last));
            return (day, day);
        }

        private static int DayNumber(int y, int m, int d) => new DateOnly(y, m, d).DayNumber;
        private static int Days(double years) => (int)(years * DaysPerYear);

        private Life LifeOf(Person p)
        {
            var life = new Life();
            (life.BirthEvent, life.Birth) = FirstDated(p, BirthTags);
            (life.DeathEvent, life.Death) = FirstDated(p, DeathTags);
            return life;
        }

        /// <summary>The first tag in preference order with a usable date; later tags are stand-ins for the first.</summary>
        private (Event?, When?) FirstDated(Person p, string[] tags)
        {
            for (var i = 0; i < tags.Length; i++)
                foreach (var e in p.Events)
                    if (e.Tag == tags[i] && Span(e.Date, standIn: i > 0) is { } w) return (e, w);
            return (null, null);
        }

        /// <summary>
        /// Below the limit. Plain dates are taken at face value; approximate ones must be below it even at
        /// their most generous reading.
        /// </summary>
        private static bool Under(When from, When to, int limit) =>
            Age(from.Date, to.Date) < limit && (!(from.Rough || to.Rough) || (to.Hi - from.Lo) / DaysPerYear < limit);

        private static bool Over(When from, When to, int limit) =>
            Age(from.Date, to.Date) > limit && (!(from.Rough || to.Rough) || (to.Lo - from.Hi) / DaysPerYear >= limit + 1);

        /// <summary>Whole years from one date to another, using month and day when both have them.</summary>
        private static int Age(GenDate from, GenDate to)
        {
            var age = to.Year - from.Year;
            if (from.Month > 0 && to.Month > 0 &&
                (to.Month < from.Month || (to.Month == from.Month && from.Day > 0 && to.Day > 0 && to.Day < from.Day)))
                age--;
            return age;
        }

        // ---- checks ----

        private void CheckPerson(Person p)
        {
            var life = _lives[p.Id];
            if (life is { Birth: { } b, Death: { } d })
            {
                var rough = b.Rough || d.Rough;
                if (d.Hi < b.Lo)
                    Add(ProblemCodes.DeathBeforeBirth, ProblemSeverity.Error, rough, p.Id, null,
                        $"{Name(p)}'s {Label(life.DeathEvent!)} ({Show(d.Date)}) is before {His(p)} {Label(life.BirthEvent!)} ({Show(b.Date)}).");
                else if (Over(b, d, _o.MaxLifespan))
                    Add(ProblemCodes.LivedTooLong, ProblemSeverity.Error, rough, p.Id, null,
                        $"{Name(p)} lived to {About(rough)}{Age(b.Date, d.Date)} ({Show(b.Date)} to {Show(d.Date)}).");
            }

            var deat = life.DeathEvent?.Tag == "DEAT" ? life.Death : null;
            foreach (var e in p.Events)
            {
                // Alternative birth or death records are a different question (conflicting facts), not an order problem.
                if (e.Tag is "BIRT" or "DEAT" || ReferenceEquals(e, life.BirthEvent) || ReferenceEquals(e, life.DeathEvent)) continue;
                if (Span(e.Date) is not { } w) continue;
                if (e.Tag is "BURI" or "CREM")
                {
                    if (deat is { } dd && w.Hi < dd.Lo)
                        Add(ProblemCodes.BurialBeforeDeath, ProblemSeverity.Error, w.Rough || dd.Rough, p.Id, null,
                            $"{Name(p)}'s {Label(e)} ({Show(e.Date)}) is before {His(p)} death ({Show(dd.Date)}).");
                    continue;
                }
                if (life.Birth is { } bb && w.Hi < bb.Lo)
                    Add(ProblemCodes.EventBeforeBirth, ProblemSeverity.Error, w.Rough || bb.Rough, p.Id, null,
                        $"{Name(p)}'s {Label(e)} ({Show(e.Date)}) is before {His(p)} {Label(life.BirthEvent!)} ({Show(bb.Date)}).");
                else if (life.Death is { } de && !AllowedAfterDeath(e) && w.Lo - de.Hi > Days(_o.YearsAfterDeath))
                    Add(ProblemCodes.EventAfterDeath, ProblemSeverity.Error, w.Rough || de.Rough, p.Id, null,
                        $"{Name(p)}'s {Label(e)} ({Show(e.Date)}) is more than {Count(_o.YearsAfterDeath, "year")} after {His(p)} {Label(life.DeathEvent!)} ({Show(de.Date)}).");
            }
        }

        private bool AllowedAfterDeath(Event e) =>
            EventTypes.IsLds(e.Tag) || _o.AfterDeathEvents.Contains(e.Tag) ||
            (e.TypeName.Length > 0 && _o.AfterDeathEvents.Contains(e.TypeName));

        private void CheckFamily(Family f)
        {
            var husband = _tree.Person(f.Partner1Id);
            var wife = _tree.Person(f.Partner2Id);
            if (f.Marriage is { } m && Span(m.Date) is { } mw)
            {
                if (husband != null) CheckMarriage(f, husband, wife, mw);
                if (wife != null) CheckMarriage(f, wife, husband, mw);
            }

            var siblings = new List<(Person Child, When Birth)>();
            foreach (var link in f.Children)
            {
                if (_tree.Person(link.ChildId) is not { } child) continue;
                var life = _lives[child.Id];
                if (life.Birth is not { } cb) continue;
                // Only birth parents: adoptive and step-parents can be any age, and can die before the child is born.
                if (husband != null && link.ToFather == ParentRelation.Birth) CheckParent(f, husband, child, cb, wifeSlot: false);
                if (wife != null && link.ToMother == ParentRelation.Birth) CheckParent(f, wife, child, cb, wifeSlot: true);
                // Christenings of brothers and sisters are often close together, so only real birth dates count.
                if (life.BirthEvent!.Tag == "BIRT" && (link.ToFather == ParentRelation.Birth || link.ToMother == ParentRelation.Birth))
                    siblings.Add((child, cb));
            }
            CheckSiblings(f, siblings);
        }

        private void CheckMarriage(Family f, Person p, Person? spouse, When mw)
        {
            var life = _lives[p.Id];
            var to = spouse != null ? $" to {Name(spouse)}" : "";
            if (life.Birth is { } b)
            {
                var rough = b.Rough || mw.Rough;
                if (mw.Hi < b.Lo)
                    Add(ProblemCodes.EventBeforeBirth, ProblemSeverity.Error, rough, p.Id, f.Id,
                        $"{Name(p)}'s marriage{to} ({Show(mw.Date)}) is before {His(p)} {Label(life.BirthEvent!)} ({Show(b.Date)}).");
                else if (Under(b, mw, _o.MinMarriageAge))
                {
                    var age = Age(b.Date, mw.Date);
                    Add(ProblemCodes.MarriedTooYoung, ProblemSeverity.Warning, rough, p.Id, f.Id, age >= 1
                        ? $"{Name(p)} was {About(rough)}{age} when married{to} ({Show(mw.Date)})."
                        : $"{Name(p)}'s marriage{to} ({Show(mw.Date)}) is very close to {His(p)} {Label(life.BirthEvent!)} ({Show(b.Date)}).");
                }
            }
            if (life.Death is { } d && mw.Lo > d.Hi)
                Add(ProblemCodes.MarriedAfterDeath, ProblemSeverity.Error, d.Rough || mw.Rough, p.Id, f.Id,
                    $"{Name(p)}'s marriage{to} ({Show(mw.Date)}) is after {His(p)} {Label(life.DeathEvent!)} ({Show(d.Date)}).");
        }

        private void CheckParent(Family f, Person parent, Person child, When cb, bool wifeSlot)
        {
            var life = _lives[parent.Id];
            var mother = parent.Sex == Sex.Female || (parent.Sex == Sex.Unknown && wifeSlot);
            if (life.Birth is { } pb)
            {
                var rough = pb.Rough || cb.Rough;
                if (pb.Lo > cb.Hi)
                    Add(ProblemCodes.ParentYoungerThanChild, ProblemSeverity.Error, rough, parent.Id, f.Id,
                        $"{Name(parent)} ({Show(pb.Date)}) is younger than {His(parent)} child {Name(child)} ({Show(cb.Date)}).");
                else
                {
                    var (min, max) = mother ? (_o.MinMotherAge, _o.MaxMotherAge) : (_o.MinFatherAge, _o.MaxFatherAge);
                    var age = Age(pb.Date, cb.Date);
                    var born = $"{Name(parent)} was {About(rough)}{age} when {Name(child)} was born ({Show(cb.Date)}).";
                    if (Under(pb, cb, min))
                        Add(mother ? ProblemCodes.MotherTooYoung : ProblemCodes.FatherTooYoung, ProblemSeverity.Warning, rough, parent.Id, f.Id,
                            age >= 1 ? born : $"{Name(parent)}'s birth ({Show(pb.Date)}) is very close to that of {His(parent)} child {Name(child)} ({Show(cb.Date)}).");
                    else if (Over(pb, cb, max))
                        Add(mother ? ProblemCodes.MotherTooOld : ProblemCodes.FatherTooOld, ProblemSeverity.Warning, rough, parent.Id, f.Id, born);
                }
            }
            if (life.Death is { } pd)
            {
                var rough = pd.Rough || cb.Rough;
                if (mother && cb.Lo > pd.Hi)
                    Add(ProblemCodes.BornAfterMotherDied, ProblemSeverity.Error, rough, child.Id, f.Id,
                        $"{Name(child)} was born ({Show(cb.Date)}) after {His(child)} mother {Name(parent)}'s {Label(life.DeathEvent!)} ({Show(pd.Date)}).");
                else if (!mother && cb.Lo - pd.Hi > _o.MaxMonthsAfterFatherDeath * DaysPerMonth)
                    Add(ProblemCodes.BornAfterFatherDied, ProblemSeverity.Error, rough, child.Id, f.Id,
                        $"{Name(child)} was born ({Show(cb.Date)}) more than {Count(_o.MaxMonthsAfterFatherDeath, "month")} after {His(child)} father {Name(parent)}'s {Label(life.DeathEvent!)} ({Show(pd.Date)}).");
            }
        }

        private void CheckSiblings(Family f, List<(Person Child, When Birth)> kids)
        {
            if (kids.Count < 2) return;
            kids.Sort((x, y) => (x.Birth.Lo + x.Birth.Hi).CompareTo(y.Birth.Lo + y.Birth.Hi));
            var limit = _o.MinMonthsBetweenSiblings * DaysPerMonth;
            var reported = new HashSet<long>();
            // Every pair, not just neighbours: a vaguely dated child sorted in between mustn't hide a clash.
            for (var j = 1; j < kids.Count; j++)
                for (var i = j - 1; i >= 0; i--)
                {
                    var (a, aw) = kids[i];
                    var (b, bw) = kids[j];
                    if (reported.Contains(b.Id)) break;
                    var shortest = bw.Lo - aw.Hi;
                    var longest = bw.Hi - aw.Lo;
                    if (shortest <= _o.TwinDays || longest >= limit) continue;
                    reported.Add(b.Id);
                    var days = (bw.Lo + bw.Hi) / 2 - (aw.Lo + aw.Hi) / 2;
                    var months = (int)(days / DaysPerMonth);
                    var gap = months >= 1 ? Count(months, "month") : Count(days, "day");
                    Add(ProblemCodes.SiblingsTooClose, ProblemSeverity.Warning, false, b.Id, f.Id,
                        $"{Name(a)} ({Show(aw.Date)}) and {Name(b)} ({Show(bw.Date)}) were born only {gap} apart.");
                }
        }

        /// <summary>
        /// People who are their own ancestor: those in a cycle of parent links. Found with an iterative
        /// strongly-connected-components pass (Tarjan), so loops of any length can't hang it.
        /// </summary>
        private void CheckLoops()
        {
            var people = _tree.People.Values.ToArray();
            var index = new Dictionary<long, int>(people.Length);
            for (var i = 0; i < people.Length; i++) index[people[i].Id] = i;
            var up = new int[people.Length][];
            var buffer = new List<int>();
            for (var i = 0; i < people.Length; i++)
            {
                buffer.Clear();
                foreach (var fid in people[i].ParentFamilyIds)
                {
                    if (_tree.Family(fid) is not { } f) continue;
                    if (f.Partner1Id is { } a && index.TryGetValue(a, out var ia)) buffer.Add(ia);
                    if (f.Partner2Id is { } b && index.TryGetValue(b, out var ib)) buffer.Add(ib);
                }
                up[i] = buffer.Count == 0 ? Array.Empty<int>() : buffer.ToArray();
            }

            var component = Components(up, out var sizes);
            for (var i = 0; i < people.Length; i++)
            {
                var self = Array.IndexOf(up[i], i) >= 0;
                if (sizes[component[i]] < 2 && !self) continue;
                var p = people[i];
                if (self)
                {
                    Add(ProblemCodes.OwnAncestor, ProblemSeverity.Error, false, p.Id, null,
                        $"{Name(p)} is recorded as {His(p)} own parent.");
                    continue;
                }
                var via = up[i].First(j => j != i && component[j] == component[i]);
                Add(ProblemCodes.OwnAncestor, ProblemSeverity.Error, false, p.Id, null,
                    $"{Name(p)} is recorded as {His(p)} own ancestor (through {Name(people[via])}), so a parent link must be wrong.");
            }
        }

        private static int[] Components(int[][] edges, out List<int> sizes)
        {
            var n = edges.Length;
            var order = new int[n];
            var low = new int[n];
            var component = new int[n];
            var onStack = new bool[n];
            Array.Fill(order, -1);
            var stack = new Stack<int>();
            var work = new Stack<(int Node, int Next)>();
            var counter = 0;
            sizes = new List<int>();
            for (var start = 0; start < n; start++)
            {
                if (order[start] >= 0) continue;
                order[start] = low[start] = counter++;
                stack.Push(start);
                onStack[start] = true;
                work.Push((start, 0));
                while (work.Count > 0)
                {
                    var (v, next) = work.Pop();
                    if (next < edges[v].Length)
                    {
                        work.Push((v, next + 1));
                        var w = edges[v][next];
                        if (order[w] < 0)
                        {
                            order[w] = low[w] = counter++;
                            stack.Push(w);
                            onStack[w] = true;
                            work.Push((w, 0));
                        }
                        else if (onStack[w]) low[v] = Math.Min(low[v], order[w]);
                        continue;
                    }
                    if (low[v] == order[v])
                    {
                        var id = sizes.Count;
                        var size = 0;
                        int x;
                        do
                        {
                            x = stack.Pop();
                            onStack[x] = false;
                            component[x] = id;
                            size++;
                        } while (x != v);
                        sizes.Add(size);
                    }
                    if (work.Count > 0)
                    {
                        var parent = work.Peek().Node;
                        low[parent] = Math.Min(low[parent], low[v]);
                    }
                }
            }
            return component;
        }

        private void CheckTextDates()
        {
            foreach (var p in _tree.People.Values)
                foreach (var e in p.Events)
                    if (e.Date.Kind == DateKind.Text && e.Date.Text.Length > 0)
                        Add(ProblemCodes.UnreadableDate, ProblemSeverity.Info, false, p.Id, null,
                            $"Lineage Roots couldn't understand the date \"{e.Date.Text}\" on {Name(p)}'s {Label(e)}, so it isn't checked.");
            foreach (var f in _tree.Families.Values)
                foreach (var e in f.Events)
                    if (e.Date.Kind == DateKind.Text && e.Date.Text.Length > 0)
                        Add(ProblemCodes.UnreadableDate, ProblemSeverity.Info, false, FamilyPerson(f), f.Id,
                            $"Lineage Roots couldn't understand the date \"{e.Date.Text}\" on the {Label(e)} of {Couple(f)}, so it isn't checked.");
        }

        /// <summary>Facts whose records disagree and haven't been settled (see <see cref="Evidence"/>).</summary>
        private void CheckEvidence()
        {
            void Check(Event e, Entity owner, long personId, long? familyId, string who)
            {
                if (e.Conflicts.Count == 0 && e.Tag is not ("BIRT" or "CHR" or "BAPM" or "DEAT" or "BURI" or "CREM" or "MARR")) return;
                var a = Evidence.Assess(_tree, e, owner);
                if (a.Level != ProofLevel.Conflicting) return;
                var detail = a.Reasons.FirstOrDefault(r => r.StartsWith("Unresolved") || r.StartsWith("Another")) ?? a.Summary;
                Add(ProblemCodes.ConflictingEvidence, ProblemSeverity.Warning, false, personId, familyId,
                    $"{who}: the records disagree about the {EventTypes.Label(e).ToLowerInvariant()}. {detail}");
            }
            foreach (var p in _tree.People.Values)
                foreach (var e in p.Events) Check(e, p, p.Id, null, Name(p));
            foreach (var f in _tree.Families.Values)
            {
                var first = f.PartnerIds.Select(id => _tree.Person(id)).OfType<Person>().ToList();
                if (first.Count == 0) continue;
                foreach (var e in f.Events) Check(e, f, first[0].Id, f.Id, string.Join(" and ", first.Select(Name)));
            }
        }

        private void CheckUnlinked()
        {
            // Linked = shares a family record with at least one other person who exists.
            var linked = new HashSet<long>();
            var members = new List<long>();
            foreach (var f in _tree.Families.Values)
            {
                members.Clear();
                foreach (var id in f.PartnerIds) if (_tree.People.ContainsKey(id)) members.Add(id);
                foreach (var c in f.Children) if (_tree.People.ContainsKey(c.ChildId)) members.Add(c.ChildId);
                if (members.Distinct().Count() > 1) linked.UnionWith(members);
            }
            foreach (var p in _tree.People.Values)
                if (!linked.Contains(p.Id))
                    Add(ProblemCodes.NoFamily, ProblemSeverity.Info, false, p.Id, null,
                        $"{Name(p)} isn't linked to any parents, partner or children.");
        }

        // ---- output ----

        private void Add(string code, ProblemSeverity severity, bool rough, long personId, long? familyId, string message) =>
            _found.Add(new Problem(code, rough && severity == ProblemSeverity.Error ? ProblemSeverity.Warning : severity,
                personId, familyId, message));

        private List<Problem> Sorted()
        {
            var keys = new Dictionary<long, string>();
            string Key(long id)
            {
                if (keys.TryGetValue(id, out var k)) return k;
                return keys[id] = _tree.People.TryGetValue(id, out var p) && p.Names.Count > 0 ? p.Names[0].SortKey : "";
            }
            return _found.OrderBy(x => x.Severity).ThenBy(x => Key(x.PersonId), StringComparer.Ordinal)
                .ThenBy(x => x.PersonId).ThenBy(x => x.Code, StringComparer.Ordinal).ToList();
        }

        private long FamilyPerson(Family f) =>
            f.Partner1Id ?? f.Partner2Id ?? f.Children.Select(c => (long?)c.ChildId).FirstOrDefault() ?? 0;

        private string Couple(Family f)
        {
            var names = f.PartnerIds.Select(id => _tree.Person(id)).OfType<Person>().Select(Name).ToList();
            return names.Count == 0 ? $"family #{f.Id}" : string.Join(" and ", names);
        }
    }

    // ---- wording ----

    /// <summary>The person's name without creating one (PrimaryName adds an empty name when there is none).</summary>
    private static string Name(Person p) =>
        p.Names.Count > 0 && p.Names[0].Full.Length > 0 ? p.Names[0].Full : $"Unnamed person #{p.Id}";

    private static string His(Person p) => p.Sex switch { Sex.Male => "his", Sex.Female => "her", _ => "their" };

    private static string About(bool rough) => rough ? "about " : "";

    private static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";

    private static string Label(Event e)
    {
        var label = EventTypes.Label(e);
        return label.Length > 0 ? char.ToLowerInvariant(label[0]) + label[1..] : "event";
    }

    /// <summary>A date the way a person would say it: "about 1850", "3 Mar 1850", "between 1850 and 1855".</summary>
    private static string Show(GenDate d) => d.Kind switch
    {
        DateKind.Text => d.Text,
        DateKind.Range => d.End is { } e ? $"between {Plain(d)} and {Plain(e)}" : Plain(d),
        DateKind.Period => d.End is { } e ? $"{Plain(d)} to {Plain(e)}" : $"from {Plain(d)}",
        _ => d.Qualifier switch
        {
            DateQualifier.None => Plain(d),
            DateQualifier.Before => "before " + Plain(d),
            DateQualifier.After => "after " + Plain(d),
            _ => "about " + Plain(d),
        },
    };

    private static string Plain(GenDate d)
    {
        var year = d.Year.ToString(CultureInfo.InvariantCulture);
        if (d.Month is < 1 or > 12) return year;
        var month = MonthNames[d.Month - 1];
        return d.Day > 0 ? $"{d.Day.ToString(CultureInfo.InvariantCulture)} {month} {year}" : $"{month} {year}";
    }
}
