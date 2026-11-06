using System.Text.RegularExpressions;
using Lineage.Core.Model;

namespace Lineage.Core.Sources;

/// <summary>One box on a source template's form.</summary>
public sealed record TemplateField(string Key, string Label, string Example);

/// <summary>
/// A way of citing one kind of source in the style of Evidence Explained: the full note for the first time it's cited,
/// a short note for later citations, and the bibliography entry. Patterns name fields as {Key}; {Page} is the citation's
/// own detail (page, entry, image). Text in [square brackets] is left out unless every field inside it is filled in.
/// </summary>
public sealed record SourceTemplate(string Id, string Name, string Group, IReadOnlyList<TemplateField> Fields,
                                    string FullNote, string ShortNote, string Bibliography, string TitlePattern)
{
    public override string ToString() => Name;
}

public static class SourceTemplates
{
    private static TemplateField F(string key, string label, string example) => new(key, label, example);

    private static readonly TemplateField Website = F("Website", "Website", "Ancestry, FamilySearch, Findmypast…");
    private static readonly TemplateField Url = F("Url", "Web address", "https://www.familysearch.org");
    private static readonly TemplateField Accessed = F("Accessed", "Date you looked", "14 March 2026");

    public static IReadOnlyList<SourceTemplate> All { get; } = new SourceTemplate[]
    {
        new("census-us", "U.S. federal census (population schedule)", "Census",
            new[] { F("Year", "Census year", "1880"), F("State", "State", "Massachusetts"), F("County", "County", "Worcester"),
                    F("Locality", "Town or district", "Worcester, enumeration district 12"), Website, Url, Accessed,
                    F("Archive", "Original held by", "National Archives microfilm publication T9"), F("Roll", "Roll", "566") },
            "{Year} U.S. census, {County} County, {State}, population schedule[, {Locality}][, {Page}][; image, {Website} ({Url} : accessed {Accessed})][; citing {Archive}][, roll {Roll}]",
            "{Year} U.S. census, {County} Co., {State}, pop. sch.[, {Page}]",
            "{State}. {County} County. {Year} U.S. census, population schedule.[ Image. {Website}. {Url} : {Accessed}.]",
            "{Year} U.S. census, {County} County, {State}"),
        new("census-uk", "Census of England and Wales (enumerator's book)", "Census",
            new[] { F("Year", "Census year", "1881"), F("Parish", "Parish or township", "Leeds"), F("County", "County", "Yorkshire"),
                    F("Archive", "Original held by", "The National Archives"), F("Piece", "Class and piece", "RG 11/4532"), Website, Url, Accessed },
            "{Year} census of England and Wales, {Parish}, {County}[, {Page}][; {Archive}][, {Piece}][; image, {Website} ({Url} : accessed {Accessed})]",
            "{Year} census, {Parish}, {County}[, {Page}]",
            "England. {County}. {Parish}. {Year} census.[ {Archive}[, {Piece}].][ Image. {Website}. {Url} : {Accessed}.]",
            "{Year} census, {Parish}, {County}"),
        new("parish-register", "Parish register (baptisms, marriages, burials)", "Church",
            new[] { F("Parish", "Church or parish", "St Mary"), F("Place", "Place", "Harrow, Middlesex"), F("Register", "Register", "Baptisms 1838–1850"),
                    F("Repository", "Held by", "London Metropolitan Archives"), F("Reference", "Reference or film", "DRO/3/A/1"), Website, Url, Accessed },
            "{Parish} ({Place}), {Register}[, {Page}][; {Repository}][, {Reference}][; image, {Website} ({Url} : accessed {Accessed})]",
            "{Parish} ({Place}), {Register}[, {Page}]",
            "{Place}. {Parish}. {Register}.[ {Repository}[, {Reference}].][ Image. {Website}. {Url} : {Accessed}.]",
            "{Parish}, {Place}, {Register}"),
        new("civil-certificate", "Civil registration certificate (birth, marriage, death)", "Vital records",
            new[] { F("Office", "Issued by", "General Register Office, England"), F("Event", "Kind of certificate", "birth"),
                    F("Name", "For", "Thomas Edward Whitfield"), F("Date", "Date of the event", "14 March 1842"),
                    F("District", "Registration district", "Hendon"), F("Reference", "Index reference", "vol. 3, p. 112") },
            "{Office}, {Event} certificate for {Name}, {Date}[, {District} registration district][, {Reference}][, {Page}]",
            "{Event} certificate for {Name}, {Date}",
            "{Office}. {Event} certificates.[ {District} registration district.]",
            "{Event} certificate, {Name}"),
        new("vital-record", "State or county vital record", "Vital records",
            new[] { F("Jurisdiction", "State or county", "Massachusetts"), F("Office", "Office", "Registry of Vital Records"),
                    F("Kind", "Kind of record", "death certificate"), F("Name", "For", "Thomas Edward Whitfield"),
                    F("Number", "Certificate number", "1911-4432") },
            "{Jurisdiction}, {Kind}[ no. {Number}], {Name}[, {Page}][; {Office}]",
            "{Jurisdiction}, {Kind}[ no. {Number}], {Name}",
            "{Jurisdiction}. {Kind}s.[ {Office}.]",
            "{Jurisdiction}, {Kind}, {Name}"),
        new("book", "Book", "Published",
            new[] { F("Author", "Author", "Margaret Hartley"), F("Title", "Title", "The Hartleys of Leeds"), F("Place", "Place published", "Leeds"),
                    F("Publisher", "Publisher", "Thoresby Society"), F("Year", "Year", "1931") },
            "{Author}, {Title} ([{Place}: ]{Publisher}, {Year})[, {Page}]",
            "{Author}, {Title}[, {Page}]",
            "{Author}. {Title}.[ {Place}:] {Publisher}, {Year}.",
            "{Title}"),
        new("article", "Journal or magazine article", "Published",
            new[] { F("Author", "Author", "John Pryor"), F("Article", "Article", "The Whitfields of Harrow"),
                    F("Journal", "Journal", "Genealogists' Magazine"), F("Volume", "Volume", "24"), F("Year", "Year", "1993") },
            "{Author}, “{Article},” {Journal}[ {Volume}] ({Year})[: {Page}]",
            "{Author}, “{Article},”[ {Page}]",
            "{Author}. “{Article}.” {Journal}[ {Volume}] ({Year}).",
            "{Article}"),
        new("newspaper", "Newspaper article", "Published",
            new[] { F("Article", "Headline", "Death of Mr T. E. Whitfield"), F("Newspaper", "Newspaper", "Worcester Evening Gazette"),
                    F("Place", "Place", "Worcester, Massachusetts"), F("Date", "Date", "10 November 1911") },
            "“{Article},” {Newspaper} ({Place}), {Date}[, {Page}]",
            "“{Article},” {Newspaper}, {Date}",
            "{Newspaper}. {Place}.",
            "{Newspaper}, {Date}"),
        new("website", "Online database or web page", "Online",
            new[] { F("Creator", "Who made it", "FamilySearch"), F("Title", "Database or page", "England Births and Christenings, 1538–1975"),
                    Website, Url, Accessed },
            "[{Creator}, ]“{Title},” database, {Website} ({Url} : accessed {Accessed})[, {Page}]",
            "“{Title},”[ {Page}]",
            "[{Creator}. ]“{Title}.” Database. {Website}. {Url} : {Accessed}.",
            "{Title}"),
        new("will", "Will or probate file", "Court and land",
            new[] { F("Jurisdiction", "Place", "Worcester County, Massachusetts"), F("Court", "Court", "Probate Court"),
                    F("Testator", "Whose will", "Thomas Edward Whitfield"), F("Date", "Date", "1911"), F("Reference", "File or case number", "case 61234") },
            "{Jurisdiction}, {Court}, will of {Testator}[, {Date}][, {Reference}][, {Page}]",
            "{Jurisdiction}, will of {Testator}[, {Page}]",
            "{Jurisdiction}. {Court}. Probate records.",
            "Will of {Testator}"),
        new("deed", "Land deed", "Court and land",
            new[] { F("Jurisdiction", "Place", "Worcester County, Massachusetts"), F("Office", "Office", "Registry of Deeds"),
                    F("Book", "Deed book", "1450"), F("Parties", "Parties", "Whitfield to Pryor") },
            "{Jurisdiction}, {Office}, deed book {Book}[: {Page}], {Parties}",
            "{Jurisdiction}, deed book {Book}[: {Page}]",
            "{Jurisdiction}. {Office}. Deed books.",
            "Deed, {Parties}"),
        new("gravestone", "Gravestone", "Cemetery",
            new[] { F("Cemetery", "Cemetery", "Hope Cemetery"), F("Place", "Place", "Worcester, Massachusetts"), F("Name", "Whose stone", "Thomas Edward Whitfield"),
                    F("Photographer", "Photographed by", "Clara Pryor"), F("Visited", "Date photographed", "June 1998") },
            "{Cemetery} ({Place}), {Name} marker[, {Page}][; photographed by {Photographer}][, {Visited}]",
            "{Cemetery}, {Name} marker",
            "{Place}. {Cemetery}. Gravestones.",
            "{Cemetery}, {Name} gravestone"),
        new("bible", "Family Bible", "Family papers",
            new[] { F("Title", "Name of the Bible", "Whitfield Family Bible"), F("Place", "Place published", "Oxford"),
                    F("Publisher", "Publisher", "University Press"), F("Year", "Year", "1860"), F("Owner", "Now owned by", "Alice Pryor") },
            "{Title}[ ({Place}: {Publisher}, {Year})][, {Page}][; owned by {Owner}]",
            "{Title}[, {Page}]",
            "{Title}.[ {Place}: {Publisher}, {Year}.][ Owned by {Owner}.]",
            "{Title}"),
        new("interview", "Interview", "Family papers",
            new[] { F("Interviewee", "Who was interviewed", "Alice Pryor"), F("Interviewer", "Interviewed by", "Henry Whitfield"),
                    F("Date", "Date", "3 May 1975"), F("Place", "Where", "Boston, Massachusetts") },
            "{Interviewee}, interview by {Interviewer}, {Date}[, {Place}][; {Page}]",
            "{Interviewee}, interview, {Date}",
            "{Interviewee}. Interview by {Interviewer}, {Date}.",
            "Interview with {Interviewee}"),
        new("letter", "Letter", "Family papers",
            new[] { F("Writer", "From", "Eliza Whitfield"), F("Recipient", "To", "Clara Pryor"), F("Date", "Date", "2 February 1898"),
                    F("Holder", "Now held by", "Alice Pryor") },
            "{Writer} to {Recipient}, letter, {Date}[, {Page}][; privately held by {Holder}]",
            "{Writer} to {Recipient}, {Date}",
            "{Writer}. Letter to {Recipient}, {Date}.[ Privately held by {Holder}.]",
            "Letter, {Writer} to {Recipient}"),
        new("military", "Military service record", "Military",
            new[] { F("Name", "Whose record", "Henry Whitfield"), F("Unit", "Unit", "2nd Battalion, West Yorkshire Regiment"),
                    F("Series", "Series", "Soldiers' documents, WO 97"), F("Archive", "Held by", "The National Archives") },
            "{Name} service record, {Unit}[, {Series}], {Archive}[, {Page}]",
            "{Name} service record[, {Page}]",
            "{Archive}.[ {Series}.]",
            "Service record, {Name}"),
        new("passenger-list", "Passenger list", "Immigration",
            new[] { F("Ship", "Ship", "SS Bothnia"), F("Port", "Arriving at", "Boston"), F("Date", "Date", "4 June 1880"), Website, Url, Accessed },
            "Passenger list, {Ship}, arriving {Port}, {Date}[, {Page}][; image, {Website} ({Url} : accessed {Accessed})]",
            "Passenger list, {Ship}, {Date}[, {Page}]",
            "Passenger lists, {Port}.[ Image. {Website}. {Url} : {Accessed}.]",
            "{Ship}, {Port}, {Date}"),
        new("directory", "City directory", "Published",
            new[] { F("Title", "Title", "Worcester Directory for 1890"), F("Place", "Place published", "Worcester"),
                    F("Publisher", "Publisher", "Drew, Allis & Co."), F("Year", "Year", "1890") },
            "{Title} ({Place}: {Publisher}, {Year})[, {Page}]",
            "{Title}[, {Page}]",
            "{Title}. {Place}: {Publisher}, {Year}.",
            "{Title}"),
    };

