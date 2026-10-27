using System.Security.Cryptography;
using System.Text;
using Lineage.Core.Export;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.Core.Sharing;

public enum ChangeKind { Added, Changed, Removed }

/// <summary>
/// One change a relative made to their copy. <see cref="Yours"/> and <see cref="Theirs"/> describe the piece as it
/// is now in each tree ("" when absent). <see cref="Conflict"/> means you changed the same piece since sending the
/// copy, so taking theirs replaces your edit. <see cref="Fingerprint"/> identifies the change, to remember a refusal.
/// </summary>
public sealed record ShareChange(string Key, ChangeKind Kind, string Who, string What, string Yours, string Theirs,
                                 bool Conflict, string Fingerprint)
{
    public string KindLabel => Kind switch { ChangeKind.Added => "Added", ChangeKind.Removed => "Removed", _ => "Changed" };
}

/// <summary>
/// Copies a tree for a relative and merges their edits back. Records are matched by their permanent ids (people,
/// families, sources and facts all keep them), and every piece — a person's names, sex, notes or other details;
/// each fact; a family's couple, status and notes; each child link; each source — is compared three ways: as sent,
/// as they have it now, and as you have it now. Only what they changed is offered; where you changed the same piece
/// too, it's marked as a conflict. Accepted changes are applied together; refused ones are remembered so they
/// aren't offered again unless the relative changes them again.
/// </summary>
public static class ShareReview
{
    // ---- making a copy ----

    /// <summary>
    /// Makes the copy to send, filtered by <paramref name="privacy"/>, and records the share (with a snapshot of
    /// exactly what's in the copy) in <paramref name="tree"/>.
    /// </summary>
    public static (FamilyTree Copy, ShareRecord Share) CreateCopy(FamilyTree tree, string with, PrivacyOptions privacy)
    {
        var copy = Privacy.Filter(tree, privacy);
        copy.Name = tree.Name;
        copy.Research.Clear();   // your own to-do list stays with you
        foreach (var p in copy.People.Values)
        {
            p.ResearchIds.Clear();
            p.DnaMatches.Clear();   // match lists name living people
        }
        foreach (var f in copy.Families.Values) f.ResearchIds.Clear();
        var share = new ShareRecord { With = with.Trim(), Snapshot = TreeSnapshot.Save(copy) };
        copy.SharedFrom = new ShareOrigin(share.Id, tree.Name);
        tree.Shares.Add(share);
        return (copy, share);
    }

    // ---- comparing ----

    private sealed class Index
    {
        public readonly FamilyTree Tree;
        public readonly Dictionary<Guid, Person> People;
        public readonly Dictionary<Guid, Family> Families;
        public readonly Dictionary<Guid, Source> Sources;

        public Index(FamilyTree tree)
        {
            Tree = tree;
            People = tree.People.Values.GroupBy(p => p.Uid).ToDictionary(g => g.Key, g => g.First());
            Families = tree.Families.Values.GroupBy(f => f.Uid).ToDictionary(g => g.Key, g => g.First());
            Sources = tree.Sources.Values.GroupBy(s => s.Uid).ToDictionary(g => g.Key, g => g.First());
        }

        public string SourceUid(long id) => Tree.Sources.TryGetValue(id, out var s) ? s.Uid.ToString("N") : "";
        public string PersonUid(long? id) => Tree.Person(id)?.Uid.ToString("N") ?? "";
    }

    public static List<ShareChange> Compare(FamilyTree ours, FamilyTree theirs, ShareRecord share) =>
        Compare(ours, theirs, TreeSnapshot.Load(share.Snapshot), share.Rejected);

