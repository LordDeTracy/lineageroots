using Avalonia;
using Avalonia.Media;
using Lineage.Core.Charts;

namespace Lineage.App.Controls;

/// <summary>
/// Lifelines: the person's family across time — great-grandparents to grandchildren, siblings and spouses — each a bar
/// from birth to death on a scale of years, so it shows at a glance who was alive together. An end that isn't recorded
/// is estimated and drawn faded.
/// </summary>
public sealed class LifelinesChart : ChartBase
{
    private const double LabelWidth = 250, RowHeight = 34, Top = 40, YearWidth = 7, Pad = 16;
    public static readonly StyledProperty<LifelineScope> ScopeProperty =
        AvaloniaProperty.Register<LifelinesChart, LifelineScope>(nameof(Scope));

    static LifelinesChart()
    {
        AffectsMeasure<LifelinesChart>(ScopeProperty);
        AffectsRender<LifelinesChart>(ScopeProperty);
    }

    /// <summary>Who to show: the close family, one side's ancestors, or the descendants.</summary>
    public LifelineScope Scope { get => GetValue(ScopeProperty); set => SetValue(ScopeProperty, value); }

    private List<LifeRow> _rows = new();
    private int _first, _last;

    protected override Size MeasureOverride(Size availableSize)
    {
        _rows = Data is { } d ? MoreCharts.Lifelines(d.Tree, d.RootId, scope: Scope) : new();
        if (_rows.Count == 0) return new Size(400, 80);
        _first = _rows.Min(r => r.From) / 10 * 10;
        _last = (_rows.Max(r => r.To) / 10 + 1) * 10;
        return new Size(LabelWidth + (_last - _first) * YearWidth + Pad * 2, Top + _rows.Count * RowHeight + Pad);
    }

    private double X(int year) => Pad + LabelWidth + (year - _first) * YearWidth;

    public override void Render(DrawingContext ctx)
    {
        if (Data == null) return;
        if (_rows.Count == 0)
        {
            ctx.DrawText(Text("No dates recorded for these people yet.", 13, Brush("Muted")), new Point(Pad, Pad));
            return;
        }
        var (average, counted) = MoreCharts.Longevity(_rows);
        if (counted > 0)
            ctx.DrawText(Text(Lineage.App.Services.Translator.T("Average age at death: {0} ({1} people)")
                                  .Replace("{0}", average.ToString("0")).Replace("{1}", counted.ToString()), 11.5, Brush("Muted"), maxWidth: LabelWidth - 12),
                         new Point(Pad, Top - 26));
        var line = new Pen(Brush("Line"), 1);
        // The scale: a faint line every ten years, labelled every fifty (or twenty-five on a short span).
        var labelEvery = _last - _first > 200 ? 50 : 25;
        for (var year = _first; year <= _last; year += 10)
        {
            ctx.DrawLine(line, new Point(X(year), Top - 6), new Point(X(year), Top + _rows.Count * RowHeight));
            if (year % labelEvery == 0)
            {
                var t = Text(year.ToString(), 11, Brush("Muted"));
                ctx.DrawText(t, new Point(X(year) - t.Width / 2, Top - 24));
            }
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var y = Top + i * RowHeight;
            var person = Data.Tree.Person(row.PersonId);
            if (Hover == row.PersonId || row.IsRoot)
                ctx.DrawRectangle(Brush(row.IsRoot ? "AccentSoft" : "Hover"), null, new Rect(Pad - 6, y, Bounds.Width - Pad * 2 + 12, RowHeight - 2), 6, 6);
            var name = Text(row.Name, 12.5, Brush("Ink"), bold: row.IsRoot, maxWidth: LabelWidth - 12);
            ctx.DrawText(name, new Point(Pad, y + 3));
            if (row.Relation.Length > 0)
                ctx.DrawText(Text(row.Relation, 10.5, Brush("Muted"), maxWidth: LabelWidth - 12), new Point(Pad, y + 3 + name.Height));

            var bar = person != null ? PersonBar(person) : Brush("OtherInk");
            var x1 = X(row.From);
            var x2 = Math.Max(X(row.To), x1 + 4);
            var r = new Rect(x1, y + 10, x2 - x1, RowHeight - 22);
            ctx.DrawRectangle(new SolidColorBrush(((ISolidColorBrush)bar).Color, 0.85), null, r, 4, 4);
            // Estimated ends fade out (or in) over the last stretch.
            const double fade = 40;
            if (row.ToEstimated) ctx.DrawRectangle(Fade(true), null, new Rect(Math.Max(x1, x2 - fade), r.Y, Math.Min(fade, x2 - x1), r.Height));
            if (row.FromEstimated) ctx.DrawRectangle(Fade(false), null, new Rect(x1, r.Y, Math.Min(fade, x2 - x1), r.Height));
            var years = Text($"{(row.FromEstimated ? "?" : row.From.ToString())}–{(row.ToEstimated ? "" : row.To.ToString())}", 10.5, Brush("Muted"));
            ctx.DrawText(years, new Point(x2 + 6, y + (RowHeight - years.Height) / 2 - 1));
        }
    }

    /// <summary>The page colour, clear at one end and solid at the other, laid over a bar's estimated end.</summary>
    private IBrush Fade(bool atEnd)
    {
        var bg = Colour("Bg");
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(atEnd ? Color.FromArgb(0, bg.R, bg.G, bg.B) : Color.FromArgb(200, bg.R, bg.G, bg.B), 0),
                new GradientStop(atEnd ? Color.FromArgb(200, bg.R, bg.G, bg.B) : Color.FromArgb(0, bg.R, bg.G, bg.B), 1),
            },
        };
    }

    protected override long? HitTest(Point p)
    {
        var i = (int)Math.Floor((p.Y - Top) / RowHeight);
        return p.Y >= Top && i >= 0 && i < _rows.Count ? _rows[i].PersonId : null;
    }
}
