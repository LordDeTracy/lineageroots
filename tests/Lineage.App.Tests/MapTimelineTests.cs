using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class MapTimelineTests
{
    [AvaloniaFact]
    public async Task TheFamilyMapPlaysThroughTheYears()
    {
        var (vm, _) = await Demo.OpenAsync();
        var tree = vm.Tree;
        // Give Thomas's birth (1842), his marriage (1868) and Clara's birth (1872) map positions.
        tree.People[1].Birth!.Location = new GeoPoint(51.58, -0.33);
        tree.Families.Values.Single(f => f.Marriage != null).Marriage!.Location = new GeoPoint(53.80, -1.55);
        tree.People[5].Birth!.Place = "Leeds, Yorkshire, England";
        tree.People[5].Birth!.Location = new GeoPoint(53.81, -1.56);

        vm.View = PersonView.Map;
        Assert.True(vm.HasMapTimeline);
        Assert.Equal((1842, 1872), (vm.MapYearMin, vm.MapYearMax));
        Assert.Equal(3, vm.PersonPins.Count);          // everything at first
        Assert.Single(vm.PersonLines);                 // Thomas's birthplace → Clara's
        Assert.True(vm.MapAutoFit);
        Assert.Equal("1872", vm.MapYearText);
        Assert.Equal("Play from the start", vm.MapPlayText);

        vm.MapYear = 1850;
        Assert.False(vm.MapAutoFit);                   // the view stays put while the years change
        Assert.Single(vm.PersonPins);
        Assert.Empty(vm.PersonLines);
        Assert.Equal("1850", vm.MapYearText);
        Assert.Contains("Birth 1842", vm.MapLatestText);

        vm.MapYear = 1870;
        Assert.Equal(2, vm.PersonPins.Count);
        Assert.Contains("Marriage 1868", vm.MapLatestText);

        // Play starts again from the beginning once at the end, and stops when it gets there.
        vm.MapYear = 1872;
        vm.PlayMapCommand.Execute(null);
        Assert.True(vm.IsMapPlaying);
        Assert.Equal(1842, vm.MapYear);
        Assert.Equal("Pause", vm.MapPlayText);
        vm.StepMap(100);
        Assert.Equal(1872, vm.MapYear);
        Assert.False(vm.IsMapPlaying);

        // Leaving the map stops playback; a new person is fitted afresh.
        vm.PlayMapCommand.Execute(null);
        vm.View = PersonView.Details;
        Assert.False(vm.IsMapPlaying);
        vm.View = PersonView.Map;
        Assert.True(vm.MapAutoFit);
    }
}
