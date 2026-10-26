using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Lineage.App.Controls;
using Lineage.App.Reports;
using Lineage.App.ViewModels;
using Lineage.Core.Model;
using QuestPDF.Fluent;

namespace Lineage.App.Tests;

internal static class NoTilesInTests
{
    /// <summary>No test anywhere in this assembly may download map tiles.</summary>
    [ModuleInitializer]
    internal static void TurnOffOnlineTiles() => MapView.AllowOnlineTiles = false;
}

/// <summary>Descendant and hourglass charts, wall chart PDFs and the map, all headless.</summary>
public class ChartsAndMapTests
{
    /// <summary>Set LINEAGE_PREVIEW_DIR to keep PNGs of the charts and map for a look by eye.</summary>
    private static readonly string? PreviewDir = Environment.GetEnvironmentVariable("LINEAGE_PREVIEW_DIR");

    private static string TempFile(string ext) => Path.Combine(Path.GetTempPath(), $"lineage-{Guid.NewGuid():N}{ext}");

    // ---- an invented family with several marriages, for pictures --------------------------------

    private static FamilyTree InventedFamily(out long rootId)
    {
        var t = new FamilyTree();
        Person P(string given, string surname, Sex sex, int born, int died = 0, string place = "", GeoPoint? at = null)
        {
            var p = t.AddPerson(new Person { Sex = sex });
            p.Names.Add(new Name { Given = given, Surname = surname });
            p.Events.Add(new Event { Tag = "BIRT", Date = GenDate.Parse(born.ToString()), Place = place, Location = at });
            if (died > 0) p.Events.Add(Event.Of("DEAT", GenDate.Parse(died.ToString())));
            return p;
        }
        Family F(Person? a, Person? b, params Person[] kids)
        {
            var f = t.AddFamily(new Family());
            if (a != null) t.SetPartner(f, a, false);
            if (b != null) t.SetPartner(f, b, true);
            foreach (var k in kids) t.AddChild(f, k);
            return f;
        }
        var york = new GeoPoint(53.959, -1.082);
        var leeds = new GeoPoint(53.800, -1.549);
        var hull = new GeoPoint(53.745, -0.336);
        var boston = new GeoPoint(42.360, -71.058);

        var root = P("Samuel James", "Garrow", Sex.Male, 1851, 1920, "York, Yorkshire, England", york);
        var gf = P("William", "Garrow", Sex.Male, 1820, 1888, "Hull, Yorkshire", hull);
        var gm = P("Hannah", "Pryce", Sex.Female, 1824, 1890, "Leeds", leeds);
        F(gf, gm, root);
        F(P("Robert", "Garrow", Sex.Male, 1790, 1850, "Hull"), P("Ann", "Toller", Sex.Female, 1795, 1861), gf);
        F(P("Edward", "Pryce", Sex.Male, 1799, 1870, "Leeds"), null, gm);

        var w1 = P("Margaret", "Holloway", Sex.Female, 1853, 1879, "Leeds", leeds);
        var w2 = P("Clara Beatrice", "Annesley", Sex.Female, 1858, 1931, "Hull", hull);
        var a = P("Alice", "Garrow", Sex.Female, 1874, 1950, "York", york);
        var b = P("Thomas", "Garrow", Sex.Male, 1876, 1940, "York", york);
        var c = P("Edith", "Garrow", Sex.Female, 1882, 1966, "Hull", hull);
        var d = P("Frederick", "Garrow", Sex.Male, 1885, 1958, "Boston, Massachusetts", boston);
        var e = P("Lily", "Garrow", Sex.Female, 1889, 1972, "Hull", hull);
        F(root, w1, a, b);
        F(root, w2, c, d, e);
        F(P("George", "Whitlock", Sex.Male, 1870, 1935), a,
          P("Mabel", "Whitlock", Sex.Female, 1898, 1980), P("Harold", "Whitlock", Sex.Male, 1900, 1971));
        F(b, P("Rose", "Fenwick", Sex.Female, 1880, 1955), P("Arthur", "Garrow", Sex.Male, 1905, 1990));
        F(P("Joseph", "Dent", Sex.Male, 1880, 1944), c);
        var dw = P("Mary", "O'Brien", Sex.Female, 1890, 1970, "Boston", boston);
        F(d, dw, P("Walter", "Garrow", Sex.Male, 1912, 1999, "Boston", boston), P("Ruth", "Garrow", Sex.Female, 1915, 2001, "Boston", boston),
          P("Frank", "Garrow", Sex.Male, 1918, 1944, "Boston", boston));
        F(null, e, P("Nora", "Garrow", Sex.Female, 1910, 1990, "Hull", hull));
        rootId = root.Id;
        return t;
    }

