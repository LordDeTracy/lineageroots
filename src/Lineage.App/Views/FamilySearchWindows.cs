using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;
using Lineage.Core.FamilySearch;

namespace Lineage.App.Views;

public partial class FamilySearchSettingsWindow : Window
{
    public FamilySearchSettingsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("KeyBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

public partial class FamilySearchSearchWindow : Window
{
    public FamilySearchSearchWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as FamilySearchSearchViewModel)?.SearchCommand.Execute(null);
    }

    private void OnLink(object? sender, RoutedEventArgs e) =>
        Close((DataContext as FamilySearchSearchViewModel)?.Selected?.Person);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}

public partial class FamilySearchCompareWindow : Window
{
    public FamilySearchCompareWindow() => InitializeComponent();

    private void OnApply(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
