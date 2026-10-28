using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class ResearchLogTests
{
    [AvaloniaFact]
    public async Task ASearchThatFoundNothingIsLogged()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var hint = vm.PersonHints.First(h => h.Task == "Find Thomas Edward Whitfield in the 1851 England and Wales census");
        dialogs.PromptAnswer = "FamilySearch index";
        dialogs.ChooseAnswer = 0;   // not found
        await vm.LogHintSearchCommand.ExecuteAsync(hint);

        var item = Assert.Single(vm.Tree.Research.Values);
        Assert.Equal((SearchResult.NotFound, "census:England:1851", "FamilySearch index", true),
                     (item.Result, item.HintKey, item.Note, item.Done));
        Assert.Contains(item.Id, vm.Tree.People[1].ResearchIds);
        Assert.DoesNotContain(vm.PersonHints, h => h.Task == hint.Task);   // not suggested again

        vm.View = PersonView.Timeline;
        var row = vm.CensusRows.Single(r => r.Check.Census.Year == 1851);
        Assert.StartsWith("Searched ", row.Reason);
        Assert.Contains("not found (FamilySearch index)", row.Reason);

        // It's in the search log, not among the tasks.
        vm.Section = AppSection.Research;
        vm.ResearchFilterIndex = 0;
        Assert.Empty(vm.ResearchList);
        vm.ResearchFilterIndex = 2;
        Assert.Empty(vm.ResearchList);
        vm.ResearchFilterIndex = 4;
        Assert.Contains("Searched: not found", Assert.Single(vm.ResearchList).Detail);
        Assert.Equal("0 open · 0 done · 1 search logged", vm.ResearchSummary);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Tree.Research.Values);
    }

    [AvaloniaFact]
    public async Task CancellingLogsNothing()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.PromptAnswer = "Ancestry";
        dialogs.ChooseAnswer = 3;   // cancel
        await vm.LogHintSearchCommand.ExecuteAsync(vm.PersonHints[0]);
        dialogs.PromptAnswer = null;
        await vm.LogHintSearchCommand.ExecuteAsync(vm.PersonHints[0]);
        Assert.Empty(vm.Tree.Research.Values);
    }

    [Fact]
    public void TheEditorRecordsASearch()
    {
        var item = new ResearchItem { Description = "Look in the 1861 census" };
        var editor = new ResearchEditorViewModel(new FamilyTree(), item, isNew: true, Array.Empty<long>());
        editor.ResultIndex = 3;
        editor.Apply(item);
        Assert.Equal((SearchResult.Inconclusive, true), (item.Result, item.Done));
    }
}
