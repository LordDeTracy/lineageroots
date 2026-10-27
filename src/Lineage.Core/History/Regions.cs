using Lineage.Core.Analysis;

namespace Lineage.Core.History;

/// <summary>
/// The country a place is in, as far as history and census records are concerned. England, Wales,
/// Scotland and Ireland are kept apart because their records are. Works from the name alone: the
/// last part that names a country, a US state, a Canadian province, an Australian state or a
/// British, Irish or German county wins ("Norfolk, Virginia" is in the United States).
/// </summary>
public static class Regions
{
    public const string UnitedStates = "United States", England = "England", Wales = "Wales", Scotland = "Scotland",
        Ireland = "Ireland", Canada = "Canada", Australia = "Australia", NewZealand = "New Zealand",
        Germany = "Germany", Netherlands = "Netherlands", France = "France", Norway = "Norway",
        Sweden = "Sweden", Denmark = "Denmark", Belgium = "Belgium", Switzerland = "Switzerland", Italy = "Italy",
        Spain = "Spain", Austria = "Austria", Poland = "Poland";

    /// <summary>England, Wales, Scotland and Ireland (all of Ireland was in the United Kingdom until 1922).</summary>
    public static readonly string[] BritishIsles = { England, Wales, Scotland, Ireland };

    /// <summary>The region of a place, or null when the name doesn't say.</summary>
    public static string? Of(string place)
    {
        var parts = PlaceIndex.Normalize(place);
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            var part = parts[i];
            if (Countries.TryGetValue(part, out var country)) return country;
            // A country as the last word of a longer part: "Sp Neth (Belgium)", "of Scotland".
            var space = part.LastIndexOf(' ');
            if (space > 0 && Countries.TryGetValue(part[(space + 1)..], out country)) return country;
            // A first part is usually a town, and towns share names with counties and states
            // ("Victoria, British Columbia"); a place that is only one part is trusted.
            if (i == 0 && parts.Count > 1) break;
            if (PlaceIndex.IsUsState(part)) return UnitedStates;
            if (Subdivisions.TryGetValue(part, out var region)) return region;
            if (part.Contains("yorkshire", StringComparison.Ordinal)) return England;   // "West Riding of Yorkshire"
        }
        return null;
    }

    /// <summary>Whether any part of the place is one of these names (compared after <see cref="PlaceIndex.Normalize(string)"/>).</summary>
    public static bool Mentions(string place, IEnumerable<string> names)
    {
        var parts = PlaceIndex.Normalize(place);
        return names.Any(n => parts.Contains(n));
    }

    /// <summary>Country names, other-language names and the abbreviations genealogists use ("Swed", "Neth").</summary>
    private static readonly Dictionary<string, string> Countries = new()
    {
        ["usa"] = UnitedStates, ["america"] = UnitedStates, ["new england"] = UnitedStates,
        ["england"] = England, ["wales"] = Wales, ["scotland"] = Scotland,
        ["ireland"] = Ireland, ["eire"] = Ireland, ["northern ireland"] = Ireland, ["irish free state"] = Ireland,
        ["ire"] = Ireland, ["irl"] = Ireland,
        ["canada"] = Canada, ["can"] = Canada, ["australia"] = Australia, ["aus"] = Australia, ["aust"] = Australia,
        ["new zealand"] = NewZealand, ["nz"] = NewZealand,
        ["germany"] = Germany, ["deutschland"] = Germany, ["prussia"] = Germany, ["preussen"] = Germany,
        ["ger"] = Germany, ["germ"] = Germany, ["deu"] = Germany,
        ["netherlands"] = Netherlands, ["the netherlands"] = Netherlands, ["holland"] = Netherlands, ["nederland"] = Netherlands,
        ["neth"] = Netherlands, ["nl"] = Netherlands,
        ["france"] = France, ["fra"] = France,
        ["norway"] = Norway, ["norge"] = Norway, ["nor"] = Norway, ["norw"] = Norway,
        ["sweden"] = Sweden, ["sverige"] = Sweden, ["swed"] = Sweden, ["swe"] = Sweden,
        ["denmark"] = Denmark, ["danmark"] = Denmark, ["den"] = Denmark, ["denm"] = Denmark, ["dk"] = Denmark,
        ["belgium"] = Belgium, ["belgie"] = Belgium, ["belgique"] = Belgium, ["flanders"] = Belgium,
        ["switzerland"] = Switzerland, ["schweiz"] = Switzerland, ["suisse"] = Switzerland, ["swit"] = Switzerland,
        ["italy"] = Italy, ["italia"] = Italy, ["spain"] = Spain, ["espana"] = Spain,
        ["austria"] = Austria, ["osterreich"] = Austria, ["poland"] = Poland, ["polska"] = Poland,
    };

    private static readonly Dictionary<string, string> Subdivisions = Build(
        (Canada, new[]
        {
            "ontario", "quebec", "nova scotia", "new brunswick", "manitoba", "british columbia", "prince edward island",
            "saskatchewan", "alberta", "newfoundland", "newfoundland and labrador", "yukon", "northwest territories",
            "nunavut", "upper canada", "lower canada", "canada west", "canada east", "ont", "bc", "pei", "sask", "alta", "nfld",
        }),
        (Australia, new[]
        {
            "new south wales", "victoria", "queensland", "south australia", "western australia", "tasmania",
            "van diemens land", "northern territory", "nsw", "qld", "tas",
        }),
        (England, new[]
        {
            "bedfordshire", "berkshire", "buckinghamshire", "cambridgeshire", "cheshire", "cornwall", "cumberland",
            "cumbria", "derbyshire", "devon", "devonshire", "dorset", "durham", "essex", "gloucestershire", "hampshire",
            "herefordshire", "hertfordshire", "huntingdonshire", "kent", "lancashire", "leicestershire", "lincolnshire",
            "london", "greater london", "middlesex", "norfolk", "northamptonshire", "northumberland", "nottinghamshire",
            "oxfordshire", "rutland", "shropshire", "somerset", "staffordshire", "suffolk", "surrey", "sussex",
            "east sussex", "west sussex", "warwickshire", "westmorland", "wiltshire", "worcestershire",
            "isle of wight", "greater manchester", "merseyside", "west midlands", "tyne and wear",
        }),
        (Wales, new[]
        {
            "anglesey", "brecknockshire", "breconshire", "caernarvonshire", "carnarvonshire", "caernarfonshire",
            "cardiganshire", "carmarthenshire", "denbighshire", "flintshire", "glamorgan", "merionethshire",
            "monmouthshire", "montgomeryshire", "pembrokeshire", "radnorshire", "gwynedd", "powys", "dyfed",
            "clwyd", "gwent",
        }),
        (Scotland, new[]
        {
            "aberdeenshire", "angus", "forfarshire", "argyll", "argyllshire", "ayrshire", "banffshire", "berwickshire",
            "bute", "buteshire", "caithness", "clackmannanshire", "dumfriesshire", "dunbartonshire", "dumbartonshire",
            "east lothian", "haddingtonshire", "fife", "inverness shire", "invernessshire", "kincardineshire",
            "kinross shire", "kinrossshire", "kirkcudbrightshire", "lanarkshire", "midlothian", "edinburghshire",
            "moray", "elginshire", "nairnshire", "orkney", "peeblesshire", "perthshire", "renfrewshire",
            "ross and cromarty", "roxburghshire", "selkirkshire", "shetland", "stirlingshire", "sutherland",
            "west lothian", "linlithgowshire", "wigtownshire",
        }),
        (Ireland, new[]
        {
            "antrim", "armagh", "carlow", "cavan", "clare", "cork", "donegal", "down", "dublin", "fermanagh", "galway",
            "kerry", "kildare", "kilkenny", "laois", "leix", "leitrim", "limerick", "londonderry", "derry", "longford",
            "louth", "mayo", "meath", "monaghan", "offaly", "roscommon", "sligo", "tipperary", "tyrone", "waterford",
            "westmeath", "wexford", "wicklow",
        }),
        (Germany, new[]
        {
            "bavaria", "bayern", "wurttemberg", "wuerttemberg", "baden", "hesse", "hessen", "saxony", "sachsen",
            "hanover", "hannover", "westphalia", "westfalen", "mecklenburg", "pomerania", "pommern",
            "schleswig holstein", "rhineland", "rheinland", "brandenburg",
        }));

    private static Dictionary<string, string> Build(params (string Region, string[] Names)[] groups)
    {
        var d = new Dictionary<string, string>();
        foreach (var (region, names) in groups)
            foreach (var n in names) d[n] = region;
        return d;
    }
}
