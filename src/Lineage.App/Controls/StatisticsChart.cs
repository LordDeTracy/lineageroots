using Avalonia;
using Avalonia.Media;
using Lineage.Core.Charts;

namespace Lineage.App.Controls;

/// <summary>
/// The whole tree in numbers: births by decade, average lifespan by century, the commonest surnames and birthplaces —
/// each as a bar chart.
/// </summary>
public sealed class StatisticsChart : ChartBase
{
    private const double PanelWidth = 860, Pad = 20, BarHeight = 20, Gap = 6, LabelWidth = 190, PanelGap = 30;
    private TreeStatistics? _stats;

    protected override Size MeasureOverride(Size availableSize)
    {
        _stats = Data is { } d ? MoreCharts.Statistics(d.Tree) : null;
        if (_stats == null) return default;
        var bars = Panels(_stats).Sum(p => Math.Max(1, p.Bars.Count) * (BarHeight + Gap) + 40 + PanelGap);
        return new Size(PanelWidth, Pad * 2 + bars);
    }

    private static IEnumerable<(string Title, IReadOnlyList<StatBar> Bars, Func<StatBar, string> Value)> Panels(TreeStatistics s) => new (string, IReadOnlyList<StatBar>, Func<StatBar, string>)[]
    {
        ("Births by decade", s.BirthsByDecade, b => b.Count.ToString()),
        ("Average lifespan by century of birth", s.LifespanByCentury, b => $"{b.Value:0} years ({b.Count})"),
        ("Commonest surnames", s.Surnames, b => b.Count.ToString()),
        ("Commonest birthplaces", s.Places, b => b.Count.ToString()),
    };

    public override void Render(DrawingContext ctx)
    {
        if (_stats == null) return;
        var y = Pad;
        var barWidth = PanelWidth - Pad * 2 - LabelWidth - 110;
        foreach (var (title, bars, value) in Panels(_stats))
        {
            ctx.DrawText(Text(title, 15, Brush("Ink"), bold: true), new Point(Pad, y));
            y += 32;
            if (bars.Count == 0)
            {
                ctx.DrawText(Text("Not enough dates or places recorded yet.", 12, Brush("Muted")), new Point(Pad, y));
                y += BarHeight + Gap + PanelGap;
                continue;
            }
            var max = bars.Max(b => b.Value);
            foreach (var bar in bars)
            {
                var label = Text(bar.Label, 12, Brush("Muted"), maxWidth: LabelWidth - 10);
                ctx.DrawText(label, new Point(Pad, y + (BarHeight - label.Height) / 2));
                var w = max > 0 ? Math.Max(2, bar.Value / max * barWidth) : 2;
                ctx.DrawRectangle(Brush("Accent"), null, new Rect(Pad + LabelWidth, y + 2, w, BarHeight - 4), 3, 3);
                var v = Text(value(bar), 11.5, Brush("Ink"));
                ctx.DrawText(v, new Point(Pad + LabelWidth + w + 8, y + (BarHeight - v.Height) / 2));
                y += BarHeight + Gap;
            }
            y += PanelGap;
        }
    }

    protected override long? HitTest(Point p) => null;
}
