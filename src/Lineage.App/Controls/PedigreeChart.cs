using Avalonia;
using Avalonia.Media;
using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>
/// The classic pedigree: the person on the left, parents to the right, grandparents beyond,
/// joined by elbow lines. Unknown ancestors show as faint empty boxes so the gaps are visible.
/// </summary>
public sealed class PedigreeChart : ChartBase
{
    public static readonly StyledProperty<int> GenerationsProperty =
        AvaloniaProperty.Register<PedigreeChart, int>(nameof(Generations), 4);

    static PedigreeChart() => AffectsMeasure<PedigreeChart>(GenerationsProperty);

    public int Generations { get => GetValue(GenerationsProperty); set => SetValue(GenerationsProperty, value); }

    private const double BoxW = 236, BoxH = 58, ColGap = 44, RowGap = 12, Pad = 24;
    private readonly Dictionary<int, Rect> _boxes = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GenerationsProperty) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var g = Math.Clamp(Generations, 2, 6);
        var slots = 1 << (g - 1);
        return new Size(Pad * 2 + g * BoxW + (g - 1) * ColGap, Pad * 2 + slots * (BoxH + RowGap) - RowGap);
    }

    private void Layout()
    {
        _boxes.Clear();
        var g = Math.Clamp(Generations, 2, 6);
        var height = (1 << (g - 1)) * (BoxH + RowGap) - RowGap;
        for (var gen = 0; gen < g; gen++)
        {
            var count = 1 << gen;
            var slot = height / count;
            for (var i = 0; i < count; i++)
            {
                var x = Pad + gen * (BoxW + ColGap);
                var y = Pad + slot * i + (slot - BoxH) / 2;
                _boxes[count + i] = new Rect(x, y, BoxW, BoxH);
            }
        }
    }

    protected override long? HitTest(Point p)
    {
        if (Data == null) return null;
        foreach (var (n, r) in _boxes)
            if (r.Contains(p) && Data.Ancestors.TryGetValue(n, out var person)) return person.Id;
        return null;
    }

    public override void Render(DrawingContext ctx)
    {
        Layout();
        if (Data is not { } data) return;
        var line = new Pen(Brush("LineStrong"), 1.5);
        var g = Math.Clamp(Generations, 2, 6);

        // Connectors: from each child's right edge to both parents' left edges.
        foreach (var (n, r) in _boxes)
        {
            if (n >= 1 << (g - 1)) continue;
            if (!_boxes.TryGetValue(2 * n, out var f) || !_boxes.TryGetValue(2 * n + 1, out var m)) continue;
            var midX = r.Right + ColGap / 2;
            ctx.DrawLine(line, new Point(r.Right, r.Center.Y), new Point(midX, r.Center.Y));
            ctx.DrawLine(line, new Point(midX, f.Center.Y), new Point(midX, m.Center.Y));
            ctx.DrawLine(line, new Point(midX, f.Center.Y), new Point(f.Left, f.Center.Y));
            ctx.DrawLine(line, new Point(midX, m.Center.Y), new Point(m.Left, m.Center.Y));
        }

        foreach (var (n, r) in _boxes)
        {
            data.Ancestors.TryGetValue(n, out var p);
            DrawBox(ctx, r, p, n, isLast: n >= 1 << (g - 1));
        }
    }

    private void DrawBox(DrawingContext ctx, Rect r, Person? p, int n, bool isLast)
    {
        var radius = 10;
        if (p == null)
        {
            var dashed = new Pen(Brush("Line"), 1.2, new DashStyle(new double[] { 4, 3 }, 0));
            ctx.DrawRectangle(null, dashed, r, radius, radius);
            var label = n % 2 == 0 ? "Father unknown" : "Mother unknown";
            var t = Text(label, 12, Brush("Faint"));
            ctx.DrawText(t, new Point(r.X + 16, r.Center.Y - t.Height / 2));
            return;
        }
        var hover = Hover == p.Id;
        var root = n == 1;
        ctx.DrawRectangle(root ? Brush("AccentSoft") : hover ? Brush("SurfaceAlt") : Brush("Surface"),
                          new Pen(root || hover ? Brush("Accent") : Brush("Line"), root ? 1.6 : 1), r, radius, radius);
        // A coloured bar for sex, like the avatars elsewhere.
        var edge = p.Sex switch { Sex.Male => Brush("MaleInk"), Sex.Female => Brush("FemaleInk"), _ => Brush("OtherInk") };
        ctx.DrawRectangle(edge, null, new Rect(r.X + 7, r.Y + 12, 3.5, r.Height - 24), 1.75, 1.75);

        var x = r.X + 16;
        var width = r.Width - 28;
        var name = Text(p.DisplayName, 13.5, Brush("Ink"), bold: true, maxWidth: width);
        var years = Text(Lines(p), 11.5, Brush("Muted"), maxWidth: width);
        var top = r.Center.Y - (name.Height + years.Height) / 2;
        ctx.DrawText(name, new Point(x, top));
        ctx.DrawText(years, new Point(x, top + name.Height));

        // More ancestors beyond the edge of the chart.
        if (isLast && p.ParentFamilyIds.Count > 0)
        {
            var arrow = new Pen(Brush("Accent"), 1.8, lineCap: PenLineCap.Round);
            var ax = r.Right + 10;
            ctx.DrawLine(arrow, new Point(ax, r.Center.Y - 5), new Point(ax + 5, r.Center.Y));
            ctx.DrawLine(arrow, new Point(ax + 5, r.Center.Y), new Point(ax, r.Center.Y + 5));
        }
    }

    private static string Lines(Person p)
    {
        var place = p.Birth?.Place.Split(',')[0].Trim() ?? "";
        return string.Join("  ·  ", new[] { Years(p), place }.Where(s => s.Length > 0));
    }
}
