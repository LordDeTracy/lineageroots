using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class CensusEntryWindow : Window
{
    public CensusEntryWindow() => InitializeComponent();

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
