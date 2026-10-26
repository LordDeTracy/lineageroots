using System.Text.RegularExpressions;
using Lineage.Core.Export;
using Lineage.Core.Model;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public sealed class HtmlSiteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"lineage-site-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static readonly Regex LinkRx = new("(?:href|src)=\"([^\"]*)\"", RegexOptions.Compiled);

    private static string Read(string path) => File.ReadAllText(path);

    [Fact]
    public void WritesEveryPageAndEveryLinkResolves()
    {
        var t = Whitfields();
        var site = Path.Combine(_dir, "site");
        var index = HtmlSite.Export(t, site, new HtmlSiteOptions { Title = "The Whitfields", Introduction = "Welcome, cousins.\n\nAdd your stories.", HomePersonId = 1 });

        Assert.Equal(Path.Combine(site, "index.html"), index);
        foreach (var f in new[] { "index.html", "style.css", "search.js", "search-index.js", "surnames/w.html", "surnames/c.html" })
            Assert.True(File.Exists(Path.Combine(site, f)), f);
        foreach (var p in t.People.Values)
            Assert.True(File.Exists(Path.Combine(site, "people", $"{p.Id}.html")), p.DisplayName);

        var pages = Directory.GetFiles(site, "*.html", SearchOption.AllDirectories);
        foreach (var page in pages)
        {
            var html = Read(page);
            Assert.StartsWith("<!DOCTYPE html>", html);
            foreach (Match m in LinkRx.Matches(html))
            {
                var href = m.Groups[1].Value;
                Assert.DoesNotContain("://", href);                    // nothing from the internet: it must work offline
                var target = href.Split('#')[0];
                if (target.Length == 0) continue;
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page)!, target.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(full), $"{Path.GetFileName(page)} links to missing {href}");
            }
        }

        var home = Read(index);
        Assert.Contains("<h1>The Whitfields</h1>", home);
        Assert.Contains("<p>Welcome, cousins.</p>", home);
        Assert.Contains("href=\"people/1.html\"", home);
        Assert.Contains("id=\"q\"", home);
        Assert.Contains("<script src=\"search-index.js\"></script>", home);
        Assert.Contains("href=\"surnames/w.html#", home);
        Assert.DoesNotContain("fetch(", Read(Path.Combine(site, "search.js")));
        Assert.Contains("prefers-color-scheme: dark", Read(Path.Combine(site, "style.css")));
    }

    [Fact]
    public void PersonPageTellsTheStoryWithRelativesAndSources()
    {
        var t = Whitfields();
        HtmlSite.Export(t, _dir, new HtmlSiteOptions());
        var page = Read(Path.Combine(_dir, "people", "1.html"));
        Assert.Contains("<h1>Thomas Edward Whitfield</h1>", page);
        Assert.Contains("1842–1911", page);
        Assert.Contains("Also known as Tom Whitfield", page);
        Assert.Contains("Thomas Edward Whitfield was born on 14 March 1842 in Harrow, Middlesex, England, the son of George Whitfield and Mary Ann Cole.", page);
        Assert.Contains("<strong>Occupation</strong>", page);
        Assert.Contains("<strong>Marriage</strong> with <a href=\"4.html\">Eliza Hartley</a>", page);
        Assert.Contains("<a href=\"2.html\">George Whitfield</a>", page);       // father
        Assert.Contains("<a href=\"5.html\">Clara Whitfield</a>", page);        // child
        Assert.Contains("Church of England, Parish registers of St Mary, Harrow, Baptisms 1842, entry 118.", page);
        Assert.Contains("<p>He sailed for Boston in 1880.</p>", page);
        Assert.DoesNotContain("Secret Street", page);                              // a private event
        Assert.DoesNotContain("Secret Street", Read(Path.Combine(_dir, "search-index.js")));

        var clara = Read(Path.Combine(_dir, "people", "5.html"));
        Assert.Contains("Brothers and sisters", clara);
        Assert.Contains("<a href=\"7.html\">Henry Whitfield</a>", clara);

        HtmlSite.Export(t, Path.Combine(_dir, "nosources"), new HtmlSiteOptions { IncludeSources = false });
        Assert.DoesNotContain("Parish registers", Read(Path.Combine(_dir, "nosources", "people", "1.html")));
    }

    [Fact]
    public void EveryPieceOfDataIsEncoded()
    {
        var t = Whitfields();
        var bad = P(t, "<script>alert(1)</script>", "O'Neil & \"Sons\"", Sex.Male, E("BIRT", "1900", "<img src=x onerror=alert(2)>"));
        bad.Note = "<b>not bold</b>";
        t.Sources[1].Title = "<i>Registers</i>";
        t.People[1].Events[0].Citations[0].Page = "</li><script>alert(3)</script>";
        HtmlSite.Export(t, _dir, new HtmlSiteOptions { Title = "<Family>", Introduction = "<marquee>hi</marquee>", HomePersonId = bad.Id });

        foreach (var file in Directory.GetFiles(_dir, "*.html", SearchOption.AllDirectories))
        {
            var html = Read(file);
            Assert.DoesNotContain("<script>alert", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("<marquee>", html);
            Assert.DoesNotContain("<b>not bold", html);
            Assert.DoesNotContain("<i>Registers", html);
            Assert.DoesNotContain("<Family>", html);
        }
        var page = Read(Path.Combine(_dir, "people", $"{bad.Id}.html"));
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page);
        Assert.Contains("O&#39;Neil &amp; &quot;Sons&quot;", page);

        var js = Read(Path.Combine(_dir, "search-index.js"));
        Assert.StartsWith("window.LINEAGE_PEOPLE = [", js);
        Assert.DoesNotContain("<script>", js);
        Assert.DoesNotContain("</script>", js);
        Assert.Contains("\\u003Cscript\\u003Ealert(1)", js);
    }

    [Fact]
    public void SearchIndexListsEveryoneWithLifespanPlaceAndPage()
    {
        var t = Whitfields();
        HtmlSite.Export(t, _dir, new HtmlSiteOptions());
        var js = Read(Path.Combine(_dir, "search-index.js"));
        var json = js["window.LINEAGE_PEOPLE = ".Length..].TrimEnd().TrimEnd(';');
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var people = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(t.People.Count, people.Count);
        var thomas = people.Single(p => p.GetProperty("n").GetString() == "Thomas Edward Whitfield");
        Assert.Equal("1842–1911", thomas.GetProperty("y").GetString());
        Assert.Equal("Harrow, Middlesex, England", thomas.GetProperty("p").GetString());
        Assert.Equal("people/1.html", thomas.GetProperty("u").GetString());
        Assert.Equal("Tom Whitfield", Assert.Single(thomas.GetProperty("a").EnumerateArray()).GetString());
    }

    [Fact]
    public void PhotosAreCopiedWithSafeNames()
    {
        var t = Whitfields();
        var store = Path.Combine(_dir, "store");
        Directory.CreateDirectory(store);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        File.WriteAllBytes(Path.Combine(store, "Thomas & Eliza (1868).PNG"), png);
        File.WriteAllBytes(Path.Combine(store, "notes.pdf"), new byte[] { 1, 2, 3 });
        t.People[1].Media.Add(new MediaLink { File = "Thomas & Eliza (1868).PNG", MediaType = "PHOTO", Primary = true, Title = "Wedding day" });
        t.People[4].Media.Add(new MediaLink { File = "Thomas & Eliza (1868).PNG", MediaType = "PHOTO" });       // same file, copied once
        t.People[1].Media.Add(new MediaLink { File = "notes.pdf", MediaType = "DOCUMENT" });
        t.People[1].Media.Add(new MediaLink { File = "missing.jpg", MediaType = "PHOTO" });

        var site = Path.Combine(_dir, "site");
        HtmlSite.Export(t, site, new HtmlSiteOptions { HomePersonId = 1 }, file => Path.Combine(store, file));
        var media = Directory.GetFiles(Path.Combine(site, "media")).Select(Path.GetFileName).ToList();
        var name = Assert.Single(media);
        Assert.Matches("^[a-z0-9-]+\\.png$", name!);
        var page = Read(Path.Combine(site, "people", "1.html"));
        Assert.Contains($"src=\"../media/{name}\"", page);
        Assert.Contains($"src=\"media/{name}\"", Read(Path.Combine(site, "index.html")));
        Assert.Contains($"src=\"../media/{name}\"", Read(Path.Combine(site, "people", "4.html")));
        Assert.DoesNotContain("missing.jpg", page);

        var without = Path.Combine(_dir, "nophotos");
        HtmlSite.Export(t, without, new HtmlSiteOptions { IncludePhotos = false }, file => Path.Combine(store, file));
        Assert.False(Directory.Exists(Path.Combine(without, "media")));
        Assert.DoesNotContain("<img", Read(Path.Combine(without, "people", "1.html")));
    }
}
