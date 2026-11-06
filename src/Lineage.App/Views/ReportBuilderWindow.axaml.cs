using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class ReportBuilderWindow : Window
{
    public ReportBuilderWindow() => InitializeComponent();

    private void OnMake(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
