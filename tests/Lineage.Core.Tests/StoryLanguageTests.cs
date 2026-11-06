using System.Text.RegularExpressions;
using Lineage.Core.Model;
using Lineage.Core.Reports;
using static Lineage.Core.Tests.ReportTrees;

namespace Lineage.Core.Tests;

public class StoryLanguageTests
{
    public static readonly TheoryData<string> Codes = new() { "sv", "de", "es", "it", "fr", "ru", "pt" };

    private static FamilyTree Family()
    {
        var t = Whitfields();
        var thomas = t.People[1];
        thomas.Events.Add(E("EMIG", "1880", "Liverpool"));
        thomas.Events.Add(E("RELI", "", "", "Methodist"));
        t.People[5].Events.Add(E("CHR", "ABT 1872", "Leeds"));
        return t;
    }

    [Fact]
    public void GermanPutsTheVerbLast()
    {
        var t = Family();
        var de = StoryLanguage.For("de");
        var story = Narrative.LifeSummary(t, t.People[1], de);
        Assert.StartsWith("Thomas Edward Whitfield wurde am 14. März 1842 in Harrow, Middlesex, England als Sohn von George Whitfield und Mary Ann Cole geboren.", story);
        Assert.Contains("Er heiratete Eliza Hartley am 18. Juni 1868 in Leeds, Yorkshire, England.", story);
        Assert.Contains("Er arbeitete als stonemason (1871).", story);
        Assert.Contains("Er starb am 9. November 1911 in Worcester, Massachusetts, USA, im Alter von 69 Jahren, und wurde am 13. November 1911 in Hope Cemetery, Worcester, Massachusetts, USA begraben.", story);
    }

    [Fact]
    public void WordsFollowSex()
    {
        var t = Family();
        var fr = StoryLanguage.For("fr");
        Assert.StartsWith("Clara Whitfield est née en 1872 à Leeds, Yorkshire, England, fille de Thomas Edward Whitfield et Eliza Hartley.", Narrative.LifeSummary(t, t.People[5], fr));
        var ru = StoryLanguage.For("ru");
        Assert.StartsWith("Thomas Edward Whitfield родился 14 марта 1842 г. в Harrow", Narrative.LifeSummary(t, t.People[1], ru));
        Assert.Contains("Она вступила в брак с John Pryor.", Narrative.LifeSummary(t, t.People[5], ru));
        Assert.Contains("в возрасте 69 лет", Narrative.LifeSummary(t, t.People[1], ru));
        // Spanish leaves out "él"/"ella" and starts with the verb.
        Assert.Contains(". Se casó con Eliza Hartley el 18 de junio de 1868", Narrative.LifeSummary(t, t.People[1], StoryLanguage.For("es")));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryLanguageWritesTheWholeStoryInItsOwnWords(string code)
    {
        var lang = StoryLanguage.For(code);
        Assert.Equal(code, lang.Code);
        var t = Family();
        foreach (var p in t.People.Values)
        {
            var story = Narrative.LifeSummary(t, p, lang);
            Assert.False(string.IsNullOrWhiteSpace(story));
            foreach (var english in new[] { " was born", " born ", " married", " died", " buried", " the son of", " the daughter of", " aged ", " lived in", " emigrated", " was recorded", "January", "March", "June" })
                Assert.DoesNotContain(english, story);
            Assert.DoesNotContain("{", story);
            Assert.DoesNotContain("  ", story);
            Assert.DoesNotContain("..", story);
        }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void TemplatesOnlyUseKnownGaps(string code)
    {
        var lang = StoryLanguage.For(code);
        var gaps = new Regex(@"\{([A-Za-z0-9]+)\}");
        using var stream = typeof(StoryLanguage).Assembly.GetManifestResourceStream($"Lineage.Core.Reports.Story.{code}.json")!;
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        var known = new HashSet<string> { "S", "N", "W", "P", "X", "XP", "K", "O", "J", "L", "V", "C", "A", "B", "0", "1", "d", "m", "mg", "mp", "y" };
        foreach (var (key, template) in map)
            foreach (Match m in gaps.Matches(template))
                Assert.True(known.Contains(m.Groups[1].Value), $"{code} {key}: unknown gap {m.Value}");
        // Every sentence names the person somewhere.
        foreach (var (key, template) in map.Where(kv => kv.Value.StartsWith("{S}") is false && kv.Key.StartsWith("ev.") && !kv.Key.Contains("recorded") && !kv.Key.Contains("PROB")))
            Assert.Contains("{S}", template);
        Assert.Equal(lang.Template("born"), map["born"]);
    }
}