    public static List<ShareChange> Compare(FamilyTree ours, FamilyTree theirs, FamilyTree sent, IReadOnlySet<string>? rejected = null)
    {
        var o = new Index(ours);
        var t = new Index(theirs);
        var b = new Index(sent);
        var changes = new List<ShareChange>();

        void Piece(string key, string? bv, string? tv, string? ov, string who, string what, Func<string?, Index, string> show)
        {
            if (tv == bv || tv == ov) return;
            var kind = tv == null ? ChangeKind.Removed : bv == null && ov == null ? ChangeKind.Added : ov == null ? ChangeKind.Added : ChangeKind.Changed;
            var conflict = ov != bv;
            var fingerprint = $"{key}|{Hash(tv)}";
            if (rejected?.Contains(fingerprint) == true) return;
            changes.Add(new ShareChange(key, kind, who, what, ov == null ? "" : show(ov, o), tv == null ? "" : show(tv, t), conflict, fingerprint));
        }

        // Sources first (facts cite them).
        foreach (var uid in Keys(b.Sources, t.Sources, o.Sources))
        {
            var (bs, ts, os) = (b.Sources.GetValueOrDefault(uid), t.Sources.GetValueOrDefault(uid), o.Sources.GetValueOrDefault(uid));
            var title = (ts ?? os ?? bs)!.Title;
            Piece($"s:{uid:N}", SourceCanon(bs), SourceCanon(ts), SourceCanon(os), "Sources", $"Source: {title}", (_, _) => title);
        }

        foreach (var uid in Keys(b.People, t.People, o.People))
        {
            var (bp, tp, op) = (b.People.GetValueOrDefault(uid), t.People.GetValueOrDefault(uid), o.People.GetValueOrDefault(uid));
            var who = (op ?? tp ?? bp)!.DisplayName;
            if (tp == null || bp == null || op == null)
            {
                // Added, removed, or (on one side) deleted: the person as a whole.
                Piece($"p:{uid:N}", bp != null ? PersonCanon(bp, b) : null, tp != null ? PersonCanon(tp, t) : null, op != null ? PersonCanon(op, o) : null,
                      who, tp == null ? "Removed from their copy" : bp == null ? "New person" : "Kept or changed in their copy, but you deleted them",
                      (_, ix) => Summary(ix == t ? tp : op, ix));
                if (tp == null || op == null) continue;
                if (bp == null) continue;   // added on both sides: shown as one change above
            }
            Piece($"p:{uid:N}:names", NamesCanon(bp), NamesCanon(tp), NamesCanon(op), who, "Names", (v, ix) => NamesText(ix == t ? tp : op));
            Piece($"p:{uid:N}:sex", bp?.Sex.ToString(), tp.Sex.ToString(), op.Sex.ToString(), who, "Sex", (v, _) => v ?? "");
            Piece($"p:{uid:N}:note", bp?.Note, tp.Note, op.Note, who, "Notes", (v, _) => Short(v));
            Piece($"p:{uid:N}:details", DetailsCanon(bp), DetailsCanon(tp), DetailsCanon(op), who, "Other details",
                  (_, ix) => DetailsText(ix == t ? tp : op));
            Piece($"p:{uid:N}:sources", CitationsCanon(bp?.Citations, b), CitationsCanon(tp.Citations, t), CitationsCanon(op.Citations, o),
                  who, "Sources for the whole person", (_, ix) => $"{(ix == t ? tp : op).Citations.Count} cited");
            EventPieces($"pe:{uid:N}", bp?.Events, tp.Events, op.Events, b, t, o, who, Piece);
        }

        foreach (var uid in Keys(b.Families, t.Families, o.Families))
        {
            var (bf, tf, of) = (b.Families.GetValueOrDefault(uid), t.Families.GetValueOrDefault(uid), o.Families.GetValueOrDefault(uid));
            var who = Couple((of ?? tf ?? bf)!, of != null ? o : tf != null ? t : b);
            if (tf == null || bf == null || of == null)
            {
                Piece($"f:{uid:N}", bf != null ? FamilyCanon(bf, b) : null, tf != null ? FamilyCanon(tf, t) : null, of != null ? FamilyCanon(of, o) : null,
                      who, tf == null ? "Family removed from their copy" : bf == null ? "New family" : "Kept in their copy, but you deleted it",
                      (_, ix) => FamilyText(ix == t ? tf : of, ix));
                if (tf == null || of == null || bf == null) continue;
            }
            Piece($"f:{uid:N}:couple", CoupleCanon(bf, b), CoupleCanon(tf, t), CoupleCanon(of, o), who, "The couple",
                  (_, ix) => Couple(ix == t ? tf : of, ix));
            Piece($"f:{uid:N}:status", StatusCanon(bf), StatusCanon(tf), StatusCanon(of), who, "Status",
                  (_, ix) => Status(ix == t ? tf : of).ToString());
            Piece($"f:{uid:N}:note", bf?.Note, tf.Note, of.Note, who, "Family notes", (v, _) => Short(v));
            // Children, by the child's id.
            var kids = new[] { (bf, b), (tf, t), (of, o) }
                .SelectMany(x => x.Item1?.Children.Select(c => x.Item2.PersonUid(c.ChildId)) ?? Enumerable.Empty<string>())
                .Where(k => k.Length > 0).Distinct();
            foreach (var kid in kids)
            {
                string? Link(Family? f, Index ix) => f?.Children.FirstOrDefault(c => ix.PersonUid(c.ChildId) == kid) is { } c
                    ? $"{c.ToFather}|{c.ToMother}|{c.Status}|{(c.Sealing != null ? EventCanon(c.Sealing, ix) : "")}" : null;
                var name = (o.People.GetValueOrDefault(Guid.Parse(kid)) ?? t.People.GetValueOrDefault(Guid.Parse(kid)))?.DisplayName ?? "a child";
                Piece($"fc:{uid:N}:{kid}", Link(bf, b), Link(tf, t), Link(of, o), who, $"Child: {name}", (v, _) => v == null ? "" : name);
            }
            EventPieces($"fe:{uid:N}", bf?.Events, tf.Events, of.Events, b, t, o, who, Piece);
        }

        return changes.OrderBy(c => c.Who == "Sources" ? 1 : 0).ThenBy(c => c.Who, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Key).ToList();
    }

