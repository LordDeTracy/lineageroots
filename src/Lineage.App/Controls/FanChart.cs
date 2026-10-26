using Avalonia;
using Avalonia.Media;
using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>
/// A 240° fan of ancestors: the person in the centre, each generation a ring further out, the
/// father's side on the left (blue) and the mother's on the right (rose). Inner rings write
/// names along the arc; outer rings, where segments get narrow, write them along the radius.
/// </summary>
public sealed class FanChart : ChartBase
{
    public static readonly StyledProperty<int> GenerationsProperty =
        AvaloniaProperty.Register<FanChart, int>(nameof(Generations), 6);

    static FanChart() => AffectsMeasure<FanChart>(GenerationsProperty);

    public int Generations { get => GetValue(GenerationsProperty); set => SetValue(GenerationsProperty, value); }

    private const double Span = 240, Pad = 24, CentreRadius = 66;
    private static readonly double[] RingWidth = { 0, 74, 72, 78, 98, 108, 112 };

    private int Gens => Math.Clamp(Generations, 2, RingWidth.Length);
    private double OuterRadius => CentreRadius + RingWidth.Take(Gens).Sum();
    private Point Centre => new(Pad + OuterRadius, Pad + OuterRadius);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GenerationsProperty) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var r = OuterRadius;
        var below = Math.Max(r * Math.Cos(Math.PI / 3), CentreRadius); // the fan's lowest point is at ±120°
        return new Size(2 * (r + Pad), r + below + 2 * Pad);
    }

    private double Inner(int gen) => CentreRadius + RingWidth.Take(gen).Sum();
    private double Outer(int gen) => Inner(gen) + RingWidth[gen];

    /// <summary>Screen point at a radius and an angle measured clockwise from straight up, in degrees.</summary>
    private Point At(double r, double deg)
    {
        var rad = deg * Math.PI / 180;
        return new Point(Centre.X + r * Math.Sin(rad), Centre.Y - r * Math.Cos(rad));
    }

    protected override long? HitTest(Point p)
    {
        if (Data == null) return null;
        var d = p - Centre;
        var r = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (r <= CentreRadius) return Data.Ancestors.TryGetValue(1, out var root) ? root.Id : null;
        var deg = Math.Atan2(d.X, -d.Y) * 180 / Math.PI;
        if (Math.Abs(deg) > Span / 2) return null;
        for (var gen = 1; gen < Gens; gen++)
        {
            if (r < Inner(gen) || r >= Outer(gen)) continue;
            var count = 1 << gen;
            var i = Math.Min((int)((deg + Span / 2) / (Span / count)), count - 1);
            return Data.Ancestors.TryGetValue(count + i, out var person) ? person.Id : null;
        }
        return null;
    }

    public override void Render(DrawingContext ctx)
    {
        if (Data is not { } data) return;
        var gap = new Pen(Brush("Bg"), 2);
        var male = Colour("MaleBg");
        var female = Colour("FemaleBg");
        var surface = Colour("Surface");

        for (var gen = 1; gen < Gens; gen++)
        {
            var count = 1 << gen;
            var step = Span / count;
            // Outer generations fade towards the page colour so the eye starts in the middle.
            var strength = 1.0 - (gen - 1) * 0.11;
            for (var i = 0; i < count; i++)
            {
                var a = -Span / 2 + i * step;
                var b = a + step;
                data.Ancestors.TryGetValue(count + i, out var p);
                var side = i < count / 2 ? male : female;
                IBrush fill = p == null ? Brush("Sidebar")
                    : Hover == p.Id ? Brush("Selected")
                    : new SolidColorBrush(Blend(surface, side, strength));
                ctx.DrawGeometry(fill, gap, Segment(Inner(gen), Outer(gen), a, b));
                if (p != null) DrawLabel(ctx, p, gen, a, b);
            }
        }

        // The person in the middle.
        if (data.Ancestors.TryGetValue(1, out var me))
        {
            ctx.DrawEllipse(Hover == me.Id ? Brush("Selected") : Brush("AccentSoft"), new Pen(Brush("Accent"), 1.6),
                            Centre, CentreRadius - 2, CentreRadius - 2);
            var name = Text(me.DisplayName, 13, Brush("Ink"), bold: true);
            name.MaxTextWidth = CentreRadius * 1.6;
            name.MaxLineCount = 3;
            name.TextAlignment = TextAlignment.Center;
            var years = Text(Years(me), 11, Brush("Muted"));
            var h = name.Height + years.Height;
            ctx.DrawText(name, new Point(Centre.X - name.MaxTextWidth / 2, Centre.Y - h / 2));
            ctx.DrawText(years, new Point(Centre.X - years.Width / 2, Centre.Y - h / 2 + name.Height));
        }
    }

    private void DrawLabel(DrawingContext ctx, Person p, int gen, double a, double b)
    {
        var mid = (a + b) / 2;
        var r1 = Inner(gen);
        var r2 = Outer(gen);
        var midR = (r1 + r2) / 2;
        var arc = midR * (b - a) * Math.PI / 180;      // room along the ring
        var depth = r2 - r1;                            // room across the ring
        var ink = Brush("Ink");
        var muted = Brush("Muted");

        if (gen <= 3)
        {
            // Along the arc, flipped on the lower half so it never reads upside down.
            var width = arc * 0.86;
            var name = Text(p.DisplayName, gen == 1 ? 13 : 12, ink, bold: true);
            name.MaxTextWidth = width;
            name.MaxLineCount = gen == 1 ? 2 : 2;
            name.TextAlignment = TextAlignment.Center;
            name.Trimming = TextTrimming.CharacterEllipsis;
            var years = Text(Years(p), 10.5, muted, maxWidth: width);
            years.TextAlignment = TextAlignment.Center;
            var h = Math.Min(name.Height, depth - years.Height - 8) + years.Height;
            var angle = Math.Abs(mid) > 90 ? mid + 180 : mid;
            using (ctx.PushTransform(Rotate(At(midR, mid), angle)))
            {
                ctx.DrawText(name, new Point(-width / 2, -h / 2));
                ctx.DrawText(years, new Point(-width / 2, -h / 2 + h - years.Height));
            }
        }
        else
        {
            // Along the radius. Two lines only where the segment is wide enough.
            var length = depth - 10;
            var size = gen >= 6 ? 10 : 11;
            var name = Text(ShortName(p, gen), size, ink, bold: true, maxWidth: length);
            var twoLines = arc >= 2.2 * name.Height;
            var years = twoLines ? Text(Years(p), size - 1, muted, maxWidth: length) : null;
            var h = name.Height + (years?.Height ?? 0);
            var flip = mid < 0;
            var angle = flip ? mid + 90 : mid - 90;
            using (ctx.PushTransform(Rotate(At(midR, mid), angle)))
            {
                ctx.DrawText(name, new Point(-length / 2, -h / 2));
                if (years != null) ctx.DrawText(years, new Point(-length / 2, -h / 2 + name.Height));
            }
        }
    }

    /// <summary>Outer rings are tight: drop middle names there.</summary>
    private static string ShortName(Person p, int gen)
    {
        if (gen < 5) return p.DisplayName;
        var n = p.PrimaryName;
        var first = n.Given.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return $"{first} {n.Surname}".Trim();
    }

    private static Matrix Rotate(Point at, double degrees) =>
        Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(at.X, at.Y);

    private StreamGeometry Segment(double r1, double r2, double a, double b)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        var large = b - a > 180;
        c.BeginFigure(At(r1, a), isFilled: true);
        c.LineTo(At(r2, a));
        c.ArcTo(At(r2, b), new Size(r2, r2), 0, large, SweepDirection.Clockwise);
        c.LineTo(At(r1, b));
        c.ArcTo(At(r1, a), new Size(r1, r1), 0, large, SweepDirection.CounterClockwise);
        c.EndFigure(true);
        return g;
    }

    private static Color Blend(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }
}
