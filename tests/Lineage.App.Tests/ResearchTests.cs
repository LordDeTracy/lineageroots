using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class ResearchTests
{
    [AvaloniaFact]
    public async Task TasksCanBeAddedLinkedFinishedAndDeleted()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Research = editor =>
        {
            Assert.Single(editor.People);                       // added from Thomas's page, so he's linked
            editor.Description = "Find Thomas in the 1881 census";
            editor.Category = "Census";
            editor.PriorityIndex = 1;
            editor.PersonToAdd = editor.AllPeople.Single(p => p.Id == 4);   // Eliza too
            return EditorResult.Saved;
        };
        await vm.AddResearchForPersonCommand.ExecuteAsync(null);
        var task = Assert.Single(vm.Tree.Research.Values);
        Assert.Contains(task.Id, vm.Tree.People[1].ResearchIds);
        Assert.Contains(task.Id, vm.Tree.People[4].ResearchIds);
        Assert.Single(vm.PersonResearch);

        vm.Section = AppSection.Research;
        var row = Assert.Single(vm.ResearchList);
        Assert.Equal("High", row.Priority);
        Assert.Equal(2, row.People.Count);

        vm.ToggleResearchDoneCommand.Execute(row);
        Assert.True(vm.Tree.Research[task.Id].Done);
        Assert.Empty(vm.ResearchList);                          // "To do" filter hides finished tasks
        vm.ResearchFilterIndex = 2;
        Assert.Single(vm.ResearchList);

        dialogs.Research = editor =>
        {
            editor.RemovePersonCommand.Execute(editor.People.Single(p => p.Id == 4));
            return EditorResult.Saved;
        };
        await vm.EditResearchCommand.ExecuteAsync(vm.ResearchList[0]);
        Assert.DoesNotContain(task.Id, vm.Tree.People[4].ResearchIds);

        dialogs.Research = _ => EditorResult.Deleted;
        await vm.EditResearchCommand.ExecuteAsync(vm.ResearchList[0]);
        Assert.Empty(vm.Tree.Research);
        Assert.Empty(vm.Tree.People[1].ResearchIds);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Tree.Research);
        Assert.Contains(task.Id, vm.Tree.People[1].ResearchIds);
    }
}
