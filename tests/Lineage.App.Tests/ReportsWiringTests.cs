using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class ReportsWiringTests
{
    [AvaloniaFact]
    public async Task EveryReportSavesAsPdfAndWord()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var commands = new[]
        {
            vm.RegisterReportCommand, vm.IndividualSummaryCommand, vm.PlaceListReportCommand,
            vm.SurnameListReportCommand, vm.BirthdayCalendarCommand, vm.FamilyBookCommand,
            vm.ClientReportCommand, vm.PrintResearchCommand,
        };
        dialogs.PromptAnswer = "Example";   // the client report's questions
        foreach (var command in commands)
            foreach (var ext in new[] { ".pdf", ".docx" })
            {
                var path = Path.Combine(Path.GetTempPath(), $"lineage-r-{Guid.NewGuid():N}{ext}");
                dialogs.SavePath = path;
                await command.ExecuteAsync(null);
                Assert.Empty(dialogs.Messages);
                var head = new byte[4];
                await using (var f = File.OpenRead(path)) _ = await f.ReadAsync(head);
                Assert.Equal(ext == ".pdf" ? "%PDF" : "PK\u0003\u0004", System.Text.Encoding.ASCII.GetString(head));
                File.Delete(path);
            }
    }

    [AvaloniaFact]
    public async Task WebsiteIsWrittenWithLivingPeopleHidden()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Person = e => { e.Given = "Young"; e.Surname = "Whitfield"; e.BirthDate = "2001"; return true; };
        await vm.AddPersonCommand.ExecuteAsync(null);
        var folder = Path.Combine(Path.GetTempPath(), $"lineage-site-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        dialogs.FolderPath = folder;
        await vm.ExportWebsiteCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.Messages);
        var index = Path.Combine(folder, "index.html");
        Assert.True(File.Exists(index));
        Assert.Contains(index, TestApp.Opened);
        var all = string.Join("\n", Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                                             .Where(f => f.EndsWith(".html") || f.EndsWith(".js")).Select(File.ReadAllText));
        Assert.Contains("Thomas Edward", all);
        Assert.DoesNotContain("Young", all);
        Directory.Delete(folder, recursive: true);
    }

    [AvaloniaFact]
    public async Task AClientReportLaysOutTheResearch()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People[1];
        var birth = thomas.Find("BIRT")!;
        birth.Conclusion = Lineage.Core.Model.FactConclusion.Proven;
        birth.Reasoning = "The baptism register entry was made at the time.";
        thomas.ResearchIds.Add(vm.Tree.AddResearch(new Lineage.Core.Model.ResearchItem
        {
            Description = "Searched the 1851 census for Thomas", Result = Lineage.Core.Model.SearchResult.NotFound,
            Note = "FamilySearch index",
        }).Id);

        var doc = Lineage.Core.Reports.ClientReport.Build(vm.Tree, thomas,
            new Lineage.Core.Reports.ClientReportOptions("The Gray family", "A. Researcher", "Who were Thomas's parents?"));
        Assert.Equal("Research report: Thomas Edward Whitfield", doc.Title);
        Assert.StartsWith("Prepared for The Gray family · by A. Researcher", doc.Subtitle);
        var headings = doc.Blocks.OfType<Lineage.Core.Reports.Heading>().Select(h => h.Text).ToList();
        Assert.Equal(new[] { "Research objective", "Summary of findings", "Conclusions", "Evidence", "Searches made",
                             "Recommendations for further research", "Sources consulted" },
                     headings.Where((_, i) => doc.Blocks.OfType<Lineage.Core.Reports.Heading>().ElementAt(i).Level == 1));
        Assert.Contains(headings, h => h.StartsWith("Birth") && h.EndsWith("proven"));
        Assert.Contains(headings, h => h == "When and where was Thomas born?");
        var searches = doc.Blocks.OfType<Lineage.Core.Reports.Table>().Single(t => t.Headers[0] == "Search");
        Assert.Equal("Not found", searches.Rows.Single()[3].Text);

        // Saved for a look when asked for (LINEAGE_SAMPLE_REPORT=path.pdf).
        if (Environment.GetEnvironmentVariable("LINEAGE_SAMPLE_REPORT") is { Length: > 0 } sample)
        {
            dialogs.SavePath = sample;
            dialogs.PromptAnswer = "The Gray family";
            await vm.ClientReportCommand.ExecuteAsync(null);
        }
    }

    [AvaloniaFact]
    public async Task ThePhoneViewerIsOneFileWithTheTreeInIt()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-phone-{Guid.NewGuid():N}.html");
        try
        {
            dialogs.SavePath = path;
            await vm.ExportPhoneViewerCommand.ExecuteAsync(null);
            var html = File.ReadAllText(path);
            Assert.StartsWith("<!doctype html>", html);
            Assert.Contains("Thomas Edward Whitfield", html);
            Assert.Contains(dialogs.Messages, m => m.Contains("web browser"));
        }
        finally { File.Delete(path); }
    }
}
