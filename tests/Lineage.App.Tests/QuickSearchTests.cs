using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class QuickSearchTests
{
    [AvaloniaFact]
    public async Task QuickSearchJumpsToPeoplePlacesSourcesAndCommands()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.OpenQuickSearchCommand.Execute(null);
        Assert.True(vm.IsQuickSearchOpen);
        Assert.All(vm.QuickResults, r => Assert.Equal("Command", r.Kind));   // nothing bookmarked yet

        vm.QuickSearchText = "clara";
        var clara = vm.QuickResults[0];
        Assert.Equal(("Person", "Clara Whitfield", "1872–"), (clara.Kind, clara.Title, clara.Detail));
        Assert.Same(clara, vm.SelectedQuickResult);
        vm.RunQuickResultCommand.Execute(null);
        Assert.False(vm.IsQuickSearchOpen);
        Assert.Equal("Clara Whitfield", vm.CurrentName);

        vm.OpenQuickSearchCommand.Execute(null);
        vm.QuickSearchText = "1842";                       // a year finds people born or dying then
        Assert.Equal(new[] { "Thomas Whitfeld", "Thomas Edward Whitfield" }, vm.QuickResults.Where(r => r.Kind == "Person").Select(r => r.Title));

        vm.QuickSearchText = "harrow";
        var place = vm.QuickResults.First(r => r.Kind == "Place");
        Assert.Equal("Harrow, Middlesex, England", place.Title);
        vm.RunQuickResultCommand.Execute(place);
        Assert.True(vm.IsPlacesSection);
        Assert.Equal("Harrow, Middlesex, England", vm.SelectedPlace!.Name);

        vm.OpenQuickSearchCommand.Execute(null);
        vm.QuickSearchText = "parish";
        vm.RunQuickResultCommand.Execute(vm.QuickResults.Single(r => r.Kind == "Source"));
        Assert.True(vm.IsSourcesSection);
        Assert.Equal("Parish registers of St Mary, Harrow", vm.SelectedSource!.Title);

        vm.OpenQuickSearchCommand.Execute(null);
        vm.QuickSearchText = "fan chart";
        Assert.Equal("Show fan chart", Assert.Single(vm.QuickResults).Title);
        vm.RunQuickResultCommand.Execute(null);
        Assert.True(vm.IsFanView);
        Assert.True(vm.IsPeopleSection);

        vm.OpenQuickSearchCommand.Execute(null);
        var first = vm.SelectedQuickResult;
        vm.MoveQuickSelection(-1);
        Assert.Same(vm.QuickResults[^1], vm.SelectedQuickResult);   // wraps around
        vm.MoveQuickSelection(1);
        Assert.Same(first, vm.SelectedQuickResult);
        vm.CloseQuickSearchCommand.Execute(null);
        Assert.False(vm.IsQuickSearchOpen);
    }
}
