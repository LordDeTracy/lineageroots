using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lineage.App.Views;

public partial class PersonEditorWindow : Window
{
    public PersonEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("GivenBox")?.Focus();
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
