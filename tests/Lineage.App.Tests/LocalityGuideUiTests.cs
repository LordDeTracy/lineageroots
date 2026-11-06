using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class LocalityGuideUiTests
{
    [AvaloniaFact]
    public async Task TheGuideShowsInAdvancedModeOnly()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Section = AppSection.Places;
        vm.SelectedPlace = vm.PlaceList.First(p => p.Name == "Leeds, Yorkshire, England");
        Assert.False(vm.HasPlaceRecords);

        vm.Settings.AdvancedMode = true;
        vm.SelectedPlace = vm.PlaceList.First(p => p.Name != "Leeds, Yorkshire, England");
        vm.SelectedPlace = vm.PlaceList.First(p => p.Name == "Leeds, Yorkshire, England");
        Assert.True(vm.HasPlaceRecords);
        Assert.Contains(vm.PlaceRecords, r => r.Name == "Censuses");
    }
}
