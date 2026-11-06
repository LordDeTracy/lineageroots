using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class SlideShowWindow : Window
{
    public SlideShowWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as SlideShowViewModel)?.Start();
        Closed += (_, _) => (DataContext as SlideShowViewModel)?.Stop();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is SlideShowViewModel vm)
        {
            switch (e.Key)
            {
                case Key.Right or Key.PageDown: vm.Next(); e.Handled = true; break;
                case Key.Left or Key.PageUp: vm.Previous(); e.Handled = true; break;
                case Key.Space: vm.TogglePlayCommand.Execute(null); e.Handled = true; break;
                case Key.Escape: Close(); e.Handled = true; break;
            }
        }
        base.OnKeyDown(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
