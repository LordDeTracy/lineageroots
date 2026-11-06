using Lineage.Core.History;

namespace Lineage.Core.Places;

/// <summary>
/// A former place name: a town (whose modern name and country replace the whole place when looking it up) or a county,
/// province or country (whose modern name replaces just that part).
/// </summary>
public sealed record HistoricalName(string Old, string Today, bool Town, int Since, string Note = "");

/// <summary>
/// Places whose names or borders changed: Königsberg is Kaliningrad, Middlesex was absorbed into London, Bombay is
/// Mumbai. Records keep the name used at the time, as they should; this says what the place is called today, and lets
/// the map and the GPS finder look up the modern place.
/// </summary>
public static class HistoricalPlaces
{
    private static HistoricalName T(string old, string today, int since, string note = "") => new(old, today, true, since, note);
    private static HistoricalName R(string old, string today, int since, string note = "") => new(old, today, false, since, note);

    public static IReadOnlyList<HistoricalName> All { get; } = new[]
    {
        // German names of towns now in Poland, Russia, the Baltic states and beyond
        T("Danzig", "Gdańsk, Poland", 1945), T("Breslau", "Wrocław, Poland", 1945), T("Stettin", "Szczecin, Poland", 1945),
        T("Posen", "Poznań, Poland", 1919), T("Thorn", "Toruń, Poland", 1920), T("Bromberg", "Bydgoszcz, Poland", 1920),
        T("Elbing", "Elbląg, Poland", 1945), T("Allenstein", "Olsztyn, Poland", 1945), T("Gleiwitz", "Gliwice, Poland", 1945),
        T("Oppeln", "Opole, Poland", 1945), T("Liegnitz", "Legnica, Poland", 1945), T("Kattowitz", "Katowice, Poland", 1922),
        T("Königsberg", "Kaliningrad, Russia", 1946), T("Tilsit", "Sovetsk, Russia", 1946), T("Memel", "Klaipėda, Lithuania", 1923),
        T("Kowno", "Kaunas, Lithuania", 1918), T("Kovno", "Kaunas, Lithuania", 1918), T("Wilna", "Vilnius, Lithuania", 1939), T("Vilna", "Vilnius, Lithuania", 1939),
        T("Reval", "Tallinn, Estonia", 1918), T("Dorpat", "Tartu, Estonia", 1918), T("Libau", "Liepāja, Latvia", 1918), T("Mitau", "Jelgava, Latvia", 1918),
        T("Lemberg", "Lviv, Ukraine", 1945), T("Czernowitz", "Chernivtsi, Ukraine", 1940),
        // Austria-Hungary
        T("Pressburg", "Bratislava, Slovakia", 1919), T("Kaschau", "Košice, Slovakia", 1919), T("Brünn", "Brno, Czech Republic", 1918),
        T("Olmütz", "Olomouc, Czech Republic", 1918), T("Karlsbad", "Karlovy Vary, Czech Republic", 1918), T("Budweis", "České Budějovice, Czech Republic", 1918),
        T("Agram", "Zagreb, Croatia", 1918), T("Laibach", "Ljubljana, Slovenia", 1918), T("Fiume", "Rijeka, Croatia", 1947),
        T("Temesvár", "Timișoara, Romania", 1920), T("Klausenburg", "Cluj-Napoca, Romania", 1920), T("Hermannstadt", "Sibiu, Romania", 1920),
        // Scandinavia and Finland
        T("Christiania", "Oslo, Norway", 1925), T("Kristiania", "Oslo, Norway", 1925), T("Trondhjem", "Trondheim, Norway", 1931),
        T("Helsingfors", "Helsinki, Finland", 1917), T("Åbo", "Turku, Finland", 1917), T("Viborg", "Vyborg, Russia", 1944),
        // Elsewhere in Europe and Asia
        T("Constantinople", "Istanbul, Turkey", 1930), T("Smyrna", "İzmir, Turkey", 1930), T("Salonica", "Thessaloniki, Greece", 1912),
        T("Leningrad", "Saint Petersburg, Russia", 1991), T("Petrograd", "Saint Petersburg, Russia", 1991), T("Stalingrad", "Volgograd, Russia", 1961),
        T("Tiflis", "Tbilisi, Georgia", 1936), T("Bombay", "Mumbai, India", 1995), T("Madras", "Chennai, India", 1996),
        T("Calcutta", "Kolkata, India", 2001), T("Peking", "Beijing, China", 1958), T("Canton", "Guangzhou, China", 1958),
        T("Batavia", "Jakarta, Indonesia", 1949), T("Rangoon", "Yangon, Myanmar", 1989),
        // The Americas, Africa and Australia
        T("New Amsterdam", "New York, United States", 1664), T("Bytown", "Ottawa, Canada", 1855),
        T("Salisbury, Rhodesia", "Harare, Zimbabwe", 1982), T("Léopoldville", "Kinshasa, Democratic Republic of the Congo", 1966),
        // Ireland
        T("Kingstown", "Dún Laoghaire, Ireland", 1920), T("Queenstown", "Cobh, Ireland", 1920), T("Maryborough", "Portlaoise, Ireland", 1929),
        // Counties and provinces
        R("Middlesex", "Greater London", 1965, "Middlesex was split between Greater London and Surrey and Hertfordshire in 1965."),
        R("Cumberland", "Cumbria", 1974), R("Westmorland", "Cumbria", 1974), R("Huntingdonshire", "Cambridgeshire", 1974),
        R("West Riding", "West Yorkshire", 1974, "The West Riding of Yorkshire was divided in 1974, mostly into West and South Yorkshire."),
        R("East Riding", "East Riding of Yorkshire", 1974), R("North Riding", "North Yorkshire", 1974),
        R("King's County", "County Offaly", 1922), R("Kings County", "County Offaly", 1922),
        R("Queen's County", "County Laois", 1922), R("Queens County", "County Laois", 1922),
        R("Upper Canada", "Ontario", 1841), R("Lower Canada", "Quebec", 1841), R("Van Diemen's Land", "Tasmania", 1856),
        R("East Prussia", "Poland", 1945, "East Prussia was divided between Poland and Russia (the Kaliningrad region) in 1945."),
        R("West Prussia", "Poland", 1920), R("Silesia", "Poland", 1945, "Most of Silesia has been in Poland since 1945."),
        R("Pomerania", "Poland", 1945, "Pomerania east of the Oder has been in Poland since 1945."),
        R("Prussia", "Germany", 1947, "Prussia was abolished in 1947; its lands are in Germany, Poland, Russia and Lithuania."),
        R("Bohemia", "Czech Republic", 1918), R("Moravia", "Czech Republic", 1918),
        R("Persia", "Iran", 1935), R("Siam", "Thailand", 1939), R("Ceylon", "Sri Lanka", 1972), R("Burma", "Myanmar", 1989),
        R("Formosa", "Taiwan", 1945), R("Rhodesia", "Zimbabwe", 1980), R("Gold Coast", "Ghana", 1957), R("Bechuanaland", "Botswana", 1966),
        R("Dutch East Indies", "Indonesia", 1949), R("Gold Coast Colony", "Ghana", 1957),
    };

