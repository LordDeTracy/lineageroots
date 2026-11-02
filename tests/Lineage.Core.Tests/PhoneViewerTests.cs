using System.Text.Json;
using System.Text.RegularExpressions;
using Lineage.Core.Export;
using Lineage.Core.Model;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class PhoneViewerTests
{
    /// <summary>The tree written into the page, read back the way the page's script sees it.</summary>
    private static JsonElement Data(string html)
    {
        var m = Regex.Match(html, @"const D = (\{.*?\});\r?\n", RegexOptions.Singleline);
        Assert.True(m.Success, "the page carries its data");
        return JsonDocument.Parse(m.Groups[1].Value).RootElement;
    }

    private static JsonElement Person(JsonElement data, long id) =>
        data.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("i").GetInt64() == id);

    [Fact]
    public void OnePageHoldsTheWholeFamily()
    {
        var t = Whitfields();
        var html = PhoneViewer.Build(t, new PhoneViewerOptions { Title = "The Whitfields", HomePersonId = 1 });

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<title>The Whitfields</title>", html);
        Assert.Contains("width=device-width", html);
        // Nothing to fetch: it has to open from a file on a phone with no internet.
        Assert.DoesNotMatch(@"(src|href)=""https?:", html);

        var data = Data(html);
        Assert.Equal(1, data.GetProperty("home").GetInt64());
        Assert.Equal(t.People.Count, data.GetProperty("people").GetArrayLength());

        var thomas = Person(data, 1);
        Assert.Equal("Thomas Edward Whitfield", thomas.GetProperty("n").GetString());
        Assert.Equal("Stonemason", thomas.GetProperty("o").GetString());
        Assert.Contains("Tom Whitfield", thomas.GetProperty("a").EnumerateArray().Select(a => a.GetString()));
        Assert.Contains("font at St Mary", thomas.GetProperty("t").GetString());
        // Parents, the marriage with its children, and life events in date order.
        Assert.Equal(2, thomas.GetProperty("r").GetArrayLength());
        var family = thomas.GetProperty("f").EnumerateArray().First();
        Assert.Equal(3, family.GetProperty("c").GetArrayLength());
        Assert.Contains("Leeds", family.GetProperty("m").GetString());
        var events = thomas.GetProperty("e").EnumerateArray().Select(e => e.GetProperty("l").GetString()).ToList();
        Assert.Equal("Birth", events[0]);
        Assert.True(events.IndexOf("Death") > events.IndexOf("Marriage"));
        // A private event stays out, as it does in reports.
        Assert.DoesNotContain("Secret Street", html);
    }

    [Fact]
    public void NamesCannotBreakThePageAndLabelsAreTranslated()
    {
        var t = new FamilyTree();
        var p = t.AddPerson(new Person { Sex = Sex.Female });
        p.Names.Add(new Name { Given = "</script><script>alert(1)</script>", Surname = "O'Brien" });
        var html = PhoneViewer.Build(t, new PhoneViewerOptions { IncludeNotes = false },
            s => s == "Search names" ? "Sök namn" : s);

        Assert.DoesNotContain("<script>alert", html);
        var data = Data(html);
        Assert.Equal("</script><script>alert(1)</script> O'Brien", Person(data, p.Id).GetProperty("n").GetString());
        Assert.Equal("Sök namn", data.GetProperty("words").GetProperty("Search names").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("home").ValueKind);
    }
}
