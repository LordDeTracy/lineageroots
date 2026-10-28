using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class FindGpsTests
{
    [AvaloniaFact]
    public async Task PlacesGetGpsFromTheGazetteerAndItCanBeUndone()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // The demo has "Harrow, Middlesex, England", "Leeds, Yorkshire, England" and a bare "Harrow" (no country).
        dialogs.FindGps = review =>
        {
            Assert.Contains("Found 2 of the 3 places without GPS", review.Summary);
            Assert.All(review.Rows, r => Assert.True(r.IsChecked));   // both sure
            var leeds = review.Rows.Single(r => r.Place == "Leeds, Yorkshire, England");
            Assert.Contains("Leeds, England, United Kingdom", leeds.Match);
            Assert.Equal("Sure", leeds.Confidence);
            review.Rows.Single(r => r.Place.StartsWith("Harrow")).IsChecked = false;
            Assert.Equal("Add GPS to 1 place", review.ApplyText);
            return true;
        };
        await vm.FindGpsCommand.ExecuteAsync(null);

        var marriage = vm.Tree.Families.Values.Single(f => f.Marriage != null).Marriage!;
        Assert.NotNull(marriage.Location);
        Assert.InRange(marriage.Location!.Value.Latitude, 53.5, 54.1);
        Assert.Null(vm.Tree.People[1].Birth!.Location);   // Harrow was unticked
        Assert.Equal("Added GPS to 1 place", vm.Status);

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Tree.Families.Values.Single(f => f.Marriage != null).Marriage!.Location);
    }
}
