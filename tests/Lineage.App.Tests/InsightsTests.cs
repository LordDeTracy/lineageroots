using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class InsightsTests
{
    [AvaloniaFact]
    public async Task SoundsLikeSearchFindsOtherSpellings()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.SearchText = "Whitfield";
        vm.SoundsLike = false;
        await Task.Delay(300);                              // the search waits for typing to pause
        Assert.DoesNotContain(vm.People, r => r.Id == 6);   // "Whitfeld"
        vm.SoundsLike = true;
        Assert.Contains(vm.People, r => r.Id == 6);
        Assert.NotEqual(6, vm.People[0].Id);                // exact matches come first
    }

    [AvaloniaFact]
    public async Task HomePageShowsHealthAndSurnames()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Section = AppSection.Home;
        Assert.True(vm.HasHealthLines);
        Assert.Contains(vm.HealthLines, l => l.Text.Contains("no sources"));
        Assert.Contains(vm.TopSurnames, s => s.StartsWith("Whitfield"));
        Assert.Contains("people", vm.HomeStatsText);
    }

    [AvaloniaFact]
    public async Task ProblemFinderAndRelationshipCalculatorOpen()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.FindProblemsCommand.Execute(null);
        Assert.NotNull(dialogs.LastProblems);
        Assert.False(string.IsNullOrEmpty(dialogs.LastProblems!.Summary));

        vm.NavigateCommand.Execute(5L);                      // Clara
        vm.FindRelationshipCommand.Execute(null);
        var rel = dialogs.LastRelationship!;
        rel.Second = rel.People.Single(p => p.Id == 2);      // George, her grandfather
        Assert.Equal("George Whitfield is Clara Whitfield's grandfather.", rel.Answer);
        Assert.True(rel.HasPath);
        rel.OpenCommand.Execute(rel.Path[0]);
        Assert.Equal("Clara Whitfield", vm.CurrentName);
    }
}
