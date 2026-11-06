using Lineage.Core.History;

namespace Lineage.Core.Places;

/// <summary>One set of records worth searching for a place: what it is, the years it covers and where to find it.</summary>
/// <param name="To">0 when the records continue to the present.</param>
public sealed record RecordSet(string Region, string Name, int From, int To, string Where, string Note = "")
{
    public string Years => To == 0 ? $"{From}–" : $"{From}–{To}";
    public bool Covers(int fromYear, int toYear) => (To == 0 || fromYear <= To) && toYear >= From;
}

/// <summary>
/// Locality guides (Advanced mode): which records exist for a place and period, and where they are kept — the first
/// question when planning research somewhere new. A built-in summary of the main record sets for 14 countries; local
/// archives hold much more.
/// </summary>
public static class LocalityGuides
{
    private static RecordSet S(string region, string name, int from, int to, string where, string note = "") => new(region, name, from, to, where, note);

    public static IReadOnlyList<RecordSet> All { get; } = new[]
    {
        // England and Wales
        S("England", "Parish registers (baptisms, marriages, burials)", 1538, 0, "County record offices; many indexed on FamilySearch, Ancestry, Findmypast"),
        S("England", "Bishop's transcripts", 1598, 1870, "Diocesan record offices", "Copies of the parish registers, useful where registers are lost."),
        S("England", "Nonconformist registers", 1642, 1837, "The National Archives (RG 4, RG 8)"),
        S("England", "Land tax assessments", 1692, 1832, "County record offices"),
        S("England", "Church court wills and probate", 1500, 1858, "Diocesan record offices; Prerogative Court of Canterbury at The National Archives"),
        S("England", "Poor law and settlement records", 1601, 1930, "County record offices (parish, then union records from 1834)"),
        S("England", "Electoral registers", 1832, 0, "County record offices, British Library, Ancestry, Findmypast"),
        S("England", "Civil registration: births, marriages, deaths", 1837, 0, "General Register Office indexes (GRO, FreeBMD)", "Started 1 July 1837; certificates from the GRO."),
        S("England", "Tithe apportionments and maps", 1836, 1860, "The National Archives (IR 29, IR 30), county record offices"),
        S("England", "Censuses", 1841, 1921, "The National Archives; Ancestry, Findmypast, FamilySearch", "Every ten years; 1931 was destroyed; the 1939 Register stands in for it."),
        S("England", "National probate calendar (wills)", 1858, 0, "Probate Service; Ancestry index"),
        S("England", "First World War service records", 1914, 1920, "The National Archives (WO 363, WO 364); about 60% of soldiers' records were lost in 1940."),
        S("England", "Newspapers", 1700, 0, "British Newspaper Archive / Findmypast"),
        // Scotland
        S("Scotland", "Old parish registers", 1553, 1854, "ScotlandsPeople", "Church of Scotland only; patchy before 1800."),
        S("Scotland", "Statutory registration: births, marriages, deaths", 1855, 0, "ScotlandsPeople", "Scottish certificates give more detail than English ones."),
        S("Scotland", "Censuses", 1841, 1921, "ScotlandsPeople"),
        S("Scotland", "Wills and testaments", 1513, 0, "ScotlandsPeople"),
        S("Scotland", "Valuation rolls", 1855, 1940, "ScotlandsPeople"),
        // Ireland
        S("Ireland", "Catholic parish registers", 1740, 1900, "National Library of Ireland (online)", "Most begin in the 1820s or later."),
        S("Ireland", "Church of Ireland registers", 1634, 0, "Representative Church Body Library", "About half were destroyed in 1922."),
        S("Ireland", "Tithe Applotment Books", 1823, 1837, "National Archives of Ireland (online)"),
        S("Ireland", "Griffith's Valuation", 1847, 1864, "askaboutireland.ie"),
        S("Ireland", "Civil registration", 1864, 0, "irishgenealogy.ie; GRONI for Northern Ireland", "Non-Catholic marriages from 1845."),
        S("Ireland", "Censuses", 1901, 1911, "National Archives of Ireland (online)", "1821–1851 mostly destroyed; 1861–1891 pulped."),
        // United States
        S("United States", "Federal censuses", 1790, 1950, "National Archives; FamilySearch, Ancestry", "1890 mostly destroyed by fire; names of everyone in the household from 1850."),
        S("United States", "County deeds and land records", 1630, 0, "County recorder of deeds; FamilySearch"),
        S("United States", "County probate records", 1630, 0, "County probate courts; FamilySearch"),
        S("United States", "Federal land entry files", 1800, 1908, "National Archives; BLM General Land Office records"),
        S("United States", "Naturalization records", 1790, 0, "Courts and National Archives; USCIS from 1906"),
        S("United States", "Passenger lists", 1820, 1957, "National Archives; Ancestry, FamilySearch"),
        S("United States", "Revolutionary War pensions", 1775, 1900, "National Archives; Fold3"),
        S("United States", "Civil War service and pension files", 1861, 1934, "National Archives; Fold3"),
        S("United States", "State vital records (births, deaths)", 1850, 0, "State vital records offices", "Most states only from about 1900–1920."),
        S("United States", "World War I draft registration", 1917, 1918, "National Archives; Ancestry, FamilySearch"),
        S("United States", "World War II draft registration", 1940, 1947, "National Archives; Ancestry, FamilySearch"),
        S("United States", "Social Security Death Index", 1936, 2014, "FamilySearch, Ancestry"),
        // Canada
        S("Canada", "Quebec parish registers", 1621, 0, "BAnQ; Généalogie Québec, FamilySearch"),
        S("Canada", "Censuses", 1851, 1931, "Library and Archives Canada (online)"),
        S("Canada", "Provincial civil registration", 1869, 0, "Provincial archives", "Ontario from 1869; other provinces at various dates."),
        S("Canada", "Passenger lists", 1865, 1935, "Library and Archives Canada"),
        S("Canada", "First World War personnel files", 1914, 1920, "Library and Archives Canada (online)"),
        // Australia
        S("Australia", "Convict records", 1788, 1868, "State archives; The National Archives (UK)"),
        S("Australia", "State birth, death and marriage registration", 1838, 0, "State registries", "Victoria from 1853, New South Wales from 1856."),
        S("Australia", "Shipping and passenger lists", 1788, 0, "State archives"),
        S("Australia", "Electoral rolls", 1903, 0, "Ancestry"),
        S("Australia", "Newspapers", 1803, 1954, "Trove (National Library of Australia)"),
        // New Zealand
        S("New Zealand", "Birth, death and marriage registration", 1848, 0, "Department of Internal Affairs (online)", "Marriages from 1854."),
        S("New Zealand", "Electoral rolls", 1853, 0, "Ancestry"),
        S("New Zealand", "Newspapers", 1839, 1989, "Papers Past (National Library)"),
        // Germany
        S("Germany", "Church books (Kirchenbücher)", 1520, 0, "Church and diocesan archives; Archion, Matricula", "Most begin after the Thirty Years' War (1648)."),
        S("Germany", "Civil registration (Standesamt)", 1876, 0, "Local registry offices and city archives", "Prussia from 1874; the Rhineland from 1798."),
        S("Germany", "Hamburg passenger lists", 1850, 1934, "Hamburg State Archives; Ancestry"),
        S("Germany", "Local family books (Ortsfamilienbücher)", 1600, 1900, "Verein für Computergenealogie (online.ofb.de)"),
        // Netherlands
        S("Netherlands", "Church registers (DTB)", 1550, 1811, "Regional archives; WieWasWie, Open Archives"),
        S("Netherlands", "Civil registration", 1811, 0, "Regional archives; WieWasWie, Open Archives"),
        S("Netherlands", "Population registers", 1850, 1940, "Municipal archives"),
        // France
        S("France", "Parish registers", 1539, 1792, "Archives départementales (online)", "Kept systematically from 1667."),
        S("France", "État civil (civil registration)", 1792, 0, "Archives départementales (online) to about 1920"),
        S("France", "Notarial records", 1500, 0, "Archives départementales"),
        S("France", "Military registers (registres matricules)", 1867, 1940, "Archives départementales (online)"),
        // Scandinavia
        S("Norway", "Church books", 1623, 0, "Digitalarkivet (online)"),
        S("Norway", "Censuses", 1801, 1910, "Digitalarkivet", "1801, 1865, 1875, 1900, 1910."),
        S("Sweden", "Household examination rolls (husförhörslängder)", 1686, 1894, "Riksarkivet, ArkivDigital"),
        S("Sweden", "Church books and moving records", 1686, 1991, "Riksarkivet, ArkivDigital"),
        S("Denmark", "Church books", 1646, 0, "Arkivalieronline (Danish National Archives)"),
        S("Denmark", "Censuses", 1787, 1940, "Danish Demographic Database; Arkivalieronline"),
        S("Denmark", "Probate records", 1700, 0, "Danish National Archives"),
    };

    /// <summary>The record sets for a place's country that overlap the years given (0 for any years).</summary>
    public static IReadOnlyList<RecordSet> For(string place, int fromYear = 0, int toYear = 0)
    {
        var region = Regions.Of(place);
        if (region == null) return Array.Empty<RecordSet>();
        if (region == Regions.Wales) region = Regions.England;   // one system for England and Wales
        var from = fromYear > 0 ? fromYear : int.MinValue;
        var to = toYear > 0 ? toYear : fromYear > 0 ? fromYear : int.MaxValue;
        return All.Where(r => r.Region == region && r.Covers(from, to)).OrderBy(r => r.From).ToList();
    }
}
