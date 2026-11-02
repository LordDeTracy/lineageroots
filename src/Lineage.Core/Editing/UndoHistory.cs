using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Editing;

/// <summary>
/// Undo/redo for any edit, kept in the tree's <see cref="FamilyTree.History"/> so it is saved with the
/// file and still works after closing and reopening it. Before an edit, the records it may touch are
/// snapshotted as JSON; afterwards, the same records (plus any created) are snapshotted again.
/// Undo puts the "before" records back, redo the "after" ones. Snapshots are per record, so an
/// edit in a 100,000-person tree costs only the handful of records it touches.
/// <para>The newest <see cref="Limit"/> changes (up to <see cref="SizeLimit"/> characters of snapshots) can be
/// undone; older ones stay in the history as a summary — when, who, what — up to <see cref="SummaryLimit"/>.</para>
/// </summary>
public sealed class UndoHistory
{
    public const int Limit = 200;
    public const int SummaryLimit = 5000;
    public const long SizeLimit = 40_000_000;

    private enum Kind { Person, Family, Source, Repository, Research }
    private readonly record struct Key(Kind Kind, long Id);

    private readonly FamilyTree _tree;
    private readonly Stack<ChangeRecord> _redo = new();

    public UndoHistory(FamilyTree tree) => _tree = tree;

    /// <summary>Who is making the changes, as the history shows it (asked at each change, so a new name applies at once).</summary>
    public Func<string> Author { get; set; } = () => Environment.UserName;
    /// <summary>The clock (tests set it).</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    private ChangeRecord? Last => _tree.History.Count > 0 ? _tree.History[^1] : null;
    public bool CanUndo => Last is { CanUndo: true };
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => CanUndo ? Last!.Label : null;
    public string? RedoLabel => _redo.TryPeek(out var c) ? c.Label : null;

    /// <summary>Starts an edit. Call <see cref="Edit.Commit"/> when done, or <see cref="Edit.Cancel"/> to drop it.</summary>
    public Edit Begin(string label, IEnumerable<Entity> touched) => new(this, label, touched);

    public sealed class Edit
    {
        private readonly UndoHistory _history;
        private readonly string _label;
        private readonly List<RecordState> _before;
        private readonly HashSet<Key> _beforeKeys;
        private bool _done;

        internal Edit(UndoHistory history, string label, IEnumerable<Entity> touched)
        {
            _history = history;
            _label = label;
            _before = touched.Distinct().Select(e => history.Take(KeyOf(e))).ToList();
            _beforeKeys = history.AllKeys().ToHashSet();
        }

        /// <summary>Adds more records to the snapshot before they change (e.g. found part-way through an edit).</summary>
        public void Touch(IEnumerable<Entity> more)
        {
            foreach (var e in more)
            {
                var key = KeyOf(e);
                if (_before.All(s => KeyOf(s) != key)) _before.Add(_history.Take(key));
            }
        }

        public void Commit()
        {
            if (_done) return;
            _done = true;
            var created = _history.AllKeys().Where(k => !_beforeKeys.Contains(k))
                                  .Select(k => new RecordState(k.Kind.ToString(), k.Id, null)).ToList();
            var before = _before.Concat(created).ToList();
            // Only the records that really changed are kept (that is all undo needs, and it keeps the file small).
            var changed = before.Select(s => (Before: s, After: _history.Take(KeyOf(s))))
                                .Where(p => p.Before.Json != p.After.Json).ToList();
            if (changed.Count == 0) return; // nothing changed
            _history.Add(new ChangeRecord
            {
                When = _history.Now(), Who = _history.Author(), Label = _label,
                Records = changed.Select(p => _history.Describe(p.Before, p.After)).ToList(),
                Before = changed.Select(p => p.Before).ToList(),
                After = changed.Select(p => p.After).ToList(),
            });
            _history._redo.Clear();
        }

        public void Cancel() => _done = true;
    }

    /// <summary>Reverts the last edit; returns its label, or null if there was nothing to undo.</summary>
    public string? Undo()
    {
        if (!CanUndo) return null;
        var change = Last!;
        _tree.History.RemoveAt(_tree.History.Count - 1);
        Apply(change.Before);
        _redo.Push(change);
        return change.Label;
    }

    public string? Redo()
    {
        if (!_redo.TryPop(out var change)) return null;
        Apply(change.After);
        _tree.History.Add(change);
        return change.Label;
    }

    /// <summary>Undoes <paramref name="change"/> and every change after it; returns how many were undone.</summary>
    public int UndoFrom(ChangeRecord change)
    {
        if (!_tree.History.Contains(change)) return 0;
        var count = 0;
        while (CanUndo && _tree.History.Contains(change) && Undo() != null) count++;
        return count;
    }