    private static readonly Dictionary<string, HistoricalName> ByKey =
        All.GroupBy(h => Gazetteer.Key(h.Old)).ToDictionary(g => g.Key, g => g.First());

    /// <summary>The former names in a place: "Harrow, Middlesex, England" has Middlesex.</summary>
    public static List<HistoricalName> In(string place)
    {
        var key = Gazetteer.Key(place);
        var found = new List<HistoricalName>();
        // Whole-place entries first ("Salisbury, Rhodesia"), then each part.
        if (ByKey.TryGetValue(key, out var whole)) found.Add(whole);
        foreach (var part in place.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ByKey.TryGetValue(Gazetteer.Key(part), out var h) && !found.Contains(h)) found.Add(h);
        foreach (var h in All.Where(h => h.Old.Contains(',') && key.Contains(Gazetteer.Key(h.Old)) && !found.Contains(h))) found.Add(h);
        // A town of the same name in a country settled from Europe isn't the old European one: Canton, Ohio isn't Guangzhou.
        var region = Regions.Of(place);
        if (region is Regions.UnitedStates or Regions.Canada or Regions.Australia or Regions.NewZealand
            or Regions.England or Regions.Wales or Regions.Scotland or Regions.Ireland)
            found.RemoveAll(h => h.Town && Regions.Of(h.Today) != region);
        return found;
    }

    /// <summary>
    /// The place under today's names, for finding it on a map: "Königsberg, Prussia" is "Kaliningrad, Russia", and
    /// "Harrow, Middlesex, England" is "Harrow, Greater London, England". Null when nothing in it has changed.
    /// </summary>
    public static string? Modern(string place)
    {
        var found = In(place);
        if (found.Count == 0) return null;
        if (found.FirstOrDefault(h => h.Town) is { } town) return town.Today;
        var parts = place.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
            if (found.FirstOrDefault(h => Gazetteer.Key(h.Old) == Gazetteer.Key(parts[i])) is { } region) parts[i] = region.Today;
        // "Poland, Poland" when a province became the country itself.
        return string.Join(", ", parts.Where((p, i) => i == 0 || !string.Equals(p, parts[i - 1], StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>One sentence for a place's page: "Königsberg has been called Kaliningrad, Russia since 1946."</summary>
    public static string Describe(HistoricalName h) =>
        h.Note.Length > 0 ? h.Note
        : h.Town ? $"{h.Old} has been called {h.Today} since {h.Since}."
        : $"{h.Old} is now part of {h.Today} (since {h.Since}).";
}
