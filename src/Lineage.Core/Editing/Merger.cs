using Lineage.Core.Model;

namespace Lineage.Core.Editing;

/// <summary>
/// Merges two records of the same person. Nothing is thrown away: differing names become
/// alternate names, missing facts are filled in, extra events/sources/photos are added, and every
/// family link moves to the kept person. Families that end up duplicated (the same couple twice)
/// are merged too.
/// </summary>
public static class Merger
{
    private static readonly HashSet<string> SingleEvents = new() { "BIRT", "CHR", "DEAT", "BURI", "CREM" };

    /// <summary>Records the merge may change — pass to <see cref="UndoHistory.Begin"/>.</summary>
    public static IEnumerable<Entity> Touched(FamilyTree tree, Person keep, Person other) =>
        tree.Around(keep).Concat(tree.Around(other)).Distinct();

    public static void Merge(FamilyTree tree, Person keep, Person other)
    {
        if (keep == other) return;

        foreach (var n in other.Names)
            if (n.Full.Length > 0 && !keep.Names.Any(k => string.Equals(k.Full, n.Full, StringComparison.OrdinalIgnoreCase)))
            {
                if (n.Type.Length == 0) n.Type = "aka";
                keep.Names.Add(n);
            }
        if (keep.Sex == Sex.Unknown) keep.Sex = other.Sex;
        keep.Title = Pick(keep.Title, other.Title);
        keep.Description = Pick(keep.Description, other.Description);
        keep.UserId = Pick(keep.UserId, other.UserId);
        keep.FamilySearchId = Pick(keep.FamilySearchId, other.FamilySearchId);
        keep.Email = Pick(keep.Email, other.Email);
        keep.Url = Pick(keep.Url, other.Url);
        keep.Address = Pick(keep.Address, other.Address);
        keep.Phone = Pick(keep.Phone, other.Phone);
        keep.Colour = Pick(keep.Colour, other.Colour);
        keep.Note = Join(keep.Note, other.Note);

        MergeEvents(keep.Events, other.Events);
        keep.Citations.AddRange(other.Citations.Where(c => !keep.Citations.Any(k => SameCitation(k, c))));
        foreach (var m in other.Media)
            if (!keep.Media.Any(k => string.Equals(k.File, m.File, StringComparison.OrdinalIgnoreCase)))
            {
                if (keep.Media.Any(k => k.Primary)) m.Primary = false;
                keep.Media.Add(m);
            }
        foreach (var t in other.Tags) if (!keep.Tags.Contains(t)) keep.Tags.Add(t);
        foreach (var r in other.ResearchIds) if (!keep.ResearchIds.Contains(r)) keep.ResearchIds.Add(r);
        keep.DnaTests.AddRange(other.DnaTests);

        // Move family links.
        foreach (var fid in other.ParentFamilyIds)
        {
            if (tree.Family(fid) is not { } f) continue;
            var link = f.Children.FirstOrDefault(c => c.ChildId == other.Id);
            if (link != null)
            {
                if (f.Children.Any(c => c.ChildId == keep.Id)) f.Children.Remove(link);
                else link.ChildId = keep.Id;
            }
            if (!keep.ParentFamilyIds.Contains(fid)) keep.ParentFamilyIds.Add(fid);
        }
        foreach (var fid in other.SpouseFamilyIds)
        {
            if (tree.Family(fid) is not { } f) continue;
            if (f.Partner1Id == other.Id) f.Partner1Id = keep.Id;
            if (f.Partner2Id == other.Id) f.Partner2Id = keep.Id;
            if (!keep.SpouseFamilyIds.Contains(fid)) keep.SpouseFamilyIds.Add(fid);
        }
        tree.People.Remove(other.Id);
        tree.NotDuplicates.RemoveWhere(k => k.Split(':').Contains(other.Id.ToString()));

        // The same couple (or the same parents) may now appear twice.
        MergeDuplicateFamilies(tree, keep.SpouseFamilyIds);
        MergeDuplicateFamilies(tree, keep.ParentFamilyIds);
    }

    private static void MergeDuplicateFamilies(FamilyTree tree, List<long> ids)
    {
        var groups = ids.Select(id => tree.Family(id)).OfType<Family>()
                        .Where(f => f.PartnerIds.Any())
                        .GroupBy(f => string.Join(",", f.PartnerIds.Order()))
                        .Where(g => g.Count() > 1).ToList();
        foreach (var g in groups)
        {
            var keep = g.First();
            foreach (var other in g.Skip(1)) MergeFamily(tree, keep, other);
        }
    }

    public static void MergeFamily(FamilyTree tree, Family keep, Family other)
    {
        keep.Partner1Id ??= other.Partner1Id;
        keep.Partner2Id ??= other.Partner2Id;
        if (keep.Status == CoupleStatus.Unspecified) keep.Status = other.Status;
        keep.Note = Join(keep.Note, other.Note);
        MergeEvents(keep.Events, other.Events);
        keep.Citations.AddRange(other.Citations.Where(c => !keep.Citations.Any(k => SameCitation(k, c))));
        keep.Media.AddRange(other.Media);
        foreach (var link in other.Children)
        {
            if (!keep.Children.Any(c => c.ChildId == link.ChildId)) keep.Children.Add(link);
            if (tree.Person(link.ChildId) is { } child) Relink(child.ParentFamilyIds, other.Id, keep.Id);
        }
        foreach (var pid in other.PartnerIds)
            if (tree.Person(pid) is { } partner) Relink(partner.SpouseFamilyIds, other.Id, keep.Id);
        tree.Families.Remove(other.Id);
    }

    private static void Relink(List<long> ids, long from, long to)
    {
        var i = ids.IndexOf(from);
        if (i < 0) return;
        if (ids.Contains(to)) ids.RemoveAt(i); else ids[i] = to;
    }

    private static void MergeEvents(List<Event> keep, List<Event> other)
    {
        foreach (var e in other)
        {
            var same = SingleEvents.Contains(e.Tag) ? keep.FirstOrDefault(k => k.Tag == e.Tag) : null;
            if (same == null)
            {
                if (!keep.Any(k => k.Tag == e.Tag && k.Date == e.Date && k.Place == e.Place && k.Value == e.Value))
                    keep.Add(e);
                continue;
            }
            // Fill gaps; when both have a value, keep ours but don't lose theirs.
            if (same.Date.IsEmpty) same.Date = e.Date;
            else if (!e.Date.IsEmpty && same.Date != e.Date) same.Note = Join(same.Note, $"Also recorded as {e.Date.ToGedcom()}");
            if (same.Place.Length == 0) { same.Place = e.Place; same.Location ??= e.Location; }
            else if (e.Place.Length > 0 && !string.Equals(same.Place, e.Place, StringComparison.OrdinalIgnoreCase))
                same.Note = Join(same.Note, $"Also recorded at {e.Place}");
            same.Location ??= e.Location;
            same.Cause = Pick(same.Cause, e.Cause);
            same.Note = Join(same.Note, e.Note);
            same.Citations.AddRange(e.Citations.Where(c => !same.Citations.Any(k => SameCitation(k, c))));
        }
    }

    private static bool SameCitation(Citation a, Citation b) => a.SourceId == b.SourceId && a.Page == b.Page;
    private static string Pick(string mine, string theirs) => mine.Length > 0 ? mine : theirs;

    private static string Join(string a, string b)
    {
        if (b.Trim().Length == 0 || a.Contains(b.Trim(), StringComparison.Ordinal)) return a;
        return a.Trim().Length == 0 ? b : a.TrimEnd() + "\n\n" + b.Trim();
    }
}
