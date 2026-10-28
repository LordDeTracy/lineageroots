using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lineage.App.ViewModels;
using Lineage.Core.Dna;

namespace Lineage.App.Controls;

/// <summary>
/// The chromosome painter: chromosomes 1–22 and X, each drawn as a pair of bars to scale — the father's side above,
/// the mother's below — with every painted segment coloured by the ancestors it came from. Segments that overlap on
/// one side stack, and a third bar holds segments whose side can't be told. Hovering a segment names it.
/// </summary>
public sealed class ChromosomeMap : Control
{
    public static readonly StyledProperty<Painting?> PaintingProperty =
        AvaloniaProperty.Register<ChromosomeMap, Painting?>(nameof(Painting));

    public Painting? Painting
    {
        get => GetValue(PaintingProperty);
        set => SetValue(PaintingProperty, value);
    }

    private const double LabelWidth = 44, SideMark = 28, Lane = 9, LaneGap = 2, SideGap = 3, RowGap = 10;
    private readonly List<(Rect Rect, PaintedSegment Segment)> _hits = new();

    static ChromosomeMap() => AffectsMeasure<ChromosomeMap>(PaintingProperty);

    private sealed record LaneRow(PaintSide Side, bool First, List<PaintedSegment> Segments);

    /// <summary>Each chromosome's lanes: segments per side, stacked so overlapping ones sit on separate lanes.</summary>
    private List<(int Chromosome, List<LaneRow> Lanes)> Layout()
    {
        var rows = new List<(int, List<LaneRow>)>();
        var segments = Painting?.Segments ?? Array.Empty<PaintedSegment>();
        for (var chr = 1; chr <= ChromosomePainter.X; chr++)
        {
            var lanes = new List<LaneRow>();
            foreach (var side in new[] { PaintSide.Father, PaintSide.Mother, PaintSide.Unknown })
            {
                var mine = segments.Where(s => s.Segment.Chromosome == chr && s.Side == side).OrderBy(s => s.Segment.Start).ToList();
                if (side == PaintSide.Unknown && mine.Count == 0) continue;
                var stacked = new List<List<PaintedSegment>>();
                foreach (var s in mine)
                {
                    var lane = stacked.FirstOrDefault(l => l[^1].Segment.End <= s.Segment.Start);
                    if (lane == null) stacked.Add(lane = new List<PaintedSegment>());
                    lane.Add(s);
                }
                if (stacked.Count == 0) stacked.Add(new List<PaintedSegment>());
                lanes.AddRange(stacked.Select((l, i) => new LaneRow(side, i == 0, l)));
            }
            rows.Add((chr, lanes));
        }
        return rows;
    }

    private static double BlockHeight(List<LaneRow> lanes) =>
        lanes.Count * (Lane + LaneGap) - LaneGap + lanes.Count(l => l.First && l.Side != PaintSide.Father) * SideGap;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Painting == null) return default;
        var height = Layout().Sum(r => BlockHeight(r.Lanes) + RowGap);
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        return new Size(width, height);
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    public override void Render(DrawingContext context)
    {
        _hits.Clear();
        if (Painting == null) return;
        var empty = Brush("SurfaceAlt", Brushes.WhiteSmoke);
        var ink = Brush("Muted", Brushes.Gray);
        var faint = Brush("Faint", Brushes.Gray);
        var scale = (Bounds.Width - LabelWidth - 4) / ChromosomePainter.Lengths[0];
        var y = 0.0;
        foreach (var (chr, lanes) in Layout())
        {
            var label = new FormattedText(ChromosomePainter.Name(chr), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                          Typeface.Default, 11.5, ink);
            context.DrawText(label, new Point(0, y + (BlockHeight(lanes) - label.Height) / 2));
            var width = ChromosomePainter.Length(chr) * scale;
            foreach (var lane in lanes)
            {
                if (lane.First)
                {
                    if (lane.Side != PaintSide.Father) y += SideGap;
                    var mark = new FormattedText(lane.Side switch { PaintSide.Father => "F", PaintSide.Mother => "M", _ => "?" },
                                                 CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, faint);
                    context.DrawText(mark, new Point(SideMark, y + (Lane - mark.Height) / 2));
                }
                context.DrawRectangle(empty, null, new RoundedRect(new Rect(LabelWidth, y, width, Lane), 3));
                foreach (var s in lane.Segments)
                {
                    var rect = new Rect(LabelWidth + s.Segment.Start * scale, y,
                                        Math.Max(2, (s.Segment.End - s.Segment.Start) * scale), Lane);
                    context.DrawRectangle(MainViewModel.ClusterBrush(s.Group), null, new RoundedRect(rect, 2));
                    _hits.Add((rect, s));
                }
                y += Lane + LaneGap;
            }
            y += RowGap - LaneGap;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var hit = _hits.LastOrDefault(h => h.Rect.Inflate(new Thickness(1, 0)).Contains(p));
        if (hit.Segment is not { } s) { ToolTip.SetIsOpen(this, false); ToolTip.SetTip(this, null); return; }
        var group = Painting?.Groups.FirstOrDefault(g => g.Number == s.Group);
        var side = s.Side switch { PaintSide.Father => "father's side", PaintSide.Mother => "mother's side", _ => "side unknown" };
        ToolTip.SetTip(this, $"{s.Match.Name}: chromosome {ChromosomePainter.Name(s.Segment.Chromosome)}, " +
                             $"{s.Segment.Start / 1e6:0.0}–{s.Segment.End / 1e6:0.0} Mb, {s.Segment.Cm:0.#} cM\n" +
                             $"{group?.Label} ({side})");
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetIsOpen(this, false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PaintingProperty) InvalidateVisual();
    }
}
