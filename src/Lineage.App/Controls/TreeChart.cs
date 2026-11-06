using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Lineage.App.ViewModels;
using Lineage.Core.Charts;
using Lineage.Core.Model;

namespace Lineage.App.Controls;

/// <summary>
/// Shared drawing for the descendant and hourglass charts: lays out a tree with <see cref="TreeLayout"/>
/// and draws couple boxes (the pedigree chart's box look, with a heart between partners), elbow
/// connectors and "more beyond the edge" chevrons. Text is cached per box so hovering a big tree stays quick.
/// </summary>
public abstract class TreeChart : ChartBase
{
    private const double Radius = 10, BadgeRadius = 8.5, ArrowGap = 12, ArrowReach = 11;
    private static readonly Geometry Heart =
        StreamGeometry.Parse("M12 20s-7.5-4.6-7.5-10.2A4.3 4.3 0 0 1 12 7.2a4.3 4.3 0 0 1 7.5 2.6C19.5 15.4 12 20 12 20z");

    private TreeLayoutResult? _layout;
    private StreamGeometry? _connectors;
    private FormattedText[]?[][]? _labels;
    private IBrush? _labelInk;

    protected TreeChart() => ActualThemeVariantChanged += (_, _) => _labels = null;

    /// <summary>The tree to draw for this chart's data.</summary>
    protected abstract TreeLayoutResult CreateLayout(ChartData data);

    /// <summary>The current layout, worked out on first use after the data or options change.</summary>
    public TreeLayoutResult? Layout => _layout ??= Data is { } d ? CreateLayout(d) : null;

    /// <summary>Call when an option that changes the layout changes.</summary>
    protected void ResetLayout()
    {
        _layout = null;
        _connectors = null;
        _labels = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty) ResetLayout();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Layout is { Nodes.Count: > 0 } l ? new Size(l.Width, l.Height) : default;

    protected override long? HitTest(Point p) => ArrowAt(p) ?? Layout?.HitTest(p.X, p.Y);

    /// <summary>The person whose "more beyond the edge" arrow is under the point: clicking one centres the chart on them.</summary>
    internal long? ArrowAt(Point p)
    {
        if (Layout is not { } layout) return null;
        static bool Near(Point p, double x, double y) => Math.Abs(p.X - x) <= ArrowReach && Math.Abs(p.Y - y) <= ArrowReach;
        foreach (var node in layout.Nodes)
        {
            if (node.MoreDescendants && Near(p, node.X + node.Width / 2, node.Y + node.Height + ArrowGap))
                return node.Slots.FirstOrDefault(s => s.PersonId != null)?.PersonId;
            for (var s = 0; s < node.Slots.Count; s++)
                if (node.Slots[s] is { MoreAncestors: true, PersonId: { } id } && node.SlotBounds(s) is var box
                    && Near(p, box.X + box.Width / 2, box.Y - ArrowGap))
                    return id;
        }
        return null;
    }

