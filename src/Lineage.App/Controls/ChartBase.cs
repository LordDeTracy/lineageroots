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
    public static readonly StyledProperty<bool> ShowEvidenceProperty =
        AvaloniaProperty.Register<ChartBase, bool>(nameof(ShowEvidence));

    static ChartBase()
    {
        AffectsMeasure<ChartBase>(DataProperty);
        AffectsRender<ChartBase>(ShowEvidenceProperty);
    }

    /// <summary>Colour each person by how well their birth and death are proven, instead of by sex.</summary>
    public bool ShowEvidence { get => GetValue(ShowEvidenceProperty); set => SetValue(ShowEvidenceProperty, value); }

    /// <summary>The theme brush keys (strong ink, soft background) for a person's evidence level.</summary>
    public static (string Ink, string Soft) EvidenceKeys(Lineage.Core.Analysis.ProofLevel? level) => level switch
    {
        Lineage.Core.Analysis.ProofLevel.Proven or Lineage.Core.Analysis.ProofLevel.Strong => ("Accent", "AccentSoft"),
        Lineage.Core.Analysis.ProofLevel.Good => ("MaleInk", "MaleBg"),
        Lineage.Core.Analysis.ProofLevel.Weak => ("WarnInk", "WarnBg"),
        Lineage.Core.Analysis.ProofLevel.Conflicting => ("Danger", "DangerBg"),
        _ => ("Faint", "OtherBg"),
    };

    /// <summary>The bar colour for a person: their sex, or how well proven they are when <see cref="ShowEvidence"/> is on.</summary>
    protected IBrush PersonBar(Person p) => ShowEvidence && Data != null
        ? Brush(EvidenceKeys(Lineage.Core.Analysis.Evidence.PersonLevel(Data.Tree, p)).Ink)
        : p.Sex switch { Sex.Male => Brush("MaleInk"), Sex.Female => Brush("FemaleInk"), _ => Brush("OtherInk") };

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
        ToolTip.SetTip(this, hit is { } id && Data?.Tree.Person(id) is { } p ? string.Join("\n", new[] { p.DisplayName, p.Lifespan, p.Birth?.Place ?? "", p.Death?.Place ?? "", "Click to centre, double-click to open" }.Where(s => s.Length > 0)) : null);
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

    /// <summary>
    /// A person's box text: given names (two lines if they're long), the surname in bold on its own line, the years,
    /// then where they were born and died.
    /// </summary>
    protected static FormattedText[] PersonLines(Person p, double width, IBrush ink, IBrush muted)
    {
        var given = p.PrimaryName.Given.Trim();
        var surname = $"{p.PrimaryName.Surname} {p.PrimaryName.Suffix}".Trim();
        var lines = new List<FormattedText>();
        if (given.Length > 0 && surname.Length > 0)
        {
            lines.Add(Text(given, 13, ink, maxWidth: width, maxLines: 2));
            lines.Add(Text(surname, 13.5, ink, bold: true, maxWidth: width));
        }
        else lines.Add(Text(p.DisplayName, 13.5, ink, bold: true, maxWidth: width, maxLines: 2));
        if (Years(p).Length > 0) lines.Add(Text(Years(p), 11.5, muted, maxWidth: width));
        if (Places(p) is { Length: > 0 } places) lines.Add(Text(places, 11, muted, maxWidth: width));
        return lines.ToArray();
    }

    /// <summary>"b. Harrow  ·  d. Worcester": the first part of the birth and death places.</summary>
    protected static string Places(Person p)
    {
        static string Town(string? place) => place?.Split(',')[0].Trim() ?? "";
        var (born, died) = (Town(p.Birth?.Place), Town(p.Death?.Place));
        return string.Join("  ·  ", new[]
        {
            born.Length > 0 ? Lineage.App.Services.Translator.T("b. {0}").Replace("{0}", born) : "",
            died.Length > 0 ? Lineage.App.Services.Translator.T("d. {0}").Replace("{0}", died) : "",
        }.Where(s => s.Length > 0));
    }

    /// <summary>Draws lines of text one under another, centred on <paramref name="centreY"/>.</summary>
    protected static void DrawLines(DrawingContext ctx, FormattedText[] lines, double x, double centreY)
    {
        var y = centreY - lines.Sum(l => l.Height) / 2;
        foreach (var line in lines)
        {
            ctx.DrawText(line, new Point(x, y));
            y += line.Height;
        }
    }

    protected static FormattedText Text(string text, double size, IBrush brush, bool bold = false, double maxWidth = 0, int maxLines = 1)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                   bold ? Semibold : Regular, size, brush);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = maxLines;
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
