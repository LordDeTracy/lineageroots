using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Lineage.Core.Reports;

namespace Lineage.App.Controls;

/// <summary>
/// Shows a note with its light formatting (<see cref="NoteFormat"/>): **bold**, *italic*, bullet points and small
/// headings. The text stays selectable so people can copy from it.
/// </summary>
public sealed class NoteView : StackPanel
{
    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<NoteView, string?>(nameof(Note));

    public string? Note { get => GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    public NoteView() => Spacing = 8;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NoteProperty) Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var piece in NoteFormat.Parse(Note))
        {
            var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
            foreach (var run in piece.Runs)
                text.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text)
                {
                    FontWeight = run.Bold || piece.Kind == NoteFormat.Kind.Heading ? FontWeight.SemiBold : FontWeight.Normal,
                    FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal,
                });
            switch (piece.Kind)
            {
                case NoteFormat.Kind.Heading:
                    text.FontSize = 15;
                    text.Margin = new Thickness(0, 6, 0, 0);
                    Children.Add(text);
                    break;
                case NoteFormat.Kind.Bullet:
                    var row = new DockPanel { Margin = new Thickness(6, -4, 0, 0) };
                    var dot = new TextBlock { Text = "•", Width = 16, LineHeight = 22, VerticalAlignment = VerticalAlignment.Top };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(text);
                    Children.Add(row);
                    break;
                default:
                    Children.Add(text);
                    break;
            }
        }
    }
}
