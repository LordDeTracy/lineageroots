using Avalonia.Controls;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class TutorialWindow : Window
{
    public TutorialWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TutorialViewModel vm) vm.Finished += Close;
        };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
