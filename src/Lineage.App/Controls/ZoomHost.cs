using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lineage.App.Controls;

/// <summary>
/// A scroll viewer for charts: the mouse wheel zooms in and out around the pointer (rather than
/// scrolling), dragging empty space pans, and the scroll bars still work. The chart is laid out at
/// its natural size and scaled, so clicks and hover keep working at any zoom.
/// </summary>
public sealed class ZoomHost : ScrollViewer
{
    public const double MinZoom = 0.2, MaxZoom = 3;
    private const double WheelStep = 1.15;

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ZoomHost, double>(nameof(Zoom), 1.0, coerce: (_, z) => Math.Clamp(z, MinZoom, MaxZoom));

    private readonly LayoutTransformControl _scaler = new();
    private bool _wrapping;
    private Point? _dragStart;
    private Vector _offsetAtDragStart;
    private bool _dragging;

    public ZoomHost()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _scaler.LayoutTransform = new ScaleTransform(1, 1);
        // Catch the wheel on the way down, before the inner scroll presenter turns it into scrolling.
        AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    /// <summary>"100%" style text for a zoom label.</summary>
    public string ZoomText => $"{Zoom * 100:0}%";

    /// <summary>The chart inside (whatever was given as content).</summary>
    public Control? Chart => _scaler.Child;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty && !_wrapping && change.NewValue is Control chart && chart != _scaler)
        {
            // Put whatever chart we're given inside the scaling layer.
            _wrapping = true;
            Content = null;           // release the chart before giving it a new parent
            _scaler.Child = chart;
            Content = _scaler;
            _wrapping = false;
        }
        else if (change.Property == ZoomProperty)
        {
            var z = (double)change.NewValue!;
            _scaler.LayoutTransform = new ScaleTransform(z, z);
        }
    }

    /// <summary>Sets the zoom keeping the content point under <paramref name="anchor"/> (viewport coordinates) still.</summary>
    public void ZoomAround(Point anchor, double zoom)
    {
        var old = Zoom;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(zoom - old) < 0.0001) return;
        var contentPoint = (anchor + Offset) / old;
        Zoom = zoom;
        UpdateLayout();
        Offset = contentPoint * zoom - anchor;
    }

    private Point ViewportCentre => new(Viewport.Width / 2, Viewport.Height / 2);

    public void ZoomIn() => ZoomAround(ViewportCentre, Zoom * 1.25);
    public void ZoomOut() => ZoomAround(ViewportCentre, Zoom / 1.25);

    public void ResetZoom() => ZoomAround(ViewportCentre, 1);

    /// <summary>Zooms so the whole chart fits the visible area (never above 100%).</summary>
    public void FitToView()
    {
        if (Chart is not { } chart || Viewport.Width <= 0 || Viewport.Height <= 0) return;
        chart.Measure(Size.Infinity);
        var size = chart.DesiredSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        Zoom = Math.Min(1, Math.Min(Viewport.Width / size.Width, Viewport.Height / size.Height) * 0.97);
        UpdateLayout();
        Offset = new Vector(Math.Max(0, (Extent.Width - Viewport.Width) / 2), Math.Max(0, (Extent.Height - Viewport.Height) / 2));
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        // The wheel zooms; Shift+wheel still scrolls sideways for people who like that.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        var steps = e.Delta.Y;
        if (steps == 0) return;
        ZoomAround(e.GetPosition(this), Zoom * Math.Pow(WheelStep, steps));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed) return;
        _dragStart = e.GetPosition(this);
        _offsetAtDragStart = Offset;
        _dragging = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start) return;
        var delta = e.GetPosition(this) - start;
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < ChartBase.DragThreshold) return;
        if (!_dragging)
        {
            _dragging = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        Offset = _offsetAtDragStart - delta;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging) e.Pointer.Capture(null);
        _dragStart = null;
        _dragging = false;
        Cursor = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
        _dragging = false;
        Cursor = null;
    }
}