    private static ChartData Data(FamilyTree tree, long rootId) =>
        new(tree, rootId, tree.Ancestors(tree.People[rootId], MainViewModel.ChartGenerations));

    /// <summary>Hosts a control in a headless window in a theme, the way the app shows it.</summary>
    private static Window Host(Control content, ThemeVariant theme, double width = 1200, double height = 800)
    {
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = theme, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // ---- descendant and hourglass charts --------------------------------------------------------

    [AvaloniaFact]
    public async Task DescendantAndHourglassChartsMeasureAndSaveAsImages()
    {
        var (vm, _) = await Demo.OpenAsync();
        Assert.NotNull(vm.Chart);
        foreach (var chart in new TreeChart[] { new DescendantChart(), new HourglassChart() })
        {
            chart.Data = vm.Chart;
            chart.Measure(Size.Infinity);
            chart.Arrange(new Rect(chart.DesiredSize));
            Assert.True(chart.DesiredSize.Width > 400 && chart.DesiredSize.Height > 150, $"{chart.GetType().Name} {chart.DesiredSize}");
            Assert.True(Math.Abs(chart.Layout!.Width - chart.DesiredSize.Width) < 1);
            var path = PreviewDir != null ? Path.Combine(PreviewDir, $"demo-{chart.GetType().Name}.png") : TempFile(".png");
            chart.SaveImage(path);
            Assert.True(new FileInfo(path).Length > 5000, $"{new FileInfo(path).Length} bytes");
            if (PreviewDir == null) File.Delete(path);
        }

        // Thomas and Eliza with Clara below; the hourglass adds George and Mary Ann above.
        var down = new DescendantChart { Data = vm.Chart };
        Assert.Equal(new long[] { 1, 5 }, down.Layout!.Nodes.Select(n => n.PersonId));
        Assert.Equal(new long[] { 4 }, down.Layout.Root!.SpouseIds);
        var hour = new HourglassChart { Data = vm.Chart };
        Assert.Contains(hour.Layout!.Nodes, n => n.Generation == -1 && n.PersonId == 2 && n.SpouseIds.SequenceEqual(new long[] { 3 }));
    }

    [AvaloniaFact]
    public void GenerationsChangeTheLayoutAndClickingRecentres()
    {
        var tree = InventedFamily(out var rootId);
        var chart = new DescendantChart { Data = Data(tree, rootId), Generations = 2 };
        chart.Measure(Size.Infinity);
        var small = chart.DesiredSize;
        chart.Generations = 3;
        chart.Measure(Size.Infinity);
        Assert.True(chart.DesiredSize.Height > small.Height);
        chart.Generations = 99;                                   // clamped to 8
        Assert.Equal(3, chart.Layout!.Nodes.Max(n => n.Generation) + 1);

        long? navigated = null;
        chart.NavigateCommand = new Relay(id => navigated = (long)id!);
        var window = Host(new ScrollViewer { Content = chart, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, ThemeVariant.Light);
        var child = chart.Layout.Nodes.First(n => n.Generation == 1);
        var at = chart.TranslatePoint(new Point(child.SlotBounds(0).CenterX, child.Bounds.CenterY), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal(child.PersonId, navigated);
        window.Close();
    }

    [AvaloniaFact]
    public void ChartsLookRightInLightAndDark()
    {
        var tree = InventedFamily(out var rootId);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var chart in new TreeChart[] { new DescendantChart { Generations = 3 }, new HourglassChart() })
            {
                chart.Data = Data(tree, rootId);
                var window = Host(new ScrollViewer { Content = chart, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, theme);
                var path = PreviewDir != null ? Path.Combine(PreviewDir, $"{chart.GetType().Name}-{theme}.png") : TempFile(".png");
                chart.SaveImage(path);
                Assert.True(new FileInfo(path).Length > 20000);
                if (PreviewDir == null) File.Delete(path);
                window.Close();
            }
    }

    // ---- wall charts ------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task WallChartsArePdfsInBothModes()
    {
        var (vm, _) = await Demo.OpenAsync();
        var tree = InventedFamily(out var rootId);
        var charts = new (ChartBase Chart, ChartData Data)[]
        {
            (new DescendantChart(), Data(tree, rootId)),
            (new HourglassChart(), vm.Chart!),
            (new PedigreeChart { Generations = 4 }, vm.Chart!),     // not laid out by anyone beforehand
        };
        foreach (var (chart, data) in charts)
        {
            chart.Data = data;
            foreach (var mode in new[] { WallChartMode.SinglePoster, WallChartMode.Tiled })
            {
                var path = PreviewDir != null ? Path.Combine(PreviewDir, $"wall-{chart.GetType().Name}-{mode}.pdf") : TempFile(".pdf");
                var options = new WallChartOptions { Title = "The Garrow family", Subtitle = "Invented for testing", Mode = mode, Dpi = 150 };
                var plan = WallChart.Export(chart, path, options);
                var bytes = await File.ReadAllBytesAsync(path);
                Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
                Assert.True(bytes.Length > 5000);
                Assert.True(plan.Pages >= 1 && plan.Dpi > 0 && plan.WidthMm > 50);
                if (mode == WallChartMode.SinglePoster) Assert.Equal(1, plan.Pages);
                if (PreviewDir == null) File.Delete(path);
                else
                {
                    var n = 0;
                    foreach (var page in WallChart.Prepare(chart, options, out _).GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings { RasterDpi = 60 }))
                        File.WriteAllBytes(Path.Combine(PreviewDir, $"wall-{chart.GetType().Name}-{mode}-{++n}.png"), page);
                }
            }
        }

        // Printed big, a chart takes several sheets, every one of them in the PDF.
        var big = new DescendantChart { Data = Data(tree, rootId) };
        var tiled = new WallChartOptions { Title = "Garrow", Mode = WallChartMode.Tiled, Paper = WallChartPaper.A4, Scale = 1.5, Dpi = 100 };
        var bigPlan = WallChart.Plan(big, tiled);
        Assert.True(bigPlan.Columns >= 2, $"{bigPlan}");
        var file = TempFile(".pdf");
        Assert.Equal(bigPlan, WallChart.Export(big, file, tiled));
        var text = System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file));
        Assert.Equal(bigPlan.Pages, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count);
        File.Delete(file);

        // The poster never grows past 2.5 m wide.
        var poster = WallChart.Plan(big, new WallChartOptions { Scale = 40 });
        Assert.True(poster.WidthMm <= WallChart.MaxPosterWidthMm + 0.5, $"{poster}");
    }

