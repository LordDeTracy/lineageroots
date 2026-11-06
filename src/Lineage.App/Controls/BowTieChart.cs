using Avalonia;
using Avalonia.Media;
using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>
/// The bow tie: the person in the middle, the father's ancestors spreading out to the left and the mother's to the
/// right, so both sides of the family get the same room. Unknown ancestors show as faint empty boxes.
/// </summary>
public sealed class BowTieChart : ChartBase
{
    public static readonly StyledProperty<int> GenerationsProperty =
        AvaloniaProperty.Register<BowTieChart, int>(nameof(Generations), 4);

    static BowTieChart() => AffectsMeasure<BowTieChart>(GenerationsProperty);

    public int Generations { get => GetValue(GenerationsProperty); set => SetValue(GenerationsProperty, value); }

    private const double BoxW = 220, BoxH = 88, ColGap = 40, RowGap = 12, Pad = 24;
    private readonly Dictionary<int, Rect> _boxes = new();

    private int Gens => Math.Clamp(Generations, 2, 7);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GenerationsProperty) InvalidateVisual();
    }

    /// <summary>Each side is as tall as its last generation: half of that generation's ancestors.</summary>
    private double SideHeight => (1 << Math.Max(0, Gens - 2)) * (BoxH + RowGap) - RowGap;

    protected override Size MeasureOverride(Size availableSize) =>
        new(Pad * 2 + (2 * Gens - 1) * BoxW + (2 * Gens - 2) * ColGap, Pad * 2 + SideHeight);

    private void Layout()
    {
        _boxes.Clear();
        var g = Gens;
        var height = SideHeight;
        var centre = Pad + (g - 1) * (BoxW + ColGap);
        _boxes[1] = new Rect(centre, Pad + (height - BoxH) / 2, BoxW, BoxH);
        for (var gen = 1; gen < g; gen++)
        {
            var count = 1 << gen;              // Ahnentafel numbers count .. 2*count-1; the first half is the father's side
            var perSide = count / 2;
            var slot = height / perSide;
            for (var i = 0; i < count; i++)
            {
                var father = i < perSide;
                var row = father ? i : i - perSide;
                var x = father ? centre - gen * (BoxW + ColGap) : centre + gen * (BoxW + ColGap);
                _boxes[count + i] = new Rect(x, Pad + slot * row + (slot - BoxH) / 2, BoxW, BoxH);
            }
        }
    }

    /// <summary>Whether ancestor <paramref name="n"/> is on the father's side (to the left).</summary>
    private static bool LeftSide(int n)
    {
        while (n > 3) n >>= 1;
        return n == 2;
    }

    protected override long? HitTest(Point p)
    {
        if (Data == null) return null;
        var last = 1 << (Gens - 1);
        foreach (var (n, r) in _boxes)
        {
            // The last generation's arrow (more ancestors beyond the edge) centres the chart on them too.
            var area = n < last ? r : LeftSide(n) ? new Rect(r.X - 24, r.Y, r.Width + 24, r.Height) : r.WithWidth(r.Width + 24);
            if (area.Contains(p) && Data.Ancestors.TryGetValue(n, out var person)) return person.Id;
        }
        return null;
    }

    public override void Render(DrawingContext ctx)
    {
        Layout();
        if (Data is not { } data) return;
        var line = new Pen(Brush("LineStrong"), 1.5);
        var g = Gens;

        // The person to each parent, then each ancestor to their parents, always outwards.
        foreach (var (n, r) in _boxes)
        {
            if (n >= 1 << (g - 1)) continue;
            if (!_boxes.TryGetValue(2 * n, out var f) || !_boxes.TryGetValue(2 * n + 1, out var m)) continue;
            if (n == 1)
            {
                ctx.DrawLine(line, new Point(r.Left, r.Center.Y), new Point(f.Right, f.Center.Y));
                ctx.DrawLine(line, new Point(r.Right, r.Center.Y), new Point(m.Left, m.Center.Y));
                continue;
            }
            var left = LeftSide(n);
            var from = left ? r.Left : r.Right;
            var midX = left ? r.Left - ColGap / 2 : r.Right + ColGap / 2;
            var to = left ? f.Right : f.Left;
            ctx.DrawLine(line, new Point(from, r.Center.Y), new Point(midX, r.Center.Y));
            ctx.DrawLine(line, new Point(midX, f.Center.Y), new Point(midX, m.Center.Y));
            ctx.DrawLine(line, new Point(midX, f.Center.Y), new Point(to, f.Center.Y));
            ctx.DrawLine(line, new Point(midX, m.Center.Y), new Point(to, m.Center.Y));
        }

        foreach (var (n, r) in _boxes)
        {
            data.Ancestors.TryGetValue(n, out var p);
            DrawBox(ctx, r, p, n, isLast: n >= 1 << (g - 1));
        }
    }

    private void DrawBox(DrawingContext ctx, Rect r, Person? p, int n, bool isLast)
    {
        const double radius = 10;
        if (p == null)
        {
            var dashed = new Pen(Brush("Line"), 1.2, new DashStyle(new double[] { 4, 3 }, 0));
            ctx.DrawRectangle(null, dashed, r, radius, radius);
            var t = Text(n % 2 == 0 ? "Father unknown" : "Mother unknown", 12, Brush("Faint"));
            ctx.DrawText(t, new Point(r.X + 16, r.Center.Y - t.Height / 2));
            return;
        }
        var hover = Hover == p.Id;
        var root = n == 1;
        ctx.DrawRectangle(root ? Brush("AccentSoft") : hover ? Brush("SurfaceAlt") : Brush("Surface"),
                          new Pen(root || hover ? Brush("Accent") : Brush("Line"), root ? 1.6 : 1), r, radius, radius);
        ctx.DrawRectangle(PersonBar(p), null, new Rect(r.X + 7, r.Y + 12, 3.5, r.Height - 24), 1.75, 1.75);

        var x = r.X + 16;
        var width = r.Width - 28;
        DrawLines(ctx, PersonLines(p, width, Brush("Ink"), Brush("Muted")), x, r.Center.Y);

        // More ancestors beyond the edge, pointing outwards.
        if (isLast && p.ParentFamilyIds.Count > 0)
        {
            var arrow = new Pen(Brush("Accent"), 1.8, lineCap: PenLineCap.Round);
            var left = LeftSide(n);
            var ax = left ? r.Left - 10 : r.Right + 10;
            var tip = left ? ax - 5 : ax + 5;
            ctx.DrawLine(arrow, new Point(ax, r.Center.Y - 5), new Point(tip, r.Center.Y));
            ctx.DrawLine(arrow, new Point(tip, r.Center.Y), new Point(ax, r.Center.Y + 5));
        }
    }
}
