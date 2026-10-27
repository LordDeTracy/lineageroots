using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class DnaMatchEditorWindow : Window
{
    public DnaMatchEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("NameBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DnaMatchEditorViewModel { IsValid: false } editor)
        {
            this.FindControl<TextBox>(editor.Name.Trim().Length == 0 ? "NameBox" : "CmBox")?.Focus();
            return;
        }
        Close(EditorResult.Saved);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(EditorResult.Cancelled);
    private void OnDelete(object? sender, RoutedEventArgs e) => Close(EditorResult.Deleted);
}
