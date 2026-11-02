using Avalonia;
using Avalonia.Media;
using Lineage.Core.Analysis;
using Lineage.Core.Charts;

namespace Lineage.App.Controls;

/// <summary>
/// How the home person is related to the person showing, drawn as a path: up a step for each parent, down for each
/// child, across for a marriage — so cousins meet at their common ancestors at the top — with the relationship named.
/// </summary>
public sealed class RelationshipChart : ChartBase
{
    private const double BoxWidth = 200, BoxHeight = 58, StepX = 236, StepY = 104, Pad = 20, Header = 54;
    private List<PathStep> _steps = new();
    private string _message = "";
    private string _title = "";

    protected override Size MeasureOverride(Size availableSize)
    {
        _steps = new();
        _message = _title = "";
        if (Data is not { } d) return default;
        if (d.Tree.Person(d.Tree.HomePersonId) is not { } home)
            _message = "Set a home person (yourself, usually) to see how everyone is related to them.";
        else if (home.Id == d.RootId)
            _message = "This is the home person. Pick someone else to see how they're related.";
        else
        {
            _steps = MoreCharts.RelationshipPath(d.Tree, home.Id, d.RootId);
            if (_steps.Count == 0) _message = $"{d.Tree.Person(d.RootId)?.DisplayName} isn't related to {home.DisplayName} in this tree.";
            else _title = $"{d.Tree.Person(d.RootId)?.DisplayName} is {home.DisplayName}'s {Relationship.Describe(d.Tree, home, d.Tree.Person(d.RootId)!).Text}";
        }
        if (_steps.Count == 0) return new Size(560, 90);
        return new Size(Pad * 2 + _steps.Max(s => s.X) * StepX + BoxWidth, Header + Pad + _steps.Max(s => s.Y) * StepY + BoxHeight + Pad);
    }

    private Rect Box(PathStep s) => new(Pad + s.X * StepX, Header + Pad + s.Y * StepY, BoxWidth, BoxHeight);

    public override void Render(DrawingContext ctx)
    {
        if (Data is not { } d) return;
        if (_steps.Count == 0)
        {
            ctx.DrawText(Text(_message, 13, Brush("Muted"), maxWidth: 520), new Point(Pad, Pad));
            return;
        }
        ctx.DrawText(Text(_title, 15, Brush("Ink"), bold: true), new Point(Pad, Pad - 4));
        var pen = new Pen(Brush("LineStrong"), 1.5);
        for (var i = 1; i < _steps.Count; i++)
        {
            var (a, b) = (Box(_steps[i - 1]), Box(_steps[i]));
            var from = new Point(a.Right, a.Center.Y);
            var to = new Point(b.Left, b.Center.Y);
            ctx.DrawLine(pen, from, to);
            var label = Text(_steps[i].Link, 11, Brush("Accent"), bold: true);
            var mid = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
            var pad = new Rect(mid.X - label.Width / 2 - 5, mid.Y - label.Height / 2 - 2, label.Width + 10, label.Height + 4);
            ctx.DrawRectangle(Brush("Bg"), null, pad, 4, 4);
            ctx.DrawText(label, new Point(pad.X + 5, pad.Y + 2));
        }
        foreach (var s in _steps)
        {
            var r = Box(s);
            var p = d.Tree.Person(s.PersonId);
            var ends = s == _steps[0] || s == _steps[^1];
            ctx.DrawRectangle(Brush(ends ? "AccentSoft" : "Surface"), new Pen(Brush(ends || Hover == s.PersonId ? "Accent" : "Line"), ends ? 1.6 : 1), r, 10, 10);
            if (p == null) continue;
            ctx.DrawRectangle(PersonBar(p), null, new Rect(r.X + 10, r.Y + 12, 3.5, r.Height - 24), 1.75, 1.75);
            var name = Text(p.DisplayName, 13, Brush("Ink"), bold: true, maxWidth: r.Width - 30);
            ctx.DrawText(name, new Point(r.X + 21, r.Y + 10));
            ctx.DrawText(Text(Years(p), 11, Brush("Muted")), new Point(r.X + 21, r.Y + 12 + name.Height));
        }
    }

    protected override long? HitTest(Point p) => _steps.FirstOrDefault(s => Box(s).Contains(p))?.PersonId;
}
