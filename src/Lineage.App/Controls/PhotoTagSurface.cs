using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Lineage.App.Controls;

/// <summary>A labelled rectangle over a photo, in coordinates normalised to the picture (0..1, top-left origin).</summary>
public interface ITaggedRegion
{
    Rect Bounds { get; }
    string Label { get; }
}

/// <summary>
/// Shows a photo fitted to the available space with a rectangle and name label for each tagged
/// person, and lets the user drag out a new rectangle while <see cref="IsTagging"/> is on. Drawing
/// the photo and the tags in one control keeps the rectangles exactly on the picture at any size.
/// </summary>
public sealed class PhotoTagSurface : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<PhotoTagSurface, IImage?>(nameof(Source));
    public static readonly StyledProperty<IEnumerable<ITaggedRegion>?> RegionsProperty =
        AvaloniaProperty.Register<PhotoTagSurface, IEnumerable<ITaggedRegion>?>(nameof(Regions));
    /// <summary>The region drawn emphasised; set from outside (a list) or by hovering a rectangle.</summary>
    public static readonly StyledProperty<ITaggedRegion?> HighlightedProperty =
        AvaloniaProperty.Register<PhotoTagSurface, ITaggedRegion?>(nameof(Highlighted), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsTaggingProperty =
        AvaloniaProperty.Register<PhotoTagSurface, bool>(nameof(IsTagging));
    /// <summary>The rectangle being drawn, normalised; set by dragging while tagging.</summary>
    public static readonly StyledProperty<Rect?> PendingProperty =
        AvaloniaProperty.Register<PhotoTagSurface, Rect?>(nameof(Pending), defaultBindingMode: BindingMode.TwoWay);
    /// <summary>Faces found but not tagged, normalised: drawn dashed, and clicking one runs <see cref="SuggestionCommand"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<Rect>?> SuggestionsProperty =
        AvaloniaProperty.Register<PhotoTagSurface, IReadOnlyList<Rect>?>(nameof(Suggestions));
    public static readonly StyledProperty<System.Windows.Input.ICommand?> SuggestionCommandProperty =
        AvaloniaProperty.Register<PhotoTagSurface, System.Windows.Input.ICommand?>(nameof(SuggestionCommand));
    public static readonly StyledProperty<IBrush> HighlightBrushProperty =
        AvaloniaProperty.Register<PhotoTagSurface, IBrush>(nameof(HighlightBrush), new SolidColorBrush(Color.Parse("#8FD3BA")));

    private static readonly IBrush LabelBackground = new SolidColorBrush(Color.FromArgb(200, 18, 20, 22));
    private static readonly IBrush LabelInk = Brushes.White;
    private static readonly IBrush DarkInk = new SolidColorBrush(Color.Parse("#10241D"));
    private static readonly IPen Shadow = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 3.5);
    private static readonly IPen Outline = new Pen(Brushes.White, 1.5);
    private static readonly IBrush PendingFill = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
    private static readonly IPen SuggestionPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.5,
                                                         new DashStyle(new double[] { 3, 3 }, 0));
    private static readonly Typeface LabelFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    /// <summary>Drags shorter than this are clicks: they place a box of a typical face size.</summary>
    private const double MinDrag = 10;

    private INotifyCollectionChanged? _watched;
    private Point? _dragFrom;
    private bool _hoverSetHighlight;

    static PhotoTagSurface()
    {
        AffectsRender<PhotoTagSurface>(SourceProperty, HighlightedProperty, IsTaggingProperty, PendingProperty, HighlightBrushProperty,
                                       SuggestionsProperty);
        ClipToBoundsProperty.OverrideDefaultValue<PhotoTagSurface>(true);
    }

    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IEnumerable<ITaggedRegion>? Regions { get => GetValue(RegionsProperty); set => SetValue(RegionsProperty, value); }
    public ITaggedRegion? Highlighted { get => GetValue(HighlightedProperty); set => SetValue(HighlightedProperty, value); }
    public bool IsTagging { get => GetValue(IsTaggingProperty); set => SetValue(IsTaggingProperty, value); }
    public Rect? Pending { get => GetValue(PendingProperty); set => SetValue(PendingProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }
    public IReadOnlyList<Rect>? Suggestions { get => GetValue(SuggestionsProperty); set => SetValue(SuggestionsProperty, value); }
    public System.Windows.Input.ICommand? SuggestionCommand { get => GetValue(SuggestionCommandProperty); set => SetValue(SuggestionCommandProperty, value); }

    /// <summary>The untagged face under a point in the view, if any (the smallest, where they overlap).</summary>
    private Rect? SuggestionAt(Point at, Rect image) =>
        (Suggestions ?? Array.Empty<Rect>()).Where(r => ToView(r, image).Inflate(4).Contains(at))
            .OrderBy(r => r.Width * r.Height).Select(r => (Rect?)r).FirstOrDefault();

    /// <summary>Where the photo is drawn: as large as fits, centred.</summary>
    public Rect ImageRect
    {
        get
        {
            var size = Source?.Size ?? default;
            if (size.Width <= 0 || size.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return default;
            var scale = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
            var (w, h) = (size.Width * scale, size.Height * scale);
            return new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RegionsProperty)
        {
            if (_watched != null) _watched.CollectionChanged -= OnRegionsChanged;
            _watched = Regions as INotifyCollectionChanged;
            if (_watched != null) _watched.CollectionChanged += OnRegionsChanged;
            InvalidateVisual();
        }
        else if (change.Property == IsTaggingProperty)
        {
            Cursor = IsTagging ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
            _dragFrom = null;
        }
    }

    private void OnRegionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // makes the whole area hit-testable
        var image = ImageRect;
        if (Source is not { } source || image.Width <= 0) return;
        context.DrawImage(source, new Rect(source.Size), image);

        var pendingNow = IsTagging ? Pending : null;
        foreach (var face in Suggestions ?? Array.Empty<Rect>())
        {
            if (pendingNow is { } pr && pr.Intersects(face)) continue;
            context.DrawRectangle(null, SuggestionPen, new RoundedRect(ToView(face, image), 4));
        }

        var lit = Highlighted;
        var regions = (Regions ?? Enumerable.Empty<ITaggedRegion>()).ToList();
        foreach (var r in regions.Where(r => !ReferenceEquals(r, lit))) DrawRegion(context, image, r, false);
        if (lit != null && regions.Contains(lit)) DrawRegion(context, image, lit, true);   // on top

        if (IsTagging && Pending is { } pending)
        {
            var rect = new RoundedRect(ToView(pending, image), 4);
            context.DrawRectangle(PendingFill, Shadow, rect);
            context.DrawRectangle(null, new Pen(HighlightBrush, 2, new DashStyle(new double[] { 4, 3 }, 0)), rect);
        }
    }

    private void DrawRegion(DrawingContext context, Rect image, ITaggedRegion region, bool lit)
    {
        var rect = ToView(region.Bounds, image);
        var rounded = new RoundedRect(rect, 4);
        if (lit)
        {
            context.DrawRectangle(new SolidColorBrush(Colors.White, 0.12), new Pen(HighlightBrush, 2.5), rounded);
        }
        else
        {
            context.DrawRectangle(null, Shadow, rounded);
            context.DrawRectangle(null, Outline, rounded);
        }
        if (region.Label.Length == 0) return;

        var text = new FormattedText(region.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 12,
                                     lit ? DarkInk : LabelInk)
        {
            MaxTextWidth = Math.Max(40, image.Width - 20), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis,
        };
        var (w, h) = (text.Width + 14, text.Height + 6);
        var x = Math.Clamp(rect.X, image.X + 2, Math.Max(image.X + 2, image.Right - w - 2));
        var y = rect.Bottom + 4;
        if (y + h > image.Bottom - 2) y = rect.Y - h - 4;         // no room below: above
        if (y < image.Y + 2) y = rect.Bottom - h - 4;             // no room either: inside, at the bottom
        context.DrawRectangle(lit ? HighlightBrush : LabelBackground, null, new RoundedRect(new Rect(x, y, w, h), 5));
        context.DrawText(text, new Point(x + 7, y + 3));
    }

    private static Rect ToView(Rect n, Rect image) =>
        new(image.X + n.X * image.Width, image.Y + n.Y * image.Height, n.Width * image.Width, n.Height * image.Height);

    private static Rect ToNormal(Rect v, Rect image)
    {
        var x = Math.Clamp((v.X - image.X) / image.Width, 0, 1);
        var y = Math.Clamp((v.Y - image.Y) / image.Height, 0, 1);
        return new Rect(x, y, Math.Clamp(v.Width / image.Width, 0, 1 - x), Math.Clamp(v.Height / image.Height, 0, 1 - y));
    }

    private static Point Clamp(Point p, Rect r) => new(Math.Clamp(p.X, r.X, r.Right), Math.Clamp(p.Y, r.Y, r.Bottom));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var image = ImageRect;
        var at = e.GetPosition(this);
        if (!IsTagging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && SuggestionAt(at, image) is { } face
            && SuggestionCommand is { } command && command.CanExecute(face))
        {
            command.Execute(face);
            e.Handled = true;
            return;
        }
        if (!IsTagging || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !image.Contains(at)) return;
        _dragFrom = at;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var image = ImageRect;
        var at = e.GetPosition(this);
        if (_dragFrom is { } from)
        {
            var to = Clamp(at, image);
            if (Math.Abs(to.X - from.X) >= MinDrag || Math.Abs(to.Y - from.Y) >= MinDrag)
                SetCurrentValue(PendingProperty, ToNormal(Box(from, to), image));
            e.Handled = true;
            return;
        }
        if (image.Width <= 0) return;
        Cursor = IsTagging ? new Cursor(StandardCursorType.Cross)
               : SuggestionAt(at, image) != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        if (IsTagging) return;
        // Hovering a rectangle highlights it (and its name in the list).
        var hit = (Regions ?? Enumerable.Empty<ITaggedRegion>())
            .Where(r => ToView(r.Bounds, image).Contains(at))
            .OrderBy(r => r.Bounds.Width * r.Bounds.Height).FirstOrDefault();
        if (hit != null || _hoverSetHighlight)
        {
            _hoverSetHighlight = hit != null;
            if (!ReferenceEquals(hit, Highlighted)) SetCurrentValue(HighlightedProperty, hit);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is not { } from) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        var image = ImageRect;
        if (image.Width <= 0) return;
        var to = Clamp(e.GetPosition(this), image);
        var box = Box(from, to);
        if (box.Width < MinDrag && box.Height < MinDrag && SuggestionAt(from, image) is { } face && SuggestionCommand is { } command)
        {
            command.Execute(face);   // a click on a face found: tag that face
            return;
        }
        if (box.Width < MinDrag && box.Height < MinDrag)
        {
            // A click: a square about the size of a face in a group photo, centred where clicked.
            var side = Math.Min(image.Width, image.Height) * 0.2;
            box = new Rect(Math.Clamp(from.X - side / 2, image.X, image.Right - side),
                           Math.Clamp(from.Y - side / 2, image.Y, image.Bottom - side), side, side);
        }
        SetCurrentValue(PendingProperty, ToNormal(box, image));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_hoverSetHighlight) return;
        _hoverSetHighlight = false;
        SetCurrentValue(HighlightedProperty, null);
    }

    private static Rect Box(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
