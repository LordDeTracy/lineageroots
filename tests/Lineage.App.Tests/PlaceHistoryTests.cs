using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class PlaceHistoryTests
{
    [AvaloniaFact]
    public async Task OldNamesAreExplainedAndStandardNamesOffered()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Section = AppSection.Places;

        vm.SelectedPlace = vm.PlaceList.First(p => p.Name == "Harrow, Middlesex, England");
        Assert.True(vm.HasPlaceHistory);
        Assert.Contains("Greater London", vm.PlaceHistory);
        Assert.False(vm.HasPlaceStandard);                        // an old name is kept as recorded

        vm.SelectedPlace = vm.PlaceList.First(p => p.Name == "Leeds, Yorkshire, England");
        Assert.False(vm.HasPlaceHistory);
        Assert.True(vm.HasPlaceStandard, vm.PlaceStandard);
        var standard = vm.PlaceStandard;
        vm.UseStandardPlaceNameCommand.Execute(null);
        Assert.Contains(vm.PlaceList, p => p.Name == standard);
        Assert.DoesNotContain(vm.PlaceList, p => p.Name == "Leeds, Yorkshire, England");
        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.PlaceList, p => p.Name == "Leeds, Yorkshire, England");
    }
}
