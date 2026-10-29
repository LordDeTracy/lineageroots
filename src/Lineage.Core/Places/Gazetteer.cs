using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lineage.Core.Analysis;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.Core.Places;

/// <summary>Where a place was found: a town, or only the state or province it's in.</summary>
public enum GeoPrecision { Town, State }

/// <summary>
/// A gazetteer match: the point, what it matched ("Leeds, England, United Kingdom"), how precise it is, and whether
/// it's certain (the country is known and only one town fits) or just the likeliest of several.
/// </summary>
public sealed record GeoMatch(GeoPoint Point, string Description, GeoPrecision Precision, bool Sure);

/// <summary>A place in the tree with no GPS yet, and the match the gazetteer suggests for it.</summary>
public sealed record GeoSuggestion(PlaceEntry Place, GeoMatch Match);

/// <summary>
/// Finds coordinates for place names, offline, from a built-in copy of the GeoNames gazetteer (every town of 1,000
/// people or more; geonames.org, CC BY 4.0). The country comes from <see cref="Regions"/> (so "Swed", "Co. Cork" and US
/// state abbreviations work), the town from the first part of the name (or the second, for "St Mary's, Harrow"), and
/// the state or county in the name picks between towns with the same name. A US or Canadian state alone gives its
/// rough centre. Places whose country can't be told are left alone rather than guessed.
/// </summary>
public sealed class Gazetteer
{
    private sealed record Town(string Name, string Country, string Admin1, double Lat, double Lon, long Population);

    private readonly Dictionary<string, List<Town>> _byName = new();
    private readonly Dictionary<string, string> _admin1 = new();     // "GB.ENG" → "England"
    private readonly Dictionary<string, string> _countries = new();  // "GB" → "United Kingdom"
    private readonly Dictionary<string, GeoPoint> _stateCentres = new();

    private static readonly Lazy<Gazetteer> BuiltIn = new(() =>
    {
        using var stream = typeof(Gazetteer).Assembly.GetManifestResourceStream("Lineage.Core.Places.gazetteer.tsv.gz")
                           ?? throw new InvalidOperationException("The built-in gazetteer is missing.");
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        return new Gazetteer(new StreamReader(gz, Encoding.UTF8));
    });

    /// <summary>The built-in gazetteer (read on first use, about a second).</summary>
    public static Gazetteer Default => BuiltIn.Value;

