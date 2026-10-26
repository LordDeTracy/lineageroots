using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Lineage.Core.Model;
using Lineage.Core.Reports;

namespace Lineage.Core.Export;

/// <summary>Choices for <see cref="HtmlSite.Export"/>.</summary>
public sealed class HtmlSiteOptions
{
    /// <summary>The site's title; the tree's name when blank.</summary>
    public string Title { get; set; } = "";
    /// <summary>Words for the home page; blank lines separate paragraphs.</summary>
    public string Introduction { get; set; } = "";
    /// <summary>The person the home page invites visitors to start with.</summary>
    public long? HomePersonId { get; set; }
    public bool IncludeSources { get; set; } = true;
    public bool IncludePhotos { get; set; } = true;
}

/// <summary>
/// Writes the tree as a small static website to share with family: a home page with search and a
/// surname index, a page per surname letter, and a page per person with their story, timeline,
/// relatives, notes, photos and sources. It needs no server and no internet — everything, search
/// included, works when the folder is opened straight from disk or a USB stick. Every piece of data
/// is HTML-encoded. Private events are skipped; any other privacy filtering is done by the caller first.
/// </summary>
public static class HtmlSite
{
    private static readonly HashSet<string> WebImages = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    /// <summary>Writes the site into <paramref name="folder"/> and returns the path of its index.html.</summary>
    /// <param name="resolveMediaPath">Turns a media link's stored path (which may be relative) into a file on disk; null or a missing file skips the photo.</param>
    public static string Export(FamilyTree tree, string folder, HtmlSiteOptions options, Func<string, string?>? resolveMediaPath = null)
    {
        var site = new Writer(tree, Path.GetFullPath(folder), options, resolveMediaPath);
        return site.Run();
    }

    private sealed class Writer
    {
        private readonly FamilyTree _tree;
        private readonly string _root;
        private readonly HtmlSiteOptions _options;
        private readonly Func<string, string?>? _resolve;
        private readonly string _title;
        private readonly Dictionary<string, string> _copied = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _stamp = DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        public Writer(FamilyTree tree, string root, HtmlSiteOptions options, Func<string, string?>? resolve)
        {
            _tree = tree;
            _root = root;
            _options = options;
            _resolve = resolve;
            _title = options.Title.Trim().Length > 0 ? options.Title.Trim()
                : tree.Name.Trim().Length > 0 && tree.Name != "Untitled" ? tree.Name.Trim() : "Our family history";
        }

