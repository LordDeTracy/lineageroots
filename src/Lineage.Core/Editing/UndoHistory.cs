using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Editing;

/// <summary>
/// Unlimited-feeling undo/redo for any edit. Before an edit, the records it may touch are
/// snapshotted as JSON; afterwards, the same records (plus any created) are snapshotted again.
/// Undo puts the "before" records back, redo the "after" ones. Snapshots are per record, so an
/// edit in a 100,000-person tree costs only the handful of records it touches.
/// </summary>
public sealed class UndoHistory
{
    public const int Limit = 200;

    private enum Kind { Person, Family, Source, Repository, Research }
    private readonly record struct Key(Kind Kind, long Id);
    private sealed record Snapshot(Key Key, string? Json);
    private sealed record Change(string Label, List<Snapshot> Before, List<Snapshot> After);

    private readonly FamilyTree _tree;
    private readonly LinkedList<Change> _undo = new();
    private readonly Stack<Change> _redo = new();

    public UndoHistory(FamilyTree tree) => _tree = tree;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoLabel => _undo.Last?.Value.Label;
    public string? RedoLabel => _redo.TryPeek(out var c) ? c.Label : null;

    /// <summary>Starts an edit. Call <see cref="Edit.Commit"/> when done, or <see cref="Edit.Cancel"/> to drop it.</summary>
    public Edit Begin(string label, IEnumerable<Entity> touched) => new(this, label, touched);

    public sealed class Edit
    {
        private readonly UndoHistory _history;
        private readonly string _label;
        private readonly List<Snapshot> _before;
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
                if (_before.All(s => s.Key != key)) _before.Add(_history.Take(key));
            }
        }

        public void Commit()
        {
            if (_done) return;
            _done = true;
            var created = _history.AllKeys().Where(k => !_beforeKeys.Contains(k))
                                  .Select(k => new Snapshot(k, null)).ToList();
            var before = _before.Concat(created).ToList();
            var after = before.Select(s => _history.Take(s.Key)).ToList();
            if (before.Zip(after).All(p => p.First.Json == p.Second.Json)) return; // nothing changed
            _history._undo.AddLast(new Change(_label, before, after));
            if (_history._undo.Count > Limit) _history._undo.RemoveFirst();
            _history._redo.Clear();
        }

        public void Cancel() => _done = true;
    }

    /// <summary>Reverts the last edit; returns its label, or null if there was nothing to undo.</summary>
    public string? Undo()
    {
        if (_undo.Last is not { } node) return null;
        _undo.RemoveLast();
        Apply(node.Value.Before);
        _redo.Push(node.Value);
        return node.Value.Label;
    }

    public string? Redo()
    {
        if (!_redo.TryPop(out var change)) return null;
        Apply(change.After);
        _undo.AddLast(change);
        return change.Label;
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); }

    private void Apply(IEnumerable<Snapshot> states)
    {
        foreach (var s in states)
        {
            switch (s.Key.Kind)
            {
                case Kind.Person: Put(_tree.People, s, j => LineageStore.FromJson<Person>(j)); break;
                case Kind.Family: Put(_tree.Families, s, j => LineageStore.FromJson<Family>(j)); break;
                case Kind.Source: Put(_tree.Sources, s, j => LineageStore.FromJson<Source>(j)); break;
                case Kind.Repository: Put(_tree.Repositories, s, j => LineageStore.FromJson<Repository>(j)); break;
                case Kind.Research: Put(_tree.Research, s, j => LineageStore.FromJson<ResearchItem>(j)); break;
            }
        }
    }

    private static void Put<T>(Dictionary<long, T> map, Snapshot s, Func<string, T> read)
    {
        if (s.Json == null) map.Remove(s.Key.Id);
        else map[s.Key.Id] = read(s.Json);
    }

    private Snapshot Take(Key key) => new(key, key.Kind switch
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
