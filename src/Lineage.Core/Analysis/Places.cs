using System.Globalization;
using System.Text;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>One place name exactly as written in the tree, with how much it is used.</summary>
/// <param name="Location">The GPS most events at this place carry, or null when none do.</param>
/// <param name="FirstYear">Earliest year of an event here; 0 when no event is dated.</param>
/// <param name="LastYear">Latest year of an event here; 0 when no event is dated.</param>
public sealed record PlaceEntry(string Name, int EventCount, int PersonCount, int FamilyCount,
                                GeoPoint? Location, int FirstYear, int LastYear);

/// <summary>
/// Place names that probably mean the same place, and the one we suggest keeping. Renaming each
/// variant to <see cref="Standard"/> (see <see cref="PlaceIndex.ApplySuggestion"/>) merges them.
/// </summary>
public sealed record PlaceSuggestion(string Standard, IReadOnlyList<string> Variants, int EventCount, string Reason)
{
    /// <summary>The standard name followed by the variants.</summary>
    public IEnumerable<string> Names => Variants.Prepend(Standard);
}

/// <summary>
/// One level of the place tree (country → region → … → place). Built from the comma-separated
/// parts of each place name, last part first, so "Leeds, Yorkshire, England" sits under
/// England → Yorkshire.
/// </summary>
public sealed class PlaceNode
{
    /// <summary>This level's own name ("Yorkshire").</summary>
    public string Name { get; init; } = "";
    /// <summary>The name from this level up to the country ("Yorkshire, England").</summary>
    public string FullName { get; init; } = "";
    public List<PlaceNode> Children { get; } = new();
    /// <summary>Place names in the tree that end exactly at this level (usually one, or none).</summary>
    public List<PlaceEntry> Entries { get; } = new();
    /// <summary>Events at this level and every level below it.</summary>
    public int EventCount { get; internal set; }
}

/// <summary>
/// Place management: lists every place used by person and family events (Place and Place2),
/// suggests names that are the same place written differently, and renames, merges or sets GPS
/// in one go. Edits change many records, so take a snapshot of <see cref="Touched"/> first for undo.
/// </summary>
public static class PlaceIndex
{
    /// <summary>Every event in the tree with the record that owns it (LDS child sealings belong to the family).</summary>
    private static IEnumerable<(Event Event, Entity Owner)> Events(FamilyTree tree)
    {
        foreach (var p in tree.People.Values)
            foreach (var e in p.Events) yield return (e, p);
        foreach (var f in tree.Families.Values)
        {
            foreach (var e in f.Events) yield return (e, f);
            foreach (var c in f.Children)
                if (c.Sealing is { } s) yield return (s, f);
        }
    }

    private sealed class Tally
    {
        public int Events;
        public readonly HashSet<long> People = new(), Families = new();
        public readonly Dictionary<GeoPoint, int> Locations = new();
        public int First, Last;

        public void Add(Event e, Entity owner, bool withLocation)
        {
            Events++;
            if (owner is Person) People.Add(owner.Id); else Families.Add(owner.Id);
            if (withLocation && e.Location is { } g) Locations[g] = Locations.GetValueOrDefault(g) + 1;
            foreach (var y in new[] { e.Date.Year, e.Date.End?.Year ?? 0 })
            {
                if (y <= 0) continue;
                if (First == 0 || y < First) First = y;
                if (y > Last) Last = y;
            }
        }
    }

