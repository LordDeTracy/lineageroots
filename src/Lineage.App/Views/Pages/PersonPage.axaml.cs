using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lineage.App.Controls;
using Lineage.App.Reports;
using Lineage.App.ViewModels;

namespace Lineage.App.Views.Pages;

/// <summary>The person's details page and the chart and map views.</summary>
public partial class PersonPage : UserControl
{
    public PersonPage()
    {
        InitializeComponent();
        foreach (var host in Hosts)
            host.PropertyChanged += (_, e) =>
            {
                if (e.Property == ZoomHost.ZoomProperty && host == VisibleHost) ZoomLabel.Content = host.ZoomText;
            };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += (_, e) =>
                {
                    // A new person starts at the top of the page.
                    if (e.PropertyName == nameof(MainViewModel.CurrentName)) PageScroller.Offset = default;
                    if (e.PropertyName == nameof(MainViewModel.View) && VisibleHost is { } h) ZoomLabel.Content = h.ZoomText;
                };
        };
    }

    private ZoomHost[] Hosts => new[] { PedigreeHost, FanHost, DescendantHost, HourglassHost, BowTieHost, LifelinesHost, RelationshipHost, StatisticsHost };

    public void ScrollPageTo(double y) =>
        Dispatcher.UIThread.Post(() => PageScroller.Offset = new Vector(0, y), DispatcherPriority.Background);

    /// <summary>The zoomable host of the chart on screen, if a chart view is showing.</summary>
    public ZoomHost? VisibleHost => DataContext is MainViewModel vm ? vm.View switch
    {
        PersonView.Pedigree => PedigreeHost,
        PersonView.FanChart => FanHost,
        PersonView.Descendants => DescendantHost,
        PersonView.Hourglass => HourglassHost,
        PersonView.BowTie => BowTieHost,
        PersonView.Lifelines => LifelinesHost,
        PersonView.Relationship => RelationshipHost,
        PersonView.Statistics => StatisticsHost,
        _ => null,
    } : null;

    /// <summary>The chart on screen, if a chart view is showing.</summary>
    public ChartBase? VisibleChart => VisibleHost?.Chart as ChartBase;

    private void OnZoomIn(object? sender, RoutedEventArgs e) => VisibleHost?.ZoomIn();
    private void OnZoomOut(object? sender, RoutedEventArgs e) => VisibleHost?.ZoomOut();
    private void OnZoomReset(object? sender, RoutedEventArgs e) => VisibleHost?.ResetZoom();
    private void OnZoomFit(object? sender, RoutedEventArgs e) => VisibleHost?.FitToView();

    private string ChartName(MainViewModel vm) => vm.View switch
    {
        PersonView.FanChart => "Fan chart",
        PersonView.Descendants => "Descendants",
        PersonView.Hourglass => "Hourglass",
        PersonView.Lifelines => "Lifelines",
        PersonView.Relationship => "Relationship",
        PersonView.Statistics => "Statistics",
        _ => "Pedigree",
    };

    private async Task<string?> AskPathAsync(string title, string suggested, string typeName, string pattern)
    {
        var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            DefaultExtension = Path.GetExtension(suggested),
            FileTypeChoices = new[] { new FilePickerFileType(typeName) { Patterns = new[] { pattern } } },
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>Saves whichever chart is showing as a PNG (drawn at twice screen resolution).</summary>
    private async void OnSaveChartImage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || VisibleChart is not { } chart) return;
        var path = await AskPathAsync("Save chart as image", MainViewModel.SafeName($"{ChartName(vm)} - {vm.CurrentName}") + ".png", "PNG image", "*.png");
        if (path == null) return;
        try
        {
            chart.SaveImage(path);
            vm.Status = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex) { vm.Status = $"Couldn't save the image: {ex.Message}"; }
    }

    /// <summary>Opens the designer on the chart showing (or a pedigree), for the person showing.</summary>
    private async void OnWallChartDesigner(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Current is not { } person || TopLevel.GetTopLevel(this) is not Window owner) return;
        var kind = vm.View switch
        {
            PersonView.FanChart => WallChartKind.Fan,
            PersonView.Descendants => WallChartKind.Descendants,
            PersonView.Hourglass => WallChartKind.Hourglass,
            PersonView.BowTie => WallChartKind.BowTie,
            _ => WallChartKind.Pedigree,
        };
        var designer = new WallChartDesignerViewModel(vm.Tree, person, vm.TreeName, kind, vm.ChartsShowEvidence);
        var path = await new WallChartDesignerWindow { DataContext = designer }.ShowDialog<string?>(owner);
        if (path == null) return;
        vm.Status = $"Saved {Path.GetFileName(path)}: {designer.PlanText}.";
        MainViewModel.OpenExternal(path);
    }

    private void OnWallChartPoster(object? sender, RoutedEventArgs e) => _ = SaveWallChartAsync(WallChartMode.SinglePoster);
    private void OnWallChartTiled(object? sender, RoutedEventArgs e) => _ = SaveWallChartAsync(WallChartMode.Tiled);

    /// <summary>Prints the chart as a PDF wall chart: one big poster, or letter/A4 pages to tape together.</summary>
    private async Task SaveWallChartAsync(WallChartMode mode)
    {
        if (DataContext is not MainViewModel vm || VisibleChart is not { } chart) return;
        var name = ChartName(vm);
        var path = await AskPathAsync("Save wall chart", MainViewModel.SafeName($"{name} - {vm.CurrentName} wall chart") + ".pdf", "PDF", "*.pdf");
        if (path == null) return;
        try
        {
            vm.Status = "Drawing the wall chart…";
            var plan = await WallChart.ExportAsync(chart, path, new WallChartOptions
            {
                Title = $"{name} of {vm.CurrentName}",
                Subtitle = vm.TreeName,
                Mode = mode,
            });
            vm.Status = mode == WallChartMode.Tiled
                ? $"Saved {Path.GetFileName(path)}: {plan.Pages} pages ({plan.Columns} across, {plan.Rows} down) to print and tape together."
                : $"Saved {Path.GetFileName(path)}: a {plan.WidthMm / 10:0} × {plan.HeightMm / 10:0} cm poster.";
            MainViewModel.OpenExternal(path);
        }
        catch (Exception ex) { vm.Status = $"Couldn't make the wall chart: {ex.Message}"; }
    }
}
