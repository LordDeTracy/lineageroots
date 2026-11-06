using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.Core.FamilySearch;

/// <summary>One fact compared between Lineage Roots and FamilySearch.</summary>
public sealed record FieldDiff(string Key, string Label, string Local, string Remote)
{
    public bool Differs => !string.Equals(Normal(Local), Normal(Remote), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dates are compared as dates ("14 March 1842" = "14 MAR 1842"); other text ignoring case, accents and
    /// punctuation but never digits, so 1850 and 1851 stay different.
    /// </summary>
    private string Normal(string s)
    {
        if (Key.EndsWith(".date", StringComparison.Ordinal)) return GenDate.Parse(s).ToGedcom();
        var plain = new System.Text.StringBuilder();
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>Compares and copies data from FamilySearch into the local tree. Only ever writes locally.</summary>
public static class FsSync
{
    private static readonly (string Tag, string Label)[] Facts =
        { ("BIRT", "Birth"), ("CHR", "Christening"), ("DEAT", "Death"), ("BURI", "Burial") };

    public static List<FieldDiff> Compare(Person local, FsPerson remote)
    {
        var list = new List<FieldDiff>
        {
            new("given", "Given names", local.PrimaryName.Given, remote.Given),
            new("surname", "Surname", local.PrimaryName.Surname, remote.Surname),
            new("sex", "Sex", local.Sex == Sex.Unknown ? "" : local.Sex.ToString(), remote.Sex == Sex.Unknown ? "" : remote.Sex.ToString()),
        };
        foreach (var (tag, label) in Facts)
        {
            var mine = local.Find(tag);
            var theirs = remote.Fact(tag);
            list.Add(new FieldDiff($"{tag}.date", $"{label} date", mine?.Date.ToGedcom() ?? "", theirs?.Date ?? ""));
            list.Add(new FieldDiff($"{tag}.place", $"{label} place", mine?.Place ?? "", theirs?.Place ?? ""));
        }
        return list.Where(d => d.Local.Length > 0 || d.Remote.Length > 0).ToList();
    }

    /// <summary>Copies the chosen FamilySearch values (by <see cref="FieldDiff.Key"/>) onto the local person.</summary>
    public static void Apply(Person local, FsPerson remote, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            switch (key)
            {
                case "given": local.PrimaryName.Given = remote.Given; break;
                case "surname": local.PrimaryName.Surname = remote.Surname; break;
                case "sex": local.Sex = remote.Sex; break;
                default:
                    var (tag, field) = (key.Split('.')[0], key.Split('.')[1]);
                    if (remote.Fact(tag) is not { } fact) break;
                    var e = local.Find(tag);
                    if (e == null) { e = Event.Of(tag); local.Events.Add(e); }
                    if (field == "date") e.Date = GenDate.Parse(fact.Date);
                    else e.Place = fact.Place;
                    break;
            }
        }
        if (local.FamilySearchId.Length == 0) local.FamilySearchId = remote.Id;
    }

    public static Person ToPerson(FsPerson fs)
    {
        var p = new Person { Sex = fs.Sex, FamilySearchId = fs.Id };
        p.PrimaryName.Given = fs.Given;
        p.PrimaryName.Surname = fs.Surname;
        foreach (var f in fs.Facts)
            if (f.Date.Length > 0 || f.Place.Length > 0)
                p.Events.Add(Event.Of(f.Tag, GenDate.Parse(f.Date), f.Place));
        return p;
    }

    /// <summary>
    /// Adds FamilySearch ancestors (numbered 1 = <paramref name="root"/>, 2n = father, 2n+1 = mother)
    /// to the tree. People already in the tree — in the parent slot, or anywhere with the same
    /// FamilySearch ID — are linked rather than duplicated. Returns how many people were added.
    /// </summary>
    public static int AddAncestors(FamilyTree tree, Person root, IReadOnlyList<FsPerson> ancestry)
    {
        var byNumber = ancestry.Where(a => a.AscendancyNumber != null).ToDictionary(a => a.AscendancyNumber!.Value);
        var byFsId = tree.People.Values.Where(p => p.FamilySearchId.Length > 0)
                         .GroupBy(p => p.FamilySearchId).ToDictionary(g => g.Key, g => g.First());
        var local = new Dictionary<int, Person> { [1] = root };
        if (root.FamilySearchId.Length == 0 && byNumber.TryGetValue(1, out var me)) root.FamilySearchId = me.Id;
        var added = 0;
        foreach (var n in byNumber.Keys.Where(n => n > 1).OrderBy(x => x))
        {
            if (!local.TryGetValue(n / 2, out var child)) continue; // the child line is missing
            var fs = byNumber[n];
            var isFather = n % 2 == 0;
            var family = tree.Family(child.ParentFamilyIds.FirstOrDefault());
            var existing = family == null ? null : tree.Person(isFather ? family.Partner1Id : family.Partner2Id);

            Person parent;
            if (existing != null)
            {
                parent = existing;
                if (parent.FamilySearchId.Length == 0) parent.FamilySearchId = fs.Id;
            }
            else
            {
                if (!byFsId.TryGetValue(fs.Id, out parent!))
                {
                    parent = tree.AddPerson(ToPerson(fs));
                    byFsId[fs.Id] = parent;
                    added++;
                }
                if (family == null)
                {
                    family = tree.AddFamily(new Family());
                    tree.AddChild(family, child);
                }
                tree.SetPartner(family, parent, second: !isFather);
            }
            local[n] = parent;
        }
        return added;
    }

    /// <summary>
    /// Adds a FamilySearch descendancy (numbered "1", "1-S", "1.1", "1.1-S"…) below <paramref name="root"/>.
    /// Spouses and children already in the tree (in the family, or anywhere with the same FamilySearch ID) are
    /// linked rather than duplicated. Where someone has several spouses, children go with the first one,
    /// since the numbers don't say which. Returns how many people were added.
    /// </summary>
    public static int AddDescendants(FamilyTree tree, Person root, IReadOnlyList<FsPerson> descendancy)
    {
        var byFsId = tree.People.Values.Where(p => p.FamilySearchId.Length > 0)
                         .GroupBy(p => p.FamilySearchId).ToDictionary(g => g.Key, g => g.First());
        var local = new Dictionary<string, Person> { ["1"] = root };
        var familyOf = new Dictionary<string, Family>();
        var me = descendancy.FirstOrDefault(d => d.DescendancyNumber == "1");
        if (root.FamilySearchId.Length == 0 && me != null) root.FamilySearchId = me.Id;
        var added = 0;

        Person Find(FsPerson fs, IEnumerable<Person> nearby)
        {
            var match = nearby.FirstOrDefault(p => p.FamilySearchId == fs.Id)
                        ?? nearby.FirstOrDefault(p => p.FamilySearchId.Length == 0 && p.DisplayName == fs.FullName);
            if (match != null) { if (match.FamilySearchId.Length == 0) match.FamilySearchId = fs.Id; return match; }
            if (byFsId.TryGetValue(fs.Id, out var known)) return known;
            var person = tree.AddPerson(ToPerson(fs));
            byFsId[fs.Id] = person;
            added++;
            return person;
        }

        // Generation by generation: a generation's people, then their spouses (whose children come in the next one).
        foreach (var fs in descendancy.Where(d => d.DescendancyNumber is { } n && n != "1")
                                      .OrderBy(d => d.DescendancyNumber!.Count(c => c == '.'))
                                      .ThenBy(d => d.DescendancyNumber!.Contains("-S") ? 1 : 0))
        {
            var number = fs.DescendancyNumber!;
            var spouseMark = number.IndexOf("-S", StringComparison.Ordinal);
            if (spouseMark > 0)
            {
                var key = number[..spouseMark];
                if (!local.TryGetValue(key, out var partner)) continue;
                var families = partner.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>().ToList();
                var spouses = families.Select(f => tree.Person(f.Partner1Id == partner.Id ? f.Partner2Id : f.Partner1Id)).OfType<Person>();
                var spouse = Find(fs, spouses);
                var family = families.FirstOrDefault(f => f.Partner1Id == spouse.Id || f.Partner2Id == spouse.Id);
                if (family == null)
                {
                    family = families.FirstOrDefault(f => f.Partner1Id == null || f.Partner2Id == null) ?? tree.AddFamily(new Family());
                    if (family.Partner1Id != partner.Id && family.Partner2Id != partner.Id) tree.SetPartner(family, partner, second: partner.Sex == Sex.Female);
                    tree.SetPartner(family, spouse, second: family.Partner2Id == null);
                }
                local[number] = spouse;
                familyOf.TryAdd(key, family);
            }
            else
            {
                var dot = number.LastIndexOf('.');
                if (dot < 0) continue;
                var key = number[..dot];
                if (!local.TryGetValue(key, out var parent)) continue;
                if (!familyOf.TryGetValue(key, out var family))
                {
                    family = parent.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>().FirstOrDefault() ?? tree.AddFamily(new Family());
                    if (family.Partner1Id != parent.Id && family.Partner2Id != parent.Id) tree.SetPartner(family, parent, second: parent.Sex == Sex.Female);
                    familyOf[key] = family;
                }
                var children = parent.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>()
                                     .SelectMany(f => f.Children).Select(c => tree.Person(c.ChildId)).OfType<Person>().ToList();
                var child = Find(fs, children);
                if (!children.Contains(child) && !family.Children.Any(c => c.ChildId == child.Id)) tree.AddChild(family, child);
                local[number] = child;
            }
        }
        return added;
    }
}