    /// <summary>Every place name used in the tree (trimmed, otherwise exactly as written), sorted by name.</summary>
    public static List<PlaceEntry> Build(FamilyTree tree)
    {
        var tallies = new Dictionary<string, Tally>(StringComparer.Ordinal);
        Tally Get(string name) => tallies.TryGetValue(name, out var t) ? t : tallies[name] = new Tally();
        foreach (var (e, owner) in Events(tree))
        {
            var place = e.Place.Trim();
            var place2 = e.Place2.Trim();
            // Location is the GPS of Place, so it never counts towards Place2.
            if (place.Length > 0) Get(place).Add(e, owner, withLocation: true);
            if (place2.Length > 0 && place2 != place) Get(place2).Add(e, owner, withLocation: false);
        }
        return tallies
            .Select(kv => new PlaceEntry(kv.Key, kv.Value.Events, kv.Value.People.Count, kv.Value.Families.Count,
                Common(kv.Value.Locations), kv.Value.First, kv.Value.Last))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static GeoPoint? Common(Dictionary<GeoPoint, int> counts) =>
        counts.Count == 0 ? null : counts.OrderByDescending(kv => kv.Value).First().Key;

    /// <summary>Person and family records with an event at any of these places — snapshot them before an edit.</summary>
    public static List<Entity> Touched(FamilyTree tree, IEnumerable<string> names)
    {
        var set = names.Select(n => n.Trim()).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<Entity>();
        var result = new List<Entity>();
        foreach (var (e, owner) in Events(tree))
            if ((set.Contains(e.Place.Trim()) || set.Contains(e.Place2.Trim())) && seen.Add(owner))
                result.Add(owner);
        return result;
    }

    /// <summary>
    /// Renames a place on every event (Place and Place2; exact match ignoring surrounding spaces).
    /// Renaming to a name already in use merges the two. Events keep their own GPS; one without GPS
    /// takes the GPS already used for the new name. Renaming to "" removes the place and its GPS.
    /// Returns the number of events changed.
    /// </summary>
    public static int Rename(FamilyTree tree, string from, string to)
    {
        from = from.Trim();
        to = to.Trim();
        if (from.Length == 0 || from == to) return 0;
        var target = to.Length == 0 ? null : LocationOf(tree, to);
        var changed = 0;
        foreach (var (e, _) in Events(tree))
        {
            var hit = false;
            if (e.Place.Trim() == from)
            {
                e.Place = to;
                e.Location = to.Length == 0 ? null : e.Location ?? target;
                hit = true;
            }
            if (e.Place2.Trim() == from) { e.Place2 = to; hit = true; }
            if (hit) changed++;
        }
        return changed;
    }

    /// <summary>Renames every variant of a suggestion to its standard name. Returns the number of events changed.</summary>
    public static int ApplySuggestion(FamilyTree tree, PlaceSuggestion suggestion) =>
        suggestion.Variants.Sum(v => Rename(tree, v, suggestion.Standard));

    /// <summary>Sets (or with null, clears) the GPS of every event at this place. Returns the number of events changed.</summary>
    public static int SetLocation(FamilyTree tree, string place, GeoPoint? location)
    {
        place = place.Trim();
        if (place.Length == 0) return 0;
        var changed = 0;
        foreach (var (e, _) in Events(tree))
            if (e.Place.Trim() == place && e.Location != location)
            {
                e.Location = location;
                changed++;
            }
        return changed;
    }

    private static GeoPoint? LocationOf(FamilyTree tree, string place)
    {
        var counts = new Dictionary<GeoPoint, int>();
        foreach (var (e, _) in Events(tree))
            if (e.Location is { } g && e.Place.Trim() == place) counts[g] = counts.GetValueOrDefault(g) + 1;
        return Common(counts);
    }

    /// <summary>The comma-separated parts of a place name, trimmed, with empty parts left out.</summary>
    public static List<string> Parts(string place) =>
        place.Split(',')
             .Select(p => string.Join(' ', p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
             .Where(p => p.Length > 0).ToList();

    /// <summary>
    /// Arranges places as a tree for browsing: country (the last part) at the top, then each part
    /// before it. Levels with the same name are combined ignoring case. Sorted by name at every level.
    /// </summary>
    public static List<PlaceNode> Hierarchy(IEnumerable<PlaceEntry> entries)
    {
        var roots = new List<PlaceNode>();
        var nodes = new Dictionary<string, PlaceNode>(StringComparer.OrdinalIgnoreCase);
        // Most-used first, so each level is shown with its most common spelling.
        foreach (var entry in entries.OrderByDescending(e => e.EventCount).ThenBy(e => e.Name, StringComparer.Ordinal))
        {
            var parts = Parts(entry.Name);
            if (parts.Count == 0) continue;
            PlaceNode? parent = null;
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                var full = string.Join(", ", parts.Skip(i));
                if (!nodes.TryGetValue(full, out var node))
                {
                    node = new PlaceNode { Name = parts[i], FullName = full };
                    nodes[full] = node;
                    (parent?.Children ?? roots).Add(node);
                }
                node.EventCount += entry.EventCount;
                parent = node;
            }
            parent!.Entries.Add(entry);
        }
        Sort(roots);
        return roots;
    }

    private static void Sort(List<PlaceNode> nodes)
    {
        nodes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        foreach (var n in nodes) Sort(n.Children);
    }

    // ---- Suggestions ----

    /// <summary>Suggests names to merge, from the places in the tree.</summary>
    public static List<PlaceSuggestion> Suggest(FamilyTree tree) => Suggest(Build(tree));

    /// <summary>
    /// Groups names that probably mean the same place. Two names are grouped when they are the
    /// same after ignoring case, spacing, punctuation and common abbreviations, or when one is a
    /// leading part of the other ("Leeds" and "Leeds, Yorkshire, England") and the shorter name
    /// fits only one fuller name. Names whose first parts differ are never grouped. The suggested
    /// standard is the most complete name, preferring full words over abbreviations, tidy spelling,
    /// and then the most used.
    /// </summary>
    public static List<PlaceSuggestion> Suggest(IReadOnlyList<PlaceEntry> entries)
    {
        var infos = new List<(PlaceEntry Entry, string[] Parts, int Abbreviations)>();
        foreach (var e in entries)
        {
            var parts = Normalize(e.Name, out var abbreviations);
            if (parts.Count > 0) infos.Add((e, parts.ToArray(), abbreviations));
        }

        var keys = infos.Select(i => string.Join(",", i.Parts)).Distinct().ToList();
        var keyIndex = keys.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        var keyParts = keys.Select(k => k.Split(',')).ToArray();
        var parent = Enumerable.Range(0, keys.Count).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

        // Shorter names that are the leading parts of fuller ones.
        var extensions = new Dictionary<string, List<int>>();
        for (var i = 0; i < keys.Count; i++)
            for (var n = 1; n < keyParts[i].Length; n++)
            {
                var prefix = string.Join(",", keyParts[i].Take(n));
                if (!extensions.TryGetValue(prefix, out var list)) extensions[prefix] = list = new List<int>();
                list.Add(i);
            }
        var viaPrefix = new HashSet<int>();
        for (var i = 0; i < keys.Count; i++)
        {
            if (!extensions.TryGetValue(keys[i], out var longer)) continue;
            var longest = longer.MaxBy(j => keyParts[j].Length);
            // Every fuller name must be on one line ("Leeds, Yorkshire" ⊂ "Leeds, Yorkshire, England");
            // "Springfield" with both Illinois and Massachusetts is ambiguous.
            if (!longer.All(j => IsPrefix(keyParts[j], keyParts[longest]))) continue;
            parent[Root(i)] = Root(longest);
            viaPrefix.Add(i);
        }

        var result = new List<PlaceSuggestion>();
        foreach (var group in infos.GroupBy(i => Root(keyIndex[string.Join(",", i.Parts)])))
        {
            var members = group.ToList();
            if (members.Count < 2) continue;
            var ranked = members
                .OrderByDescending(m => m.Parts.Length)
                .ThenBy(m => m.Abbreviations)
                .ThenBy(m => Untidiness(m.Entry.Name))
                .ThenByDescending(m => m.Entry.EventCount)
                .ThenByDescending(m => m.Entry.Name.Length)
                .ThenBy(m => m.Entry.Name, StringComparer.Ordinal)
                .ToList();
            var shorter = members.Any(m => viaPrefix.Contains(keyIndex[string.Join(",", m.Parts)]));
            result.Add(new PlaceSuggestion(
                ranked[0].Entry.Name,
                ranked.Skip(1).Select(m => m.Entry.Name).ToList(),
                members.Sum(m => m.Entry.EventCount),
                shorter ? "Some names leave out the county, state or country" : "The same place written differently"));
        }
        return result.OrderBy(s => s.Standard, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsPrefix(string[] shorter, string[] longer) =>
        shorter.Length <= longer.Length && shorter.Select((p, i) => p == longer[i]).All(x => x);

    /// <summary>
    /// The form used to compare place names: one entry per part, lower case, no accents or
    /// punctuation, common abbreviations expanded ("Yorks" → yorkshire, "MA" → massachusetts,
    /// "U.S.A." → usa), and Saint/St/Ste treated as the same word.
    /// </summary>
    public static IReadOnlyList<string> Normalize(string place) => Normalize(place, out _);

    private static List<string> Normalize(string place, out int abbreviations)
    {
        abbreviations = 0;
        var result = new List<string>();
        foreach (var raw in place.Split(','))
        {
            var words = Words(raw);
            if (words.Count == 0) continue;
            result.Add(NormalizePart(words, first: result.Count == 0, ref abbreviations));
        }
        return result;
    }

    private static string NormalizePart(List<string> words, bool first, ref int abbreviations)
    {
        var joined = string.Join(' ', words);
        if (Countries.TryGetValue(joined, out var country))
        {
            if (country.Abbreviated) abbreviations++;
            return country.Name;
        }
        // State codes only after the first part: "LA, California" is Los Angeles, not Louisiana.
        if (!first && States.TryGetValue(joined, out var state))
        {
            if (state != joined) abbreviations++;
            return state;
        }
        var output = new List<string>(words.Count);
        foreach (var w in words)
        {
            var word = w;
            if (words.Count > 1 && w is "co" or "cnty" or "cty" or "county")
            {
                if (w != "county") abbreviations++;
                // "Suffolk Co., MA" and "Suffolk, MA" are both common for the same county.
                if (!first) continue;
                word = "county";
            }
            else if (words.Count > 1 && InPhrase.TryGetValue(w, out var phrase)) { word = phrase; abbreviations++; }
            else if (Abbreviations.TryGetValue(w, out var full)) { word = full; abbreviations++; }
            else if (w is "saint" or "sainte" or "ste" or "street") word = "st";   // kept as written, compared as equal
            output.Add(word);
        }
        if (output.Count == 0) return joined;
        var part = string.Join(' ', output);
        return Countries.TryGetValue(part, out var c) ? c.Name : part;
    }

    /// <summary>Lower-case words without accents or punctuation; "U.S.A." becomes one word "usa".</summary>
    private static List<string> Words(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\'' or '’' or '‘' or '`') continue;   // "St Mary's" = "St Marys"
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        var result = new List<string>();
        var run = new StringBuilder();
        void Flush()
        {
            if (run.Length > 0) result.Add(run.ToString());
            run.Clear();
        }
        foreach (var w in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w.Length == 1 && char.IsLetter(w[0])) { run.Append(w); continue; }
            Flush();
            result.Add(w);
        }
        Flush();
        return result;
    }

    /// <summary>Signs of careless typing: odd spacing around commas, or a part in all lower or all upper case.</summary>
    private static int Untidiness(string name)
    {
        var n = 0;
        if (name.Contains("  ", StringComparison.Ordinal)) n++;
        if (name.Contains(" ,", StringComparison.Ordinal)) n++;
        for (var i = 0; i < name.Length - 1; i++)
            if (name[i] == ',' && name[i + 1] != ' ') { n++; break; }
        foreach (var part in Parts(name))
        {
            var letters = part.Where(char.IsLetter).ToList();
            if (letters.Count == 0) continue;
            if (letters.All(char.IsLower)) n++;
            else if (letters.Count > 3 && letters.All(char.IsUpper)) n++;
        }
        return n;
    }

    private static readonly Dictionary<string, (string Name, bool Abbreviated)> Countries = new()
    {
        ["usa"] = ("usa", false), ["us"] = ("usa", false), ["united states"] = ("usa", false),
        ["united states of america"] = ("usa", false),
        ["uk"] = ("united kingdom", false), ["united kingdom"] = ("united kingdom", false),
        ["eng"] = ("england", true), ["engl"] = ("england", true),
        ["scot"] = ("scotland", true),
    };

    /// <summary>US state postal codes and traditional abbreviations.</summary>
    private static readonly Dictionary<string, string> States = BuildStates();

    /// <summary>Whether a part from <see cref="Normalize(string)"/> is a US state (by name or abbreviation).</summary>
    internal static bool IsUsState(string normalizedPart) => States.ContainsKey(normalizedPart);

    private static Dictionary<string, string> BuildStates()
    {
        var d = new Dictionary<string, string>();
        foreach (var (code, name) in new[]
        {
            ("al", "alabama"), ("ak", "alaska"), ("az", "arizona"), ("ar", "arkansas"), ("ca", "california"),
            ("co", "colorado"), ("ct", "connecticut"), ("de", "delaware"), ("fl", "florida"), ("ga", "georgia"),
            ("hi", "hawaii"), ("id", "idaho"), ("il", "illinois"), ("in", "indiana"), ("ia", "iowa"),
            ("ks", "kansas"), ("ky", "kentucky"), ("la", "louisiana"), ("me", "maine"), ("md", "maryland"),
            ("ma", "massachusetts"), ("mi", "michigan"), ("mn", "minnesota"), ("ms", "mississippi"),
            ("mo", "missouri"), ("mt", "montana"), ("ne", "nebraska"), ("nv", "nevada"), ("nh", "new hampshire"),
            ("nj", "new jersey"), ("nm", "new mexico"), ("ny", "new york"), ("nc", "north carolina"),
            ("nd", "north dakota"), ("oh", "ohio"), ("ok", "oklahoma"), ("or", "oregon"), ("pa", "pennsylvania"),
            ("ri", "rhode island"), ("sc", "south carolina"), ("sd", "south dakota"), ("tn", "tennessee"),
            ("tx", "texas"), ("ut", "utah"), ("vt", "vermont"), ("va", "virginia"), ("wa", "washington"),
            ("wv", "west virginia"), ("wi", "wisconsin"), ("wy", "wyoming"), ("dc", "district of columbia"),
            // Older forms still common in records.
            ("ala", "alabama"), ("ariz", "arizona"), ("ark", "arkansas"), ("calif", "california"),
            ("colo", "colorado"), ("conn", "connecticut"), ("del", "delaware"), ("fla", "florida"),
            ("ill", "illinois"), ("ind", "indiana"), ("kan", "kansas"), ("kans", "kansas"),
            ("mass", "massachusetts"), ("mich", "michigan"), ("minn", "minnesota"), ("miss", "mississippi"),
            ("mont", "montana"), ("neb", "nebraska"), ("nebr", "nebraska"), ("nev", "nevada"),
            ("okla", "oklahoma"), ("ore", "oregon"), ("oreg", "oregon"), ("penn", "pennsylvania"),
            ("penna", "pennsylvania"), ("tenn", "tennessee"), ("tex", "texas"), ("wash", "washington"),
            ("wis", "wisconsin"), ("wisc", "wisconsin"), ("wyo", "wyoming"), ("w va", "west virginia"),
        })
        {
            d[code] = name;
            d[name] = name;
        }
        return d;
    }

    /// <summary>Abbreviations expanded wherever they appear (English counties and similar).</summary>
    private static readonly Dictionary<string, string> Abbreviations = new()
    {
        ["yorks"] = "yorkshire", ["lancs"] = "lancashire", ["glos"] = "gloucestershire", ["gloucs"] = "gloucestershire",
        ["hants"] = "hampshire", ["wilts"] = "wiltshire", ["berks"] = "berkshire", ["bucks"] = "buckinghamshire",
        ["herts"] = "hertfordshire", ["beds"] = "bedfordshire", ["northants"] = "northamptonshire",
        ["notts"] = "nottinghamshire", ["staffs"] = "staffordshire", ["warks"] = "warwickshire",
        ["warwicks"] = "warwickshire", ["worcs"] = "worcestershire", ["leics"] = "leicestershire",
        ["lincs"] = "lincolnshire", ["derbys"] = "derbyshire", ["oxon"] = "oxfordshire", ["cambs"] = "cambridgeshire",
        ["salop"] = "shropshire", ["northumb"] = "northumberland", ["cumb"] = "cumberland", ["hunts"] = "huntingdonshire",
        ["middx"] = "middlesex", ["mddx"] = "middlesex", ["som"] = "somerset", ["glam"] = "glamorgan",
        ["twp"] = "township", ["dept"] = "department", ["prov"] = "province",
    };

    /// <summary>Abbreviations expanded only next to other words ("Mt Vernon"; a part that is just "MT" is Montana).</summary>
    private static readonly Dictionary<string, string> InPhrase = new()
    {
        ["mt"] = "mount", ["ft"] = "fort", ["n"] = "north", ["s"] = "south", ["e"] = "east", ["w"] = "west",
    };
}
