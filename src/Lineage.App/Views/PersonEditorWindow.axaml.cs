using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lineage.App.Controls;

namespace Lineage.App.Views;

public partial class PersonEditorWindow : Window
{
    public PersonEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("GivenBox")?.Focus();
        NoteBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Control) return;
            if (e.Key == Key.B) { NoteText.Wrap(NoteBox, "**"); e.Handled = true; }
            if (e.Key == Key.I) { NoteText.Wrap(NoteBox, "*"); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    private void OnNoteBold(object? sender, RoutedEventArgs e) => NoteText.Wrap(NoteBox, "**");
    private void OnNoteItalic(object? sender, RoutedEventArgs e) => NoteText.Wrap(NoteBox, "*");
    private void OnNoteBullet(object? sender, RoutedEventArgs e) => NoteText.StartLines(NoteBox, "- ");
    private void OnNoteHeading(object? sender, RoutedEventArgs e) => NoteText.StartLines(NoteBox, "## ");

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
