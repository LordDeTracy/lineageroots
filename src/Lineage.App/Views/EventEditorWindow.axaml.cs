using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class EventEditorWindow : Window
{
    public EventEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<ComboBox>("TypeBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EventEditorViewModel { IsValid: false }) return; // the form shows what's wrong
        Close(EditorResult.Saved);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(EditorResult.Cancelled);
    private void OnDelete(object? sender, RoutedEventArgs e) => Close(EditorResult.Deleted);
}
