using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Analysis;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class EvidenceViewTests
{
    [AvaloniaFact]
    public async Task FactsShowHowWellTheyAreProvenAndTheEditorRatesThem()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        // Thomas's birth cites the parish register (not yet rated); his death has no source.
        var birth = vm.Events.Single(r => r.Event.Tag == "BIRT");
        Assert.Equal("Weak", birth.ProofLabel);
        Assert.True(birth.IsProofWeak);
        Assert.Equal("No source", vm.Events.Single(r => r.Event.Tag == "DEAT").ProofLabel);
        Assert.Contains("1 weak", vm.ProofTally);
        Assert.Contains("without a source", vm.ProofTally);

        dialogs.Event = editor =>
        {
            var row = editor.Citations.Single();
            row.SourceKindIndex = 1;    // original record
            row.InformationIndex = 1;   // first-hand
            row.EvidenceIndex = 1;      // states it
            Assert.Equal("Good", editor.ProofLabel);
            Assert.True(editor.IsProofGood);

            editor.AddConflictCommand.Execute(null);
            editor.Conflicts[0].Claim = "1851 census: aged 10";
            Assert.Equal("Conflicting", editor.ProofLabel);
            editor.Conflicts[0].Resolution = "Census ages are approximate";
            editor.Conflicts[0].Resolved = true;
            Assert.Equal("Good", editor.ProofLabel);

            editor.ConclusionIndex = 1;
            Assert.Equal("Proven", editor.ProofLabel);
            Assert.Contains("Write down the reasoning", editor.ProofDetails);
            editor.Reasoning = "Baptised days after birth; the register is the original.";
            Assert.DoesNotContain("Write down the reasoning", editor.ProofDetails);
            return EditorResult.Saved;
        };
        await vm.EditEventCommand.ExecuteAsync(birth);

        var source = vm.Tree.Sources.Values.Single();
        Assert.Equal(SourceKind.Original, source.Kind);
        var e = vm.Tree.People[1].Events.Single(x => x.Tag == "BIRT");
        Assert.Equal((FactConclusion.Proven, InformationKind.Primary, EvidenceKind.Direct), (e.Conclusion, e.Citations[0].Information, e.Citations[0].Evidence));
        Assert.True(Assert.Single(e.Conflicts).Resolved);
        Assert.Equal("Proven", vm.Events.Single(r => r.Event.Tag == "BIRT").ProofLabel);
        Assert.Contains("1 proven", vm.ProofTally);

        // Undo puts the source's kind back too.
        vm.UndoCommand.Execute(null);
        Assert.Equal(SourceKind.Unknown, vm.Tree.Sources.Values.Single().Kind);
        Assert.Equal("Weak", vm.Events.Single(r => r.Event.Tag == "BIRT").ProofLabel);
    }

    [AvaloniaFact]
    public async Task ASecondBirthRecordThatDisagreesShowsAsAConflict()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        dialogs.Event = editor =>
        {
            editor.Type = editor.Types.Single(t => t.Tag == "BIRT");
            editor.Date = "1838";
            editor.Place = "Leeds, Yorkshire, England";
            Assert.Equal("Conflicting", editor.ProofLabel);   // the preview already sees the other birth
            Assert.Contains("Another birth record says 14 March 1842", editor.ProofDetails);
            return EditorResult.Saved;
        };
        await vm.AddEventCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Events.Count(r => r.Event.Tag == "BIRT" && r.IsProofConflicting));
        Assert.Contains("2 conflicting", vm.ProofTally);

        vm.ChartsShowEvidence = true;   // the charts colour by evidence
        Assert.Equal(ProofLevel.Conflicting, Evidence.PersonLevel(vm.Tree, vm.Tree.People[1]));
    }
}
