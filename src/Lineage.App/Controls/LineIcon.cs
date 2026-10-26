using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Lineage.App.Controls;

/// <summary>
/// A stroke icon drawn from 24×24 path data (lucide/feather style), scaled to the control size.
/// Keeps every icon the same visual weight without bundling image files.
/// </summary>
public sealed class LineIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LineIcon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<LineIcon>();
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LineIcon, double>(nameof(StrokeThickness), 1.8);

    static LineIcon()
    {
        AffectsRender<LineIcon>(DataProperty, ForegroundProperty, StrokeThicknessProperty);
        WidthProperty.OverrideDefaultValue<LineIcon>(18);
        HeightProperty.OverrideDefaultValue<LineIcon>(18);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var pen = new Pen(Foreground, StrokeThickness / Math.Max(scale, 0.01) * Math.Max(scale, 0.75),
                          lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, Data);
    }
}
