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
        };
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
}
