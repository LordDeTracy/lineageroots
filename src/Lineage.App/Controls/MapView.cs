using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;
using System.Net.Http;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>
/// A slippy map for showing where a family lived: OpenStreetMap tiles (cached on disk for 30 days, at most
/// two downloads at a time, as the tile usage policy asks), teardrop pins coloured by event, migration
/// arcs, drag to pan, wheel or +/− to zoom. Without tiles (offline, or <see cref="AllowOnlineTiles"/> off)
/// it draws a soft background with a latitude/longitude grid so the pins still make sense.
/// </summary>
public sealed class MapView : Control
{
    public const int TileSize = 256;
    public const double MinZoom = 2, MaxZoom = 17;
    public const double MaxLatitude = 85.05112878;
    public const string Attribution = "© OpenStreetMap contributors";

    /// <summary>When false the map never contacts the tile server (and shows no tiles at all).</summary>
    public static bool AllowOnlineTiles { get; set; } = true;

    public static readonly StyledProperty<IEnumerable<MapPin>?> PinsProperty =
        AvaloniaProperty.Register<MapView, IEnumerable<MapPin>?>(nameof(Pins));
    public static readonly StyledProperty<IEnumerable<MapLine>?> LinesProperty =
        AvaloniaProperty.Register<MapView, IEnumerable<MapLine>?>(nameof(Lines));
    /// <summary>Runs with the pin's PersonId when a pin linked to a person is clicked.</summary>
    public static readonly StyledProperty<ICommand?> PinClickCommandProperty =
        AvaloniaProperty.Register<MapView, ICommand?>(nameof(PinClickCommand));
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MapView, double>(nameof(Zoom), MinZoom,
            coerce: (_, z) => double.IsNaN(z) ? MinZoom : Math.Clamp(z, MinZoom, MaxZoom));
    public static readonly StyledProperty<GeoPoint> CenterProperty =
        AvaloniaProperty.Register<MapView, GeoPoint>(nameof(Center), new GeoPoint(30, 0));
    /// <summary>Fit the view to the pins every time they change (otherwise only the first time).</summary>
    public static readonly StyledProperty<bool> AutoFitProperty =
        AvaloniaProperty.Register<MapView, bool>(nameof(AutoFit));

    static MapView()
    {
        AffectsRender<MapView>(ZoomProperty, CenterProperty);
        FocusableProperty.OverrideDefaultValue<MapView>(true);
    }

