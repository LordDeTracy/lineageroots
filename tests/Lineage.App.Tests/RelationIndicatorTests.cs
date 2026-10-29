using Avalonia.Headless.XUnit;

namespace Lineage.App.Tests;

public class RelationIndicatorTests
{
    [AvaloniaFact]
    public async Task PeopleShowHowTheyAreRelatedToTheHomePerson()
    {
        var (vm, _) = await Demo.OpenAsync();
        // Clara (5) is the home person; Thomas (1) is her father, George (2) her grandfather.
        vm.NavigateCommand.Execute(5L);
        vm.SetHomePersonCommand.Execute(null);
        Assert.Equal("Home person", vm.CurrentRelation);

        vm.NavigateCommand.Execute(1L);
        Assert.Equal("Clara's father", vm.CurrentRelation);
        Assert.Equal("Clara's grandfather", vm.Parents.Single(c => c.Id == 2).Relation);
        Assert.Equal("Clara's mother", vm.Families.Single().Spouse!.Relation);
        Assert.Equal("Home person", vm.Families.Single().Children.Single(c => c.Id == 5).Relation);
    }
}
