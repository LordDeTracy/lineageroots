using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lineage.App.ViewModels;
using Lineage.Core.Dna;

namespace Lineage.App.Controls;

/// <summary>
/// The cluster grid: one row and column per match, in cluster order; a filled square where two matches share DNA,
/// coloured by cluster when both are in the same one. Clusters show as coloured blocks along the diagonal.
/// Names run down the left when there's room; hovering a square names the pair.
/// </summary>
public sealed class ClusterGrid : Control
{
    public static readonly StyledProperty<ClusterResult?> ResultProperty =
        AvaloniaProperty.Register<ClusterGrid, ClusterResult?>(nameof(Result));

    public ClusterResult? Result
    {
        get => GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    private const double MaxCell = 16, MinCell = 3, MaxGrid = 720, LabelWidth = 170, MinCellForLabels = 9;

    static ClusterGrid() => AffectsMeasure<ClusterGrid>(ResultProperty);

    private int Count => Result?.Order.Count ?? 0;
    private double Cell => Count == 0 ? MaxCell : Math.Clamp(MaxGrid / Count, MinCell, MaxCell);
    private double Left => Cell >= MinCellForLabels ? LabelWidth : 0;

    protected override Size MeasureOverride(Size availableSize) =>
        Count == 0 ? default : new Size(Left + Count * Cell + 1, Count * Cell + 1);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var r) && r is IBrush b ? b : fallback;

    public override void Render(DrawingContext context)
    {
        if (Result is not { } result || Count == 0) return;
        var cell = Cell;
        var left = Left;
        var empty = Brush("SurfaceAlt", Brushes.WhiteSmoke);
        var other = Brush("LineStrong", Brushes.Gray);
        var line = new Pen(Brush("Line", Brushes.LightGray), 1);
        var ink = Brush("Muted", Brushes.Gray);

        context.FillRectangle(empty, new Rect(left, 0, Count * cell, Count * cell));
        for (var i = 0; i < Count; i++)
        {
            for (var j = 0; j < Count; j++)
            {
                if (i != j && !result.Shares(i, j)) continue;
                var ci = result.ClusterOf[i];
                var fill = ci != 0 && ci == result.ClusterOf[j] ? MainViewModel.ClusterBrush(ci) : other;
                context.FillRectangle(fill, new Rect(left + j * cell, i * cell, cell - (cell > 5 ? 1 : 0), cell - (cell > 5 ? 1 : 0)));
            }
            if (left > 0)
            {
                var m = result.Order[i];
                var text = new FormattedText($"{m.Name}  {m.SharedCm:0}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                             Typeface.Default, Math.Min(11.5, cell - 1), ink)
                { MaxTextWidth = LabelWidth - 10, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                context.DrawText(text, new Point(0, i * cell + (cell - text.Height) / 2));
            }
        }
        context.DrawRectangle(null, line, new Rect(left - 0.5, -0.5, Count * cell + 1, Count * cell + 1));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Result is not { } result || Count == 0) return;
        var p = e.GetPosition(this);
        var j = (int)((p.X - Left) / Cell);
        var i = (int)(p.Y / Cell);
        if (p.X < Left || i < 0 || j < 0 || i >= Count || j >= Count) { ToolTip.SetTip(this, null); return; }
        var a = result.Order[i];
        var b = result.Order[j];
        var tip = i == j ? $"{a.Name} ({a.SharedCm:0} cM)"
            : $"{a.Name} and {b.Name} {(result.Shares(i, j) ? "are shared matches" : "don't share DNA (as far as the lists say)")}";
        ToolTip.SetTip(this, tip);
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
        if (change.Property == ResultProperty) InvalidateVisual();
    }
}
