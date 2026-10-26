using Lineage.Core.Model;

namespace Lineage.Core.Analysis;

/// <summary>
/// Birth and death years for lists and searches: the first birth, christening or baptism (or death, burial or
/// cremation) that has a year. Unlike <see cref="Person.Birth"/>, an undated birth record doesn't hide a dated
/// christening. Plain loops rather than LINQ because searches call this for every person.
/// </summary>
internal static class VitalYears
{
    public static int Birth(Person p) => First(p, "BIRT", "CHR", "BAPM");
    public static int Death(Person p) => First(p, "DEAT", "BURI", "CREM");

    private static int First(Person p, string a, string b, string c)
    {
        int second = 0, third = 0;
        foreach (var e in p.Events)
        {
            var y = e.Date.Kind == DateKind.Text ? 0 : e.Date.Year;
            if (y <= 0) continue;
            if (e.Tag == a) return y;
            if (e.Tag == b && second == 0) second = y;
            else if (e.Tag == c && third == 0) third = y;
        }
        return second != 0 ? second : third;
    }
}
