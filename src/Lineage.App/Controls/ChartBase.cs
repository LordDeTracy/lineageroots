using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lineage.App.ViewModels;
using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>
/// Shared plumbing for the pedigree and fan charts: the data, theme colours, text drawing,
/// hover highlighting, click to re-centre (NavigateCommand) and double-click to open (OpenCommand).
/// </summary>
public abstract class ChartBase : Control
{
    public static readonly StyledProperty<ChartData?> DataProperty =
        AvaloniaProperty.Register<ChartBase, ChartData?>(nameof(Data));
    public static readonly StyledProperty<ICommand?> NavigateCommandProperty =
        AvaloniaProperty.Register<ChartBase, ICommand?>(nameof(NavigateCommand));
    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<ChartBase, ICommand?>(nameof(OpenCommand));

    static ChartBase() => AffectsMeasure<ChartBase>(DataProperty);

    protected ChartBase()
    {
        ClipToBounds = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        DoubleTapped += (_, e) =>
        {
            if (HitTest(e.GetPosition(this)) is { } id) OpenCommand?.Execute(id);
        };
    }

    public ChartData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public ICommand? NavigateCommand { get => GetValue(NavigateCommandProperty); set => SetValue(NavigateCommandProperty, value); }
    public ICommand? OpenCommand { get => GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }

    /// <summary>How far (in screen pixels) the pointer must move before a press becomes a drag (pan), not a click.</summary>
    public const double DragThreshold = 5;

    private long? _hover;
    protected long? Hover => _hover;
    private Point? _pressedAt;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressedAt = e.GetPosition(TopLevel.GetTopLevel(this));
    }

    /// <summary>The person under a point, if any.</summary>
    protected abstract long? HitTest(Point p);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit == _hover) return;
        _hover = hit;
        Cursor = new Cursor(hit != null ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, hit is { } id && Data?.Tree.Person(id) is { } p ? $"{p.DisplayName}\n{p.Lifespan}\nClick to centre, double-click to open" : null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var moved = _pressedAt is { } at && e.GetPosition(TopLevel.GetTopLevel(this)) is var now
                    && Math.Abs(now.X - at.X) + Math.Abs(now.Y - at.Y) >= DragThreshold;
        _pressedAt = null;
        if (moved) return; // that was a pan, not a click
        if (e.InitialPressMouseButton == MouseButton.Left && HitTest(e.GetPosition(this)) is { } id && id != Data?.RootId)
            NavigateCommand?.Execute(id);
    }


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty) { _hover = null; InvalidateVisual(); }
    }

    // ---- drawing helpers ------------------------------------------------------------------------

    protected IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    protected Color Colour(string key) => Brush(key) is ISolidColorBrush s ? s.Color : Colors.Gray;

    private static readonly Typeface Regular = new(FontFamily.Default);
    private static readonly Typeface Semibold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    protected static FormattedText Text(string text, double size, IBrush brush, bool bold = false, double maxWidth = 0)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                   bold ? Semibold : Regular, size, brush);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
        }
        return ft;
    }

    protected static string Years(Person p) => p.Lifespan;

    /// <summary>Renders the whole chart (not just what's visible) to a PNG.</summary>
    public void SaveImage(string path)
    {
        var size = DesiredSize;
        var scale = 2.0;
        using var bmp = new RenderTargetBitmap(new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)),
                                               new Vector(96 * scale, 96 * scale));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.FillRectangle(Brush("Bg"), new Rect(size));
            Render(ctx);
        }
        bmp.Save(path);
    }
}
