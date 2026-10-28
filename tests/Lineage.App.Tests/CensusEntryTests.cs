using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.History;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class CensusEntryTests
{
    private static CensusRecord Census(string region, int year) => Censuses.All.Single(c => c.Region == region && c.Year == year && !c.Name.Contains("Register"));

    [Fact]
    public void EachCensusHasItsOwnColumns()
    {
        Assert.Empty(CensusHousehold.Columns(Census(Regions.UnitedStates, 1830)));                       // heads of household only
        Assert.DoesNotContain(CensusColumn.Relationship, CensusHousehold.Columns(Census(Regions.UnitedStates, 1870)));
        Assert.Contains(CensusColumn.Relationship, CensusHousehold.Columns(Census(Regions.UnitedStates, 1880)));
        Assert.Contains(CensusColumn.Immigrated, CensusHousehold.Columns(Census(Regions.UnitedStates, 1900)));
        Assert.DoesNotContain(CensusColumn.Relationship, CensusHousehold.Columns(Census(Regions.England, 1841)));
        Assert.Contains(CensusColumn.YearsMarried, CensusHousehold.Columns(Census(Regions.England, 1911)));
        Assert.Contains(CensusColumn.BirthDate, CensusHousehold.Columns(Censuses.All.First(c => c.Name == "1939 Register")));
    }

    [AvaloniaFact]
    public async Task AHouseholdIsEnteredAtOnce()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // Thomas (born 14 Mar 1842) married Eliza (1845) in 1868; their daughter Clara was born in 1872.
        vm.View = PersonView.Timeline;
        var row1881 = vm.CensusRows.Single(r => r.Check.Census.Year == 1881);
        dialogs.CensusEntry = form =>
        {
            Assert.Equal("1881 England and Wales census", form.Census.Census.Name);
            Assert.Equal("Leeds, Yorkshire, England", form.Place);
            Assert.True(form.ShowRelationship && form.ShowAge && !form.ShowImmigrated);
            Assert.Equal(new[] { ("Thomas Edward Whitfield", "Head", "39"), ("Eliza Hartley", "Wife", "36"), ("Clara Whitfield", "Daughter", "9") },
                         form.Rows.Select(r => (r.Name, r.Relationship, r.Age)));
            form.Reference = "RG11/4535 f.20 p.33";
            form.Rows[0].Occupation = "Stonemason";
            form.Rows[2].Occupation = "Scholar";
            form.AddLineCommand.Execute(null);
            var albert = form.Rows[^1];
            (albert.Name, albert.Relationship, albert.Sex, albert.Age, albert.Person) = ("Albert Whitfield", "Son", "M", "5", CensusLineRow.AddNew);
            return true;
        };
        await vm.EnterCensusHouseholdCommand.ExecuteAsync(row1881);

        var tree = vm.Tree;
        var source = tree.Sources.Values.Single(s => s.Title == "1881 England and Wales census");
        Assert.Equal(SourceKind.Original, source.Kind);
        foreach (var id in new long[] { 1, 4, 5 })
        {
            var cens = Assert.Single(tree.People[id].Events, e => e.Tag == "CENS");
            Assert.Equal("3 APR 1881", cens.Date.ToGedcom());
            Assert.Equal("RG11/4535 f.20 p.33", cens.Citations.Single().Page);
            Assert.Contains("Albert Whitfield", cens.Citations.Single().Text);   // the whole household transcribed
        }
        Assert.Contains("Relation to head: Head", tree.People[1].Events.Single(e => e.Tag == "CENS").Description);
        Assert.Equal("Stonemason", tree.People[1].Events.Single(e => e.Tag == "OCCU").Value);
        var albertPerson = tree.People.Values.Single(p => p.PrimaryName.Given == "Albert");
        Assert.Equal("ABT 1876", albertPerson.Birth!.Date.ToGedcom());
        Assert.Contains(tree.Family(tree.People[1].SpouseFamilyIds[0])!.Children, c => c.ChildId == albertPerson.Id);
        Assert.Equal(CensusStatus.Found, LifeTimeline.CensusChecklist(tree, tree.People[1]).Single(c => c.Census.Year == 1881).Status);

        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Tree.People.Values, p => p.PrimaryName.Given == "Albert");
        Assert.DoesNotContain(vm.Tree.People[1].Events, e => e.Tag == "CENS");
    }
}
