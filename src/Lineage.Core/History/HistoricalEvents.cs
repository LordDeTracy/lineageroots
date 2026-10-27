using static Lineage.Core.History.Regions;

namespace Lineage.Core.History;

public enum HistoryKind { War, Epidemic, Migration, Law, Records, Economy, Disaster, Politics }

/// <summary>
/// Something that happened in the world that touched ordinary families. <see cref="Regions"/> lists
/// where it mattered; empty means everywhere. <see cref="Detail"/> says why it matters for family
/// history — often the records it left behind.
/// </summary>
public sealed record HistoricalEvent(int From, int To, string Title, string Detail, HistoryKind Kind, string[] Regions)
{
    public bool Everywhere => Regions.Length == 0;
    public bool AppliesTo(string region) => Everywhere || Regions.Contains(region);
    public string Years => To > From ? $"{From}–{To}" : From.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The built-in list of historical events shown on life timelines.</summary>
public static class HistoricalEvents
{
    private static readonly string[] Us = { UnitedStates };
    private static readonly string[] UsCanada = { UnitedStates, Canada };
    private static readonly string[] Britain = { England, Wales, Scotland };
    private static readonly string[] EnglandWales = { England, Wales };
    private static readonly string[] Ire = { Ireland };
    private static readonly string[] Can = { Canada };
    private static readonly string[] Aus = { Australia };
    private static readonly string[] Ger = { Germany };
    private static readonly string[] Commonwealth = { England, Wales, Scotland, Ireland, Canada, Australia, NewZealand };