    /// <summary>
    /// Theme colour that also works before the chart is in a window (image export, tests), where the
    /// normal resource lookup can't reach the application's theme dictionaries.
    /// </summary>
    protected IBrush Paint(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b
        : Application.Current is { } app && (app.TryGetResource(key, ActualThemeVariant, out v) || app.TryGetResource(key, ThemeVariant.Light, out v)) && v is IBrush a ? a
        : Brushes.Gray;

    public override void Render(DrawingContext ctx)
    {
        if (Layout is not { Nodes.Count: > 0 } layout || Data is not { } data) return;
        var ink = Paint("Ink");
        if (_labels == null || !ReferenceEquals(ink, _labelInk))
        {
            _labels = layout.Nodes.Select(n => new FormattedText[]?[n.Slots.Count]).ToArray();
            _labelInk = ink;
        }

        // Square caps close the corners where an elbow's pieces meet.
        ctx.DrawGeometry(null, new Pen(Paint("LineStrong"), 1.5, lineCap: PenLineCap.Square), Connectors(layout));

        var b = new Palette(Paint("Surface"), Paint("AccentSoft"), Paint("Hover"), Paint("Line"), Paint("Accent"),
                            ink, Paint("Muted"), Paint("Faint"), Paint("MaleInk"), Paint("FemaleInk"), Paint("OtherInk"));
        for (var i = 0; i < layout.Nodes.Count; i++) DrawNode(ctx, layout.Nodes[i], _labels[i], data.Tree, b);
    }

    private sealed record Palette(IBrush Surface, IBrush AccentSoft, IBrush Hover, IBrush Line, IBrush Accent,
                                  IBrush Ink, IBrush Muted, IBrush Faint, IBrush Male, IBrush Female, IBrush Other);

    private StreamGeometry Connectors(TreeLayoutResult layout)
    {
        if (_connectors != null) return _connectors;
        var g = new StreamGeometry();
        using (var c = g.Open())
            foreach (var l in layout.Lines)
            {
                c.BeginFigure(new Point(l.X1, l.Y1), isFilled: false);
                c.LineTo(new Point(l.X2, l.Y2));
                c.EndFigure(false);
            }
        return _connectors = g;
    }

    private void DrawNode(DrawingContext ctx, TreeNode node, FormattedText[]?[] labels, FamilyTree tree, Palette b)
    {
        var r = new Rect(node.X, node.Y, node.Width, node.Height);
        var root = node.Generation == 0;
        var hovered = Hover is { } h && node.Slots.Any(s => s.PersonId == h);
        var fill = root ? b.AccentSoft : b.Surface;
        var border = new Pen(root || hovered ? b.Accent : b.Line, root ? 1.6 : 1,
                             node.IsRepeat ? new DashStyle(new double[] { 4, 3 }, 0) : null);
        ctx.DrawRectangle(fill, border, r, Radius, Radius);

        var numbered = node.Slots.Count > 2 && node.Slots[1].Role == SlotRole.Partner;
        var dividerPen = new Pen(b.Line, 1);
        for (var s = 0; s < node.Slots.Count; s++)
        {
            var slot = node.Slots[s];
            var sr = node.SlotBounds(s);
            var box = new Rect(sr.X, sr.Y, sr.Width, sr.Height);
            if (Hover is { } hv && slot.PersonId == hv)
                ctx.DrawRectangle(b.Hover, null, box.Deflate(3), Radius - 2.5, Radius - 2.5);
            if (s > 0) ctx.DrawLine(dividerPen, new Point(box.X, box.Y + 9), new Point(box.X, box.Bottom - 9));

            // Room for the badge on the divider either side.
            var left = s > 0 ? 13.0 : 7.0;
            var rightRoom = s < node.Slots.Count - 1 ? 14.0 : 12.0;
            if (tree.Person(slot.PersonId) is not { } p)
            {
                DrawLines(ctx, labels[s] ??= new[] { Text("Partner unknown", 12, b.Faint, maxWidth: box.Width - left - 9 - rightRoom) }, box.X + left + 9, box.Center.Y);
                continue;
            }
            var bar = ShowEvidence ? PersonBar(p) : p.Sex switch { Sex.Male => b.Male, Sex.Female => b.Female, _ => b.Other };
            ctx.DrawRectangle(bar, null, new Rect(box.X + left, box.Y + 12, 3.5, box.Height - 24), 1.75, 1.75);
            var x = box.X + left + 9;
            var width = box.Right - rightRoom - x;
            DrawLines(ctx, labels[s] ??= PersonLines(p, width, b.Ink, b.Muted), x, box.Center.Y);

            if (slot.MoreAncestors) Chevron(ctx, b, new Point(box.Center.X, box.Y - ArrowGap), up: true, Hover == slot.PersonId);
        }

        // Badges on the dividers: a heart for a couple, numbers when someone married more than once.
        for (var s = 1; s < node.Slots.Count; s++)
        {
            var c = new Point(node.SlotBounds(s).X, r.Center.Y);
            ctx.DrawEllipse(fill, dividerPen, c, BadgeRadius, BadgeRadius);
            if (numbered)
            {
                var n = Text(s.ToString(), 10.5, b.Accent, bold: true);
                ctx.DrawText(n, new Point(c.X - n.Width / 2, c.Y - n.Height / 2));
            }
            else
            {
                const double scale = 0.5;
                using (ctx.PushTransform(Matrix.CreateTranslation(-12, -12.8) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(c.X, c.Y)))
                    ctx.DrawGeometry(b.Female, null, Heart);
            }
        }

        if (node.MoreDescendants) Chevron(ctx, b, new Point(r.Center.X, r.Bottom + ArrowGap), up: false, hovered);
    }

    /// <summary>A round button with an arrow: there are more people that way, and clicking it goes to them.</summary>
    private static void Chevron(DrawingContext ctx, Palette b, Point at, bool up, bool hovered)
    {
        ctx.DrawEllipse(hovered ? b.AccentSoft : b.Surface, new Pen(hovered ? b.Accent : b.Line, 1), at, 9.5, 9.5);
        var pen = new Pen(b.Accent, 1.8, lineCap: PenLineCap.Round);
        var dy = up ? 2.5 : -2.5;
        ctx.DrawLine(pen, new Point(at.X - 5, at.Y + dy), new Point(at.X, at.Y - dy));
        ctx.DrawLine(pen, new Point(at.X, at.Y - dy), new Point(at.X + 5, at.Y + dy));
    }
}
