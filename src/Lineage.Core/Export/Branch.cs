using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Export;

/// <summary>Which part of the tree to take: everyone, or one person's line.</summary>
public enum BranchKind { Everyone, Ancestors, Descendants, AncestorsAndDescendants }

/// <summary>
/// Takes one branch of a tree: a person's ancestors, their descendants (with husbands and wives), or both. The copy
/// keeps the families between the people taken, and only the sources, repositories and research they use, so it can
/// be shared or saved as a family file of its own.
/// </summary>
public static class Branch
{
    /// <summary>The people in the branch, the person themselves included.</summary>
    public static HashSet<long> People(FamilyTree tree, Person root, BranchKind kind, bool spouses = true)
    {
        var ids = new HashSet<long> { root.Id };
        if (kind == BranchKind.Everyone) return tree.People.Keys.ToHashSet();
        if (kind is BranchKind.Ancestors or BranchKind.AncestorsAndDescendants)
        {
            var queue = new Queue<Person>(new[] { root });
            while (queue.Count > 0)
                foreach (var parent in tree.ParentsOf(queue.Dequeue()))
                    if (ids.Add(parent.Id)) queue.Enqueue(parent);
        }
        if (kind is BranchKind.Descendants or BranchKind.AncestorsAndDescendants)
        {
            var line = new HashSet<long> { root.Id };
            var queue = new Queue<Person>(new[] { root });
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var (family, spouse, children) in tree.FamiliesOf(p))
                {
                    if (spouses && spouse != null) ids.Add(spouse.Id);
                    foreach (var child in children)
                        if (line.Add(child.Id)) { ids.Add(child.Id); queue.Enqueue(child); }
                }
            }
        }
        return ids;
    }

    /// <summary>A copy of the tree holding only <paramref name="keep"/> and what they need.</summary>
    public static FamilyTree Extract(FamilyTree tree, IReadOnlySet<long> keep, string? name = null)
    {
        static T Clone<T>(T record) where T : Entity => LineageStore.FromJson<T>(LineageStore.ToJson(record));
        var copy = new FamilyTree { Name = name ?? tree.Name, HomePersonId = tree.HomePersonId is { } h && keep.Contains(h) ? h : null };
        foreach (var p in tree.People.Values.Where(p => keep.Contains(p.Id))) copy.AddPerson(Clone(p));

        foreach (var f in tree.Families.Values)
        {
            var c = Clone(f);
            if (c.Partner1Id is { } a && !keep.Contains(a)) c.Partner1Id = null;
            if (c.Partner2Id is { } b && !keep.Contains(b)) c.Partner2Id = null;
            c.Children.RemoveAll(x => !keep.Contains(x.ChildId));
            // A family is kept when it still joins two people: a couple, or a parent and child.
            var members = c.PartnerIds.Count() + c.Children.Count;
            if (members >= 2) copy.AddFamily(c);
        }
        foreach (var p in copy.People.Values)
        {
            p.ParentFamilyIds.RemoveAll(id => !copy.Families.ContainsKey(id));
            p.SpouseFamilyIds.RemoveAll(id => !copy.Families.ContainsKey(id));
            foreach (var m in p.DnaMatches.Where(m => m.PersonId is { } other && !keep.Contains(other))) m.PersonId = null;   // the match stays, unlinked
        }

        // Only the sources the branch cites, and the archives that hold them.
        var cited = copy.People.Values.SelectMany(p => p.Citations.Concat(p.Events.SelectMany(e => e.Citations)))
            .Concat(copy.Families.Values.SelectMany(f => f.Citations.Concat(f.Events.SelectMany(e => e.Citations))))
            .Select(c => c.SourceId).ToHashSet();
        foreach (var s in tree.Sources.Values.Where(s => cited.Contains(s.Id))) copy.AddSource(Clone(s));
        var held = copy.Sources.Values.Select(s => s.RepositoryId).OfType<long>().ToHashSet();
        foreach (var r in tree.Repositories.Values.Where(r => held.Contains(r.Id))) copy.AddRepository(Clone(r));
        var research = copy.People.Values.SelectMany(p => p.ResearchIds).ToHashSet();
        foreach (var r in tree.Research.Values.Where(r => research.Contains(r.Id))) copy.AddResearch(Clone(r));
        copy.NotDuplicates.UnionWith(tree.NotDuplicates.Where(k => k.Split(':').All(x => !long.TryParse(x, out var id) || keep.Contains(id))));
        return copy;
    }
}