    public static readonly IReadOnlyList<HistoricalEvent> All = new HistoricalEvent[]
    {
        // ---- Everywhere ----
        new(1918, 1920, "Influenza pandemic", "The \"Spanish flu\" killed tens of millions worldwide, many of them young adults. Look for clusters of deaths in the family in late 1918.", HistoryKind.Epidemic, Array.Empty<string>()),
        new(1929, 1939, "The Great Depression", "Began with the Wall Street Crash in October 1929. Unemployment, lost farms and moves in search of work were common.", HistoryKind.Economy, Array.Empty<string>()),

        // ---- Wars across several countries ----
        new(1803, 1815, "Napoleonic Wars", "Britain and much of Europe at war with France. British militia lists and army records name many men.", HistoryKind.War, new[] { England, Wales, Scotland, Ireland, France, Germany, Netherlands, Denmark, Norway, Sweden }),
        new(1899, 1902, "Second Boer War", "Fought in South Africa. Volunteers came from Britain, Ireland, Canada, Australia and New Zealand.", HistoryKind.War, Commonwealth),
        new(1914, 1918, "First World War", "Most men of military age served. Service records, medal cards and war memorials are good places to look.", HistoryKind.War, new[] { England, Wales, Scotland, Ireland, Canada, Australia, NewZealand, Germany, France }),
        new(1917, 1918, "United States in the First World War", "About 24 million men filled in draft registration cards in 1917–18 — almost every man born between 1872 and 1900.", HistoryKind.War, Us),
        new(1939, 1945, "Second World War", "Service records, civilian war-dead rolls and evacuation records may mention the family.", HistoryKind.War, new[] { England, Wales, Scotland, Canada, Australia, NewZealand, Germany, France, Netherlands, Norway, Denmark }),
        new(1941, 1945, "United States in the Second World War", "Draft registration cards survive for men born 1877–1897 (the 1942 \"old man's draft\"), and enlistment records for many who served.", HistoryKind.War, Us),
        new(1950, 1953, "Korean War", "Troops served from the United States, Britain, Canada and Australia.", HistoryKind.War, new[] { UnitedStates, England, Wales, Scotland, Canada, Australia, NewZealand }),
        new(1955, 1975, "Vietnam War", "Many young American men were drafted; Australia chose national servicemen by birthday ballot from 1965.", HistoryKind.War, new[] { UnitedStates, Australia, NewZealand }),

        // ---- Epidemics ----
        new(1831, 1832, "Cholera reaches Britain", "The first cholera epidemic killed tens of thousands in Britain and Ireland.", HistoryKind.Epidemic, BritishIsles),
        new(1832, 1832, "Cholera epidemic", "Cholera arrived in North America with immigrant ships and spread along rivers and canals.", HistoryKind.Epidemic, UsCanada),
        new(1848, 1849, "Cholera epidemic", "A second wave of cholera swept through Britain and the United States.", HistoryKind.Epidemic, new[] { England, Wales, Scotland, UnitedStates }),
        new(1854, 1854, "Cholera in London", "The Broad Street outbreak, which showed that cholera spreads through water.", HistoryKind.Epidemic, new[] { England }),
        new(1866, 1866, "Cholera epidemic", "The last major cholera epidemic in Britain; it also struck American cities.", HistoryKind.Epidemic, new[] { England, Wales, UnitedStates }),
        new(1878, 1878, "Yellow fever epidemic", "Yellow fever killed about 20,000 people in the Mississippi Valley, including Memphis and New Orleans.", HistoryKind.Epidemic, Us),

        // ---- United States ----
        new(1775, 1783, "American Revolutionary War", "Pension and bounty-land files for veterans and widows are full of family detail.", HistoryKind.War, Us),
        new(1812, 1815, "War of 1812", "Fought between the United States and Britain, much of it along the Canadian border. Pension and bounty-land files survive.", HistoryKind.War, UsCanada),
        new(1825, 1825, "Erie Canal opens", "Opened a cheap route west from New York; many families moved to western New York, Ohio and Michigan.", HistoryKind.Migration, Us),
        new(1838, 1839, "Trail of Tears", "The forced removal of the Cherokee Nation to Indian Territory, part of the removals after the Indian Removal Act of 1830.", HistoryKind.Migration, Us),
        new(1843, 1869, "Oregon Trail", "Hundreds of thousands of settlers travelled west by wagon until the transcontinental railroad opened in 1869.", HistoryKind.Migration, Us),
        new(1846, 1848, "Mexican–American War", "Service and pension records survive for those who served.", HistoryKind.War, Us),
        new(1848, 1855, "California Gold Rush", "About 300,000 people went to California. Many men left their families for years.", HistoryKind.Migration, Us),
        new(1861, 1865, "American Civil War", "Union and Confederate service records, Union pensions, and Confederate pensions from the Southern states.", HistoryKind.War, Us),
        new(1862, 1862, "Homestead Act", "Offered 160 acres of public land to settlers. Homestead case files at the National Archives give details of the family.", HistoryKind.Law, Us),
        new(1865, 1865, "Slavery abolished", "The 13th Amendment ended slavery. The 1870 census is the first to name most formerly enslaved people; Freedmen's Bureau records (1865–1872) may help before that.", HistoryKind.Law, Us),
        new(1882, 1882, "Chinese Exclusion Act", "Stopped most Chinese immigration until 1943. Chinese Exclusion case files hold interviews and photographs.", HistoryKind.Law, Us),
        new(1892, 1954, "Ellis Island", "The main entry point for immigrants to New York. Its passenger lists are searchable online.", HistoryKind.Migration, Us),
        new(1898, 1898, "Spanish–American War", "Short war fought in Cuba and the Philippines.", HistoryKind.War, Us),
        new(1906, 1906, "Naturalization records standardized", "From 27 September 1906, naturalization papers ask for birthplace, date of arrival and ship — far more than earlier ones.", HistoryKind.Records, Us),
        new(1920, 1920, "Women win the vote", "The 19th Amendment gave women the vote nationwide; look for women on voter registers from then on.", HistoryKind.Law, Us),
        new(1920, 1933, "Prohibition", "The sale of alcohol was banned nationwide.", HistoryKind.Law, Us),
        new(1924, 1924, "Immigration Act of 1924", "Set strict quotas by country of origin, sharply cutting immigration from southern and eastern Europe.", HistoryKind.Law, Us),
        new(1930, 1936, "Dust Bowl", "Drought and dust storms drove many farm families from the Great Plains, often to California.", HistoryKind.Disaster, Us),
        new(1935, 1935, "Social Security begins", "Applications for a Social Security number (from 1936) name the applicant's parents and birthplace.", HistoryKind.Records, Us),

        // ---- Britain and Ireland ----
        new(1642, 1651, "Civil Wars", "Fighting across England, Wales, Scotland and Ireland. Parish registers often have gaps in these years.", HistoryKind.War, BritishIsles),
        new(1665, 1665, "Great Plague of London", "Bubonic plague killed about a quarter of London's people.", HistoryKind.Epidemic, new[] { England }),
        new(1666, 1666, "Great Fire of London", "Destroyed most of the City of London, including many parish churches and their records.", HistoryKind.Disaster, new[] { England }),
        new(1707, 1707, "Union of England and Scotland", "The Kingdom of Great Britain was formed.", HistoryKind.Politics, Britain),
        new(1745, 1746, "Jacobite rising", "Ended at Culloden. Many Highlanders were killed, imprisoned or transported.", HistoryKind.War, new[] { Scotland }),
        new(1752, 1752, "Calendar change", "Britain and its colonies moved to the Gregorian calendar: 2 September was followed by 14 September, and the year now began on 1 January instead of 25 March. Earlier dates between January and March are often written as \"1750/51\".", HistoryKind.Records, new[] { England, Wales, Scotland, Ireland, UnitedStates }),
        new(1754, 1754, "Hardwicke's Marriage Act", "From 25 March 1754 marriages in England and Wales had to take place in the Church of England after banns or a licence (Quakers and Jews excepted).", HistoryKind.Law, EnglandWales),
        new(1780, 1855, "Highland Clearances", "Tenants were evicted from the Scottish Highlands to make way for sheep; many emigrated to Canada, Australia and America.", HistoryKind.Migration, new[] { Scotland }),
        new(1788, 1868, "Transportation to Australia", "About 160,000 convicts were sent from Britain and Ireland to Australia. Convict records describe each person in detail.", HistoryKind.Law, new[] { England, Wales, Scotland, Ireland, Australia }),
        new(1798, 1798, "Irish Rebellion", "A rising against British rule, put down with heavy loss of life.", HistoryKind.War, Ire),
        new(1801, 1801, "Union of Great Britain and Ireland", "Ireland joined the United Kingdom.", HistoryKind.Politics, BritishIsles),
        new(1829, 1829, "Catholic Emancipation", "Catholics could sit in Parliament and hold most public offices.", HistoryKind.Law, BritishIsles),
        new(1834, 1834, "New Poor Law", "Relief for the poor moved into workhouses. Workhouse admission and discharge registers often survive.", HistoryKind.Law, EnglandWales),
        new(1837, 1837, "Civil registration begins", "From 1 July 1837 every birth, marriage and death in England and Wales was registered; the national indexes start here.", HistoryKind.Records, EnglandWales),
        new(1845, 1852, "The Great Famine", "Potato blight caused about a million deaths, and more than a million people emigrated.", HistoryKind.Disaster, Ire),
        new(1846, 1856, "Highland Potato Famine", "Potato blight in the Highlands and islands led to mass emigration.", HistoryKind.Disaster, new[] { Scotland }),
        new(1855, 1855, "Civil registration begins", "From 1 January 1855 births, marriages and deaths in Scotland were registered. Scottish certificates of this year give unusual detail.", HistoryKind.Records, new[] { Scotland }),
        new(1858, 1858, "Wills proved in civil courts", "From 12 January 1858 wills in England and Wales were proved by the civil probate service, with one national index. Earlier wills are in church courts.", HistoryKind.Records, EnglandWales),
        new(1864, 1864, "Civil registration begins", "From 1 January 1864 all births, marriages and deaths in Ireland were registered (non-Catholic marriages from 1845).", HistoryKind.Records, Ire),
        new(1880, 1880, "School becomes compulsory", "Children aged 5 to 10 had to go to school; school admission registers name parents and addresses.", HistoryKind.Law, EnglandWales),
        new(1916, 1916, "Easter Rising", "An armed rising in Dublin, followed by the War of Independence (1919–1921).", HistoryKind.War, Ire),
        new(1922, 1922, "Irish Free State; Four Courts fire", "Most of Ireland became independent. On 30 June 1922 the Public Record Office fire destroyed most 19th-century census returns and many Church of Ireland registers.", HistoryKind.Records, Ire),

        // ---- Canada ----
        new(1763, 1763, "New France becomes British", "The Treaty of Paris handed Canada to Britain. Catholic parish registers in Quebec continue unbroken and are exceptionally complete.", HistoryKind.Politics, Can),
        new(1783, 1784, "Loyalists arrive", "Tens of thousands of Loyalists left the United States for Nova Scotia, New Brunswick and Quebec, many receiving land grants.", HistoryKind.Migration, new[] { Canada, UnitedStates }),
        new(1837, 1838, "Rebellions in Upper and Lower Canada", "Armed risings against the colonial government.", HistoryKind.War, Can),
        new(1847, 1847, "Famine ships at Grosse Île", "Thousands of Irish emigrants died of typhus at the quarantine station near Quebec City.", HistoryKind.Migration, new[] { Canada, Ireland }),
        new(1867, 1867, "Confederation", "Ontario, Quebec, Nova Scotia and New Brunswick formed the Dominion of Canada on 1 July 1867.", HistoryKind.Politics, Can),
        new(1896, 1899, "Klondike Gold Rush", "About 100,000 people set out for the Yukon.", HistoryKind.Migration, UsCanada),

        // ---- Australia and New Zealand ----
        new(1788, 1788, "First Fleet arrives", "The first convicts and settlers landed at Sydney Cove.", HistoryKind.Migration, Aus),
        new(1840, 1840, "Treaty of Waitangi", "Signed by the British Crown and Māori chiefs; organised British settlement of New Zealand followed.", HistoryKind.Politics, new[] { NewZealand }),
        new(1851, 1860, "Victorian gold rush", "Gold drew hundreds of thousands of migrants to Victoria and New South Wales.", HistoryKind.Migration, Aus),
        new(1861, 1863, "Otago gold rush", "Gold in Otago brought thousands of miners to New Zealand.", HistoryKind.Migration, new[] { NewZealand }),
        new(1901, 1901, "Federation", "The Australian colonies united as the Commonwealth of Australia on 1 January 1901.", HistoryKind.Politics, Aus),
        new(1915, 1915, "Gallipoli", "Australian and New Zealand troops landed on 25 April 1915; Anzac Day remembers them.", HistoryKind.War, new[] { Australia, NewZealand }),

        // ---- Europe ----
        new(1618, 1648, "Thirty Years' War", "Devastated much of Germany; many parish registers were lost or have gaps.", HistoryKind.War, Ger),
        new(1789, 1799, "French Revolution", "Civil registration (état civil) began in France in 1792.", HistoryKind.Politics, new[] { France }),
        new(1811, 1811, "Civil registration begins", "Introduced across the Netherlands under French rule.", HistoryKind.Records, new[] { Netherlands }),
        new(1814, 1814, "Norway's constitution", "Norway passed from Denmark to a union with Sweden that lasted until 1905.", HistoryKind.Politics, new[] { Norway, Sweden, Denmark }),
        new(1825, 1825, "Norwegian emigration begins", "The sloop Restauration carried the first organised group of Norwegian emigrants to America.", HistoryKind.Migration, new[] { Norway }),
        new(1845, 1849, "Potato blight in Europe", "Hunger in Germany, the Netherlands and elsewhere pushed many families to emigrate.", HistoryKind.Disaster, new[] { Germany, Netherlands }),
        new(1848, 1849, "Revolutions of 1848", "After failed revolutions many Germans (the \"Forty-Eighters\") emigrated to America.", HistoryKind.Politics, Ger),
        new(1864, 1864, "Second Schleswig War", "Denmark lost Schleswig and Holstein to Prussia and Austria.", HistoryKind.War, new[] { Denmark, Germany }),
        new(1850, 1930, "Swedish emigration", "About 1.3 million Swedes emigrated, most to the United States. Swedish parish records note when someone moved out, and where to.", HistoryKind.Migration, new[] { Sweden }),
        new(1867, 1869, "Swedish famine", "Crop failures led to famine and the start of mass emigration to America.", HistoryKind.Disaster, new[] { Sweden }),
        new(1870, 1871, "Franco-Prussian War", "Ended with the unification of Germany in 1871.", HistoryKind.War, new[] { France, Germany }),
        new(1874, 1876, "Civil registration begins", "Births, marriages and deaths were registered in Prussia from 1 October 1874 and in all of Germany from 1 January 1876.", HistoryKind.Records, Ger),
        new(1905, 1905, "Norway becomes independent", "The union with Sweden ended.", HistoryKind.Politics, new[] { Norway, Sweden }),
    };
}
