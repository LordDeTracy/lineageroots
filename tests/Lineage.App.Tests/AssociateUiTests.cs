using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class AssociateUiTests
{
    [AvaloniaFact]
    public async Task AnAssociateIsAddedShownBothWaysAndRemoved()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var thomas = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        var mary = vm.Tree.People.Values.Single(p => p.DisplayName == "Mary Ann Cole");
        vm.NavigateCommand.Execute(thomas.Id);
        Assert.False(vm.HasAssociates);

        dialogs.PromptAnswers.Enqueue(EventEditorViewModel.PersonLabel(mary));
        dialogs.PromptAnswers.Enqueue("Neighbour");
        await vm.AddAssociateCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.AssociateRows);
        Assert.Equal(("Mary Ann Cole", "Neighbour", true), (row.Name, row.Line, row.CanRemove));

        vm.NavigateCommand.Execute(mary.Id);
        var back = Assert.Single(vm.AssociateRows);                       // seen from her side too, but kept on his
        Assert.Equal("Thomas Edward Whitfield", back.Name);
        Assert.False(back.CanRemove);

        vm.NavigateCommand.Execute(thomas.Id);
        vm.RemoveAssociateCommand.Execute(vm.AssociateRows[0]);
        Assert.Empty(vm.AssociateRows);
        vm.UndoCommand.Execute(null);
        Assert.Single(vm.AssociateRows);
    }

    [AvaloniaFact]
    public void TheEventEditorKeepsWitnesses()
    {
        var tree = new FamilyTree();
        var ann = tree.AddPerson(new Person { Names = { new Name { Given = "Ann", Surname = "Bell" } } });
        var e = Event.Of("MARR");
        var editor = new EventEditorViewModel(tree, e, isNew: true, forFamily: true, "Marriage");
        editor.AddRoleCommand.Execute(null);
        editor.Roles[0].Person = "Ann Bell";
        editor.AddRoleCommand.Execute(null);
        editor.Roles[1].Person = "Nobody Known";                         // not in the tree: left out
        editor.Apply(e);
        var role = Assert.Single(e.Roles);
        Assert.Equal((ann.Id, "Witness"), (role.PersonId, role.Role));

        var again = new EventEditorViewModel(tree, e, isNew: false, forFamily: true, "Marriage");
        Assert.Equal("Ann Bell", Assert.Single(again.Roles).Person);
    }
}
