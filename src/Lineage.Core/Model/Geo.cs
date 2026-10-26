using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lineage.Core.Model;

/// <summary>A latitude/longitude pair in decimal degrees (WGS 84).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    /// <summary>"42.26260, -71.80230" — the form most map sites accept when pasted back.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.00000}, {Longitude:0.00000}");

    /// <summary>GEDCOM 5.5.1 MAP values: LATI "N42.2626", LONG "W71.8023".</summary>
    [JsonIgnore] public string GedcomLatitude => (Latitude < 0 ? "S" : "N") + Math.Abs(Latitude).ToString("0.######", CultureInfo.InvariantCulture);
    [JsonIgnore] public string GedcomLongitude => (Longitude < 0 ? "W" : "E") + Math.Abs(Longitude).ToString("0.######", CultureInfo.InvariantCulture);

    [JsonIgnore] public string OpenStreetMapUrl => string.Create(CultureInfo.InvariantCulture,
        $"https://www.openstreetmap.org/?mlat={Latitude:0.######}&mlon={Longitude:0.######}#map=15/{Latitude:0.######}/{Longitude:0.######}");

    public static GeoPoint? FromGedcom(string lati, string longi) =>
        ParseAxis(lati, isLatitude: true) is { } la && ParseAxis(longi, isLatitude: false) is { } lo ? new GeoPoint(la, lo) : null;

    /// <summary>
    /// Reads coordinates the way people paste them: "42.2626, -71.8023", "N42.2626 W71.8023",
    /// "42.2626° N, 71.8023° W" or degrees-minutes-seconds like 42°15'45"N 71°48'08"W.
    /// </summary>
    public static GeoPoint? Parse(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return null;
        if (Split(s) is not { } pair) return null;
        var (first, second) = pair;
        if (ParseAxis(first, isLatitude: true) is { } la && ParseAxis(second, isLatitude: false) is { } lo)
            return new GeoPoint(la, lo);
        // Written longitude-first with hemisphere letters ("W71.8 N42.2"): swap back.
        if (HasHemisphere(second, "NS") && ParseAxis(second, isLatitude: true) is { } la2 && ParseAxis(first, isLatitude: false) is { } lo2)
            return new GeoPoint(la2, lo2);
        return null;
    }

    private static readonly Regex PrefixPair = new(@"^([NSEW].+?)\s+([NSEW].+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SuffixPair = new(@"^(.+?[NSEW])\s*(.+?[NSEW])$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Splits pasted text into its two coordinates.</summary>
    private static (string, string)? Split(string s)
    {
        var sep = s.IndexOfAny(new[] { ',', ';' });
        if (sep > 0) return (s[..sep], s[(sep + 1)..]);
        foreach (var rx in new[] { PrefixPair, SuffixPair })
            if (rx.Match(s) is { Success: true } m) return (m.Groups[1].Value, m.Groups[2].Value);
        var words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 2 ? (words[0], words[1]) : null;
    }

    private static readonly Regex Axis = new(
        @"^(?<h1>[NSEW])?\s*(?<deg>-?\d+(?:\.\d+)?)\s*°?\s*(?:(?<min>\d+(?:\.\d+)?)\s*['′]\s*)?(?:(?<sec>\d+(?:\.\d+)?)\s*(?:""|″|'')\s*)?(?<h2>[NSEW])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static bool HasHemisphere(string s, string letters) => s.ToUpperInvariant().IndexOfAny(letters.ToCharArray()) >= 0;

    private static double? ParseAxis(string text, bool isLatitude)
    {
        var m = Axis.Match(text.Trim());
        if (!m.Success) return null;
        var h = (m.Groups["h1"].Success ? m.Groups["h1"].Value : m.Groups["h2"].Value).ToUpperInvariant();
        if (m.Groups["h1"].Success && m.Groups["h2"].Success) return null;
        if (h.Length > 0 && (isLatitude ? "NS" : "EW").IndexOf(h[0]) < 0) return null;
        double Num(string g) => m.Groups[g].Success ? double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
        var deg = Num("deg");
        var value = Math.Abs(deg) + Num("min") / 60 + Num("sec") / 3600;
        if (deg < 0 || h is "S" or "W") value = -value;
        var limit = isLatitude ? 90 : 180;
        return Math.Abs(value) <= limit ? value : null;
    }
}
