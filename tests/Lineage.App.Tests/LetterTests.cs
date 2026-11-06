using Avalonia.Headless.XUnit;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class LetterTests
{
    [AvaloniaFact]
    public async Task LettersHaveTheirOwnListAndReplyStatus()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.ResearchFilterIndex = 5;
        Assert.Equal("New letter", vm.NewResearchText);
        dialogs.Research = e =>
        {
            Assert.True(e.IsLetter);
            e.Description = "Asked for a copy of the 1868 marriage entry";
            e.With = "Leeds Register Office";
            e.Method = "Email";
            return Lineage.App.ViewModels.EditorResult.Saved;
        };
        await vm.NewResearchCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.ResearchList);
        Assert.Contains("Email to Leeds Register Office", row.Detail);
        Assert.Contains("awaiting a reply", row.Detail);

        vm.ResearchFilterIndex = 0;
        Assert.DoesNotContain(vm.ResearchList, r => r.Id == row.Id);                // letters aren't tasks

        // A reply answers it.
        vm.ResearchFilterIndex = 5;
        dialogs.Research = e => { e.Replied = "20 Mar 2026"; return Lineage.App.ViewModels.EditorResult.Saved; };
        await vm.EditResearchCommand.ExecuteAsync(vm.ResearchList[0]);
        Assert.Contains("answered 20 Mar 2026", vm.ResearchList[0].Detail);
        Assert.True(vm.Tree.Research.Values.Single(r => r.Letter != null).Done);
    }

    [AvaloniaFact]
    public async Task ATripNeedsAnArchive()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        await vm.PlanTripCommand.ExecuteAsync(null);
        Assert.Contains(dialogs.Messages, m => m.Contains("no archives"));

        vm.Tree.AddRepository(new Repository { Name = "Harrow Library" });
        var path = Path.Combine(Path.GetTempPath(), $"lineage-trip-{Guid.NewGuid():N}.pdf");
        try
        {
            dialogs.PromptAnswer = "Harrow Library";
            dialogs.SavePath = path;
            await vm.PlanTripCommand.ExecuteAsync(null);
            Assert.Single(dialogs.Previews);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
