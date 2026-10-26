using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class ResearchEditorWindow : Window
{
    public ResearchEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("DescriptionBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResearchEditorViewModel { IsValid: false }) { this.FindControl<TextBox>("DescriptionBox")?.Focus(); return; }
        Close(EditorResult.Saved);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(EditorResult.Cancelled);
    private void OnDelete(object? sender, RoutedEventArgs e) => Close(EditorResult.Deleted);
}
