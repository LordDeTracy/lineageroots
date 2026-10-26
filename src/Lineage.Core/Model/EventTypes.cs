namespace Lineage.Core.Model;

/// <summary>Human-readable names for GEDCOM event/fact tags.</summary>
public static class EventTypes
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["BIRT"] = "Birth", ["CHR"] = "Christening", ["DEAT"] = "Death", ["BURI"] = "Burial",
        ["CREM"] = "Cremation", ["ADOP"] = "Adoption", ["BAPM"] = "Baptism", ["BARM"] = "Bar Mitzvah",
        ["BASM"] = "Bas Mitzvah", ["BLES"] = "Blessing", ["CHRA"] = "Adult christening",
        ["CONF"] = "Confirmation", ["FCOM"] = "First communion", ["ORDN"] = "Ordination",
        ["NATU"] = "Naturalization", ["EMIG"] = "Emigration", ["IMMI"] = "Immigration",
        ["CENS"] = "Census", ["PROB"] = "Probate", ["WILL"] = "Will", ["GRAD"] = "Graduation",
        ["RETI"] = "Retirement", ["OCCU"] = "Occupation", ["RESI"] = "Residence",
        ["EDUC"] = "Education", ["RELI"] = "Religion", ["NATI"] = "Nationality", ["CAST"] = "Caste",
        ["NCHI"] = "Number of children", ["PROP"] = "Property", ["MILI"] = "Military service",
        ["MARR"] = "Marriage", ["MARB"] = "Marriage banns", ["MARC"] = "Marriage contract",
        ["MARL"] = "Marriage license", ["MARS"] = "Marriage settlement", ["ENGA"] = "Engagement",
        ["DIV"] = "Divorce", ["DIVF"] = "Divorce filed", ["ANUL"] = "Annulment",
        ["BAPL"] = "Baptism (LDS)", ["CONL"] = "Confirmation (LDS)", ["WAC"] = "Initiatory (LDS)",
        ["ENDL"] = "Endowment (LDS)", ["SLGC"] = "Sealing to parents (LDS)", ["SLGS"] = "Sealing to spouse (LDS)",
    };

    public static IReadOnlyDictionary<string, string> All => Labels;

    public static string Label(Event e) =>
        e.Tag == "EVEN" && e.TypeName.Length > 0 ? e.TypeName
        : Labels.TryGetValue(e.Tag, out var l) ? l : e.Tag;

    public static bool IsLds(string tag) => tag is "BAPL" or "CONL" or "WAC" or "ENDL" or "SLGC" or "SLGS";
}