    private delegate void PieceFn(string key, string? bv, string? tv, string? ov, string who, string what, Func<string?, Index, string> show);

    private static void EventPieces(string prefix, List<Event>? be, List<Event> te, List<Event> oe, Index b, Index t, Index o, string who, PieceFn piece)
    {
        var ids = (be ?? new()).Concat(te).Concat(oe).Select(e => e.Id).Distinct();
        foreach (var id in ids)
        {
            var (bx, tx, ox) = (be?.FirstOrDefault(e => e.Id == id), te.FirstOrDefault(e => e.Id == id), oe.FirstOrDefault(e => e.Id == id));
            var label = EventTypes.Label((tx ?? ox ?? bx)!);
            piece($"{prefix}:{id:N}", bx != null ? EventCanon(bx, b) : null, tx != null ? EventCanon(tx, t) : null, ox != null ? EventCanon(ox, o) : null,
                  who, label, (_, ix) => EventText(ix == t ? tx : ox, ix));
        }
    }

    private static IEnumerable<Guid> Keys<T>(params Dictionary<Guid, T>[] maps) => maps.SelectMany(m => m.Keys).Distinct();

    // ---- canonical forms (ids replaced by permanent ids, so trees can be compared) ----

    private static readonly System.Text.Json.JsonSerializerOptions CanonJson = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static string Canon(object value) => System.Text.Json.JsonSerializer.Serialize(value, CanonJson);

    private static string? SourceCanon(Source? s) => s == null ? null : Canon(new
    {
        s.Title, s.ShortTitle, s.Author, s.Publication, s.CallNumber, s.Text, s.Note, s.Type, s.Quality, s.Kind,
    });

    private static string PersonCanon(Person p, Index ix) => Canon(new
    {
        Names = NamesCanon(p), p.Sex, p.Note, Details = DetailsCanon(p), Events = p.Events.Select(e => EventCanon(e, ix)).ToList(),
    });

    private static string? NamesCanon(Person? p) => p == null ? null : Canon(p.Names);

    private static string? DetailsCanon(Person? p) => p == null ? null : Canon(new
    {
        p.Title, p.Description, p.UserId, p.FamilySearchId, p.Email, p.Url, p.Address, p.Phone, p.Colour, Tags = p.Tags.Order().ToList(),
    });

    private static string? CitationsCanon(List<Citation>? cs, Index ix) =>
        cs == null ? null : Canon(cs.Select(c => CitationCanon(c, ix)).ToList());

