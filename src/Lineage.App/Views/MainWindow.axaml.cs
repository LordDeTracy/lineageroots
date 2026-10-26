using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as MainViewModel)?.StartAutoSave();
    }

    /// <summary>Asks about unsaved changes before the window closes.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainViewModel vm) return;
        e.Cancel = true;
        if (!await vm.CanCloseAsync()) return;
        _closeConfirmed = true;
        Close();
    }

    /// <summary>Closes without the unsaved-changes question (used by screenshots).</summary>
    public void CloseWithoutAsking()
    {
        _closeConfirmed = true;
        Close();
    }

    private void OnNewWindow(object? sender, RoutedEventArgs e) => App.OpenWindow(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.N && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            App.OpenWindow(null);
            e.Handled = true;
        }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    public void ScrollPageTo(double y) => PersonView.ScrollPageTo(y);

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        (DataContext as MainViewModel)?.RememberTheme(app.RequestedThemeVariant == ThemeVariant.Dark ? "Dark" : "Light");
    }

    /// <summary>
    /// Renders a window to a PNG and closes it. Used by "LineageRoots --screenshot out.png [file]"
    /// to review the UI in CI or from scripts without a visible desktop session.
    /// </summary>
    public static async Task SaveScreenshotAsync(Window window, string path, int delayMs = 1500)
    {
        await Task.Delay(delayMs);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var scale = window.RenderScaling;
            var size = new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale));
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bmp.Render(window);
            bmp.Save(path);
            if (window is MainWindow main) main.CloseWithoutAsking(); else window.Close();
        });
    }
}