    // ---- map --------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void MercatorRoundTrips()
    {
        var places = new[] { (0.0, 0.0), (51.5791, -0.3368), (-33.8688, 151.2093), (64.1466, -21.9426), (-54.8019, -68.3030), (84.9, 179.9) };
        foreach (var zoom in new[] { 0.0, 2, 5.5, 12, 17 })
            foreach (var (lat, lon) in places)
            {
                var world = MapView.LatLonToWorld(lat, lon, zoom);
                var back = MapView.WorldToLatLon(world, zoom);
                Assert.Equal(lat, back.Latitude, 6);
                Assert.Equal(lon, back.Longitude, 6);
            }
        Assert.Equal(new Point(128, 128), MapView.LatLonToWorld(0, 0, 0));
        Assert.Equal(1024, MapView.WorldSize(2));
        Assert.Equal(512, MapView.LatLonToWorld(0, 0, 2).X, 9);
        Assert.True(MapView.LatLonToWorld(60, 0, 3).Y < MapView.LatLonToWorld(10, 0, 3).Y);   // north is up
    }

    [AvaloniaFact]
    public void FitToShowsEveryPoint()
    {
        var sets = new[]
        {
            new[] { new GeoPoint(53.959, -1.082), new GeoPoint(53.800, -1.549), new GeoPoint(53.745, -0.336) },   // Yorkshire
            new[] { new GeoPoint(53.959, -1.082), new GeoPoint(42.360, -71.058), new GeoPoint(-33.87, 151.21) },  // three continents
            new[] { new GeoPoint(48.8566, 2.3522) },                                                                // one place
        };
        foreach (var points in sets)
        {
            var map = new MapView();
            map.Measure(new Size(800, 600));
            map.Arrange(new Rect(0, 0, 800, 600));
            map.FitTo(points);
            Assert.InRange(map.Zoom, MapView.MinZoom, MapView.MaxZoom);
            Assert.Equal(Math.Round(map.Zoom), map.Zoom);
            foreach (var p in points)
            {
                var s = map.ToScreen(p);
                Assert.InRange(s.X, 0, 800);
                Assert.InRange(s.Y, 30, 600);                   // room above for the pin's head
            }
            if (points.Length > 1)
            {
                // One step closer would lose a point: the zoom is as close as it can be.
                var zoomedIn = new MapView();
                zoomedIn.Measure(new Size(800, 600));
                zoomedIn.Arrange(new Rect(0, 0, 800, 600));
                zoomedIn.Center = map.Center;
                zoomedIn.Zoom = map.Zoom + 1;
                if (zoomedIn.Zoom > map.Zoom)
                    Assert.Contains(points, p => zoomedIn.ToScreen(p) is var s && (s.X < 44 || s.X > 800 - 64 || s.Y < 64 || s.Y > 600 - 40));
            }
        }
        // Fitting before the map has a size waits for its first layout.
        var early = new MapView { Pins = sets[0].Select(p => new MapPin(p, "x", "y")).ToList() };
        early.Measure(new Size(800, 600));
        early.Arrange(new Rect(0, 0, 800, 600));
        Assert.True(early.Zoom >= 7);
        Assert.All(sets[0], p => Assert.InRange(early.ToScreen(p).X, 0, 800));
    }