    private static object CitationCanon(Citation c, Index ix) =>
        new { Source = ix.SourceUid(c.SourceId), c.Page, c.Text, c.Note, Quality = Analysis.Evidence.QualityFor(ix.Tree, c), c.Information, c.Evidence };

    private static string EventCanon(Event e, Index ix) => Canon(new
    {
        e.Tag, e.TypeName, e.Value, Date = e.Date.ToGedcom(), e.Place, e.Place2, Location = e.Location?.ToString(), e.Description,
        e.Cause, e.Temple, e.Status, e.Note, e.Private, e.Conclusion, e.Reasoning,
        Citations = e.Citations.Select(c => CitationCanon(c, ix)).ToList(),
        Conflicts = e.Conflicts.Select(c => new { c.Claim, Source = c.SourceId is { } sid ? ix.SourceUid(sid) : "", c.Page, c.Resolution, c.Resolved }).ToList(),
    });

    /// <summary>A family with a marriage and no stated status counts as married (as GEDCOM files read it).</summary>
    private static CoupleStatus Status(Family f) =>
        f.Status == CoupleStatus.Unspecified && f.Events.Any(e => e.Tag == "MARR") ? CoupleStatus.Married : f.Status;

    private static string? StatusCanon(Family? f) => f == null ? null : $"{Status(f)}|{f.NoChildren}";

    private static string? CoupleCanon(Family? f, Index ix) => f == null ? null : $"{ix.PersonUid(f.Partner1Id)}|{ix.PersonUid(f.Partner2Id)}";

    private static string FamilyCanon(Family f, Index ix) =>
        $"{CoupleCanon(f, ix)}|{Status(f)}|{f.Note}|{string.Join(",", f.Children.Select(c => ix.PersonUid(c.ChildId)))}|{string.Join(",", f.Events.Select(e => EventCanon(e, ix)))}";

