using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class PeopleFilterTests
{
    [AvaloniaFact]
    public async Task ThePeopleListFiltersByPlaceDatesAndYearsLived()
    {
        var (vm, _) = await Demo.OpenAsync();
        // Thomas (1842–1911, Harrow; married in Leeds), Eliza (1845), Clara (1872), Thomas Whitfeld (1842, Harrow).
        var all = vm.People.Count;
        Assert.Equal("Filters", vm.FiltersText);

        vm.FilterPlace = "harrow";
        Assert.Equal(new[] { "Whitfeld, Thomas", "Whitfield, Thomas Edward" }, vm.People.Select(r => r.Name).Order());
        vm.FilterPlace = "Leeds";   // the marriage place counts for both partners
        Assert.Equal(new[] { "Hartley, Eliza", "Whitfield, Thomas Edward" }, vm.People.Select(r => r.Name).Order());
        Assert.Equal("Filters (1)", vm.FiltersText);

        vm.FilterPlace = "";
        vm.FilterBornFrom = "1860";
        Assert.Equal(new[] { "Whitfield, Clara" }, vm.People.Select(r => r.Name));
        vm.FilterBornFrom = "";

        vm.FilterDiedTo = "1911";
        Assert.Equal(new[] { "Whitfield, Thomas Edward" }, vm.People.Select(r => r.Name));
        vm.FilterDiedTo = "";

        // Alive in 1940: only those born recently enough (a life is taken as up to 100 years when it has no end).
        vm.FilterLivedFrom = "1940";
        vm.FilterLivedTo = "1940";
        Assert.DoesNotContain("Whitfield, Thomas Edward", vm.People.Select(r => r.Name));
        Assert.Contains("Whitfield, Clara", vm.People.Select(r => r.Name));

        vm.FilterSexIndex = 1;   // men
        Assert.Equal("Filters (2)", vm.FiltersText);
        Assert.DoesNotContain("Whitfield, Clara", vm.People.Select(r => r.Name));

        vm.ClearFiltersCommand.Execute(null);
        Assert.Equal(all, vm.People.Count);
        Assert.False(vm.HasActiveFilters);
    }
}