    public MapView()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        DoubleTapped += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (ButtonAt(p) == MapButton.None && MarkerAt(p) == null) ZoomAround(p, Math.Round(Zoom) + 1);
        };
    }

    public IEnumerable<MapPin>? Pins { get => GetValue(PinsProperty); set => SetValue(PinsProperty, value); }
    public IEnumerable<MapLine>? Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    public ICommand? PinClickCommand { get => GetValue(PinClickCommandProperty); set => SetValue(PinClickCommandProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public GeoPoint Center { get => GetValue(CenterProperty); set => SetValue(CenterProperty, value); }
    public bool AutoFit { get => GetValue(AutoFitProperty); set => SetValue(AutoFitProperty, value); }

    // ---- Web Mercator ---------------------------------------------------------------------------

    /// <summary>Width (and height) of the whole world in pixels at a zoom level.</summary>
    public static double WorldSize(double zoom) => TileSize * Math.Pow(2, zoom);

    /// <summary>Latitude/longitude to "world pixels" at a zoom: (0,0) is the top-left of the world map.</summary>
    public static Point LatLonToWorld(double latitude, double longitude, double zoom)
    {
        var size = WorldSize(zoom);
        var lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        var x = (longitude + 180) / 360 * size;
        var y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * size;
        return new Point(x, y);
    }

    public static Point LatLonToWorld(GeoPoint p, double zoom) => LatLonToWorld(p.Latitude, p.Longitude, zoom);

    /// <summary>The inverse of <see cref="LatLonToWorld(double, double, double)"/>.</summary>
    public static GeoPoint WorldToLatLon(Point world, double zoom)
    {
        var size = WorldSize(zoom);
        var lon = world.X / size * 360 - 180;
        var n = Math.PI - 2 * Math.PI * world.Y / size;
        return new GeoPoint(Math.Atan(Math.Sinh(n)) * 180 / Math.PI, lon);
    }

    /// <summary>The centre and (whole-number) zoom that show all points inside a viewport, with room for the pins.</summary>
    public static (GeoPoint Center, double Zoom) Fit(IEnumerable<GeoPoint> points, Size viewport)
    {
        var pad = FitPadding;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        foreach (var p in points)
        {
            var w = LatLonToWorld(p, 0);
            minX = Math.Min(minX, w.X); maxX = Math.Max(maxX, w.X);
            minY = Math.Min(minY, w.Y); maxY = Math.Max(maxY, w.Y);
            any = true;
        }
        if (!any) return (new GeoPoint(30, 0), MinZoom);
        var width = Math.Max(40, viewport.Width - pad.Left - pad.Right);
        var height = Math.Max(40, viewport.Height - pad.Top - pad.Bottom);
        double dx = maxX - minX, dy = maxY - minY;
        double zoom;
        if (dx < 1e-12 && dy < 1e-12) zoom = SinglePlaceZoom;
        else zoom = Math.Floor(Math.Log2(Math.Min(dx > 1e-12 ? width / dx : double.MaxValue, dy > 1e-12 ? height / dy : double.MaxValue)) + 1e-9);
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var s = Math.Pow(2, zoom);
        var centre = new Point((minX + maxX) / 2 * s + (pad.Right - pad.Left) / 2, (minY + maxY) / 2 * s + (pad.Bottom - pad.Top) / 2);
        return (Normalise(WorldToLatLon(centre, zoom)), zoom);
    }

    private const double SinglePlaceZoom = 12;
    private static readonly Thickness FitPadding = new(44, 64, 64, 40);   // pins rise above their point; buttons on the right

    private static GeoPoint Normalise(GeoPoint p) =>
        new(Math.Clamp(p.Latitude, -MaxLatitude, MaxLatitude), ((p.Longitude + 180) % 360 + 360) % 360 - 180);

    private Point CentreWorld => LatLonToWorld(Center, Zoom);

    /// <summary>Screen position of a world pixel, taking the nearest copy of a world that wraps sideways.</summary>
    private Point WorldToScreen(Point world, Point centre, double size)
    {
        var dx = world.X - centre.X;
        dx -= size * Math.Round(dx / size);
        return new Point(Bounds.Width / 2 + dx, Bounds.Height / 2 + world.Y - centre.Y);
    }

    /// <summary>Where a place is on screen at the current view.</summary>
    public Point ToScreen(GeoPoint p) => WorldToScreen(LatLonToWorld(p, Zoom), CentreWorld, WorldSize(Zoom));

    /// <summary>The place under a screen point.</summary>
    public GeoPoint FromScreen(Point screen)
    {
        var c = CentreWorld;
        return Normalise(WorldToLatLon(new Point(c.X + screen.X - Bounds.Width / 2, c.Y + screen.Y - Bounds.Height / 2), Zoom));
    }

    // ---- navigation -----------------------------------------------------------------------------

    private List<GeoPoint>? _pendingFit;
    private bool _fitted;

    /// <summary>Shows all the points with some padding. Waits for the first layout if the map has no size yet.</summary>
    public void FitTo(IEnumerable<GeoPoint> points)
    {
        var list = points.ToList();
        if (list.Count == 0) return;
        _fitted = true;
        if (Bounds.Width < 1 || Bounds.Height < 1) { _pendingFit = list; return; }
        Apply(list, Bounds.Size);
    }

    /// <summary>Fits the view to every pin and line end.</summary>
    public void FitToPins() => FitTo(_pins.Select(p => p.Location).Concat(_lines.SelectMany(l => new[] { l.From, l.To })));

    private void Apply(List<GeoPoint> points, Size viewport)
    {
        var (centre, zoom) = Fit(points, viewport);
        SetCurrentValue(ZoomProperty, zoom);
        SetCurrentValue(CenterProperty, centre);
    }

    public void ZoomIn() => ZoomAround(new Point(Bounds.Width / 2, Bounds.Height / 2), Math.Round(Zoom) + 1);
    public void ZoomOut() => ZoomAround(new Point(Bounds.Width / 2, Bounds.Height / 2), Math.Round(Zoom) - 1);

    /// <summary>Changes zoom keeping the place under <paramref name="screen"/> where it is.</summary>
    public void ZoomAround(Point screen, double zoom)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Bounds.Width < 1 || Bounds.Height < 1) { SetCurrentValue(ZoomProperty, zoom); return; }
        var place = FromScreen(screen);
        var w = LatLonToWorld(place, zoom);
        var centre = new Point(w.X - (screen.X - Bounds.Width / 2), w.Y - (screen.Y - Bounds.Height / 2));
        SetCurrentValue(ZoomProperty, zoom);
        SetCurrentValue(CenterProperty, Normalise(WorldToLatLon(centre, zoom)));
    }

    /// <summary>Moves the map with the pointer (positive x drags the map to the right).</summary>
    public void PanBy(Vector screenDelta)
    {
        var c = CentreWorld - screenDelta;
        var size = WorldSize(Zoom);
        SetCurrentValue(CenterProperty, Normalise(WorldToLatLon(new Point(c.X, Math.Clamp(c.Y, 0, size)), Zoom)));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 480 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 320 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_pendingFit is { } points && finalSize.Width >= 1 && finalSize.Height >= 1)
        {
            _pendingFit = null;
            Apply(points, finalSize);
        }
        return base.ArrangeOverride(finalSize);
    }

    // ---- pins and lines ---------------------------------------------------------------------------

    private List<MapPin> _pins = new();
    private List<MapLine> _lines = new();
    private INotifyCollectionChanged? _pinSource, _lineSource;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PinsProperty)
        {
            Watch(ref _pinSource, change.NewValue, OnPinsChanged);
            OnPinsChanged(null, null);
        }
        else if (change.Property == LinesProperty)
        {
            Watch(ref _lineSource, change.NewValue, OnLinesChanged);
            OnLinesChanged(null, null);
        }
        else if (change.Property == ZoomProperty)
        {
            _clusters = null;
            _hover = null;
        }
    }

    private static void Watch(ref INotifyCollectionChanged? current, object? value, NotifyCollectionChangedEventHandler handler)
    {
        if (current != null) current.CollectionChanged -= handler;
        current = value as INotifyCollectionChanged;
        if (current != null) current.CollectionChanged += handler;
    }

    private void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        _pins = Pins?.ToList() ?? new List<MapPin>();
        _clusters = null;
        _hover = null;
        if (_pins.Count > 0 && (!_fitted || AutoFit)) FitToPins();
        InvalidateVisual();
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        _lines = Lines?.ToList() ?? new List<MapLine>();
        _hover = null;
        InvalidateVisual();
    }

    /// <summary>Pins close together at the current zoom, gathered so they can fan out or show a count.</summary>
    private sealed class Cluster
    {
        public Point World;
        public readonly List<MapPin> Pins = new();
    }

    private const double ClusterRadius = 14, FanLimit = 8, HeadRadius = 9, HeadRise = 21, CountRadius = 12.5, CountRise = 26;
    private List<Cluster>? _clusters;

    private List<Cluster> Clusters()
    {
        if (_clusters != null) return _clusters;
        var grid = new Dictionary<(long, long), List<Cluster>>();
        var list = new List<Cluster>();
        foreach (var pin in _pins)
        {
            var w = LatLonToWorld(pin.Location, Zoom);
            var cell = ((long)Math.Floor(w.X / ClusterRadius), (long)Math.Floor(w.Y / ClusterRadius));
            Cluster? found = null;
            for (var dx = -1; dx <= 1 && found == null; dx++)
                for (var dy = -1; dy <= 1 && found == null; dy++)
                    if (grid.TryGetValue((cell.Item1 + dx, cell.Item2 + dy), out var near))
                        found = near.FirstOrDefault(c => Distance(c.World, w) <= ClusterRadius);
            if (found == null)
            {
                found = new Cluster { World = w };
                list.Add(found);
                if (!grid.TryGetValue(cell, out var bucket)) grid[cell] = bucket = new List<Cluster>();
                bucket.Add(found);
            }
            found.Pins.Add(pin);
        }
        return _clusters = list;
    }

    /// <summary>A drawn marker: a single pin (possibly fanned out from its spot) or a count for a crowd.</summary>
    private sealed record Marker(Point Tip, Point Spot, MapPin? Pin, Cluster Cluster)
    {
        public bool IsCount => Pin == null;
        public double Radius => IsCount ? CountRadius : HeadRadius;
        public double Rise => IsCount ? CountRise : HeadRise;
        public Point Head => new(Tip.X, Tip.Y - Rise);
        public bool Contains(Point p) =>
            Distance(p, Head) <= Radius + 2 || (Math.Abs(p.X - Tip.X) <= Radius * 0.6 && p.Y <= Tip.Y + 1 && p.Y >= Head.Y);
    }

    internal static double FanRadius(int pins) => Math.Max(16, 2.6 * HeadRadius * (pins - 1) / Math.PI);

    private List<Marker> _markers = new();
    private List<(MapLine Line, Point[] Path)> _linePaths = new();

    private List<Marker> Layout(Size view)
    {
        var centre = CentreWorld;
        var size = WorldSize(Zoom);
        var area = new Rect(view).Inflate(60);
        var markers = new List<Marker>();
        foreach (var c in Clusters())
        {
            var spot = WorldToScreen(c.World, centre, size);
            if (!area.Contains(spot)) continue;
            if (c.Pins.Count == 1) markers.Add(new Marker(spot, spot, c.Pins[0], c));
            else if (c.Pins.Count <= FanLimit)
            {
                // Fan out along an arc over the spot, left to right, far enough apart that the heads don't touch.
                var n = c.Pins.Count;
                var radius = FanRadius(n);
                for (var i = 0; i < n; i++)
                {
                    var a = Math.PI + Math.PI * i / (n - 1);
                    markers.Add(new Marker(new Point(spot.X + radius * Math.Cos(a), spot.Y + radius * Math.Sin(a)), spot, c.Pins[i], c));
                }
            }
            else markers.Add(new Marker(spot, spot, null, c));
        }
        // Southern pins overlap northern ones, as if standing on the map.
        markers.Sort((a, b) => a.Tip.Y.CompareTo(b.Tip.Y));
        return markers;
    }

    // ---- drawing ----------------------------------------------------------------------------------

    private IBrush Paint(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b
        : Application.Current is { } app && (app.TryGetResource(key, ActualThemeVariant, out v) || app.TryGetResource(key, ThemeVariant.Light, out v)) && v is IBrush a ? a
        : Brushes.Gray;

    private Color Colour(string key) => Paint(key) is ISolidColorBrush s ? s.Color : Colors.Gray;

    private static Color Blend(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static IBrush WithAlpha(IBrush brush, double alpha) =>
        brush is ISolidColorBrush s ? new SolidColorBrush(s.Color, alpha) : brush;

    private static readonly Typeface Regular = new(FontFamily.Default);
    private static readonly Typeface Semibold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    private static FormattedText Text(string text, double size, IBrush brush, bool bold = false, double maxWidth = 0, int maxLines = 1)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Semibold : Regular, size, brush);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = maxLines;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        return ft;
    }

    public IBrush KindBrush(MapPinKind kind) => Paint(kind switch
    {
        MapPinKind.Birth => "Accent",
        MapPinKind.Marriage => "FemaleInk",
        MapPinKind.Residence => "MaleInk",
        MapPinKind.Death => "Muted",
        _ => "Faint",
    });

    public override void Render(DrawingContext ctx)
    {
        var view = new Size(Bounds.Width, Bounds.Height);
        if (view.Width < 1 || view.Height < 1) return;
        var dark = ActualThemeVariant == ThemeVariant.Dark;

        ctx.FillRectangle(new SolidColorBrush(Blend(Colour("Bg"), Colour("MaleBg"), dark ? 0.45 : 0.35)), new Rect(view));
        DrawGraticule(ctx, view);
        if (AllowOnlineTiles && DrawTiles(ctx, view) && dark)
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), new Rect(view)); // tone tiles down at night

        DrawLines(ctx, view);
        _markers = Layout(view);
        DrawMarkers(ctx);
        DrawLegend(ctx, view);
        if (AllowOnlineTiles) DrawAttribution(ctx, view);
        DrawButtons(ctx, view);
        DrawTooltip(ctx, view);
    }

    private static readonly double[] GridSteps = { 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30 };

    private void DrawGraticule(DrawingContext ctx, Size view)
    {
        var size = WorldSize(Zoom);
        var c = CentreWorld;
        var originX = c.X - view.Width / 2;
        var originY = c.Y - view.Height / 2;
        var perDegree = size / 360;
        var step = GridSteps.FirstOrDefault(s => s * perDegree >= 90, 30);
        var pen = new Pen(WithAlpha(Paint("LineStrong"), 0.8), 1);
        var strong = new Pen(Paint("LineStrong"), 1.4);
        var faint = Paint("Faint");

        var west = originX / size * 360 - 180;
        var east = (originX + view.Width) / size * 360 - 180;
        for (var lon = Math.Ceiling(west / step) * step; lon <= east; lon += step)
        {
            var x = Math.Round((lon + 180) / 360 * size - originX) + 0.5;
            var wrapped = ((lon + 180) % 360 + 360) % 360 - 180;
            ctx.DrawLine(Math.Abs(wrapped) < 1e-9 ? strong : pen, new Point(x, 0), new Point(x, view.Height));
            if (x > view.Width - 96) continue;                    // the zoom buttons live there
            var t = Text(Degrees(wrapped, "E", "W", step), 10, faint);
            ctx.DrawText(t, new Point(x + 4, 6));
        }
        for (var lat = -80.0; lat <= 80 + 1e-9; lat += Math.Max(step, 0.05))
        {
            var y = Math.Round(LatLonToWorld(lat, 0, Zoom).Y - originY) + 0.5;
            if (y < 0 || y > view.Height) continue;
            ctx.DrawLine(Math.Abs(lat) < 1e-9 ? strong : pen, new Point(0, y), new Point(view.Width, y));
            var t = Text(Degrees(lat, "N", "S", step), 10, faint);
            ctx.DrawText(t, new Point(6, y - t.Height - 2));
        }
    }

    private static string Degrees(double value, string positive, string negative, double step)
    {
        var format = step < 0.1 ? "0.00" : step < 1 ? "0.##" : "0";
        var text = Math.Abs(value).ToString(format, CultureInfo.InvariantCulture) + "°";
        return Math.Abs(value) < 1e-9 ? text : text + (value > 0 ? positive : negative);
    }

    /// <summary>Draws the tiles it has; asks for the rest. A missing tile borrows a scaled-up parent while it loads.</summary>
    private bool DrawTiles(DrawingContext ctx, Size view)
    {
        var z = (int)Math.Clamp(Math.Round(Zoom), MinZoom, MaxZoom);
        var ts = TileSize * Math.Pow(2, Zoom - z);
        var c = CentreWorld;
        var originX = c.X - view.Width / 2;
        var originY = c.Y - view.Height / 2;
        var n = 1 << z;
        int tx0 = (int)Math.Floor(originX / ts), tx1 = (int)Math.Floor((originX + view.Width) / ts);
        int ty0 = Math.Max(0, (int)Math.Floor(originY / ts)), ty1 = Math.Min(n - 1, (int)Math.Floor((originY + view.Height) / ts));
        var drew = false;
        for (var ty = ty0; ty <= ty1; ty++)
            for (var tx = tx0; tx <= tx1; tx++)
            {
                // Whole-pixel edges with a hair of overlap, so no seams show between tiles.
                double x0 = Math.Round(tx * ts - originX), x1 = Math.Round((tx + 1) * ts - originX);
                double y0 = Math.Round(ty * ts - originY), y1 = Math.Round((ty + 1) * ts - originY);
                var dest = new Rect(x0, y0, x1 - x0 + 0.5, y1 - y0 + 0.5);
                var key = new TileKey(z, ((tx % n) + n) % n, ty);
                if (TileCache.Get(key) is { } image)
                {
                    ctx.DrawImage(image, new Rect(image.Size), dest);
                    drew = true;
                    continue;
                }
                TileCache.Request(key);
                for (var up = 1; up <= 4 && z - up >= 0; up++)
                {
                    if (TileCache.Get(new TileKey(z - up, key.X >> up, key.Y >> up)) is not { } parent) continue;
                    var part = parent.Size.Width / (1 << up);
                    var src = new Rect((key.X & ((1 << up) - 1)) * part, (key.Y & ((1 << up) - 1)) * part, part, part);
                    ctx.DrawImage(parent, src, dest);
                    drew = true;
                    break;
                }
            }
        return drew;
    }

    private void DrawLines(DrawingContext ctx, Size view)
    {
        var paths = new List<(MapLine, Point[])>();
        if (_lines.Count > 0)
        {
            var accent = Paint("Accent");
            var pen = new Pen(WithAlpha(accent, 0.85), 1.8, lineCap: PenLineCap.Round);
            var halo = new Pen(WithAlpha(Paint("Surface"), 0.55), 4.2, lineCap: PenLineCap.Round);
            var arrowFill = WithAlpha(accent, 0.95);
            var area = new Rect(view).Inflate(40);
            var size = WorldSize(Zoom);
            foreach (var line in _lines)
            {
                var a = ToScreen(line.From);
                // The other end relative to the first, the short way round the world.
                var wa = LatLonToWorld(line.From, Zoom);
                var wb = LatLonToWorld(line.To, Zoom);
                var dx = wb.X - wa.X;
                dx -= size * Math.Round(dx / size);
                var b = new Point(a.X + dx, a.Y + wb.Y - wa.Y);
                var d = b - a;
                var length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (length < 8) continue;
                var perp = new Vector(-d.Y / length, d.X / length);
                if (perp.Y > 0) perp = -perp;                         // bulge upwards
                var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var control = mid + perp * (length * 0.22);
                if (!area.Intersects(new Rect(a, b).Union(new Rect(control, control)))) continue;

                var t = b - control;
                var tl = Math.Max(1e-6, Math.Sqrt(t.X * t.X + t.Y * t.Y));
                var dir = new Vector(t.X / tl, t.Y / tl);
                var tip = b - dir * 4;
                var tail = tip - dir * 9;
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(a, isFilled: false);
                    s.QuadraticBezierTo(control, tail);
                    s.EndFigure(false);
                }
                ctx.DrawGeometry(null, halo, g);
                ctx.DrawGeometry(null, pen, g);

                var side = new Vector(-dir.Y, dir.X) * 4.6;
                var arrow = new StreamGeometry();
                using (var s = arrow.Open())
                {
                    s.BeginFigure(tip, isFilled: true);
                    s.LineTo(tail + side);
                    s.LineTo(tail - side);
                    s.EndFigure(true);
                }
                ctx.DrawGeometry(arrowFill, null, arrow);

                var samples = new Point[17];
                for (var i = 0; i <= 16; i++)
                {
                    var u = i / 16.0;
                    samples[i] = new Point((1 - u) * (1 - u) * a.X + 2 * (1 - u) * u * control.X + u * u * tail.X,
                                           (1 - u) * (1 - u) * a.Y + 2 * (1 - u) * u * control.Y + u * u * tail.Y);
                }
                paths.Add((line, samples));
            }
        }
        _linePaths = paths;
    }

    private static readonly Dictionary<(double, double), StreamGeometry> Drops = new();

    /// <summary>A teardrop with its point at (0,0) and a round head of radius r centred <paramref name="rise"/> above it.</summary>
    private static StreamGeometry Teardrop(double r, double rise)
    {
        if (Drops.TryGetValue((r, rise), out var cached)) return cached;
        var cos = r / rise;
        var sin = Math.Sqrt(1 - cos * cos);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), isFilled: true);
            c.LineTo(new Point(r * sin, -rise + r * cos));
            c.ArcTo(new Point(-r * sin, -rise + r * cos), new Size(r, r), 0, isLargeArc: true, SweepDirection.CounterClockwise);
            c.EndFigure(true);
        }
        return Drops[(r, rise)] = g;
    }

    private void DrawMarkers(DrawingContext ctx)
    {
        if (_markers.Count == 0) return;
        var surface = Paint("Surface");
        var outline = new Pen(surface, 1.5);
        var leader = new Pen(WithAlpha(Paint("Muted"), 0.8), 1);
        var shadow = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0));
        var spotFill = Paint("Ink");

        // Fanned-out pins: a thin leader back to the real spot, and a dot on it.
        foreach (var group in _markers.Where(m => m.Tip != m.Spot).GroupBy(m => m.Cluster))
        {
            foreach (var m in group) ctx.DrawLine(leader, m.Spot, m.Tip);
            ctx.DrawEllipse(spotFill, outline, group.First().Spot, 3, 3);
        }

        Marker? hovered = null;
        foreach (var m in _markers)
        {
            if (ReferenceEquals(m, _hover)) { hovered = m; continue; }
            DrawMarker(ctx, m, 1, surface, outline, shadow);
        }
        if (hovered != null) DrawMarker(ctx, hovered, 1.18, surface, outline, shadow);
    }

    private void DrawMarker(DrawingContext ctx, Marker m, double scale, IBrush surface, IPen outline, IBrush shadow)
    {
        var fill = m.IsCount ? Paint("Accent") : KindBrush(m.Pin!.Kind);
        ctx.DrawEllipse(shadow, null, new Point(m.Tip.X, m.Tip.Y), 6 * scale, 2.2 * scale);
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(m.Tip.X, m.Tip.Y)))
        {
            ctx.DrawGeometry(fill, outline, Teardrop(m.Radius, m.Rise));
            if (!m.IsCount)
            {
                ctx.DrawEllipse(surface, null, new Point(0, -m.Rise), 3.4, 3.4);
                return;
            }
            var count = m.Cluster.Pins.Count;
            var t = Text(count > 99 ? "99+" : count.ToString(CultureInfo.CurrentCulture), count > 99 ? 9.5 : 11, Paint("OnAccent"), bold: true);
            ctx.DrawText(t, new Point(-t.Width / 2, -m.Rise - t.Height / 2));
        }
    }

    private void DrawLegend(DrawingContext ctx, Size view)
    {
        var kinds = _pins.Select(p => p.Kind).Distinct().OrderBy(x => x).ToList();
        if (kinds.Count == 0) return;
        var muted = Paint("Muted");
        var labels = kinds.Select(k => (Kind: k, Text: Text(k.ToString(), 11, muted))).ToList();
        var width = 16 + labels.Sum(l => 14 + l.Text.Width) + (labels.Count - 1) * 10;
        var height = 24.0;
        var r = new Rect(12, view.Height - height - 12, width, height);
        ctx.DrawRectangle(WithAlpha(Paint("Surface"), 0.92), new Pen(Paint("Line"), 1), r, 12, 12);
        var x = r.X + 10;
        foreach (var (kind, text) in labels)
        {
            ctx.DrawEllipse(KindBrush(kind), null, new Point(x + 4.5, r.Center.Y), 4.5, 4.5);
            ctx.DrawText(text, new Point(x + 14, r.Center.Y - text.Height / 2));
            x += 14 + text.Width + 10;
        }
    }

    private void DrawAttribution(DrawingContext ctx, Size view)
    {
        var t = Text(Attribution, 10.5, Paint("Muted"));
        var r = new Rect(view.Width - t.Width - 12, view.Height - t.Height - 4, t.Width + 12, t.Height + 4);
        ctx.DrawRectangle(WithAlpha(Paint("Surface"), 0.85), null, r, 4, 4);
        ctx.DrawText(t, new Point(r.X + 6, r.Y + 2));
    }

    private enum MapButton { None, ZoomIn, ZoomOut, Fit }

    private Rect ZoomInRect => new(Bounds.Width - 46, 12, 34, 34);
    private Rect ZoomOutRect => new(Bounds.Width - 46, 46, 34, 34);
    private Rect FitRect => new(Bounds.Width - 46, 88, 34, 34);
    private bool HasFit => _pins.Count > 0 || _lines.Count > 0;

    private MapButton ButtonAt(Point p) =>
        ZoomInRect.Contains(p) ? MapButton.ZoomIn
        : ZoomOutRect.Contains(p) ? MapButton.ZoomOut
        : HasFit && FitRect.Contains(p) ? MapButton.Fit
        : MapButton.None;

    private void DrawButtons(DrawingContext ctx, Size view)
    {
        var surface = Paint("Surface");
        var border = new Pen(Paint("Line"), 1);
        var hover = Paint("Hover");
        var ink = new Pen(Paint("Ink"), 1.6, lineCap: PenLineCap.Round);
        var off = new Pen(Paint("Faint"), 1.6, lineCap: PenLineCap.Round);

        var pill = new Rect(ZoomInRect.X, ZoomInRect.Y, 34, 68);
        ctx.DrawRectangle(surface, border, pill, 9, 9);
        if (_hover is MapButton.ZoomIn) ctx.DrawRectangle(hover, null, ZoomInRect.Deflate(3), 6, 6);
        if (_hover is MapButton.ZoomOut) ctx.DrawRectangle(hover, null, ZoomOutRect.Deflate(3), 6, 6);
        ctx.DrawLine(border, new Point(pill.X + 6, pill.Center.Y), new Point(pill.Right - 6, pill.Center.Y));
        var plus = ZoomInRect.Center;
        var p1 = Zoom < MaxZoom ? ink : off;
        ctx.DrawLine(p1, new Point(plus.X - 6, plus.Y), new Point(plus.X + 6, plus.Y));
        ctx.DrawLine(p1, new Point(plus.X, plus.Y - 6), new Point(plus.X, plus.Y + 6));
        var minus = ZoomOutRect.Center;
        ctx.DrawLine(Zoom > MinZoom ? ink : off, new Point(minus.X - 6, minus.Y), new Point(minus.X + 6, minus.Y));

        if (!HasFit) return;
        ctx.DrawRectangle(surface, border, FitRect, 9, 9);
        if (_hover is MapButton.Fit) ctx.DrawRectangle(hover, null, FitRect.Deflate(3), 6, 6);
        // Four corner brackets: "show everything".
        var f = FitRect.Center;
        foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var corner = new Point(f.X + sx * 7, f.Y + sy * 7);
            ctx.DrawLine(ink, corner, new Point(corner.X - sx * 4.5, corner.Y));
            ctx.DrawLine(ink, corner, new Point(corner.X, corner.Y - sy * 4.5));
        }
    }

    private void DrawTooltip(DrawingContext ctx, Size view)
    {
        string title, detail;
        Point anchor;
        switch (_hover)
        {
            case Marker { Pin: { } pin } m:
                (title, detail, anchor) = (pin.Label, pin.Detail, new Point(m.Tip.X, m.Head.Y - m.Radius - 6));
                break;
            case Marker m:
                var pins = m.Cluster.Pins;
                title = $"{pins.Count} places here";
                detail = string.Join("\n", pins.Take(5).Select(p => $"{p.Label} — {p.Detail}"));
                if (pins.Count > 5) detail += $"\n…and {pins.Count - 5} more. Click to zoom in.";
                anchor = new Point(m.Tip.X, m.Head.Y - m.Radius - 6);
                break;
            case MapLine line when line.Label.Length > 0:
                (title, detail, anchor) = (line.Label, "", new Point(_hoverAt.X, _hoverAt.Y - 12));
                break;
            default:
                return;
        }
        var t1 = Text(title, 12.5, Paint("Ink"), bold: true, maxWidth: 300);
        var t2 = detail.Length > 0 ? Text(detail, 11.5, Paint("Muted"), maxWidth: 300, maxLines: 7) : null;
        var w = Math.Max(t1.Width, t2?.Width ?? 0) + 20;
        var h = t1.Height + (t2?.Height ?? 0) + 14;
        var x = Math.Clamp(anchor.X - w / 2, 6, Math.Max(6, view.Width - w - 6));
        var y = anchor.Y - h;
        if (y < 6) y = anchor.Y + (_hover is Marker hm ? hm.Rise + hm.Radius + 18 : 24);   // no room above: show it below
        var r = new Rect(x, Math.Min(y, view.Height - h - 6), w, h);
        ctx.DrawRectangle(Paint("Surface"), new Pen(Paint("LineStrong"), 1), r, 8, 8);
        ctx.DrawText(t1, new Point(r.X + 10, r.Y + 7));
        if (t2 != null) ctx.DrawText(t2, new Point(r.X + 10, r.Y + 7 + t1.Height));
    }

    // ---- input ------------------------------------------------------------------------------------

    private object? _hover;           // a Marker, a MapLine or a MapButton
    private Point _hoverAt;
    private Point? _pressAt;
    private Point _lastDrag;
    private bool _dragging;
    private double _wheel;

    private Marker? MarkerAt(Point p)
    {
        for (var i = _markers.Count - 1; i >= 0; i--)
            if (_markers[i].Contains(p)) return _markers[i];
        return null;
    }

    private MapLine? LineAt(Point p)
    {
        foreach (var (line, path) in _linePaths)
            for (var i = 1; i < path.Length; i++)
                if (DistanceToSegment(p, path[i - 1], path[i]) <= 5) return line;
        return null;
    }

    private void UpdateHover(Point p)
    {
        object? hit = ButtonAt(p) is var b && b != MapButton.None ? b : MarkerAt(p) ?? (object?)LineAt(p);
        Cursor = new Cursor(hit is MapButton or Marker ? StandardCursorType.Hand : StandardCursorType.Arrow);
        var moved = hit is MapLine && Distance(p, _hoverAt) > 1;
        _hoverAt = p;
        if (Equals(hit, _hover) && !moved) return;
        _hover = hit;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        Focus();
        switch (ButtonAt(p))
        {
            case MapButton.ZoomIn: ZoomIn(); e.Handled = true; return;
            case MapButton.ZoomOut: ZoomOut(); e.Handled = true; return;
            case MapButton.Fit: FitToPins(); e.Handled = true; return;
        }
        _pressAt = _lastDrag = p;
        _dragging = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_pressAt is { } start)
        {
            if (!_dragging && Distance(start, p) > 4)
            {
                _dragging = true;
                _hover = null;
                Cursor = new Cursor(StandardCursorType.SizeAll);
            }
            if (_dragging)
            {
                PanBy(p - _lastDrag);
                _lastDrag = p;
            }
            return;
        }
        UpdateHover(p);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressAt == null) return;
        var dragged = _dragging;
        _pressAt = null;
        _dragging = false;
        e.Pointer.Capture(null);
        var p = e.GetPosition(this);
        if (!dragged) Click(p);
        UpdateHover(p);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressAt = null;
        _dragging = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_pressAt != null || _hover == null) return;
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _wheel += e.Delta.Y;
        if (Math.Abs(_wheel) >= 1)
        {
            var steps = Math.Truncate(_wheel);
            _wheel -= steps;
            ZoomAround(e.GetPosition(this), Math.Round(Zoom) + steps);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus: ZoomIn(); break;
            case Key.Subtract or Key.OemMinus: ZoomOut(); break;
            case Key.Left: PanBy(new Vector(80, 0)); break;
            case Key.Right: PanBy(new Vector(-80, 0)); break;
            case Key.Up: PanBy(new Vector(0, 80)); break;
            case Key.Down: PanBy(new Vector(0, -80)); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Click(Point p)
    {
        if (MarkerAt(p) is not { } m) return;
        if (m.Pin is { PersonId: { } id })
        {
            if (PinClickCommand?.CanExecute(id) == true) PinClickCommand.Execute(id);
        }
        else if (m.IsCount && m.Cluster.Pins.Select(x => x.Location).Distinct().Count() > 1)
            ZoomAround(m.Tip, Math.Round(Zoom) + 2);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TileCache.Arrived += OnTileArrived;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TileCache.Arrived -= OnTileArrived;
    }

    private void OnTileArrived() => InvalidateVisual();

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var d = b - a;
        var len2 = d.X * d.X + d.Y * d.Y;
        var t = len2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / len2, 0, 1);
        return Distance(p, new Point(a.X + t * d.X, a.Y + t * d.Y));
    }
}

