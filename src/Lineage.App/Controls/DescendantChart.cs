using Avalonia;
using Lineage.App.ViewModels;
using Lineage.Core.Charts;

namespace Lineage.App.Controls;

/// <summary>
/// Everyone descended from the person, top to bottom: each descendant in a box with their partners
/// beside them, children of each marriage hanging from that couple. Sized to the whole tree so a
/// ScrollViewer can scroll it; click a box to re-centre, double-click to open.
/// </summary>
public sealed class DescendantChart : TreeChart
{
    /// <summary>Rows including the root couple's own (1–8), like the pedigree chart's generations.</summary>
    public static readonly StyledProperty<int> GenerationsProperty =
        AvaloniaProperty.Register<DescendantChart, int>(nameof(Generations), 3);

    static DescendantChart() => AffectsMeasure<DescendantChart>(GenerationsProperty);

    public int Generations { get => GetValue(GenerationsProperty); set => SetValue(GenerationsProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GenerationsProperty) ResetLayout();
    }

    protected override TreeLayoutResult CreateLayout(ChartData data) =>
        TreeLayout.Descendants(data.Tree, data.RootId, Math.Clamp(Generations, 1, 8));
}
