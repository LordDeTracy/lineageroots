using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class TranscribeTests
{
    [AvaloniaFact]
    public async Task ABaptismIsTranscribedIntoTheFamily()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // Thomas (1) and Eliza (4) are a couple with a daughter, Clara (5).
        dialogs.Transcribe = form =>
        {
            Assert.Equal("Child", form.Rows[0].Role);                   // the person showing starts as the principal
            form.KindIndex = 1;
            Assert.Equal(("Groom", "Marriage date"), (form.Rows[0].Role, form.DateLabel));
            form.KindIndex = 0;                                          // back to a baptism
            Assert.Equal("Born (if given)", form.SecondDateLabel);

            form.Rows[0].Person = CensusLineRow.AddNew;
            form.Rows[0].Name = "Mary Whitfield";
            form.AddLineCommand.Execute(null);
            Assert.Equal("Father", form.Rows[1].Role);
            form.Rows[1].Person = form.People.Single(p => p.Id == 1);
            Assert.Equal("Thomas Edward Whitfield", form.Rows[1].Name);  // filled in from the tree
            form.AddLineCommand.Execute(null);
            form.Rows[2].Person = form.People.Single(p => p.Id == 4);
            form.SourceTitle = "St Peter's, Leeds: baptisms";
            form.Reference = "1874 entry 212";
            form.Date = "2 Aug 1874";
            form.Place = "St Peter, Leeds, Yorkshire, England";
            return true;
        };
        await vm.TranscribeRecordCommand.ExecuteAsync(null);

        var mary = vm.Tree.People.Values.Single(p => p.PrimaryName.Given == "Mary" && p.PrimaryName.Surname == "Whitfield");
        Assert.Equal("2 AUG 1874", mary.Find("CHR")!.Date.ToGedcom());
        Assert.Equal("1874 entry 212", mary.Find("CHR")!.Citations.Single().Page);
        var family = vm.Tree.Families.Values.Single(f => f.PartnerIds.Contains(1L) && f.PartnerIds.Contains(4L));
        Assert.Contains(family.Children, c => c.ChildId == mary.Id);   // joined Thomas and Eliza's family, not a new one
        Assert.StartsWith("Recorded the baptism", vm.Status);

        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Tree.People.Values, p => p.PrimaryName.Given == "Mary" && p.PrimaryName.Surname == "Whitfield");
    }
}
