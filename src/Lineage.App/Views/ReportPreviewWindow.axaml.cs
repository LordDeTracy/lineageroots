using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class ReportPreviewWindow : Window
{
    public ReportPreviewWindow() => InitializeComponent();

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