    /// <summary>Forgets the history (redo too) — in memory and, once saved, in the file.</summary>
    public void Clear() { _tree.History.Clear(); _redo.Clear(); }

    private void Add(ChangeRecord change)
    {
        var list = _tree.History;
        list.Add(change);
        // Entries are replaced, never changed, so a save running in the background reads a consistent copy.
        long size = 0;
        for (int i = list.Count - 1, kept = 0; i >= 0; i--)
        {
            if (!list[i].CanUndo) break;   // everything older is already a summary
            size += list[i].Size;
            if (++kept > Limit || size > SizeLimit) list[i] = list[i].Summary();
        }
        if (list.Count > SummaryLimit) list.RemoveRange(0, list.Count - SummaryLimit);
    }

    private ChangedRecord Describe(RecordState before, RecordState after)
    {
        var how = before.Json == null ? "Added" : after.Json == null ? "Removed" : "Changed";
        var json = after.Json ?? before.Json!;
        var name = Enum.Parse<Kind>(before.Kind) switch
        {
            Kind.Person => LineageStore.FromJson<Person>(json).DisplayName,
            Kind.Family => FamilyName(LineageStore.FromJson<Family>(json)),
            Kind.Source => LineageStore.FromJson<Source>(json).Title,
            Kind.Repository => LineageStore.FromJson<Repository>(json).Name,
            _ => LineageStore.FromJson<ResearchItem>(json).Description,
        };
        return new ChangedRecord(before.Kind, before.Id, name, how);
    }

    private string FamilyName(Family f)
    {
        var names = f.PartnerIds.Select(id => _tree.Person(id)?.DisplayName).OfType<string>().ToList();
        return names.Count > 0 ? string.Join(" & ", names) : $"family {f.Id}";
    }

    private void Apply(IEnumerable<RecordState> states)
    {
        foreach (var s in states)
        {
            switch (Enum.Parse<Kind>(s.Kind))
            {
                case Kind.Person: Put(_tree.People, s, j => LineageStore.FromJson<Person>(j)); break;
                case Kind.Family: Put(_tree.Families, s, j => LineageStore.FromJson<Family>(j)); break;
                case Kind.Source: Put(_tree.Sources, s, j => LineageStore.FromJson<Source>(j)); break;
                case Kind.Repository: Put(_tree.Repositories, s, j => LineageStore.FromJson<Repository>(j)); break;
                case Kind.Research: Put(_tree.Research, s, j => LineageStore.FromJson<ResearchItem>(j)); break;
            }
        }
    }

    private static void Put<T>(Dictionary<long, T> map, RecordState s, Func<string, T> read)
    {
        if (s.Json == null) map.Remove(s.Id);
        else map[s.Id] = read(s.Json);
    }

    private RecordState Take(Key key) => new(key.Kind.ToString(), key.Id, key.Kind switch
    {
        Kind.Person => _tree.People.TryGetValue(key.Id, out var p) ? LineageStore.ToJson(p) : null,
        Kind.Family => _tree.Families.TryGetValue(key.Id, out var f) ? LineageStore.ToJson(f) : null,
        Kind.Source => _tree.Sources.TryGetValue(key.Id, out var s) ? LineageStore.ToJson(s) : null,
        Kind.Repository => _tree.Repositories.TryGetValue(key.Id, out var r) ? LineageStore.ToJson(r) : null,
        _ => _tree.Research.TryGetValue(key.Id, out var x) ? LineageStore.ToJson(x) : null,
    });

    private IEnumerable<Key> AllKeys() =>
        _tree.People.Keys.Select(id => new Key(Kind.Person, id))
             .Concat(_tree.Families.Keys.Select(id => new Key(Kind.Family, id)))
             .Concat(_tree.Sources.Keys.Select(id => new Key(Kind.Source, id)))
             .Concat(_tree.Repositories.Keys.Select(id => new Key(Kind.Repository, id)))
             .Concat(_tree.Research.Keys.Select(id => new Key(Kind.Research, id)));

    private static Key KeyOf(RecordState s) => new(Enum.Parse<Kind>(s.Kind), s.Id);

    private static Key KeyOf(Entity e) => e switch
    {
        Person => new Key(Kind.Person, e.Id),
        Family => new Key(Kind.Family, e.Id),
        Source => new Key(Kind.Source, e.Id),
        Repository => new Key(Kind.Repository, e.Id),
        ResearchItem => new Key(Kind.Research, e.Id),
        _ => throw new ArgumentException($"Unknown record type {e.GetType().Name}"),
    };
}