    public static SourceTemplate? Find(string? id) => id is { Length: > 0 } ? All.FirstOrDefault(t => t.Id == id) : null;

    /// <summary>The full note for the first time the source is cited.</summary>
    public static string FullNote(Source s, string page) => Render(s, page, t => t.FullNote);
    /// <summary>The short note for citing it again.</summary>
    public static string ShortNote(Source s, string page) => Render(s, page, t => t.ShortNote);
    public static string Bibliography(Source s) => Render(s, "", t => t.Bibliography);
    /// <summary>A title made from the fields, for lists of sources.</summary>
    public static string Title(Source s) => Render(s, "", t => t.TitlePattern);

    private static string Render(Source s, string page, Func<SourceTemplate, string> pattern)
    {
        if (Find(s.Template) is not { } t) return "";
        var values = new Dictionary<string, string>(s.Fields.Where(kv => kv.Value.Trim().Length > 0)
            .Select(kv => KeyValuePair.Create(kv.Key, kv.Value.Trim())), StringComparer.Ordinal);
        if (page.Trim().Length > 0) values["Page"] = page.Trim();
        return Fill(pattern(t), values);
    }

    private static readonly Regex Gap = new(@"\{(\w+)\}");
    private static readonly Regex Optional = new(@"\[([^\[\]]*)\]");

