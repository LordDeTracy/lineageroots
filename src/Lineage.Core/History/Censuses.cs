using static Lineage.Core.History.Regions;

namespace Lineage.Core.History;

/// <summary>
/// A national census (or similar list of everyone) that is open to the public. <see cref="Year"/>
/// is the year it is known by; <see cref="TakenYear"/>, <see cref="Month"/> and <see cref="Day"/>
/// are census day (month 0 when unknown). <see cref="OnlyIn"/> limits it to part of the region
/// ("Manitoba", …) and <see cref="NotIn"/> leaves part out, both as normalized place parts.
/// </summary>
public sealed record CensusRecord(string Region, int Year, int TakenYear, int Month, int Day, string Name, bool Survives, string Note)
{
    public string[] OnlyIn { get; init; } = Array.Empty<string>();
    public string[] NotIn { get; init; } = Array.Empty<string>();

    /// <summary>Census day as a sortable number (yyyymmdd), with an unknown day treated as mid-year.</summary>
    public int DayKey => TakenYear * 10000 + (Month > 0 ? Month * 100 + Day : 701);

    /// <summary>Whether the census covered this place.</summary>
    public bool Covers(string place) =>
        Regions.Of(place) == Region
        && (OnlyIn.Length == 0 || Regions.Mentions(place, OnlyIn))
        && (NotIn.Length == 0 || !Regions.Mentions(place, NotIn));
}

/// <summary>The censuses the checklist knows about: the ones that are open to the public, and the lost ones.</summary>
public static class Censuses
{
    private static CensusRecord Us(int year, int month, int day, string note, bool survives = true) =>
        new(UnitedStates, year, year, month, day, $"{year} US census", survives, note);

    private static CensusRecord Uk(string region, string name, int year, int month, int day, string note, bool survives = true) =>
        new(region, year, year, month, day, $"{year} {name} census", survives, note);

    private static IEnumerable<CensusRecord> Britain(string region, string name)
    {
        yield return Uk(region, name, 1841, 6, 6, "Ages over 15 were usually rounded down to a multiple of 5, and birthplace only says whether the person was born in the same county.");
        yield return Uk(region, name, 1851, 3, 30, "The first to give exact ages, relationship to the head of the household and parish of birth.");
        yield return Uk(region, name, 1861, 4, 7, "");
        yield return Uk(region, name, 1871, 4, 2, "");
        yield return Uk(region, name, 1881, 4, 3, "");
        yield return Uk(region, name, 1891, 4, 5, "");
        yield return Uk(region, name, 1901, 3, 31, "");
        yield return Uk(region, name, 1911, 4, 2, "Married women give how many years they had been married and how many children were born, still living and died.");
        yield return Uk(region, name, 1921, 6, 19, "Released in 2022.");
    }

    private static readonly string[] NorthernIreland = { "antrim", "armagh", "down", "fermanagh", "londonderry", "derry", "tyrone", "northern ireland" };
    private static readonly string[] Prairies = { "manitoba", "saskatchewan", "alberta" };
    private const string Swedish = "Compiled from parish records and searchable at the Swedish National Archives (Riksarkivet). The parish household examination rolls (husförhörslängder) list the family almost every year in between.";

