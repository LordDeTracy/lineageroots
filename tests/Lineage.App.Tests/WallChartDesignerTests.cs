using QuestPDF.Fluent;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lineage.App.Controls;
using Lineage.App.Reports;
using Lineage.App.ViewModels;
using Lineage.App.Views;

namespace Lineage.App.Tests;

public class WallChartDesignerTests
{
    private static async Task Settle()
    {
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(80);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ThePreviewFitsTheSheetAndFollowsTheChoices()
    {
        var (vm, _) = await Demo.OpenAsync();
        var designer = new WallChartDesignerViewModel(vm.Tree, vm.Current!, vm.TreeName, WallChartKind.Pedigree, false);
        var window = new WallChartDesignerWindow { DataContext = designer, Width = 1100, Height = 760 };
        window.Show();
        await Settle();

        Assert.StartsWith("A ", designer.PlanText);                 // "A 41 × 37 cm poster"
        Assert.EndsWith("cm poster", designer.PlanText);
        var sheet = window.FindControl<Border>("Sheet")!;
        var picture = window.FindControl<Image>("ChartPicture")!;
        Assert.NotNull(picture.Source);
        Assert.True(picture.Bounds.Width <= sheet.Bounds.Width && picture.Bounds.Height <= sheet.Bounds.Height,
            $"picture {picture.Bounds.Size} should fit the sheet {sheet.Bounds.Size}");

        // Pages to tape together are counted instead of measured.
        designer.ModeIndex = 1;
        designer.SizePercent = 200;
        await Settle();
        Assert.Contains("pages", designer.PlanText);
        Assert.False(designer.ToOptions().Frame);                  // a frame only makes sense on a poster

        // A new kind of chart keeps an unedited title in step.
        designer.KindIndex = (int)WallChartKind.Descendants;
        Assert.StartsWith("Descendants of", designer.Title);
        designer.Title = "Our family";
        designer.KindIndex = (int)WallChartKind.Fan;
        Assert.Equal("Our family", designer.Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task APosterWithANoteAndFrameSaves()
    {
        var (vm, _) = await Demo.OpenAsync();
        var designer = new WallChartDesignerViewModel(vm.Tree, vm.Current!, vm.TreeName, WallChartKind.Hourglass, false) { Note = "Made for the reunion" };
        var chart = new HourglassChart { Data = new ChartData(vm.Tree, vm.Current!.Id, vm.Tree.Ancestors(vm.Current, 3)) };
        var path = Path.Combine(Path.GetTempPath(), $"lineage-wall-{Guid.NewGuid():N}.pdf");
        try
        {
            var plan = WallChart.Export(chart, path, designer.ToOptions());
            Assert.Equal(WallChartMode.SinglePoster, plan.Mode);
            Assert.True(new FileInfo(path).Length > 2000);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task ABowTiePosterInEachStyle()
    {
        var (vm, _) = await Demo.OpenAsync();
        var root = vm.Tree.People.Values.Single(p => p.DisplayName == "Thomas Edward Whitfield");
        foreach (var style in Enum.GetValues<WallChartStyle>())
        {
            var designer = new WallChartDesignerViewModel(vm.Tree, root, vm.TreeName, WallChartKind.BowTie, false) { StyleIndex = (int)style };
            Assert.StartsWith("The ancestors of", designer.Title);
            var chart = new BowTieChart { Generations = 3, Data = new ChartData(vm.Tree, root.Id, vm.Tree.Ancestors(root, 3)) };
            var options = designer.ToOptions();
            Assert.Equal(style, options.Style);
            var pages = WallChart.Prepare(chart, options, out _).GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings { RasterDpi = 40 }).ToList();
            Assert.Single(pages);
            if (Environment.GetEnvironmentVariable("LINEAGE_SAMPLES") is { Length: > 0 } dir)
                File.WriteAllBytes(Path.Combine(dir, $"bowtie-{style}.png"), pages[0]);
            Assert.False(chart.Resources.ContainsKey("Bg"));       // the style's colours come off the chart again
        }
    }
}
