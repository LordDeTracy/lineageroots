using System.Globalization;
using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>
/// Finds people by name the way a family historian types: words in any order, partial words, any of the person's
/// names (birth, married, also-known-as), accents ignored. With "sounds like" it also catches the spelling variants
/// old records are full of (Smyth/Smith, Jon/John, Kameron/Cameron). A four-digit year narrows to people born or
/// died within two years of it, and a number can also be a person's id. Exact matches come first, then by name.
/// </summary>
/// <remarks>
/// Each distinct name word in the tree is compared with the query once per call, and people are then matched by
/// looking up their words, so a 15,000-person tree takes a few milliseconds.
/// </remarks>
public static class NameSearch
{
    /// <summary>How far a birth or death year may be from a year typed in the query.</summary>
    public const int YearSlack = 2;

    private const byte Prefix = 0, Inside = 1, Fuzzy = 2, NoMatch = 3;
    private static readonly char[] Separators = { ' ', ',', ';', '\t', '\n', '\r' };

    public static List<Person> Find(FamilyTree tree, string query, bool soundsLike)
    {
        var words = new List<string>();
        var numbers = new List<(long Value, bool Year)>();
        foreach (var raw in (query ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.TrimStart('#');
            if (token.Length is > 0 and <= 18 && token.All(char.IsAsciiDigit))
            {
                numbers.Add((long.Parse(token, CultureInfo.InvariantCulture), token.Length == 4));
                continue;
            }
            foreach (var w in Duplicates.Simplify(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (!words.Contains(w)) words.Add(w);
        }
        if (words.Count == 0 && numbers.Count == 0) return Ranked(tree.People.Values.Select(p => (p, 0)));

        var matcher = new Matcher(words, soundsLike);
        var best = new byte[words.Count];
        var hits = new List<(Person, int)>();
        foreach (var p in tree.People.Values)
        {
            Array.Fill(best, NoMatch);
            foreach (var n in p.Names)
            {
                matcher.Visit(n.Given, best);
                matcher.Visit(n.Surname, best);
                matcher.Visit(n.Nickname, best);
            }
            var rank = 0;
            foreach (var q in best) rank = Math.Max(rank, q);
            if (rank == NoMatch || !MatchesNumbers(p, numbers, out var byId)) continue;
            hits.Add((p, byId ? -1 : rank));
        }
        return Ranked(hits);
    }

    private static List<Person> Ranked(IEnumerable<(Person Person, int Rank)> hits) =>
        hits.OrderBy(h => h.Rank)
            .ThenBy(h => h.Person.Names.Count > 0 ? h.Person.Names[0].SortKey : "", StringComparer.Ordinal)
            .ThenBy(h => h.Person.Id)
            .Select(h => h.Person).ToList();

    /// <summary>Every number must be the person's id or, for a four-digit number, near their birth or death year.</summary>
    private static bool MatchesNumbers(Person p, List<(long Value, bool Year)> numbers, out bool byId)
    {
        byId = false;
        if (numbers.Count == 0) return true;
        int? birth = null, death = null;
        foreach (var (value, year) in numbers)
        {
            if (p.Id == value) { byId = true; continue; }
            if (!year) return false;
            birth ??= VitalYears.Birth(p);
            death ??= VitalYears.Death(p);
            if (!Near(birth.Value, value) && !Near(death.Value, value)) return false;
        }
        return true;
    }

    private static bool Near(int year, long target) => year > 0 && Math.Abs(year - target) <= YearSlack;

    /// <summary>Compares each distinct name word with the query once and remembers the answer.</summary>
    private sealed class Matcher
    {
        private readonly string[] _query;
        private readonly string[] _querySound;
        private readonly bool _soundsLike;
        private readonly Dictionary<string, int[]> _parts = new(StringComparer.Ordinal);   // name text → word ids
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);       // simplified word → id
        private readonly List<byte> _quality = new();                                      // [id * query words + i]

        public Matcher(List<string> query, bool soundsLike)
        {
            _query = query.ToArray();
            _soundsLike = soundsLike;
            _querySound = _query.Select(q => soundsLike && q.Length >= 3 ? Duplicates.Soundex(q) : "").ToArray();
        }

        public void Visit(string text, byte[] best)
        {
            if (text.Length == 0 || _query.Length == 0) return;
            if (!_parts.TryGetValue(text, out var ids)) _parts[text] = ids = Split(text);
            var q = _query.Length;
            foreach (var id in ids)
                for (var i = 0; i < q; i++)
                {
                    var v = _quality[id * q + i];
                    if (v < best[i]) best[i] = v;
                }
        }

        private int[] Split(string text)
        {
            var words = Duplicates.Simplify(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return Array.Empty<int>();
            var ids = new int[words.Length > 1 ? words.Length + 1 : 1];
            for (var i = 0; i < words.Length; i++) ids[i] = Id(words[i]);
            // "van der Berg" or "O'Brien" can also be typed as one word.
            if (words.Length > 1) ids[^1] = Id(string.Concat(words));
            return ids;
        }

        private int Id(string word)
        {
            if (_ids.TryGetValue(word, out var id)) return id;
            id = _ids.Count;
            _ids[word] = id;
            var sound = _soundsLike && word.Length >= 3 ? Duplicates.Soundex(word) : "";
            for (var i = 0; i < _query.Length; i++) _quality.Add(Compare(_query[i], _querySound[i], word, sound));
            return id;
        }

        private byte Compare(string q, string qSound, string word, string wordSound)
        {
            if (word.StartsWith(q, StringComparison.Ordinal)) return Prefix;
            if (word.Contains(q, StringComparison.Ordinal)) return Inside;
            if (!_soundsLike) return NoMatch;
            if (qSound.Length > 0 && qSound == wordSound) return Fuzzy;
            var max = q.Length >= 8 ? 2 : q.Length >= 5 ? 1 : 0;
            return max > 0 && Math.Abs(q.Length - word.Length) <= max && Distance(q, word, max) <= max ? Fuzzy : NoMatch;
        }
    }

    /// <summary>
    /// Damerau-Levenshtein distance (optimal string alignment: insert, delete, change or swap two neighbouring
    /// letters), giving up as soon as it must exceed <paramref name="max"/> (and then returning max + 1).
    /// </summary>
    public static int Distance(string a, string b, int max)
    {
        var m = b.Length;
        if (a.Length > 64 || m > 64) return a == b ? 0 : max + 1;
        Span<int> before = stackalloc int[m + 1];
        Span<int> prev = stackalloc int[m + 1];
        Span<int> cur = stackalloc int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            var rowMin = i;
            for (var j = 1; j <= m; j++)
            {
                var v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, before[j - 2] + 1);
                cur[j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > max) return max + 1;
            var spare = before;
            before = prev;
            prev = cur;
            cur = spare;
        }
        return prev[m];
    }
}
