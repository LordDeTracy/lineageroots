using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class NavigationTests
{
    [AvaloniaFact]
    public async Task ATreeHealthLineListsEveryoneAndBackReturnsHome()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Section = AppSection.Home;
        var line = vm.HealthLines.OrderByDescending(l => l.Ids?.Count ?? 0).First();   // e.g. "people have no sources"
        Assert.True(line.Ids!.Count > 1);

        vm.OpenHomeLineCommand.Execute(line);
        Assert.Equal(AppSection.People, vm.Section);
        Assert.True(vm.HasListFilter);
        Assert.Equal($"{line.Ids.Count} {line.Text}", vm.ListFilterText);
        Assert.Equal(line.Ids.Order(), vm.People.Select(r => r.Id).Order());   // all of them, to work through

        vm.NavigateCommand.Execute(line.Ids[1]);   // the next one in the list
        vm.BackCommand.Execute(null);
        Assert.Equal(vm.Tree.People[line.Ids[0]].DisplayName, vm.CurrentName);
        vm.BackCommand.Execute(null);
        Assert.Equal(AppSection.Home, vm.Section);   // back where she started

        vm.ClearListFilterCommand.Execute(null);
        Assert.Equal(vm.Tree.People.Count, vm.People.Count);
    }

    [AvaloniaFact]
    public async Task SearchTasksLeaveOutTitles()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Tree.People[1].PrimaryName.Prefix = "Pvt.";
        Assert.Equal("Pvt. Thomas Edward Whitfield", vm.Tree.People[1].DisplayName);
        Assert.Equal("Thomas Edward Whitfield", vm.Tree.People[1].RecordName);
        var hints = Lineage.Core.History.ResearchSuggestions.For(vm.Tree, vm.Tree.People[1]);
        Assert.All(hints, h => Assert.DoesNotContain("Pvt.", h.Task));
        Assert.StartsWith("Version ", vm.VersionText);
    }
}
