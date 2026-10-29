using Avalonia.Headless.XUnit;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class PersonCardTests
{
    [AvaloniaFact]
    public async Task CardsShowTitleAndLatestOccupation()
    {
        var (vm, _) = await Demo.OpenAsync();
        var george = vm.Tree.People[2];   // Thomas's father
        george.Title = "Rev.";
        george.Events.Add(new Event { Tag = "OCCU", Value = "Curate", Date = GenDate.Parse("1835") });
        george.Events.Add(new Event { Tag = "OCCU", Value = "Vicar of Harrow", Date = GenDate.Parse("1850") });
        vm.NavigateCommand.Execute(3L);
        vm.NavigateCommand.Execute(1L);   // Thomas's page, with his parents' cards

        var card = vm.Parents.Single(c => c.Id == 2);
        Assert.Equal("Rev.  ·  Vicar of Harrow", card.Calling);
        Assert.False(vm.Parents.Single(c => c.Id == 3).HasCalling);
    }
}