    /// <summary>Fills a pattern: optional [parts] go when a field in them is empty, then the gaps are filled.</summary>
    public static string Fill(string pattern, IReadOnlyDictionary<string, string> values)
    {
        // Innermost brackets first, so "[ {A}[, {B}].]" keeps A when only B is missing.
        var text = pattern;
        for (var guard = 0; guard < 10 && Optional.IsMatch(text); guard++)
            text = Optional.Replace(text, m => Gap.Matches(m.Groups[1].Value).All(g => values.ContainsKey(g.Groups[1].Value)) ? m.Groups[1].Value : "");
        text = Gap.Replace(text, m => values.GetValueOrDefault(m.Groups[1].Value, ""));
        // Tidy what missing required fields leave behind.
        // (Not before a colon: Evidence Explained writes "URL : accessed".)
        text = Regex.Replace(text, @"\s+([,.;)])", "$1");
        text = Regex.Replace(text, @"([,;:])(?=[,;.])", "");
        text = Regex.Replace(text, @"\(\s*\)", "");
        text = Regex.Replace(text, @"“\s*,?”", "");
        text = Regex.Replace(text, @"\s{2,}", " ").Trim().Trim(',', ';', ':').Trim();
        if (text.Length > 0 && char.IsLower(text[0]) && !text.StartsWith("“")) text = char.ToUpperInvariant(text[0]) + text[1..];
        return text;
    }
}
