using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Lineage.App.Controls;
using Lineage.App.Reports;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

/// <summary>
/// Designs a wall chart: the chart is drawn live in a sheet shaped like the finished poster, and saved as a PDF
/// with <see cref="WallChart"/>. The window returns the saved path, or null.
/// </summary>
public partial class WallChartDesignerWindow : Window
{
    private ChartBase? _chart;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(120) };

    public WallChartDesignerWindow()
    {
        InitializeComponent();
        _refresh.Tick += (_, _) => { _refresh.Stop(); Refresh(); };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WallChartDesignerViewModel vm) vm.PropertyChanged += OnChoiceChanged;
            Rebuild();
        };
        PreviewArea.SizeChanged += (_, _) => _refresh.Start();
    }

    private WallChartDesignerViewModel? Vm => DataContext as WallChartDesignerViewModel;

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WallChartDesignerViewModel.PlanText)) return;
        if (e.PropertyName is nameof(WallChartDesignerViewModel.KindIndex) or nameof(WallChartDesignerViewModel.Generations)
            or nameof(WallChartDesignerViewModel.ColourByEvidence))
            Rebuild();
        else
            _refresh.Start();
    }

    /// <summary>A fresh chart of the chosen kind, as far back or forward as asked.</summary>
    private void Rebuild()
    {
        if (Vm is not { } vm) return;
        var g = vm.Generations;
        var data = new ChartData(vm.Tree, vm.Root.Id, vm.Tree.Ancestors(vm.Root, g));
        _chart = vm.Kind switch
        {
            WallChartKind.Fan => new FanChart { Generations = g },
            WallChartKind.Descendants => new DescendantChart { Generations = g },
            WallChartKind.BowTie => new BowTieChart { Generations = g },
            WallChartKind.Hourglass => new HourglassChart { AncestorGenerations = Math.Max(2, g / 2 + 1), DescendantGenerations = Math.Max(2, g / 2 + 1) },
            _ => new PedigreeChart { Generations = g },
        };
        _chart.Data = data;
        _chart.ShowEvidence = vm.ColourByEvidence;
        _chart.IsHitTestVisible = false;
        ChartHost.Children.Clear();
        ChartHost.Children.Add(_chart);
        _refresh.Start();
    }

    /// <summary>Colours, frame and the sheet's shape follow the choices; the finished size goes under the preview.</summary>
    private void Refresh()
    {
        if (Vm is not { } vm || _chart == null) return;
        var options = vm.ToOptions();
        SheetScope.RequestedThemeVariant = options.LightTheme ? ThemeVariant.Light : ActualThemeVariant;
        // The sheet around the preview wears the chosen look too.
        foreach (var key in WallChart.PaletteKeys) SheetScope.Resources.Remove(key);
        foreach (var (key, colour) in WallChart.Palette(options.Style)) SheetScope.Resources[key] = new SolidColorBrush(colour);
        var centred = options.Style == WallChartStyle.Heritage;
        PreviewTitle.HorizontalAlignment = PreviewSubtitle.HorizontalAlignment = PreviewNote.HorizontalAlignment =
            centred ? Avalonia.Layout.HorizontalAlignment.Center : Avalonia.Layout.HorizontalAlignment.Left;
        if (options.Style == WallChartStyle.Modern && SheetScope.FindResource("Accent") is IBrush accent) PreviewTitle.Foreground = accent;
        else PreviewTitle.ClearValue(TextBlock.ForegroundProperty);
        PreviewBand.IsVisible = options.Style == WallChartStyle.Modern;
        ChartFrame.BorderThickness = new Thickness(options.Frame ? (centred ? 3 : 2) : 0);
        PreviewNote.IsVisible = vm.Note.Trim().Length > 0;
        PreviewSubtitle.IsVisible = vm.Subtitle.Trim().Length > 0;
        try
        {
            var plan = WallChart.Plan(_chart, options);
            vm.PlanText = WallChartDesignerViewModel.Describe(plan);
            // The sheet takes the poster's proportions (for pages, the whole taped-together chart) and fits the preview.
            var area = PreviewArea.Bounds.Size.Deflate(PreviewArea.Padding);
            if (area.Width < 50) area = new Size(Math.Max(300, Bounds.Width - 360 - 48), Math.Max(300, Bounds.Height - 70 - 48));
            var aspect = plan.Mode == WallChartMode.SinglePoster ? plan.WidthMm / plan.HeightMm : (double)plan.Columns / Math.Max(1, plan.Rows) * 1.35;
            if (area.Width > 50 && area.Height > 50 && aspect > 0)
            {
                var w = Math.Min(area.Width, area.Height * aspect);
                Sheet.Width = w;
                Sheet.Height = w / aspect;
            }
            Dispatcher.UIThread.Post(DrawPreview, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            vm.PlanText = ex.Message;
        }
    }

    /// <summary>A picture of the whole chart, as it will print, fitted into the preview sheet.</summary>
    private void DrawPreview()
    {
        if (Vm is not { } vm || _chart == null) return;
        try
        {
            var bitmap = WallChart.Preview(_chart, vm.ToOptions(), 1600);
            var old = ChartPicture.Source as IDisposable;
            ChartPicture.Source = bitmap;
            old?.Dispose();
            // Fit the picture into what's left of the sheet under the title.
            var room = new Size(Math.Max(40, Sheet.Width - 36 - 12), Math.Max(40, Sheet.Height - 36 - 12 - 60 - (PreviewNote.IsVisible ? 22 : 0)));
            var k = Math.Min(room.Width / bitmap.Size.Width, room.Height / bitmap.Size.Height);
            ChartPicture.Width = bitmap.Size.Width * k;
            ChartPicture.Height = bitmap.Size.Height * k;
        }
        catch (Exception ex) { vm.PlanText = ex.Message; }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || _chart == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save wall chart",
            SuggestedFileName = MainViewModel.SafeName(vm.Title.Length > 0 ? vm.Title : "Wall chart") + ".pdf",
            DefaultExtension = ".pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            IsEnabled = false;
            vm.PlanText = "Drawing the wall chart…";
            await WallChart.ExportAsync(_chart, path, vm.ToOptions());
            Close(path);
        }
        catch (Exception ex)
        {
            vm.PlanText = $"Couldn't make the wall chart: {ex.Message}";
            IsEnabled = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
