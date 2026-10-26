using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class BookOptionsWindow : Window
{
    public BookOptionsWindow() => InitializeComponent();

    private void OnCreate(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
