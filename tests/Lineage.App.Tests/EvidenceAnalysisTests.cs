using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Tests;

public class EvidenceAnalysisTests
{
    [AvaloniaFact]
    public async Task FactsAreAnalysedAndConcludedInTheEvidenceTab()
    {
        var (vm, _) = await Demo.OpenAsync();
        vm.Section = AppSection.Sources;
        Assert.True(vm.IsSourcesList && vm.HasSidebar);
        vm.EvidenceModeIndex = 1;
        Assert.True(vm.IsEvidenceAnalysis);
        Assert.False(vm.HasSidebar);   // the analysis uses the whole width
        await vm.EvidenceTask;
        Assert.Contains("facts:", vm.EvidenceSummary);

        // Thomas's birth (14 Mar 1842, Harrow) has one unrated source: weak evidence, so it needs work.
        Assert.Equal(0, vm.EvidenceFilterIndex);
        var row = vm.EvidenceRows.Single(r => r.Who == "Thomas Edward Whitfield" && r.What.StartsWith("Birth"));
        Assert.Equal("Weak", row.Level);
        vm.SelectedEvidence = row;
        Assert.Equal("When and where was Thomas born?", vm.EvidenceQuestion);
        Assert.Contains(vm.EvidenceClaims, c => c.Source.StartsWith("Parish registers of St Mary, Harrow"));
        Assert.Equal(5, vm.EvidenceGps.Count);

        vm.EvidenceConclusionIndex = 1;   // proven
        vm.EvidenceReasoning = "Baptism entry made at the time; matches his age in later records.";
        vm.SaveConclusionCommand.Execute(null);
        var birth = vm.Tree.People[1].Find("BIRT")!;
        Assert.Equal(FactConclusion.Proven, birth.Conclusion);
        Assert.StartsWith("Baptism entry", birth.Reasoning);
        Assert.True(vm.SelectedEvidence!.IsConcluded);   // still selected, re-assessed

        vm.UndoCommand.Execute(null);
        Assert.Equal(FactConclusion.NotDecided, vm.Tree.People[1].Find("BIRT")!.Conclusion);
    }
}
