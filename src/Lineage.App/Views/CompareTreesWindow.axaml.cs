using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class CompareTreesWindow : Window
{
    public CompareTreesWindow() => InitializeComponent();

    private void OnCopy(object? sender, RoutedEventArgs e) => Close(true);
    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
