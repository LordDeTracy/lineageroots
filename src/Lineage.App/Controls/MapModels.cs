using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>What happened at a pin; decides its colour.</summary>
public enum MapPinKind { Birth, Marriage, Death, Residence, Other }

/// <summary>A place on the map: a person's event with its coordinates, and its year (0 when undated).</summary>
public sealed record MapPin(GeoPoint Location, string Label, string Detail, MapPinKind Kind = MapPinKind.Other, long? PersonId = null, int Year = 0);

/// <summary>
/// A move between two places (e.g. a parent's birthplace to a child's), drawn as an arc with an arrowhead.
/// <see cref="Year"/> is when it happened (the child's birth), 0 when unknown.
/// </summary>
public sealed record MapLine(GeoPoint From, GeoPoint To, string Label = "", int Year = 0);

/// <summary>Builds pins and migration lines for a set of people from the coordinates on their events.</summary>
public static class MapData
{
    public static MapPinKind KindOf(string tag) => tag switch
    {
        "BIRT" or "CHR" or "BAPM" => MapPinKind.Birth,
        "MARR" or "MARB" or "MARL" or "ENGA" => MapPinKind.Marriage,
        "DEAT" or "BURI" or "CREM" => MapPinKind.Death,
        "RESI" or "CENS" or "EMIG" or "IMMI" or "NATU" => MapPinKind.Residence,
        _ => MapPinKind.Other,
    };

    /// <summary>
    /// A pin for every located, non-private event of these people and their marriages, and a line from
    /// each parent's birthplace to the child's where both are known.
    /// </summary>
    public static (IReadOnlyList<MapPin> Pins, IReadOnlyList<MapLine> Lines) For(FamilyTree tree, IEnumerable<Person> people)
    {
        var pins = new List<MapPin>();
        var lines = new List<MapLine>();
        var families = new HashSet<long>();
        var seenLines = new HashSet<(GeoPoint, GeoPoint)>();
        foreach (var p in people.DistinctBy(p => p.Id))
        {
            foreach (var e in p.Events.Where(e => e.Location != null && !e.Private))
                pins.Add(new MapPin(e.Location!.Value, p.DisplayName, Describe(e), KindOf(e.Tag), p.Id, YearOf(e)));

            foreach (var (family, spouse, _) in tree.FamiliesOf(p))
            {
                if (!families.Add(family.Id)) continue;
                var couple = spouse != null ? $"{p.DisplayName} & {spouse.DisplayName}" : p.DisplayName;
                foreach (var e in family.Events.Where(e => e.Location != null && !e.Private))
                    pins.Add(new MapPin(e.Location!.Value, couple, Describe(e), KindOf(e.Tag), p.Id, YearOf(e)));
            }

            if (p.Birth?.Location is not { } born) continue;
            foreach (var parent in tree.ParentsOf(p))
            {
                if (parent.Birth?.Location is not { } from || Close(from, born) || !seenLines.Add((from, born))) continue;
                lines.Add(new MapLine(from, born, $"{parent.DisplayName} → {p.DisplayName}", YearOf(p.Birth!)));
            }
        }
        return (pins, lines);
    }

    private static int YearOf(Event e) => e.Date.Kind == DateKind.Text ? 0 : e.Date.Year;

    private static bool Close(GeoPoint a, GeoPoint b) =>
        Math.Abs(a.Latitude - b.Latitude) < 0.01 && Math.Abs(a.Longitude - b.Longitude) < 0.01;

    /// <summary>"Birth 1842 · Harrow, Middlesex, England".</summary>
    private static string Describe(Event e)
    {
        var what = EventTypes.Label(e);
        if (e.Date.Year > 0) what += $" {e.Date.Year}";
        return e.Place.Length > 0 ? $"{what} · {e.Place}" : what;
    }
}