/// <summary>A map tile address: zoom, column, row.</summary>
internal readonly record struct TileKey(int Z, int X, int Y);

/// <summary>
/// Tiles shared by every map: an in-memory LRU of decoded bitmaps, a 30-day disk cache under
/// %LocalAppData%\LineageRoots\tiles, and at most two downloads at once with a proper User-Agent,
/// as the OpenStreetMap tile usage policy requires. Requests the user has panned away from are dropped
/// before they download. Everything except the file and network work runs on the UI thread.
/// </summary>
internal static class TileCache
{
    public const string UserAgent = "LineageRoots/0.1 (genealogy desktop app)";
    private const int Capacity = 320;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30), RetryAfter = TimeSpan.FromMinutes(1);

    private static readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, Bitmap Image)>> Map = new();
    private static readonly LinkedList<(TileKey Key, Bitmap Image)> Recent = new();
    private static readonly HashSet<TileKey> Pending = new();
    private static readonly Dictionary<TileKey, DateTime> Failed = new();
    private static readonly ConcurrentDictionary<TileKey, long> Wanted = new();
    private static readonly SemaphoreSlim Downloads = new(2);
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 2, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    });

    internal static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LineageRoots", "tiles");

    /// <summary>Tests stand in for the network here; nothing else should set it.</summary>
    internal static Func<TileKey, CancellationToken, Task<byte[]?>>? Downloader { get; set; }

    internal static int InFlight => Pending.Count;
    internal static int DownloadCount => _downloads;
    private static int _downloads;

    /// <summary>Raised on the UI thread when a tile has arrived (or failed), so maps can redraw.</summary>
    public static event Action? Arrived;

    public static Bitmap? Get(TileKey key)
    {
        if (!Map.TryGetValue(key, out var node)) return null;
        Recent.Remove(node);
        Recent.AddFirst(node);
        return node.Value.Image;
    }

    public static void Request(TileKey key)
    {
        Wanted[key] = Environment.TickCount64;
        if (!MapView.AllowOnlineTiles || Map.ContainsKey(key) || Pending.Contains(key)) return;
        if (Failed.TryGetValue(key, out var when) && DateTime.UtcNow - when < RetryAfter) return;
        Pending.Add(key);
        _ = Task.Run(() => LoadAsync(key));
    }

    internal static void Clear()
    {
        foreach (var (_, image) in Recent) image.Dispose();
        Recent.Clear();
        Map.Clear();
        Failed.Clear();
        Wanted.Clear();
    }

    private static bool StillWanted(TileKey key) =>
        Wanted.TryGetValue(key, out var at) && Environment.TickCount64 - at < 8000;

    private static async Task LoadAsync(TileKey key)
    {
        Bitmap? image = null;
        var failed = false;
        try
        {
            var file = Path.Combine(Folder, key.Z.ToString(CultureInfo.InvariantCulture), key.X.ToString(CultureInfo.InvariantCulture),
                                    key.Y.ToString(CultureInfo.InvariantCulture) + ".png");
            var info = new FileInfo(file);
            byte[]? data = null;
            if (info.Exists && info.Length > 0 && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge)
                data = await File.ReadAllBytesAsync(file);
            else if (StillWanted(key))
            {
                await Downloads.WaitAsync();
                try { if (StillWanted(key)) data = await DownloadAsync(key); }
                finally { Downloads.Release(); }
                if (data != null) Save(file, data);
                else if (info.Exists && info.Length > 0) data = await File.ReadAllBytesAsync(file);   // stale beats nothing
                failed = data == null && StillWanted(key);
            }
            if (data != null)
            {
                using var ms = new MemoryStream(data);
                image = new Bitmap(ms);
            }
        }
        catch (Exception)
        {
            failed = true;      // offline, refused, or a broken file: the grid shows through
        }
        Dispatcher.UIThread.Post(() => Complete(key, image, failed));
    }

    private static async Task<byte[]?> DownloadAsync(TileKey key)
    {
        if (!MapView.AllowOnlineTiles) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Interlocked.Increment(ref _downloads);
        if (Downloader is { } fake) return await fake(key, timeout.Token);
        var url = $"https://tile.openstreetmap.org/{key.Z}/{key.X}/{key.Y}.png";
        using var response = await Http.Value.GetAsync(url, timeout.Token);
        if (!response.IsSuccessStatusCode) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        return bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' ? bytes : null;
    }

    private static void Save(string file, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, file, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Complete(TileKey key, Bitmap? image, bool failed)
    {
        Pending.Remove(key);
        if (image != null)
        {
            Failed.Remove(key);
            if (Map.Remove(key, out var old)) { Recent.Remove(old); old.Value.Image.Dispose(); }
            Map[key] = Recent.AddFirst((key, image));
            while (Recent.Count > Capacity && Recent.Last is { } last)
            {
                Recent.RemoveLast();
                Map.Remove(last.Value.Key);
                last.Value.Image.Dispose();
            }
        }
        else if (failed) Failed[key] = DateTime.UtcNow;
        Arrived?.Invoke();
    }
}
