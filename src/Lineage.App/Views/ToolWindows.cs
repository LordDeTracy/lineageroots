using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class ProblemsWindow : Window
{
    public ProblemsWindow() => InitializeComponent();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

public partial class RelationshipWindow : Window
{
    public RelationshipWindow() => InitializeComponent();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
