using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;
using Lineage.Core.Storage;

namespace Lineage.App.Tests;

public class ShareViewTests
{
    [AvaloniaFact]
    public async Task ShareACopyThenTakeSomeOfTheRelativesChanges()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-copy-{Guid.NewGuid():N}.lineage");
        try
        {
            dialogs.PromptAnswer = "Cousin Ada";
            dialogs.ConfirmAnswer = true;   // leave out living people
            dialogs.SavePath = path;
            await vm.ShareWithRelativeCommand.ExecuteAsync(null);
            var share = Assert.Single(vm.Tree.Shares);
            Assert.Equal("Cousin Ada", share.With);
            Assert.True(vm.IsDirty);
            Assert.Contains("Review a relative's changes", dialogs.Messages[^1]);

            // The cousin adds Clara's death and renames George, and sends the file back.
            var theirs = LineageStore.Load(path);
            Assert.Equal(share.Id, theirs.SharedFrom!.Id);
            theirs.People.Values.Single(p => p.PrimaryName.Given == "Clara").Events.Add(Event.Of("DEAT", GenDate.Parse("1950"), "Leeds, Yorkshire, England"));
            theirs.People.Values.Single(p => p.PrimaryName.Given == "George").PrimaryName.Given = "Georgius";
            LineageStore.Save(theirs, path);

            dialogs.OpenPath = path;
            dialogs.Review = review =>
            {
                Assert.Equal("Changes from Cousin Ada", review.Title);
                Assert.Equal(2, review.Items.Count);
                Assert.All(review.Items, i => Assert.True(i.IsAccepted));
                Assert.Equal("Take 2 changes", review.ApplyText);
                review.Items.Single(i => i.What == "Names").IsAccepted = false;
                Assert.Equal("Take 1 change", review.ApplyText);
                return true;
            };
            await vm.ReviewRelativeChangesCommand.ExecuteAsync(null);
            Assert.Contains(vm.Tree.People[5].Events, e => e.Tag == "DEAT" && e.Date.Year == 1950);
            Assert.Equal("George", vm.Tree.People[2].PrimaryName.Given);
            Assert.Single(share.Rejected);
            Assert.Equal("Took 1 of 2 changes from Cousin Ada", vm.Status);

            // Nothing new the second time; undo takes the change back out.
            await vm.ReviewRelativeChangesCommand.ExecuteAsync(null);
            Assert.Contains("Nothing has changed in Cousin Ada's copy", dialogs.Messages[^1]);
            vm.UndoCommand.Execute(null);
            Assert.DoesNotContain(vm.Tree.People[5].Events, e => e.Tag == "DEAT");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task AFileThatWasNotSharedFromThisTreeIsExplained()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-other-{Guid.NewGuid():N}.lineage");
        try
        {
            LineageStore.Save(new FamilyTree { Name = "Someone else's" }, path);
            dialogs.OpenPath = path;
            await vm.ReviewRelativeChangesCommand.ExecuteAsync(null);
            Assert.Contains("Compare with another tree", dialogs.Messages[^1]);
        }
        finally { File.Delete(path); }
    }
}
