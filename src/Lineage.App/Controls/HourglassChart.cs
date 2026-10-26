using Avalonia;
using Lineage.App.ViewModels;
using Lineage.Core.Charts;

namespace Lineage.App.Controls;

/// <summary>
/// The person in the middle with their ancestors above (as couples, parents over each child) and their
/// descendants below, both halves sharing the person's centre line.
/// </summary>
public sealed class HourglassChart : TreeChart
{
    /// <summary>Rows above and including the root's (1–8): 3 shows parents and grandparents.</summary>
    public static readonly StyledProperty<int> AncestorGenerationsProperty =
        AvaloniaProperty.Register<HourglassChart, int>(nameof(AncestorGenerations), 3);

    /// <summary>Rows below and including the root's (1–8): 3 shows children and grandchildren.</summary>
    public static readonly StyledProperty<int> DescendantGenerationsProperty =
        AvaloniaProperty.Register<HourglassChart, int>(nameof(DescendantGenerations), 3);

    static HourglassChart() => AffectsMeasure<HourglassChart>(AncestorGenerationsProperty, DescendantGenerationsProperty);

    public int AncestorGenerations { get => GetValue(AncestorGenerationsProperty); set => SetValue(AncestorGenerationsProperty, value); }
    public int DescendantGenerations { get => GetValue(DescendantGenerationsProperty); set => SetValue(DescendantGenerationsProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AncestorGenerationsProperty || change.Property == DescendantGenerationsProperty) ResetLayout();
    }

    protected override TreeLayoutResult CreateLayout(ChartData data) =>
        TreeLayout.Hourglass(data.Tree, data.RootId, Math.Clamp(AncestorGenerations, 1, 8), Math.Clamp(DescendantGenerations, 1, 8));
}
