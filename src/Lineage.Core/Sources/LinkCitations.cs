using System.Globalization;
using System.Text.RegularExpressions;

namespace Lineage.Core.Sources;

/// <summary>What a web link says about the source it points to: a template and its fields, ready for the source editor.</summary>
public sealed record LinkCitation(string TemplateId, IReadOnlyDictionary<string, string> Fields, string Site);

/// <summary>
/// Makes a citation from a web link (Advanced mode): records on FamilySearch, Find a Grave, Ancestry, FreeBMD,
/// FreeCEN, WikiTree, newspapers on Chronicling America, Trove and Papers Past, books on the Internet Archive and Google
/// Books, and The National Archives' catalogue are recognised; any other link becomes a web-page citation. Nothing is
/// downloaded — only the link itself is read, so the user fills in what the page says (names, dates).
/// </summary>
public static class LinkCitations
{
    private sealed record Site(string Name, Regex Pattern, Func<Match, Uri, Dictionary<string, string>> Fields, string Template = "website");

    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Words(string slug) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Uri.UnescapeDataString(slug).Replace('-', ' ').Replace('_', ' ').Trim());

    private static readonly Site[] Sites =
    {
        new("FamilySearch", R(@"familysearch\.org/ark:/61903/1:1:([A-Z0-9-]+)"),
            (m, _) => new() { ["Creator"] = "FamilySearch", ["Title"] = $"FamilySearch record {m.Groups[1].Value}" }),
        new("FamilySearch", R(@"familysearch\.org/ark:/61903/3:1:([A-Z0-9-]+)"),
            (m, _) => new() { ["Creator"] = "FamilySearch", ["Title"] = $"FamilySearch image {m.Groups[1].Value}" }),
        new("FamilySearch", R(@"familysearch\.org/(?:tree/person/details|ark:/61903/4:1:)/?([A-Z0-9-]+)"),
            (m, _) => new() { ["Creator"] = "FamilySearch", ["Title"] = $"Family Tree, person {m.Groups[1].Value}" }),
        new("Find a Grave", R(@"findagrave\.com/memorial/(\d+)(?:/([^/?#]+))?"),
            (m, _) => new() { ["Creator"] = "Find a Grave", ["Title"] = Join($"Memorial {m.Groups[1].Value}", m.Groups[2].Success ? Words(m.Groups[2].Value) : "") }),
        new("Ancestry", R(@"ancestry\.[a-z.]+/discoveryui-content/view/(\d+):(\d+)"),
            (m, _) => new() { ["Creator"] = "Ancestry", ["Title"] = $"Database {m.Groups[2].Value}, record {m.Groups[1].Value}" }),
        new("Ancestry", R(@"ancestry\.[a-z.]+/search/collections/(\d+)"),
            (m, _) => new() { ["Creator"] = "Ancestry", ["Title"] = $"Database {m.Groups[1].Value}" }),
        new("FreeBMD", R(@"freebmd\.org\.uk/cgi/information\.pl\?.*?(?:r=|cite=)([^&]+)"),
            (m, _) => new() { ["Creator"] = "FreeBMD", ["Title"] = "England & Wales Civil Registration Index" }),
        new("FreeCEN", R(@"freecen\.org\.uk/search_records/([a-z0-9]+)"),
            (m, _) => new() { ["Creator"] = "FreeCEN", ["Title"] = $"Census transcription {m.Groups[1].Value}" }),
        new("WikiTree", R(@"wikitree\.com/wiki/([A-Za-z\-']+-\d+)"),
            (m, _) => new() { ["Creator"] = "WikiTree", ["Title"] = $"WikiTree profile {Uri.UnescapeDataString(m.Groups[1].Value)}" }),
        new("Chronicling America", R(@"chroniclingamerica\.loc\.gov/lccn/([a-z0-9]+)/(\d{4})-(\d{2})-(\d{2})/ed-\d+/seq-(\d+)"),
            (m, _) => new() { ["Newspaper"] = $"Newspaper {m.Groups[1].Value}", ["Date"] = Date(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value),
                              ["Article"] = $"Page {m.Groups[5].Value}" }, "newspaper"),
        new("Trove", R(@"trove\.nla\.gov\.au/newspaper/article/(\d+)"),
            (m, _) => new() { ["Creator"] = "National Library of Australia, Trove", ["Title"] = $"Newspaper article {m.Groups[1].Value}" }),
        new("Papers Past", R(@"paperspast\.natlib\.govt\.nz/newspapers/([A-Z]+)(\d{4})(\d{2})(\d{2})"),
            (m, _) => new() { ["Newspaper"] = m.Groups[1].Value, ["Date"] = Date(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value) }, "newspaper"),
        new("Internet Archive", R(@"archive\.org/details/([^/?#]+)"),
            (m, _) => new() { ["Creator"] = "Internet Archive", ["Title"] = Words(m.Groups[1].Value) }),
        new("Google Books", R(@"books\.google\.[a-z.]+/books\?id=([^&#]+)"),
            (m, _) => new() { ["Creator"] = "Google Books", ["Title"] = $"Book {m.Groups[1].Value}" }),
        new("The National Archives", R(@"discovery\.nationalarchives\.gov\.uk/details/r/([A-Z0-9-]+)"),
            (m, _) => new() { ["Creator"] = "The National Archives (UK)", ["Title"] = $"Catalogue entry {m.Groups[1].Value}" }),
    };

    private static string Join(string a, string b) => b.Length > 0 ? $"{a}, {b}" : a;

    private static string Date(string y, string m, string d)
    {
        var month = int.TryParse(m, out var mm) && mm is >= 1 and <= 12 ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(mm) : m;
        return $"{int.Parse(d, CultureInfo.InvariantCulture)} {month} {y}";
    }

    /// <summary>The citation a link points to, or null when the text isn't a web link.</summary>
    public static LinkCitation? From(string link, DateTime today)
    {
        var text = link.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.')) return null;
        var accessed = today.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        foreach (var site in Sites)
        {
            var m = site.Pattern.Match(uri.ToString());
            if (!m.Success) continue;
            var fields = site.Fields(m, uri);
            if (site.Template == "website")
            {
                fields["Website"] = site.Name;
                fields["Url"] = uri.ToString();
                fields["Accessed"] = accessed;
            }
            return new LinkCitation(site.Template, fields, site.Name);
        }
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var lastPart = uri.Segments.LastOrDefault(s => s.Trim('/').Length > 0)?.Trim('/') ?? "";
        return new LinkCitation("website", new Dictionary<string, string>
        {
            ["Title"] = lastPart.Length > 0 && !lastPart.Contains('.') ? Words(lastPart) : host,
            ["Website"] = host, ["Url"] = uri.ToString(), ["Accessed"] = accessed,
        }, host);
    }
}
