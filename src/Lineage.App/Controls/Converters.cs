using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Lineage.App.Controls;

public static class Converters
{
    /// <summary>Two-way int ⇄ "is this radio button the selected index" for a pair of radio buttons.</summary>
    public static readonly IValueConverter IndexIs0 = new IndexIs(0);
    public static readonly IValueConverter IndexIs1 = new IndexIs(1);

    /// <summary>A colour-coding name ("Blue") to its swatch brush; "None" or unknown gives transparent.</summary>
    public static readonly IValueConverter ColourName =
        new FuncValueConverter<string, IBrush>(name => ViewModels.Display.ColourBrush(name ?? "") ?? Brushes.Transparent);

    public static readonly IValueConverter BoldIf =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);

    private sealed class IndexIs(int index) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int i && i == index;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? index : Avalonia.Data.BindingOperations.DoNothing;
    }
}
