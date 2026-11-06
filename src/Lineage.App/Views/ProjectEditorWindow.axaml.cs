using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class ProjectEditorWindow : Window
{
    public ProjectEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("ClientBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectEditorViewModel { IsValid: false }) { this.FindControl<TextBox>("ClientBox")?.Focus(); return; }
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
