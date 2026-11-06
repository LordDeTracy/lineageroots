using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lineage.App.Services;
using System.Text.RegularExpressions;

namespace Lineage.App.Tests;

public class TranslationTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lineage.App"))) dir = dir.Parent;
        return dir!.FullName;
    }

    public static TheoryData<string> Languages => new() { "sv", "de", "es", "it", "fr", "ru", "pt" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLabelOnScreenIsTranslated(string code)
    {
        var repo = Repo();
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(repo, "src", "Lineage.App", "Assets", "Lang", code + ".json")))!;
        var labels = Directory.GetFiles(Path.Combine(repo, "src", "Lineage.App"), "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\b(Text|Content|Watermark|ToolTip\.Tip|Header|Title|PlaceholderText)=""([^""]*)""")
                .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[2].Value)))
            .Where(v => v.Length > 0 && !v.StartsWith('{') && Regex.IsMatch(v, "[A-Za-z]"))
            .Distinct().ToList();
        // Product names, codes and examples may stay as they are; nearly everything else needs a translation.
        var missing = labels.Where(l => !map.ContainsKey(l)).ToList();
        Assert.True(missing.Count <= labels.Count / 20, $"{missing.Count} labels without a translation, e.g. {string.Join(" | ", missing.Take(8))}");
        // Shortcuts and product names survive translation.
        foreach (var (en, tr) in map)
        {
            foreach (Match k in Regex.Matches(en, @"\+(\w)\)"))   // the key itself: "+K)", "+5)" ("Ctrl" may become "Strg")
                Assert.True(tr.Contains(k.Value), $"{code}: '{tr}' lost the shortcut {k.Value}");
            foreach (var name in new[] { "Lineage Roots", "GEDCOM", "FamilySearch" })
                if (en.Contains(name)) Assert.Contains(name, tr);
            // A pattern keeps every gap, or a name or number would go missing.
            Assert.True(Gaps(en).SequenceEqual(Gaps(tr)), $"{code}: '{tr}' doesn't keep the gaps of '{en}'");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void SourceTypesFromAqAreTranslated(string code)
    {
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Repo(), "src", "Lineage.App", "Assets", "Lang", code + ".json")))!;
        var missing = Lineage.Core.Import.Aq.AqSourceTypes.Names.Values.Where(n => !map.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0, $"{code}: {string.Join(" | ", missing)}");
    }

    private static IEnumerable<string> Gaps(string s) => Regex.Matches(s, @"\{\d\}").Select(m => m.Value).Order();

    [AvaloniaFact]
    public void MessagesWithNamesAndNumbersAreTranslated()
    {
        try
        {
            Translator.Use("sv");
            Assert.Equal("Sparade Familjen.lineage på 12 ms", Translator.T("Saved Familjen.lineage in 12 ms"));
            Assert.Equal("48 PERSONER", Translator.T("48 PEOPLE"));
            // A line of several phrases is translated phrase by phrase.
            Assert.Equal("Född 14 Mar 1842 · Död 9 Nov 1911 · 69 år gammal", Translator.T("Born 14 Mar 1842 · Died 9 Nov 1911 · aged 69"));
            // The gap is a phrase of its own, so it's translated too.
            Assert.Equal("Ångra lägg till person", Translator.T("Undo add person"));
            // Names and anything that isn't a known phrase stay as they are.
            Assert.Equal("Ann Grey", Translator.T("Ann Grey"));
            Assert.Equal("Undo", Translator.T("Undo"));
        }
        finally { Translator.Use("en"); }
        Assert.Equal("48 PEOPLE", Translator.T("48 PEOPLE"));
    }

    [AvaloniaFact]
    public void HistoryCensusesAndReportsAreTranslated()
    {
        try
        {
            Translator.Use("pt");
            Assert.Equal("Censo de 1881 em Irlanda", Translator.T("1881 Ireland census"));
            Assert.Equal("Censo de 1851 do Canadá", Translator.T("1851 census of Canada"));
            Assert.Equal("Primeira Guerra Mundial", Translator.T("First World War"));

            var doc = new Lineage.Core.Reports.ReportDocument { Title = "Ancestors of Ann Grey" };
            doc.Add(new Lineage.Core.Reports.Heading(1, "Facts and events"))
               .Add(new Lineage.Core.Reports.Paragraph(new[] { new Lineage.Core.Reports.Run("Ann Grey ", Bold: true), new Lineage.Core.Reports.Run("No events are recorded.") }));
            var pt = Translator.Translate(doc);
            Assert.Equal("Ancestrais de Ann Grey", pt.Title);
            Assert.Equal("Fatos e eventos", ((Lineage.Core.Reports.Heading)pt.Blocks[0]).Text);
            // Each run keeps the space that joins it to the next; names are left alone.
            Assert.Equal("Ann Grey Nenhum evento registrado.", ((Lineage.Core.Reports.Paragraph)pt.Blocks[1]).Text);
        }
        finally { Translator.Use("en"); }
    }
}
