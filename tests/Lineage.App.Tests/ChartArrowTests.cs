using Avalonia;
using Avalonia.Headless.XUnit;
using Lineage.App.Controls;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class ChartArrowTests
{
    [AvaloniaFact]
    public async Task TheArrowsLeadToThePeopleBeyondTheEdge()
    {
        var (vm, _) = await Demo.OpenAsync();
        var george = vm.Tree.People.Values.Single(p => p.DisplayName == "George Whitfield");
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        var clara = vm.Tree.People.Values.Single(p => p.DisplayName == "Clara Whitfield");

        // From Clara, one row up shows Thomas, whose parents are beyond the edge.
        var up = new HourglassChart { AncestorGenerations = 2, DescendantGenerations = 1, Data = new ChartData(vm.Tree, clara.Id, vm.Tree.Ancestors(clara, 3)) };
        var parents = up.Layout!.Nodes.Single(n => n.Slots.Any(x => x.PersonId == thomas.Id));
        var s = parents.Slots.ToList().FindIndex(x => x.PersonId == thomas.Id);
        Assert.True(parents.Slots[s].MoreAncestors);
        var box = parents.SlotBounds(s);
        Assert.Equal(thomas.Id, up.ArrowAt(new Point(box.X + box.Width / 2, box.Y - 12)));
        Assert.Null(up.ArrowAt(new Point(0, 0)));

        // From George, one row down shows Thomas, whose children are beyond the edge.
        var down = new DescendantChart { Generations = 2, Data = new ChartData(vm.Tree, george.Id, vm.Tree.Ancestors(george, 3)) };
        var node = down.Layout!.Nodes.Single(n => n.Slots.Any(x => x.PersonId == thomas.Id));
        Assert.True(node.MoreDescendants);
        Assert.Equal(thomas.Id, down.ArrowAt(new Point(node.X + node.Width / 2, node.Y + node.Height + 12)));
    }
}
