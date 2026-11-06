using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class SideBySideWindow : Window
{
    public SideBySideWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (DataContext is SideBySideViewModel vm) vm.CloseRequested += Close; };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
