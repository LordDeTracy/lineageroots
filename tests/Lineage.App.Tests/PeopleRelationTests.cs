using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Lineage.App.Tests;

public class PeopleRelationTests
{
    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 50 && !done(); i++)
        {
            await Task.Delay(40);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ThePeopleListSaysHowEachIsRelatedToTheHomePerson()
    {
        var (vm, _) = await Demo.OpenAsync();
        Assert.All(vm.People, r => Assert.False(r.HasRelation));      // no home person yet: no labels

        vm.OpenPersonCommand.Execute(1L);                             // Thomas Edward Whitfield
        vm.SetHomePersonCommand.Execute(null);
        await WaitFor(() => vm.People.Any(r => r.HasRelation));

        Assert.Equal("Home person", vm.People.Single(r => r.Id == 1).Relation);
        Assert.Equal("father", vm.People.Single(r => r.Name == "Whitfield, George").Relation);
        Assert.Equal("wife", vm.People.Single(r => r.Name == "Hartley, Eliza").Relation);
        Assert.Equal("daughter", vm.People.Single(r => r.Name == "Whitfield, Clara").Relation);
    }
}