    private static List<MapPin> Pins(FamilyTree tree, out List<MapLine> lines)
    {
        var (pins, l) = MapData.For(tree, tree.People.Values);
        lines = l.ToList();
        return pins.ToList();
    }

    [AvaloniaFact]
    public void MapRendersOfflineWithPinsLinesAndClusters()
    {
        Assert.False(MapView.AllowOnlineTiles);
        var tree = InventedFamily(out _);
        var pins = Pins(tree, out var lines);
        var downloads = TileCache.DownloadCount;
        Assert.Contains(pins, p => p.Kind == MapPinKind.Birth && p.PersonId != null);
        Assert.Contains(lines, l => l.Label.Contains("→"));

        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var (name, size, zoomToYorkshire) in new[] { ("world", new Size(900, 560), false), ("yorkshire", new Size(900, 560), true) })
            {
                var map = new MapView { Pins = pins, Lines = lines };
                var window = Host(map, theme, size.Width, size.Height);
                if (zoomToYorkshire) map.FitTo(pins.Where(p => p.Location.Longitude > -5).Select(p => p.Location));
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame()!;
                Assert.Equal(new PixelSize((int)size.Width, (int)size.Height), frame.PixelSize);
                Assert.Equal(255, Pixel(frame, 450, 280).A);
                if (PreviewDir != null) frame.Save(Path.Combine(PreviewDir, $"map-{name}-{theme}.png"));
                Assert.Equal(downloads, TileCache.DownloadCount);
                window.Close();
            }
    }

    [AvaloniaFact]
    public void ClickingAPinRunsTheCommandAndHoverShowsATooltip()
    {
        var hull = new GeoPoint(53.745, -0.336);
        var pins = new List<MapPin>
        {
            new(new GeoPoint(53.959, -1.082), "Samuel James Garrow", "Birth 1851 · York", MapPinKind.Birth, 7),
            new(hull, "Clara Beatrice Annesley", "Birth 1858 · Hull", MapPinKind.Birth, 8),
            new(hull, "Edith Garrow", "Birth 1882 · Hull", MapPinKind.Birth, 9),
        };
        object? clicked = null;
        var map = new MapView { Pins = pins, PinClickCommand = new Relay(id => clicked = id) };
        var window = Host(map, ThemeVariant.Light, 800, 600);
        using (window.CaptureRenderedFrame()) { }

        // York's pin head sits a little above the point.
        var york = map.ToScreen(pins[0].Location);
        var head = new Point(york.X, york.Y - 21);
        window.MouseMove(head);
        using (var hover = window.CaptureRenderedFrame()!)
            if (PreviewDir != null) hover.Save(Path.Combine(PreviewDir, "map-hover.png"));
        window.MouseDown(head, MouseButton.Left);
        window.MouseUp(head, MouseButton.Left);
        Assert.Equal(7L, clicked);

        // The two Hull pins share a spot, so they fan out rather than hide each other.
        var spot = map.ToScreen(hull);
        var fanned = new List<object?>();
        foreach (var dx in new[] { -MapView.FanRadius(2), MapView.FanRadius(2) })      // two pins: one each side of the spot
        {
            clicked = null;
            var p = new Point(spot.X + dx, spot.Y - 21);
            window.MouseMove(p);
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            fanned.Add(clicked);
        }
        Assert.Equal(new object?[] { 8L, 9L }, fanned);

        // Wheel zooms around the pointer, the buttons zoom too.
        var before = map.Zoom;
        window.MouseWheel(new Point(400, 300), new Vector(0, 1));
        Assert.Equal(before + 1, map.Zoom);
        map.ZoomOut();
        Assert.Equal(before, map.Zoom);
        window.Close();
    }

    [AvaloniaFact]
    public void TilesAreFetchedOnceAndKeptOnDisk()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"lineage-tiles-{Guid.NewGuid():N}");
        var png = SolidPng(Color.FromRgb(200, 30, 160));
        var oldFolder = TileCache.Folder;
        var calls = 0;
        TileCache.Folder = folder;
        TileCache.Downloader = (_, _) => { Interlocked.Increment(ref calls); return Task.FromResult<byte[]?>(png); };
        MapView.AllowOnlineTiles = true;
        try
        {
            var map = new MapView { Center = new GeoPoint(53.8, -1.5), Zoom = 6 };
            var window = Host(map, ThemeVariant.Light, 600, 400);
            Pump(window);
            using (var frame = window.CaptureRenderedFrame()!)
            {
                var c = Pixel(frame, 200, 200);
                Assert.True(c.R == 200 && c.G == 30 && c.B == 160, $"tile drawn: {c}");
                if (PreviewDir != null) frame.Save(Path.Combine(PreviewDir, "map-fake-tiles.png"));
            }
            var files = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            Assert.All(files, f => Assert.Matches(@"[\\/]6[\\/]\d+[\\/]\d+\.png$", f));
            Assert.Equal(calls, files.Length);

            // A new session reads them back from disk instead of downloading again.
            TileCache.Clear();
            var before = calls;
            window.Content = new MapView { Center = new GeoPoint(53.8, -1.5), Zoom = 6 };
            Pump(window);
            Assert.Equal(before, calls);
            window.Close();
        }
        finally
        {
            MapView.AllowOnlineTiles = false;
            TileCache.Downloader = null;
            TileCache.Folder = oldFolder;
            TileCache.Clear();
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static void Pump(Window window)
    {
        using (window.CaptureRenderedFrame()) { }                  // the first frame asks for the tiles
        var until = DateTime.UtcNow.AddSeconds(15);
        while (TileCache.InFlight > 0 && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, TileCache.InFlight);
        using (window.CaptureRenderedFrame()) { }                  // draw what arrived
    }

    private static byte[] SolidPng(Color colour)
    {
        using var bmp = new RenderTargetBitmap(new PixelSize(256, 256));
        using (var ctx = bmp.CreateDrawingContext()) ctx.FillRectangle(new SolidColorBrush(colour), new Rect(0, 0, 256, 256));
        using var ms = new MemoryStream();
        bmp.Save(ms);
        return ms.ToArray();
    }

    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        var buffer = new byte[4];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4); }
        finally { handle.Free(); }
        return bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(buffer[3], buffer[0], buffer[1], buffer[2])
            : Color.FromArgb(buffer[3], buffer[2], buffer[1], buffer[0]);

    }

    private sealed class Relay(Action<object?> run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run(parameter);
    }
}
