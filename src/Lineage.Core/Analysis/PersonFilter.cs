using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>
/// Narrows the people list beyond names: a place (any of their events or marriages), years born or died between,
/// the years they were alive, and sex. A christening stands in for a missing birth and a burial for a missing death.
/// Zero (or null) means "no limit".
/// </summary>
public sealed record PersonFilter(string Place = "", int BornFrom = 0, int BornTo = 0, int DiedFrom = 0, int DiedTo = 0,
                                  int LivedFrom = 0, int LivedTo = 0, Sex? Sex = null)
{
    /// <summary>The longest a life is assumed to last when only one end of it is known.</summary>
    private const int LongestLife = 100;

    public bool IsEmpty => Place.Trim().Length == 0 && BornFrom == 0 && BornTo == 0 && DiedFrom == 0 && DiedTo == 0
                           && LivedFrom == 0 && LivedTo == 0 && Sex == null;

    public int Count => (Place.Trim().Length > 0 ? 1 : 0) + (BornFrom > 0 || BornTo > 0 ? 1 : 0)
                        + (DiedFrom > 0 || DiedTo > 0 ? 1 : 0) + (LivedFrom > 0 || LivedTo > 0 ? 1 : 0) + (Sex != null ? 1 : 0);

    public bool Matches(FamilyTree tree, Person p)
    {
        if (Sex is { } sex && p.Sex != sex) return false;
        var born = p.Birth?.Date.Year ?? 0;
        var died = p.Death?.Date.Year ?? 0;
        if ((BornFrom > 0 || BornTo > 0) && !Within(born, BornFrom, BornTo)) return false;
        if ((DiedFrom > 0 || DiedTo > 0) && !Within(died, DiedFrom, DiedTo)) return false;
        if (LivedFrom > 0 || LivedTo > 0)
        {
            if (born == 0 && died == 0) return false;
            var start = born > 0 ? born : died - LongestLife;
            var end = died > 0 ? died : born + LongestLife;
            if ((LivedTo > 0 && start > LivedTo) || (LivedFrom > 0 && end < LivedFrom)) return false;
        }
        var place = Place.Trim();
        if (place.Length > 0)
        {
            var events = p.Events.Concat(p.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>().SelectMany(f => f.Events));
            if (!events.Any(e => e.Place.Contains(place, StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }

    /// <summary>
    /// Whether the words appear in what's written about the person: their notes, their events' notes and descriptions,
    /// and their marriages' notes ("killed by Indians" finds the man whose note says so).
    /// </summary>
    public static bool Mentions(FamilyTree tree, Person p, string phrase)
    {
        var text = phrase.Trim();
        if (text.Length < 3) return false;
        bool In(string s) => s.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (In(p.Note) || p.Events.Any(e => In(e.Note) || In(e.Description) || In(e.Value))) return true;
        return p.SpouseFamilyIds.Select(id => tree.Family(id)).OfType<Family>()
            .Any(f => In(f.Note) || f.Events.Any(e => In(e.Note) || In(e.Description)));
    }

    private static bool Within(int year, int from, int to) =>
        year > 0 && (from == 0 || year >= from) && (to == 0 || year <= to);
}
