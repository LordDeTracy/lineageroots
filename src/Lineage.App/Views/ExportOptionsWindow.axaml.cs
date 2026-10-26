using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class ExportOptionsWindow : Window
{
    public ExportOptionsWindow() => InitializeComponent();

    private void OnContinue(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