    public static readonly IReadOnlyList<CensusRecord> All = new List<CensusRecord>
    {
        Us(1790, 8, 2, "Names only the head of each household; everyone else is counted by age and sex."),
        Us(1800, 8, 4, "Names only the head of each household."),
        Us(1810, 8, 6, "Names only the head of each household."),
        Us(1820, 8, 7, "Names only the head of each household."),
        Us(1830, 6, 1, "Names only the head of each household."),
        Us(1840, 6, 1, "Names only the head of each household, but also lists Revolutionary War pensioners by name."),
        Us(1850, 6, 1, "The first US census to name everyone in the household, with age and birthplace."),
        Us(1860, 6, 1, ""),
        Us(1870, 6, 1, "The first to name most formerly enslaved people."),
        Us(1880, 6, 1, "The first to give each person's relationship to the head of the household and their parents' birthplaces."),
        Us(1890, 6, 2, "Almost all destroyed after a fire in 1921; only about 6,000 names survive. Special schedules of Union veterans and widows mostly survive.", survives: false),
        Us(1900, 6, 1, "Gives month and year of birth, and year of immigration."),
        Us(1910, 4, 15, ""),
        Us(1920, 1, 1, ""),
        Us(1930, 4, 1, ""),
        Us(1940, 4, 1, "Asks where each person lived in 1935."),
        Us(1950, 4, 1, "Released in 2022."),
    }
    .Concat(Britain(England, "England and Wales"))
    .Append(new CensusRecord(England, 1931, 1931, 4, 26, "1931 England and Wales census", false, "Destroyed by fire in 1942."))
    .Append(new CensusRecord(England, 1939, 1939, 9, 29, "1939 Register", true, "Taken at the start of the Second World War; gives exact dates of birth and occupations."))
    .Concat(Britain(Wales, "England and Wales"))
    .Append(new CensusRecord(Wales, 1931, 1931, 4, 26, "1931 England and Wales census", false, "Destroyed by fire in 1942."))
    .Append(new CensusRecord(Wales, 1939, 1939, 9, 29, "1939 Register", true, "Taken at the start of the Second World War; gives exact dates of birth and occupations."))
    .Concat(Britain(Scotland, "Scotland"))
    .Concat(new[]
    {
        new CensusRecord(Ireland, 1821, 1821, 0, 0, "1821 Ireland census", false, "Almost all destroyed in the Four Courts fire of 1922; fragments survive for a few counties."),
        new CensusRecord(Ireland, 1831, 1831, 0, 0, "1831 Ireland census", false, "Almost all destroyed in the Four Courts fire of 1922; fragments survive for a few counties."),
        new CensusRecord(Ireland, 1841, 1841, 6, 6, "1841 Ireland census", false, "Almost all destroyed in the Four Courts fire of 1922; fragments survive for a few counties."),
        new CensusRecord(Ireland, 1851, 1851, 3, 30, "1851 Ireland census", false, "Almost all destroyed in the Four Courts fire of 1922; fragments survive for a few counties."),
        new CensusRecord(Ireland, 1861, 1861, 4, 7, "1861 Ireland census", false, "Destroyed by the government soon after it was taken."),
        new CensusRecord(Ireland, 1871, 1871, 4, 2, "1871 Ireland census", false, "Destroyed by the government soon after it was taken."),
        new CensusRecord(Ireland, 1881, 1881, 4, 3, "1881 Ireland census", false, "Destroyed by the government soon after it was taken."),
        new CensusRecord(Ireland, 1891, 1891, 4, 5, "1891 Ireland census", false, "Destroyed by the government soon after it was taken."),
        new CensusRecord(Ireland, 1901, 1901, 3, 31, "1901 Ireland census", true, "Complete for the whole island, and free to search online."),
        new CensusRecord(Ireland, 1911, 1911, 4, 2, "1911 Ireland census", true, "Complete for the whole island, and free to search online."),
        new CensusRecord(Ireland, 1926, 1926, 4, 18, "1926 Irish Free State census", true, "Released in 2026. Covers the Irish Free State only, not Northern Ireland.") { NotIn = NorthernIreland },

        new CensusRecord(Canada, 1851, 1852, 1, 12, "1851 census of Canada", true, "Taken in January 1852. Returns for some districts are missing."),
        new CensusRecord(Canada, 1861, 1861, 1, 14, "1861 census of Canada", true, ""),
        new CensusRecord(Canada, 1871, 1871, 4, 2, "1871 census of Canada", true, ""),
        new CensusRecord(Canada, 1881, 1881, 4, 4, "1881 census of Canada", true, ""),
        new CensusRecord(Canada, 1891, 1891, 4, 6, "1891 census of Canada", true, ""),
        new CensusRecord(Canada, 1901, 1901, 3, 31, "1901 census of Canada", true, "Gives full date of birth and year of immigration."),
        new CensusRecord(Canada, 1906, 1906, 6, 24, "1906 census of the Prairie Provinces", true, "Manitoba, Saskatchewan and Alberta only.") { OnlyIn = Prairies },
        new CensusRecord(Canada, 1911, 1911, 6, 1, "1911 census of Canada", true, ""),
        new CensusRecord(Canada, 1916, 1916, 6, 1, "1916 census of the Prairie Provinces", true, "Manitoba, Saskatchewan and Alberta only.") { OnlyIn = Prairies },
        new CensusRecord(Canada, 1921, 1921, 6, 1, "1921 census of Canada", true, ""),
        new CensusRecord(Canada, 1931, 1931, 6, 1, "1931 census of Canada", true, "Released in 2023."),

        new CensusRecord(Sweden, 1880, 1880, 12, 31, "1880 Sweden census", true, Swedish),
        new CensusRecord(Sweden, 1890, 1890, 12, 31, "1890 Sweden census", true, Swedish),
        new CensusRecord(Sweden, 1900, 1900, 12, 31, "1900 Sweden census", true, Swedish),
        new CensusRecord(Sweden, 1910, 1910, 12, 31, "1910 Sweden census", true, Swedish),
        new CensusRecord(Sweden, 1930, 1930, 12, 31, "1930 Sweden census", true, Swedish),

        new CensusRecord(Norway, 1801, 1801, 2, 1, "1801 Norway census", true, "The first Norwegian census to name everyone."),
        new CensusRecord(Norway, 1865, 1865, 12, 31, "1865 Norway census", true, ""),
        new CensusRecord(Norway, 1875, 1875, 12, 31, "1875 Norway census", true, ""),
        new CensusRecord(Norway, 1900, 1900, 12, 3, "1900 Norway census", true, ""),
        new CensusRecord(Norway, 1910, 1910, 12, 1, "1910 Norway census", true, ""),
        new CensusRecord(Norway, 1920, 1920, 12, 1, "1920 Norway census", true, "Released in 2020."),
    })
    .OrderBy(c => c.Region, StringComparer.Ordinal).ThenBy(c => c.Year)
    .ToList();

    /// <summary>The regions that have censuses in this list.</summary>
    public static IEnumerable<string> RegionsCovered => All.Select(c => c.Region).Distinct();
}
