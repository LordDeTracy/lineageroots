using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class SuggestionViewTests
{
    [AvaloniaFact]
    public async Task PersonPageSuggestsNextStepsThatBecomeTasks()
    {
        var (vm, _) = await Demo.OpenAsync();
        // Thomas: born 1842 in Harrow, died 1911; his censuses come first, then probate and sources.
        Assert.True(vm.HasPersonHints);
        Assert.Equal(4, vm.PersonHints.Count);
        Assert.Equal("Find Thomas Edward Whitfield in the 1851 England and Wales census", vm.PersonHints[0].Task);
        Assert.Equal("Show all 9", vm.MoreHintsText);
        vm.ToggleAllHintsCommand.Execute(null);
        Assert.Equal(9, vm.PersonHints.Count);
        Assert.Contains(vm.PersonHints, h => h.Task == "Look for a will or estate record for Thomas Edward Whitfield" && h.Where.Contains("National Probate Calendar"));
        Assert.Equal("Show fewer", vm.MoreHintsText);

        vm.AddHintTaskCommand.Execute(vm.PersonHints[0]);
        var task = Assert.Single(vm.Tree.Research.Values);
        Assert.Equal(("Find Thomas Edward Whitfield in the 1851 England and Wales census", "Census", 2),
                     (task.Description, task.Category, task.Priority));
        Assert.Contains(task.Id, vm.Tree.People[1].ResearchIds);
        Assert.True(vm.PersonHints[0].HasTask);
        Assert.Single(vm.PersonResearch);

        var second = vm.PersonHints[1];
        vm.DismissHintCommand.Execute(second);
        Assert.DoesNotContain(vm.PersonHints, h => h.Task == second.Task);
        Assert.Contains(second.Hint.Key, vm.Tree.People[1].DismissedHints);
        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.PersonHints, h => h.Task == second.Task);
    }

    [AvaloniaFact]
    public async Task ResearchPageSuggestsForAncestors()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Section = AppSection.Research;
        vm.ResearchFilterIndex = 3;
        Assert.True(vm.IsSuggestedFilter);
        Assert.False(vm.HasNoResearch);   // the "no tasks yet" panel stays out of the way
        Assert.Contains("Thomas Edward Whitfield and their ancestors", vm.SuggestedSummary);
        Assert.Contains(vm.SuggestedResearch, h => h.ShowPerson && h.PersonName == "George Whitfield" && h.Task == "Find George Whitfield's parents");
        Assert.All(vm.SuggestedResearch, h => Assert.True(h.Hint.Priority <= 2));

        var row = vm.SuggestedResearch.First(h => h.PersonName == "George Whitfield");
        vm.AddHintTaskCommand.Execute(row);
        Assert.True(vm.SuggestedResearch.First(h => h.Task == row.Task).HasTask);
        Assert.Contains(vm.Tree.Research.Values.Single().Id, vm.Tree.People[2].ResearchIds);

        vm.OpenHintPersonCommand.Execute(row);
        Assert.Equal("George Whitfield", vm.CurrentName);
    }
}
