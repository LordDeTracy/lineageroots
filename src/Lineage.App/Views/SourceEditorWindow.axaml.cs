using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class SourceEditorWindow : Window
{
    public SourceEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("TitleBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceEditorViewModel { IsValid: false }) { this.FindControl<TextBox>("TitleBox")?.Focus(); return; }
        Close(EditorResult.Saved);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(EditorResult.Cancelled);
    private void OnDelete(object? sender, RoutedEventArgs e) => Close(EditorResult.Deleted);
}