    private static string Hash(string? value) =>
        value == null ? "-" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];

    // ---- wording ----

    private static string Summary(Person? p, Index ix) => p == null ? "" : p.Lifespan.Length > 0 ? $"{p.DisplayName} ({p.Lifespan})" : p.DisplayName;

    private static string NamesText(Person? p) =>
        p == null ? "" : string.Join("; ", p.Names.Select(n => n.Type.Length > 0 ? $"{n.Full} ({n.Type})" : n.Full));

    private static string DetailsText(Person? p) => p == null ? "" : string.Join("  ·  ", new[]
    {
        p.Title, p.Description, p.UserId.Length > 0 ? "Ref " + p.UserId : "", p.FamilySearchId.Length > 0 ? "FamilySearch " + p.FamilySearchId : "",
        p.Email, p.Url, p.Address, p.Phone, p.Tags.Count > 0 ? "Tags: " + string.Join(", ", p.Tags) : "",
    }.Where(x => x.Length > 0));

    private static string EventText(Event? e, Index ix)
    {
        if (e == null) return "";
        var parts = new List<string> { EventTypes.Label(e) };
        if (!e.Date.IsEmpty) parts.Add(Reports.ReportText.Date(e.Date));
        if (e.Place.Length > 0) parts.Add(e.Place);
        if (e.Value.Length > 0 && e.Value != "Y") parts.Add(e.Value);
        if (e.Citations.Count > 0) parts.Add(e.Citations.Count == 1 ? "1 source" : $"{e.Citations.Count} sources");
        if (e.Note.Length > 0) parts.Add("note: " + Short(e.Note));
        return string.Join("  ·  ", parts);
    }

    private static string Couple(Family f, Index ix) =>
        string.Join(" and ", f.PartnerIds.Select(id => ix.Tree.Person(id)?.DisplayName).OfType<string>().DefaultIfEmpty("A family"));

    private static string FamilyText(Family? f, Index ix)
    {
        if (f == null) return "";
        var kids = f.Children.Select(c => ix.Tree.Person(c.ChildId)?.DisplayName).OfType<string>().ToList();
        return Couple(f, ix) + (kids.Count > 0 ? $"; children: {string.Join(", ", kids)}" : "");
    }

    private static string Short(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var one = text.Replace('\n', ' ').Trim();
        return one.Length > 120 ? one[..120].TrimEnd() + "…" : one;
    }

    // ---- applying ----

    /// <summary>Everything in your tree that applying these changes may modify (for undo). New records are found by the undo itself.</summary>
    public static List<Entity> Touched(FamilyTree ours, FamilyTree theirs, IEnumerable<ShareChange> changes)
    {
        var o = new Index(ours);
        var t = new Index(theirs);
        var touched = new HashSet<Entity>();
        foreach (var c in changes)
        {
            var parts = c.Key.Split(':');
            var uid = Guid.Parse(parts[1]);
            switch (parts[0])
            {
                case "s":
                    if (o.Sources.TryGetValue(uid, out var s)) touched.Add(s);
                    break;
                case "p" or "pe":
                    if (o.People.TryGetValue(uid, out var p)) { touched.Add(p); Around(ours, p, touched); }
                    break;
                case "f" or "fe" or "fc":
                    if (o.Families.TryGetValue(uid, out var f)) Family(ours, f, touched);
                    // People a new or changed family will link to.
                    if (t.Families.TryGetValue(uid, out var tf))
                        foreach (var pid in tf.PartnerIds.Concat(tf.Children.Select(k => k.ChildId)))
                            if (theirs.Person(pid) is { } tp && o.People.TryGetValue(tp.Uid, out var op)) touched.Add(op);
                    break;
            }
        }
        return touched.ToList();

        static void Around(FamilyTree tree, Person p, HashSet<Entity> into)
        {
            foreach (var fid in p.ParentFamilyIds.Concat(p.SpouseFamilyIds))
                if (tree.Family(fid) is { } f) Family(tree, f, into);
        }

        static void Family(FamilyTree tree, Family f, HashSet<Entity> into)
        {
            into.Add(f);
            foreach (var id in f.PartnerIds.Concat(f.Children.Select(c => c.ChildId)))
                if (tree.Person(id) is { } p) into.Add(p);
        }
    }

    /// <summary>Applies accepted changes to your tree, in an order that keeps links valid.</summary>
    public static void Apply(FamilyTree ours, FamilyTree theirs, IEnumerable<ShareChange> accepted)
    {
        var list = accepted.ToList();
        var t = new Index(theirs);
        var o = new Index(ours);

        long SourceFor(long theirsId)
        {
            if (!theirs.Sources.TryGetValue(theirsId, out var ts)) return theirsId;
            if (o.Sources.TryGetValue(ts.Uid, out var os)) return os.Id;
            var copy = LineageStore.Copy(ts);
            copy.Id = 0;
            copy.RepositoryId = null;
            ours.AddSource(copy);
            o.Sources[copy.Uid] = copy;
            return copy.Id;
        }
        long? PersonFor(long? theirsId) => theirs.Person(theirsId) is { } tp && o.People.TryGetValue(tp.Uid, out var op) ? op.Id : null;
        // Photo tags point at people by id: move them to our ids, dropping tags of people we don't have.
        void Retag(List<MediaLink> media)
        {
            foreach (var m in media)
            {
                foreach (var r in m.Regions) r.PersonId = PersonFor(r.PersonId) ?? 0;
                m.Regions.RemoveAll(r => r.PersonId == 0);
            }
        }
        Event CopyEvent(Event e)
        {
            var c = LineageStore.Copy(e);
            foreach (var cit in c.Citations) cit.SourceId = SourceFor(cit.SourceId);
            foreach (var con in c.Conflicts) if (con.SourceId is { } sid) con.SourceId = SourceFor(sid);
            Retag(c.Media);
            return c;
        }
        void Replace(List<Event> into, Event theirsEvent)
        {
            var i = into.FindIndex(x => x.Id == theirsEvent.Id);
            if (i >= 0) into[i] = CopyEvent(theirsEvent);
            else into.Add(CopyEvent(theirsEvent));
        }
        static (string Kind, Guid Uid, string Tail) Parse(ShareChange c)
        {
            var parts = c.Key.Split(':', 3);
            return (parts[0], Guid.Parse(parts[1]), parts.Length > 2 ? parts[2] : "");
        }

        // 1. Sources.
        foreach (var c in list.Where(c => Parse(c).Kind == "s"))
        {
            var uid = Parse(c).Uid;
            if (!t.Sources.TryGetValue(uid, out var ts)) continue;   // removed sources stay: facts may still cite them
            if (o.Sources.TryGetValue(uid, out var os))
            {
                (os.Title, os.ShortTitle, os.Author, os.Publication, os.CallNumber, os.Text, os.Note, os.Type, os.Quality, os.Kind) =
                    (ts.Title, ts.ShortTitle, ts.Author, ts.Publication, ts.CallNumber, ts.Text, ts.Note, ts.Type, ts.Quality, ts.Kind);
            }
            else SourceFor(ts.Id);
        }

        // 2. New people, without their family links (the families bring those).
        var added = new List<(Person Ours, Person Theirs)>();
        foreach (var c in list.Where(c => Parse(c) is ("p", _, "") && c.Kind == ChangeKind.Added))
        {
            if (!t.People.TryGetValue(Parse(c).Uid, out var tp) || o.People.ContainsKey(tp.Uid)) continue;
            var p = LineageStore.Copy(tp);
            p.Id = 0;
            p.ParentFamilyIds.Clear();
            p.SpouseFamilyIds.Clear();
            p.ResearchIds.Clear();
            p.DnaMatches.Clear();
            p.Events.Clear();
            foreach (var cit in p.Citations) cit.SourceId = SourceFor(cit.SourceId);
            ours.AddPerson(p);
            o.People[p.Uid] = p;
            added.Add((p, tp));
        }
        // Their facts and photo tags, once every new person has an id here.
        foreach (var (p, tp) in added)
        {
            p.Events.AddRange(tp.Events.Select(CopyEvent));
            Retag(p.Media);
        }

        // 3. New families.
        foreach (var c in list.Where(c => Parse(c) is ("f", _, "") && c.Kind == ChangeKind.Added))
        {
            if (!t.Families.TryGetValue(Parse(c).Uid, out var tf) || o.Families.ContainsKey(tf.Uid)) continue;
            var f = LineageStore.Copy(tf);
            f.Id = 0;
            f.ResearchIds.Clear();
            f.Partner1Id = PersonFor(tf.Partner1Id);
            f.Partner2Id = PersonFor(tf.Partner2Id);
            f.Children.Clear();
            f.Events.Clear();
            f.Events.AddRange(tf.Events.Select(CopyEvent));
            foreach (var cit in f.Citations) cit.SourceId = SourceFor(cit.SourceId);
            ours.AddFamily(f);
            o.Families[f.Uid] = f;
            foreach (var pid in f.PartnerIds)
                if (ours.Person(pid) is { } partner && !partner.SpouseFamilyIds.Contains(f.Id)) partner.SpouseFamilyIds.Add(f.Id);
            foreach (var link in tf.Children)
                if (PersonFor(link.ChildId) is { } kid && ours.Person(kid) is { } child)
                {
                    var copy = LineageStore.Copy(link);
                    copy.ChildId = kid;
                    if (copy.Sealing != null) copy.Sealing = CopyEvent(copy.Sealing);
                    ours.AddChild(f, child, copy);
                }
        }

        // 4. Changes to people.
        foreach (var c in list)
        {
            var (kind, uid, rest) = Parse(c);
            if (kind is not ("p" or "pe") || !o.People.TryGetValue(uid, out var op)) continue;
            var tp = t.People.GetValueOrDefault(uid);
            switch (kind, rest)
            {
                case ("p", "names") when tp != null:
                    op.Names.Clear();
                    op.Names.AddRange(tp.Names.Select(LineageStore.Copy));
                    break;
                case ("p", "sex") when tp != null: op.Sex = tp.Sex; break;
                case ("p", "note") when tp != null: op.Note = tp.Note; break;
                case ("p", "details") when tp != null:
                    (op.Title, op.Description, op.UserId, op.FamilySearchId, op.Email, op.Url, op.Address, op.Phone, op.Colour) =
                        (tp.Title, tp.Description, tp.UserId, tp.FamilySearchId, tp.Email, tp.Url, tp.Address, tp.Phone, tp.Colour);
                    op.Tags.Clear();
                    op.Tags.AddRange(tp.Tags);
                    break;
                case ("p", "sources") when tp != null:
                    op.Citations.Clear();
                    op.Citations.AddRange(tp.Citations.Select(x => { var y = LineageStore.Copy(x); y.SourceId = SourceFor(x.SourceId); return y; }));
                    break;
                case ("pe", _):
                    var id = Guid.Parse(rest);
                    if (tp?.Events.FirstOrDefault(e => e.Id == id) is { } te) Replace(op.Events, te);
                    else op.Events.RemoveAll(e => e.Id == id);
                    break;
            }
        }

        // 5. Changes to families.
        foreach (var c in list)
        {
            var (kind, uid, rest) = Parse(c);
            if (kind is not ("f" or "fe" or "fc") || !o.Families.TryGetValue(uid, out var of) || !t.Families.TryGetValue(uid, out var tf)) continue;
            switch (kind, rest)
            {
                case ("f", "couple"):
                    foreach (var pid in of.PartnerIds) ours.Person(pid)?.SpouseFamilyIds.Remove(of.Id);
                    of.Partner1Id = PersonFor(tf.Partner1Id);
                    of.Partner2Id = PersonFor(tf.Partner2Id);
                    foreach (var pid in of.PartnerIds)
                        if (ours.Person(pid) is { } partner && !partner.SpouseFamilyIds.Contains(of.Id)) partner.SpouseFamilyIds.Add(of.Id);
                    break;
                case ("f", "status"): of.Status = tf.Status; of.NoChildren = tf.NoChildren; break;
                case ("f", "note"): of.Note = tf.Note; break;
                case ("fe", _):
                    var id = Guid.Parse(rest);
                    if (tf.Events.FirstOrDefault(e => e.Id == id) is { } te) Replace(of.Events, te);
                    else of.Events.RemoveAll(e => e.Id == id);
                    break;
                case ("fc", _):
                    var kidUid = Guid.Parse(rest);
                    var theirsLink = tf.Children.FirstOrDefault(k => theirs.Person(k.ChildId)?.Uid == kidUid);
                    if (!o.People.TryGetValue(kidUid, out var child)) break;
                    var mine = of.Children.FirstOrDefault(k => k.ChildId == child.Id);
                    if (theirsLink == null)
                    {
                        if (mine != null) of.Children.Remove(mine);
                        child.ParentFamilyIds.Remove(of.Id);
                    }
                    else
                    {
                        var copy = LineageStore.Copy(theirsLink);
                        copy.ChildId = child.Id;
                        if (copy.Sealing != null) copy.Sealing = CopyEvent(copy.Sealing);
                        if (mine != null) of.Children[of.Children.IndexOf(mine)] = copy;
                        else ours.AddChild(of, child, copy);
                    }
                    break;
            }
        }

        // 6. Removals last: families, then people.
        foreach (var c in list.Where(c => Parse(c) is ("f", _, "") && c.Kind == ChangeKind.Removed))
            if (o.Families.TryGetValue(Parse(c).Uid, out var f)) RemoveFamily(ours, f);
        foreach (var c in list.Where(c => Parse(c) is ("p", _, "") && c.Kind == ChangeKind.Removed))
            if (o.People.TryGetValue(Parse(c).Uid, out var p)) ours.RemovePerson(p);
    }

    private static void RemoveFamily(FamilyTree tree, Family f)
    {
        foreach (var pid in f.PartnerIds) tree.Person(pid)?.SpouseFamilyIds.Remove(f.Id);
        foreach (var c in f.Children) tree.Person(c.ChildId)?.ParentFamilyIds.Remove(f.Id);
        tree.Families.Remove(f.Id);
    }

    /// <summary>Remembers refused changes so they aren't offered again (unless the relative changes them again).</summary>
    public static void Reject(ShareRecord share, IEnumerable<ShareChange> refused)
    {
        foreach (var c in refused) share.Rejected.Add(c.Fingerprint);
    }
}