        public string Run()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "people"));
            Directory.CreateDirectory(Path.Combine(_root, "surnames"));
            File.WriteAllText(Path.Combine(_root, "style.css"), Css, Utf8);
            File.WriteAllText(Path.Combine(_root, "search.js"), SearchJs, Utf8);

            var groups = SurnameGroups();
            var groupOf = groups.SelectMany(g => g.People.Select(p => (p.Id, g))).ToDictionary(x => x.Id, x => x.g);
            foreach (var p in _tree.People.Values) WritePerson(p, groupOf.GetValueOrDefault(p.Id));
            foreach (var letter in groups.GroupBy(g => g.Letter)) WriteLetter(letter.Key, letter.ToList(), groups);
            WriteSearchIndex();
            var index = Path.Combine(_root, "index.html");
            File.WriteAllText(index, HomePage(groups), Utf8);
            return index;
        }

        // ---- surnames ----------------------------------------------------------------------------

        private sealed record SurnameGroup(string Surname, string Letter, string Anchor, List<Person> People);

        private List<SurnameGroup> SurnameGroups()
        {
            var groups = new List<SurnameGroup>();
            var byLetter = new Dictionary<string, int>();
            foreach (var g in _tree.People.Values
                         .GroupBy(p => p.PrimaryName.Surname.Trim(), StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var letter = g.Key.Length == 0 ? "other" : LetterKey(g.Key);
                byLetter[letter] = byLetter.GetValueOrDefault(letter) + 1;
                var people = g.OrderBy(p => p.PrimaryName.Given, StringComparer.CurrentCultureIgnoreCase)
                              .ThenBy(p => p.Birth?.Date.SortKey ?? 0).ThenBy(p => p.Id).ToList();
                groups.Add(new SurnameGroup(g.Key.Length > 0 ? g.Key : "(no surname)", letter, $"s{byLetter[letter]}", people));
            }
            return groups;
        }

        private static string LetterKey(string surname)
        {
            foreach (var ch in surname.Normalize(NormalizationForm.FormD))
            {
                if (!char.IsLetterOrDigit(ch)) continue;  // apostrophes, accents
                var u = char.ToUpperInvariant(ch);
                return u is >= 'A' and <= 'Z' ? char.ToLowerInvariant(u).ToString() : "other";
            }
            return "other";
        }

        private static string LetterLabel(string key) => key == "other" ? "Other" : key.ToUpperInvariant();

        private static string Letters(IEnumerable<SurnameGroup> groups, string prefix, string? current = null) =>
            "<nav class=\"letters\" aria-label=\"Surnames by letter\">" +
            string.Concat(groups.Select(g => g.Letter).Distinct().Select(l => l == current
                ? $"<a class=\"current\" aria-current=\"page\" href=\"{prefix}{l}.html\">{LetterLabel(l)}</a>"
                : $"<a href=\"{prefix}{l}.html\">{LetterLabel(l)}</a>")) +
            "</nav>";

        private void WriteLetter(string letter, List<SurnameGroup> mine, List<SurnameGroup> all)
        {
            var sb = new StringBuilder();
            sb.Append($"<section class=\"card intro\"><h1>Surnames — {LetterLabel(letter)}</h1>");
            sb.Append($"<p class=\"muted\">{Count(mine.Sum(g => g.People.Count), "person", "people")} with {Count(mine.Count, "surname", "surnames")}.</p>");
            sb.Append(Letters(all, "", letter)).Append("</section>");
            foreach (var g in mine)
            {
                sb.Append($"<section class=\"card\" id=\"{g.Anchor}\"><h2 class=\"surname\">{H(g.Surname)} <span class=\"count\">{Count(g.People.Count, "person", "people")}</span></h2>");
                sb.Append("<ul class=\"people\">");
                foreach (var p in g.People) sb.Append($"<li>{PersonLink(p, "../people/")}{Dates(p)}</li>");
                sb.Append("</ul></section>");
            }
            File.WriteAllText(Path.Combine(_root, "surnames", letter + ".html"), Page($"Surnames {LetterLabel(letter)} — {_title}", "../", sb.ToString()), Utf8);
        }

        // ---- home page ---------------------------------------------------------------------------

        private string HomePage(List<SurnameGroup> groups)
        {
            var sb = new StringBuilder();
            sb.Append($"<section class=\"hero\"><h1>{H(_title)}</h1>");
            foreach (var para in Paragraphs(_options.Introduction)) sb.Append($"<p>{para}</p>");
            sb.Append($"<p class=\"stats\">{Count(_tree.People.Count, "person", "people")} · {Count(_tree.Families.Count, "family", "families")} · {Count(groups.Count(g => g.Surname != "(no surname)"), "surname", "surnames")}</p>");
            sb.Append("</section>");

            if (_tree.Person(_options.HomePersonId) is { } home)
            {
                sb.Append("<section class=\"card home\"><h2>Start here</h2><div class=\"home-person\">");
                sb.Append(Portrait(home, "people/", small: true));
                sb.Append($"<div><a class=\"big\" href=\"people/{home.Id}.html\">{H(home.DisplayName)}</a>");
                if (home.Lifespan.Length > 0) sb.Append($"<div class=\"muted\">{H(home.Lifespan)}</div>");
                sb.Append("</div></div></section>");
            }

            sb.Append("<section class=\"card search\" id=\"search\"><h2>Find a person</h2>");
            sb.Append("<label class=\"visually-hidden\" for=\"q\">Search by name</label>");
            sb.Append("<input type=\"search\" id=\"q\" placeholder=\"Type a name, a year or a place…\" autocomplete=\"off\" spellcheck=\"false\">");
            sb.Append("<p id=\"search-status\" class=\"muted\" aria-live=\"polite\"></p><ul id=\"results\" class=\"people\"></ul>");
            sb.Append("<noscript><p class=\"muted\">Search needs JavaScript; the surname index below works without it.</p></noscript></section>");

            sb.Append("<section class=\"card\" id=\"surnames\"><h2>Surnames</h2>");
            sb.Append(Letters(groups, "surnames/"));
            foreach (var letter in groups.GroupBy(g => g.Letter))
            {
                sb.Append($"<h3><a href=\"surnames/{letter.Key}.html\">{LetterLabel(letter.Key)}</a></h3><p class=\"cloud\">");
                foreach (var g in letter)
                    sb.Append($"<a href=\"surnames/{g.Letter}.html#{g.Anchor}\">{H(g.Surname)} <span class=\"count\">{g.People.Count}</span></a> ");
                sb.Append("</p>");
            }
            sb.Append("</section>");
            return Page(_title, "", sb.ToString(), "<script src=\"search-index.js\"></script><script src=\"search.js\"></script>");
        }

        private void WriteSearchIndex()
        {
            var entries = _tree.People.Values.OrderBy(p => p.PrimaryName.SortKey, StringComparer.Ordinal).Select(p => new
            {
                n = p.DisplayName,
                a = p.Names.Skip(1).Select(x => x.Full).Where(x => x.Length > 0 && x != p.DisplayName).Distinct().ToArray(),
                y = p.Lifespan,
                p = StoryWriter.Find(p, "BIRT")?.Place.Trim() ?? "",
                u = $"people/{p.Id}.html",
            });
            // A script that sets a global, not JSON to fetch(): browsers block fetch() for pages opened from disk.
            // The default encoder escapes <, >, & and quotes, so names can't break out of the string.
            var json = JsonSerializer.Serialize(entries);
            File.WriteAllText(Path.Combine(_root, "search-index.js"), $"window.LINEAGE_PEOPLE = {json};\n", Utf8);
        }

        // ---- person pages ------------------------------------------------------------------------

        private void WritePerson(Person p, SurnameGroup? group)
        {
            var sb = new StringBuilder();

            sb.Append("<article class=\"card person\"><div class=\"person-head\">");
            sb.Append(Portrait(p, "../people/", small: false));
            sb.Append($"<div><h1>{H(p.DisplayName)}</h1>");
            if (p.Lifespan.Length > 0) sb.Append($"<p class=\"lifespan\">{H(p.Lifespan)}</p>");
            var others = p.Names.Skip(1).Select(n => n.Full).Where(n => n.Length > 0 && n != p.DisplayName).Distinct().ToList();
            if (p.PrimaryName.Nickname.Length > 0) others.Insert(0, $"“{p.PrimaryName.Nickname}”");
            if (others.Count > 0) sb.Append($"<p class=\"muted\">Also known as {H(string.Join(", ", others))}</p>");
            if (group != null) sb.Append($"<p class=\"crumbs\"><a href=\"../surnames/{group.Letter}.html#{group.Anchor}\">All {H(group.Surname)} entries</a></p>");
            sb.Append("</div></div>");
            var summary = Narrative.LifeSummary(_tree, p);
            if (summary.Length > 0 && summary != p.DisplayName + ".") sb.Append($"<p class=\"summary\">{H(summary)}</p>");
            sb.Append("</article>");

            Timeline(sb, p);
            Relatives(sb, p);

            var notes = Paragraphs(p.Note).ToList();
            if (notes.Count > 0)
            {
                sb.Append("<section class=\"card\"><h2>Notes</h2>");
                foreach (var para in notes) sb.Append($"<p>{para}</p>");
                sb.Append("</section>");
            }

            Gallery(sb, p);
            if (_options.IncludeSources) Sources(sb, p);

            File.WriteAllText(Path.Combine(_root, "people", $"{p.Id}.html"), Page($"{p.DisplayName} — {_title}", "../", sb.ToString()), Utf8);
        }

        private void Timeline(StringBuilder sb, Person p)
        {
            var rows = StoryWriter.Visible(p.Events).Select(e => (Event: e, Label: EventTypes.Label(e), With: (Person?)null)).ToList();
            foreach (var (family, spouse, _) in _tree.FamiliesOf(p))
                rows.AddRange(StoryWriter.Visible(family.Events).Select(e => (Event: e, Label: EventTypes.Label(e), With: spouse)));
            if (rows.Count == 0) return;
            sb.Append("<section class=\"card\"><h2>Timeline</h2><ol class=\"timeline\">");
            foreach (var (e, label, with) in rows.Select((r, i) => (r, i))
                         .OrderBy(x => x.r.Event.Date.SortKey == 0 ? int.MaxValue : x.r.Event.Date.SortKey).ThenBy(x => x.i).Select(x => x.r))
            {
                var year = ReportText.Year(e.Date);
                sb.Append($"<li><span class=\"year\">{(year > 0 ? year.ToString(CultureInfo.InvariantCulture) : "")}</span><div>");
                sb.Append($"<strong>{H(label)}</strong>");
                if (with != null) sb.Append($" with {PersonLink(with, "")}");
                var date = ReportText.Date(e.Date);
                if (date.Length > 0) sb.Append($"<div class=\"when\">{H(date)}</div>");
                if (e.Place.Trim().Length > 0) sb.Append($"<div class=\"where\">{H(e.Place.Trim())}</div>");
                var details = new List<string>();
                var value = StoryWriter.Value(e);
                if (value.Length > 0) details.Add(value);
                if (e.Cause.Trim().Length > 0) details.Add("Cause: " + e.Cause.Trim());
                if (e.Note.Trim().Length > 0) details.Add(e.Note.Trim());
                if (details.Count > 0) sb.Append($"<div class=\"detail\">{H(string.Join(". ", details))}</div>");
                sb.Append("</div></li>");
            }
            sb.Append("</ol></section>");
        }

        private void Relatives(StringBuilder sb, Person p)
        {
            var parents = _tree.ParentsOf(p).Distinct().ToList();
            var siblings = p.ParentFamilyIds.Select(id => _tree.Family(id)).OfType<Family>()
                .SelectMany(_tree.ChildrenOf).Where(c => c.Id != p.Id).Distinct()
                .OrderBy(c => c.Birth is { } b && b.Date.SortKey > 0 ? b.Date.SortKey : int.MaxValue).ToList();
            var families = _tree.FamiliesOf(p).ToList();
            if (parents.Count == 0 && siblings.Count == 0 && families.Count == 0) return;

            sb.Append("<section class=\"card\"><h2>Family</h2>");
            if (parents.Count > 0) PeopleList(sb, "Parents", parents);
            foreach (var (family, spouse, children) in families)
            {
                sb.Append("<div class=\"family\">");
                sb.Append(spouse != null ? $"<h3>With {PersonLink(spouse, "")}{Dates(spouse)}</h3>" : "<h3>Family</h3>");
                if (StoryWriter.Visible(family.Events).FirstOrDefault(e => e.Tag == "MARR") is { } m)
                {
                    var when = string.Join(" ", new[] { ReportText.DatePhrase(m.Date), m.Place.Trim().Length > 0 ? "in " + m.Place.Trim() : "" }.Where(x => x.Length > 0));
                    sb.Append($"<p class=\"muted\">Married{(when.Length > 0 ? " " + H(when) : "")}</p>");
                }
                if (children.Count > 0) PeopleList(sb, children.Count == 1 ? "Child" : "Children", children, heading: false);
                sb.Append("</div>");
            }
            if (siblings.Count > 0) PeopleList(sb, "Brothers and sisters", siblings);
            sb.Append("</section>");
        }

        private static void PeopleList(StringBuilder sb, string title, IEnumerable<Person> people, bool heading = true)
        {
            sb.Append(heading ? $"<h3>{H(title)}</h3>" : $"<p class=\"label\">{H(title)}</p>");
            sb.Append("<ul class=\"people\">");
            foreach (var x in people) sb.Append($"<li>{PersonLink(x, "")}{Dates(x)}</li>");
            sb.Append("</ul>");
        }

        private void Gallery(StringBuilder sb, Person p)
        {
            if (!_options.IncludePhotos) return;
            var portrait = ProfilePhoto(p);
            var photos = p.Media.Where(m => m != portrait).Select(m => (Media: m, File: Copy(m))).Where(x => x.File != null).ToList();
            if (photos.Count == 0) return;
            sb.Append("<section class=\"card\"><h2>Photos</h2><div class=\"gallery\">");
            foreach (var (m, file) in photos)
            {
                var caption = m.Title.Trim();
                sb.Append($"<figure><a href=\"../media/{file}\"><img src=\"../media/{file}\" alt=\"{H(caption.Length > 0 ? caption : "Photo of " + p.DisplayName)}\" loading=\"lazy\"></a>");
                if (caption.Length > 0) sb.Append($"<figcaption>{H(caption)}</figcaption>");
                sb.Append("</figure>");
            }
            sb.Append("</div></section>");
        }

        private void Sources(StringBuilder sb, Person p)
        {
            var citations = p.Citations.Concat(StoryWriter.Visible(p.Events).SelectMany(e => e.Citations)).ToList();
            foreach (var (family, _, _) in _tree.FamiliesOf(p))
                citations.AddRange(family.Citations.Concat(StoryWriter.Visible(family.Events).SelectMany(e => e.Citations)));
            var lines = citations.Select(c => NoteBook.CitationText(_tree, c)).Where(t => t.Length > 0).Distinct().ToList();
            if (lines.Count == 0) return;
            sb.Append("<section class=\"card\"><h2>Sources</h2><ol class=\"sources\">");
            foreach (var line in lines) sb.Append($"<li>{H(line)}</li>");
            sb.Append("</ol></section>");
        }

        // ---- photos ------------------------------------------------------------------------------

        private MediaLink? ProfilePhoto(Person p) =>
            p.Media.Where(m => m.Primary).Concat(p.Media).FirstOrDefault(m => Copy(m) != null);

        private string Portrait(Person p, string peoplePrefix, bool small)
        {
            var cls = small ? "portrait small" : "portrait";
            if (_options.IncludePhotos && ProfilePhoto(p) is { } m && Copy(m) is { } file)
            {
                var media = peoplePrefix.StartsWith("../", StringComparison.Ordinal) ? "../media/" : "media/";
                return $"<img class=\"{cls}\" src=\"{media}{file}\" alt=\"{H("Photo of " + p.DisplayName)}\">";
            }
            return $"<div class=\"{cls} initials\" aria-hidden=\"true\">{H(Initials(p))}</div>";
        }

        /// <summary>Copies an image into media/ once, under a safe unique name; null when it isn't a web image or can't be found.</summary>
        private string? Copy(MediaLink m)
        {
            if (!_options.IncludePhotos || m.File.Trim().Length == 0) return null;
            if (m.MediaType.Length > 0 && !m.MediaType.Equals("PHOTO", StringComparison.OrdinalIgnoreCase)) return null;
            string? source;
            try
            {
                source = _resolve != null ? _resolve(m.File) : m.File;
                if (string.IsNullOrWhiteSpace(source)) return null;
                source = Path.GetFullPath(source);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return null; }
            if (_copied.TryGetValue(source, out var done)) return done.Length > 0 ? done : null;
            var ext = Path.GetExtension(source).ToLowerInvariant();
            if (!WebImages.Contains(ext) || !File.Exists(source)) { _copied[source] = ""; return null; }

            var stem = new string(Path.GetFileNameWithoutExtension(source).ToLowerInvariant()
                .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray()).Trim('-');
            while (stem.Contains("--")) stem = stem.Replace("--", "-");
            if (stem.Length == 0) stem = "photo";
            if (stem.Length > 40) stem = stem[..40].Trim('-');
            var name = $"{stem}{ext}";
            for (var n = 2; _usedNames.Contains(name); n++) name = $"{stem}-{n}{ext}";
            try
            {
                Directory.CreateDirectory(Path.Combine(_root, "media"));
                File.Copy(source, Path.Combine(_root, "media", name), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _copied[source] = ""; return null; }
            _usedNames.Add(name);
            _copied[source] = name;
            return name;
        }

        // ---- shared ------------------------------------------------------------------------------

        private string Page(string title, string root, string body, string scripts = "") => $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="Lineage Roots">
            <title>{H(title)}</title>
            <link rel="stylesheet" href="{root}style.css">
            </head>
            <body>
            <header class="site-header"><div class="wrap"><a class="site-title" href="{root}index.html">{H(_title)}</a><nav><a href="{root}index.html#search">Search</a><a href="{root}index.html#surnames">Surnames</a></nav></div></header>
            <main class="wrap">
            {body}
            </main>
            <footer class="wrap">Made with Lineage Roots · {H(_stamp)}</footer>
            {scripts}
            </body>
            </html>

            """;

        private static string PersonLink(Person p, string prefix) => $"<a href=\"{prefix}{p.Id}.html\">{H(p.DisplayName)}</a>";

        private static string Dates(Person p) => p.Lifespan.Length > 0 ? $" <span class=\"muted\">{H(p.Lifespan)}</span>" : "";

        private static string Initials(Person p)
        {
            var g = p.PrimaryName.Given.Trim();
            var s = p.PrimaryName.Surname.Trim();
            var text = (g.Length > 0 ? char.ToUpperInvariant(g[0]).ToString() : "") + (s.Length > 0 ? char.ToUpperInvariant(s[0]).ToString() : "");
            return text.Length > 0 ? text : "?";
        }

        /// <summary>Encoded paragraphs; single line breaks become &lt;br&gt;.</summary>
        private static IEnumerable<string> Paragraphs(string text) =>
            text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => string.Join("<br>", p.Split('\n', StringSplitOptions.TrimEntries).Select(H)));

        private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} {many}";
    }

    private static string H(string s) => WebUtility.HtmlEncode(s);

    private static readonly UTF8Encoding Utf8 = new(false);

    private const string Css = """
        :root {
          --paper: #F6F3EE; --card: #FFFFFF; --ink: #22252A; --muted: #6F7378; --faint: #9A9DA2;
          --line: #E4DED5; --accent: #2E6B58; --accent-soft: #E4EFEA;
          --radius: 14px; --shadow: 0 1px 2px rgba(34, 37, 42, .05), 0 6px 20px rgba(34, 37, 42, .05);
          color-scheme: light dark;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --paper: #16181B; --card: #202327; --ink: #ECE9E4; --muted: #A5A8AC; --faint: #7E8186;
            --line: #33373C; --accent: #7CC4A6; --accent-soft: #23362F; --shadow: none;
          }
        }
        * { box-sizing: border-box; }
        html { -webkit-text-size-adjust: 100%; }
        body {
          margin: 0; background: var(--paper); color: var(--ink);
          font: 16px/1.6 system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
        }
        a { color: var(--accent); text-decoration: none; }
        a:hover { text-decoration: underline; }
        .wrap { max-width: 920px; margin: 0 auto; padding: 0 16px; }
        .site-header { border-bottom: 1px solid var(--line); background: var(--card); }
        .site-header .wrap { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 20px; padding-top: 14px; padding-bottom: 14px; }
        .site-title { color: var(--ink); font-weight: 650; font-size: 1.05rem; }
        .site-header nav { display: flex; gap: 18px; font-size: .95rem; }
        main { padding-top: 8px; padding-bottom: 8px; }
        .hero { padding: 36px 4px 12px; }
        .hero p { max-width: 64ch; }
        .stats { color: var(--muted); font-size: .95rem; }
        .card {
          background: var(--card); border: 1px solid var(--line); border-radius: var(--radius);
          padding: 22px 26px; margin: 16px 0; box-shadow: var(--shadow);
        }
        h1 { font-size: clamp(1.6rem, 4.5vw, 2.25rem); line-height: 1.2; margin: 0 0 6px; font-weight: 650; letter-spacing: -0.01em; }
        h2 { font-size: .78rem; text-transform: uppercase; letter-spacing: .09em; color: var(--accent); margin: 0 0 12px; font-weight: 650; }
        h2.surname { font-size: 1.2rem; text-transform: none; letter-spacing: 0; color: var(--ink); }
        h3 { font-size: 1rem; margin: 18px 0 6px; font-weight: 600; }
        .muted, .count { color: var(--muted); }
        .count { font-size: .85em; font-weight: 400; }
        .label { margin: 8px 0 2px; color: var(--muted); font-size: .9rem; }
        .person-head { display: flex; gap: 22px; align-items: center; }
        .portrait {
          width: 120px; height: 120px; border-radius: 50%; object-fit: cover; flex: none;
          background: var(--accent-soft); color: var(--accent); border: 1px solid var(--line);
        }
        .portrait.small { width: 64px; height: 64px; }
        .initials { display: flex; align-items: center; justify-content: center; font-size: 2.2rem; font-weight: 600; }
        .portrait.small.initials { font-size: 1.3rem; }
        .lifespan { margin: 0; color: var(--muted); font-size: 1.1rem; }
        .crumbs { margin: 6px 0 0; font-size: .9rem; }
        .summary { margin: 18px 0 0; font-size: 1.05rem; max-width: 70ch; }
        .home-person { display: flex; gap: 16px; align-items: center; }
        .big { font-size: 1.25rem; font-weight: 600; }
        .timeline { list-style: none; margin: 0; padding: 0; }
        .timeline li { display: grid; grid-template-columns: 64px 1fr; gap: 14px; padding: 11px 0; border-top: 1px solid var(--line); }
        .timeline li:first-child { border-top: 0; padding-top: 0; }
        .year { color: var(--accent); font-weight: 650; font-variant-numeric: tabular-nums; }
        .when, .where { color: var(--muted); font-size: .95rem; }
        .detail { font-size: .95rem; margin-top: 2px; }
        .people { list-style: none; padding: 0; margin: 0; }
        .people li { padding: 3px 0; }
        .family { border-top: 1px solid var(--line); margin-top: 14px; padding-top: 2px; }
        .family:first-of-type { border-top: 0; }
        .sources { padding-left: 1.4em; margin: 0; }
        .sources li { margin: 4px 0; }
        .search input {
          width: 100%; font: inherit; padding: 12px 14px; border-radius: 10px;
          border: 1px solid var(--line); background: var(--paper); color: var(--ink);
        }
        .search input:focus { outline: 2px solid var(--accent); outline-offset: 1px; }
        #search-status { margin: 10px 0 4px; font-size: .9rem; }
        #search-status:empty, #results:empty { display: none; }
        .letters { display: flex; flex-wrap: wrap; gap: 6px; margin: 4px 0 8px; }
        .letters a { min-width: 36px; text-align: center; padding: 4px 9px; border-radius: 8px; background: var(--accent-soft); font-weight: 600; }
        .letters a.current { background: var(--accent); color: var(--card); }
        .cloud { line-height: 2; margin: 0 0 6px; }
        .cloud a { display: inline-block; margin-right: 14px; }
        .gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 14px; }
        .gallery figure { margin: 0; }
        .gallery img { width: 100%; aspect-ratio: 1; object-fit: cover; border-radius: 10px; display: block; background: var(--accent-soft); }
        .gallery figcaption { font-size: .85rem; color: var(--muted); margin-top: 4px; }
        footer { color: var(--faint); font-size: .85rem; text-align: center; padding-top: 24px; padding-bottom: 40px; }
        .visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
        @media (max-width: 560px) {
          .card { padding: 18px; border-radius: 12px; }
          .person-head { flex-direction: column; align-items: flex-start; gap: 14px; }
          .portrait { width: 96px; height: 96px; }
          .timeline li { grid-template-columns: 50px 1fr; gap: 10px; }
        }
        @media print {
          body { background: #fff; color: #000; }
          .card { box-shadow: none; break-inside: avoid; }
          .search, .site-header nav { display: none; }
        }

        """;

    private const string SearchJs = """
        // Name search for the home page. The people come from search-index.js (a plain script, so it
        // also works when the site is opened from disk); results are built with textContent only.
        (function () {
          var input = document.getElementById('q');
          var list = document.getElementById('results');
          var status = document.getElementById('search-status');
          if (!input || !list) return;
          var people = window.LINEAGE_PEOPLE || [];
          function fold(s) {
            s = String(s || '');
            if (s.normalize) s = s.normalize('NFD').replace(/[̀-ͯ]/g, '');
            return s.toLowerCase();
          }
          var keys = people.map(function (p) {
            return fold([p.n].concat(p.a || [], [p.y || '', p.p || '']).join(' '));
          });
          function show() {
            var words = fold(input.value).split(/\s+/).filter(Boolean);
            list.textContent = '';
            if (!words.length) { status.textContent = ''; return; }
            var found = 0, shown = 0;
            for (var i = 0; i < people.length; i++) {
              var key = keys[i];
              if (!words.every(function (w) { return key.indexOf(w) >= 0; })) continue;
              found++;
              if (shown >= 100) continue;
              shown++;
              var p = people[i];
              var li = document.createElement('li');
              var a = document.createElement('a');
              a.href = p.u;
              a.textContent = p.n;
              li.appendChild(a);
              var extra = [p.y, p.p].filter(Boolean).join(' · ');
              if (extra) {
                var span = document.createElement('span');
                span.className = 'muted';
                span.textContent = ' ' + extra;
                li.appendChild(span);
              }
              list.appendChild(li);
            }
            status.textContent = found === 0 ? 'No one matches “' + input.value.trim() + '”.'
              : found > shown ? 'Showing the first ' + shown + ' of ' + found + ' matches.'
              : found === 1 ? '1 match.' : found + ' matches.';
          }
          input.addEventListener('input', show);
          if (location.hash === '#search') input.focus();
          if (input.value) show();
        })();

        """;
}
