using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class DnaPageTests
{
    /// <summary>Opens the demo family on Thomas (id 1) and adds two matches through the editor: his daughter Clara, linked, and an unknown.</summary>
    private static async Task<(MainViewModel Vm, FakeDialogs Dialogs)> WithMatchesAsync()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.Section = AppSection.Dna;
        Assert.True(vm.IsDnaSection);
        Assert.False(vm.HasSidebar);
        Assert.Equal(1, vm.DnaTester!.Id);   // the person who was showing

        dialogs.DnaMatch = editor =>
        {
            editor.Name = "C. Whitfield";
            editor.SharedCm = "3,400";
            editor.Segments = "60";
            editor.Company = "Ancestry";
            editor.Linked = editor.AllPeople.Single(p => p.Id == 5);
            Assert.StartsWith("Most likely parent or child", editor.Prediction);
            Assert.Equal("In the tree: parent or child. The DNA fits.", editor.TreeRelationship);
            return EditorResult.Saved;
        };
        await vm.AddDnaMatchCommand.ExecuteAsync(null);

        dialogs.DnaMatch = editor =>
        {
            editor.Name = "mystery_cousin";
            editor.SharedCm = "1700";
            editor.SharedOptions.Single().IsChecked = true;   // shares DNA with Clara
            return EditorResult.Saved;
        };
        await vm.AddDnaMatchCommand.ExecuteAsync(null);
        return (vm, dialogs);
    }

    [AvaloniaFact]
    public async Task MatchesAreListedWithPredictionsAndTreeChecks()
    {
        var (vm, dialogs) = await WithMatchesAsync();
        var thomas = vm.Tree.People[1];
        Assert.Equal(2, thomas.DnaMatches.Count);
        var clara = thomas.DnaMatches.Single(m => m.Name == "C. Whitfield");
        var mystery = thomas.DnaMatches.Single(m => m.Name == "mystery_cousin");
        Assert.Equal((3400.0, 60, 5L), (clara.SharedCm, clara.Segments, clara.PersonId!.Value));
        Assert.Contains(clara.Id, mystery.SharedWith);
        Assert.Contains(mystery.Id, clara.SharedWith);   // shared matches go both ways

        Assert.Equal(new[] { "C. Whitfield", "mystery_cousin" }, vm.DnaMatchRows.Select(r => r.Name));
        var row = vm.DnaMatchRows[0];
        Assert.Equal("Clara Whitfield", row.Linked);
        Assert.Equal("tree says parent or child — fits", row.TreeText);
        Assert.False(row.Misfit);
        Assert.Equal("2 matches  ·  1 linked to the tree", vm.DnaSummary);
        Assert.Contains("Thomas Edward Whitfield (2 matches)", vm.DnaTesters.Select(t => t.Text));

        // Too little DNA for a daughter: flagged.
        dialogs.DnaMatch = editor => { editor.SharedCm = "850"; Assert.False(editor.FitsTree); return EditorResult.Saved; };
        await vm.EditDnaMatchCommand.ExecuteAsync(row);
        Assert.True(vm.DnaMatchRows.Single(r => r.Name == "C. Whitfield").Misfit);
        Assert.EndsWith("1 don't fit the tree", vm.DnaSummary);

        vm.DnaFilter = "myst";
        Assert.Equal("mystery_cousin", Assert.Single(vm.DnaMatchRows).Name);
        vm.DnaFilter = "";

        // Deleting a match removes it from the others' shared matches; undo brings it back.
        dialogs.DnaMatch = _ => EditorResult.Deleted;
        await vm.EditDnaMatchCommand.ExecuteAsync(vm.DnaMatchRows.Single(r => r.Name == "C. Whitfield"));
        Assert.Single(thomas.DnaMatches);
        Assert.Empty(thomas.DnaMatches[0].SharedWith);
        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Tree.People[1].DnaMatches.Count);
    }

    [AvaloniaFact]
    public async Task CsvListsImport()
    {
        var (vm, dialogs) = await WithMatchesAsync();
        var path = Path.Combine(Path.GetTempPath(), $"lineage-dna-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, "Name,Shared cM,Segments\nAda Pemberwick,95,5\nBo Quorrow,61,3\nmystery_cousin,1710,40\n");
        dialogs.OpenPath = path;
        dialogs.PromptAnswer = "Ancestry";
        await vm.ImportDnaMatchesCommand.ExecuteAsync(null);
        File.Delete(path);

        Assert.Contains("2 matches added, 1 updated", dialogs.Messages[^1]);
        Assert.Equal(4, vm.Tree.People[1].DnaMatches.Count);
        Assert.Equal(1710, vm.Tree.People[1].DnaMatches.Single(m => m.Name == "mystery_cousin").SharedCm);
        Assert.Equal(4, vm.DnaMatchRows.Count);
    }

    [AvaloniaFact]
    public async Task ClustersAreBuiltFromSharedMatches()
    {
        var (vm, _) = await WithMatchesAsync();
        vm.ClusterMaxCm = "4000";   // the demo's matches are close family
        vm.DnaTabIndex = 1;
        Assert.True(vm.IsDnaClustersTab);
        var cluster = Assert.Single(vm.DnaClusterCards);
        Assert.Equal("Cluster 1  ·  2 matches", cluster.Heading);
        Assert.Contains("mystery_cousin", cluster.Members);
        Assert.NotNull(vm.DnaClusters);
        Assert.StartsWith("2 matches between 20 and 4000 cM: 1 cluster", vm.ClusterSummary);

        vm.ClusterMaxCm = "400";
        Assert.Empty(vm.DnaClusterCards);
        Assert.Equal("No matches between 20 and 400 cM.", vm.ClusterSummary);
    }

    [AvaloniaFact]
    public async Task WhatAreTheOddsPlacesAnUnknownMatch()
    {
        var (vm, _) = await WithMatchesAsync();
        vm.DnaTabIndex = 2;
        vm.WatoSubject = vm.WatoSubjects.Single(s => s.MatchName == "mystery_cousin");
        var observation = Assert.Single(vm.WatoObservations);
        Assert.Equal(("Thomas Edward Whitfield", 1700.0), (observation.Name, observation.Observation.Cm));
        Assert.Contains("Add two or more places", vm.WatoSummary);

        // 1,700 cM with Thomas: a grandchild (through Clara) fits; a child of Thomas and Eliza doesn't.
        vm.WatoHypothesisToAdd = vm.ParentChoices.Single(c => c.Text == "Child of Clara Whitfield (other parent unknown)");
        vm.WatoHypothesisToAdd = vm.ParentChoices.Single(c => c.Text == "Child of Thomas Edward Whitfield and Eliza Hartley");
        Assert.Null(vm.WatoHypothesisToAdd);
        Assert.Equal(2, vm.WatoResults.Count);
        var best = vm.WatoResults[0];
        Assert.Equal("Child of Clara Whitfield", best.Label);
        Assert.Equal(">99%", best.Probability);
        Assert.Equal("Most likely", best.Odds);
        Assert.Contains("would be grandparent or grandchild", best.Details);
        Assert.EndsWith("less likely", vm.WatoResults[1].Odds);

        // Leaving the only relative out leaves nothing to compare.
        observation.Include = false;
        Assert.Empty(vm.WatoResults);
        observation.Include = true;
        vm.RemoveWatoHypothesisCommand.Execute(vm.WatoHypotheses[0]);
        Assert.Single(vm.WatoResults);
    }

    [AvaloniaFact]
    public async Task CalculatorAndPersonPageShortcut()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.ShowDnaMatchesCommand.Execute(null);
        Assert.True(vm.IsDnaSection);
        Assert.Equal(1, vm.DnaTester!.Id);
        Assert.False(vm.HasDnaMatches);

        vm.DnaTabIndex = 3;
        vm.CmCalculatorText = "870";
        Assert.Contains("1st cousin", vm.CmCalculatorRows[0].Label);
        Assert.Equal("", vm.CmCalculatorNote);
        vm.CmCalculatorText = "12";
        Assert.Contains("further back", vm.CmCalculatorNote);
        vm.CmCalculatorText = "9000";
        Assert.Empty(vm.CmCalculatorRows);
        Assert.Contains("more than a parent", vm.CmCalculatorNote);
    }
}
