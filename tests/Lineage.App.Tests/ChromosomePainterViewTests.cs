using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;
using Lineage.Core.Dna;

namespace Lineage.App.Tests;

public class ChromosomePainterViewTests
{
    [AvaloniaFact]
    public async Task SegmentsArePaintedOnTheChromosomesTab()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        vm.Section = AppSection.Dna;
        Assert.Equal(1, vm.DnaTester!.Id);   // Thomas, son of George and Mary Ann, father of Clara
        vm.DnaTabIndex = 4;
        Assert.True(vm.IsDnaChromosomesTab);
        Assert.False(vm.HasPainting);
        Assert.StartsWith("No segment data yet", vm.PaintingSummary);

        var path = Path.Combine(Path.GetTempPath(), $"lineage-segments-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path,
            "Match Name,Chromosome,Start Location,End Location,Centimorgans,Matching SNPs\n" +
            "G. Whitfield,1,1000000,180000000,180,20000\n" +
            "G. Whitfield,2,5000000,200000000,190,21000\n" +
            "C. Whitfield,1,90000000,240000000,150,18000\n" +
            "Unknown cousin,9,10000000,25000000,14,1800\n");
        dialogs.OpenPath = path;
        dialogs.PromptAnswer = "FamilyTreeDNA";
        await vm.ImportDnaMatchesCommand.ExecuteAsync(null);
        File.Delete(path);
        Assert.Contains("4 segments for 3 matches (3 new)", dialogs.Messages[^1]);

        var thomas = vm.Tree.People[1];
        thomas.DnaMatches.Single(m => m.Name == "G. Whitfield").PersonId = 2;
        thomas.DnaMatches.Single(m => m.Name == "C. Whitfield").PersonId = 5;
        vm.DnaTabIndex = 0;
        vm.DnaTabIndex = 4;

        Assert.True(vm.HasPainting);
        Assert.Equal(3, vm.DnaPainting!.Segments.Count);
        Assert.Equal(new[] { ("George Whitfield", "Father's side"), ("Thomas Edward Whitfield", "Either side") },
                     vm.PaintGroupRows.Select(r => (r.Label, r.Side)));
        Assert.Contains("2 segments, 370 cM · G. Whitfield", vm.PaintGroupRows[0].Detail);
        Assert.Contains("of the father's side", vm.PaintingSummary);
        Assert.Contains("1 match has segments but no link to the tree yet", vm.PaintingSummary);
        Assert.All(vm.DnaPainting.Segments.Where(s => s.Match.PersonId == 2), s => Assert.Equal(PaintSide.Father, s.Side));
    }
}