    /// <summary>Reads the gazetteer format: "#countries", "#admin1" and "#places" sections of tab-separated lines.</summary>
    public Gazetteer(TextReader reader)
    {
        var section = "";
        var sums = new Dictionary<string, (double Lat, double Lon, double Weight)>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith('#')) { section = line; continue; }
            var f = line.Split('\t');
            switch (section)
            {
                case "#countries" when f.Length >= 2: _countries[f[0]] = f[1]; break;
                case "#admin1" when f.Length >= 2: _admin1[f[0]] = f[1]; break;
                case "#places" when f.Length >= 7:
                    var town = new Town(f[0], f[2], f[3], Num(f[4]), Num(f[5]), long.TryParse(f[6], out var pop) ? pop : 0);
                    Add(Key(f[0]), town);
                    if (f[1].Length > 0) Add(Key(f[1]), town);
                    var admin = $"{town.Country}.{town.Admin1}";
                    var w = Math.Max(1, town.Population);
                    var s = sums.GetValueOrDefault(admin);
                    sums[admin] = (s.Lat + town.Lat * w, s.Lon + town.Lon * w, s.Weight + w);
                    break;
            }
        }
        foreach (var (admin, s) in sums) _stateCentres[admin] = new GeoPoint(s.Lat / s.Weight, s.Lon / s.Weight);
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private void Add(string key, Town town)
    {
        if (key.Length == 0) return;
        if (!_byName.TryGetValue(key, out var list)) _byName[key] = list = new List<Town>(1);
        if (!list.Contains(town)) list.Add(town);
    }

    /// <summary>A name in comparable form: lower case, no accents or punctuation, "Saint" and "St." alike.</summary>
    public static string Key(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is '\'' or '’') continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w is "saint" or "sainte" or "ste" ? "st" : w);
        return string.Join(' ', words);
    }

    /// <summary>The ISO country code (and, for Britain, the constituent country's name) for a region.</summary>
    private static (string Code, string? Admin1Name)? CountryOf(string? region, IReadOnlyList<string> parts) => region switch
    {
        Regions.UnitedStates => ("US", null),
        Regions.England or Regions.Wales or Regions.Scotland => ("GB", region),
        Regions.Ireland => parts.Any(p => p is "northern ireland" or "antrim" or "armagh" or "down" or "fermanagh" or "londonderry" or "derry" or "tyrone")
            ? ("GB", "Northern Ireland") : ("IE", null),
        Regions.Canada => ("CA", null),
        Regions.Australia => ("AU", null),
        Regions.NewZealand => ("NZ", null),
        Regions.Germany => ("DE", null),
        Regions.Netherlands => ("NL", null),
        Regions.France => ("FR", null),
        Regions.Norway => ("NO", null),
        Regions.Sweden => ("SE", null),
        Regions.Denmark => ("DK", null),
        Regions.Belgium => ("BE", null),
        Regions.Switzerland => ("CH", null),
        Regions.Italy => ("IT", null),
        Regions.Spain => ("ES", null),
        Regions.Austria => ("AT", null),
        Regions.Poland => ("PL", null),
        _ => null,
    };

    /// <summary>Finds a place, or null when it can't be placed with reasonable confidence.</summary>
    /// <summary>
    /// A map link for a place without coordinates: straight to the built-in gazetteer's match when there is one
    /// (OpenStreetMap's own search is rate-limited and can pick a same-named town in another country), else that search.
    /// </summary>
    public static string MapUrl(string place) =>
        place.Trim().Length > 0 && Default.Locate(place) is { } match ? match.Point.OpenStreetMapUrl
        : "https://www.openstreetmap.org/search?query=" + Uri.EscapeDataString(place.Trim());

    public GeoMatch? Locate(string place)
    {
        var parts = PlaceIndex.Parts(place);
        if (parts.Count == 0) return null;
        var normalized = PlaceIndex.Normalize(place);
        if (CountryOf(Regions.Of(place), normalized) is not { } country) return null;
        var otherWords = normalized.Select(Key).ToHashSet();

        // The town is normally the first part; a church or farm name may come before it.
        for (var i = 0; i < Math.Min(2, parts.Count); i++)
        {
            if (!_byName.TryGetValue(Key(parts[i]), out var all)) continue;
            var candidates = all.Where(t => t.Country == country.Code
                                            && (country.Admin1Name == null || Admin1Name(t) == country.Admin1Name)).ToList();
            if (candidates.Count == 0) continue;
            // In a country with states or provinces, the one named in the place must match when there is one.
            var withState = candidates.Where(t => otherWords.Contains(Key(Admin1Name(t)))).ToList();
            var named = normalized.Skip(i + 1).Any(p => StateNamed(country.Code, p));
            if (named)
            {
                if (withState.Count == 0) continue;
                candidates = withState;
            }
            var best = candidates.OrderByDescending(t => t.Population).First();
            var sure = candidates.Count == 1 || (named && withState.Count == 1)
                       // One town far bigger than any other of the name: what people almost always mean.
                       || candidates.Where(t => t != best).All(t => t.Population * 20 < best.Population);
            return new GeoMatch(new GeoPoint(best.Lat, best.Lon), Describe(best), GeoPrecision.Town, sure);
        }

        // No town: a state or province on its own gives its rough centre.
        foreach (var p in normalized)
        {
            var admin = _admin1.FirstOrDefault(kv => kv.Key.StartsWith(country.Code + ".", StringComparison.Ordinal)
                                                     && Key(kv.Value) == Key(p));
            if (admin.Key != null && country.Code is "US" or "CA" or "AU" && _stateCentres.TryGetValue(admin.Key, out var centre))
                return new GeoMatch(centre, $"{admin.Value}, {CountryName(country.Code)} (centre)", GeoPrecision.State, false);
        }
        return null;
    }

    private bool StateNamed(string countryCode, string part) =>
        countryCode is "US" or "CA" or "AU"
        && _admin1.Any(kv => kv.Key.StartsWith(countryCode + ".", StringComparison.Ordinal) && Key(kv.Value) == Key(part));

    private string Admin1Name(Town t) => _admin1.GetValueOrDefault($"{t.Country}.{t.Admin1}", "");

    private string CountryName(string code) => _countries.GetValueOrDefault(code, code);

    private string Describe(Town t)
    {
        var admin = Admin1Name(t);
        return string.Join(", ", new[] { t.Name, admin, CountryName(t.Country) }.Where(x => x.Length > 0).Distinct());
    }

    /// <summary>Suggestions for every place in the tree that has no GPS yet.</summary>
    public List<GeoSuggestion> Suggest(FamilyTree tree) =>
        PlaceIndex.Build(tree).Where(p => p.Location == null)
            .Select(p => (p, m: Locate(p.Name)))
            .Where(x => x.m != null)
            .Select(x => new GeoSuggestion(x.p, x.m!))
            .ToList();
}
