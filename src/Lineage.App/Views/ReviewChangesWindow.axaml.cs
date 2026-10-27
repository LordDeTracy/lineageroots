using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class ReviewChangesWindow : Window
{
    public ReviewChangesWindow() => InitializeComponent();

    private void OnApply(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
